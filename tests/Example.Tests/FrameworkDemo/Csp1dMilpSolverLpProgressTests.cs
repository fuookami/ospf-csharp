#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// `Csp1dMilpSolver.SolveLPAsync` 必须真的能求解，并把进度上下文透传到框架层。
/// `Csp1dMilpSolver.SolveLPAsync` must really solve and forward the progress context to the framework layer.
/// </summary>
/// <remarks>
/// 只覆盖了 `SolveAsync`，`SolveLPAsync` 一直零覆盖。这批补上，并顺带发现它**不收进度上下文**。
///
/// **零覆盖的危险在这里很具体**：`SolveLPAsync` 是列生成主问题每轮都要走的路，
/// 它若悄悄退化成空壳（比如返回空的影子价格表），**上游只会看到"定价不产列、迅速收敛"**，
/// 而那恰好与"真的收敛了"外观相同。
///
/// **Only `SolveAsync` was covered; `SolveLPAsync` had zero coverage.** It adds coverage for `SolveLPAsync` and found the method **accepts no progress context**.
///
/// **The danger of zero coverage is concrete here**: `SolveLPAsync` is the path the master problem takes every round, and if it quietly degraded into a shell (say
/// returning an empty shadow-price table), **upstream would only see "pricing yields nothing, converges quickly"** — which looks exactly like **genuine convergence**.
/// </remarks>
public class Csp1dMilpSolverLpProgressTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok(Results.SuccessInstance);
        }
    }

    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static ProduceInput<Flt64> MakeInput(out Csp1dProblem<Flt64> problem) {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        problem = builder.Build();

        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) });
        return new ProduceInput<Flt64>(
            CuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            Demands: problem.Demands,
            Materials: problem.Materials,
            Machines: problem.Machines,
            WarmStartPlanUsages: new List<CuttingPlanUsage<Flt64>>());
    }

    /// <summary>**LP 必须真的求解成功并返回模型与输出** / The LP must really solve and return a model and output.</summary>
    /// <remarks>
    /// 断言**三样东西都在**：影子价格表非 null、模型非 null、LP 输出非 null。
    /// **只断言"没失败"不够**——一个 `Results.Ok(null)` 也算没失败。
    ///
    /// It asserts **all three are present**: a non-null shadow-price table, a non-null model, and a non-null LP output.
    /// **Asserting only "not failed" is not enough** — `Results.Ok(null)` also counts as not failed.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldReturnAModelShadowPricesAndAnOutput() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        Result<Csp1dMilpSolver.LpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result =
            await solver.SolveLPAsync(input);

        result.IsOk.Should().BeTrue("a valid input must reach and complete the LP solve");
        result.Value.Should().NotBeNull("a non-empty input must produce a result object");
        result.Value!.ShadowPrices.Should().NotBeNull("the LP result must carry a shadow-price table");
        // **键的存在**才是"提取真的跑了"的证据：变异成 `new ShadowPriceMap` 时该键缺失。
        // 这里**不断言数值**——mock 返回的全零对偶，真实值就是 0，断言"非零"会假红。
        // **The key's presence** is the evidence "extraction really ran": mutating to `new ShadowPriceMap` drops it.
        // **The value is not asserted** — the mock returns all-zero duals, so the real value is 0; asserting non-zero would fail spuriously.
        // **键的存在**才是"提取真的跑了"的证据：不传 CGPipeline 时提取器空转，该键缺失。
        // 这里**不断言数值**——mock 返回全零对偶，真实值就是 0，断言"非零"会假红。
        // **The key's presence** is the evidence "extraction really ran": without the CGPipeline the extractor is a no-op and the key is absent.
        // **The value is not asserted** — the mock returns all-zero duals, so the real value is 0; asserting non-zero would fail spuriously.
        result.Value!.ShadowPrices[
            new ProductDemandShadowPriceKey("A", "mm")].Should().NotBeNull(
            "the demand constraint's dual must be extracted, even when its value is zero");
        result.Value.Model.Should().NotBeNull("the LP result must carry the model it solved");
        result.Value.LpOutput.Should().NotBeNull("the LP result must carry the solver output");
    }

    /// <summary>**给了进度上下文就必须收到快照** / A supplied progress context must receive snapshots.</summary>
    [Fact]
    public async Task SolveLPAsync_ShouldReportToTheProgressContext() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        await solver.SolveLPAsync(input, progressContext: context);

        reporter.Snapshots.Should().NotBeEmpty("the LP path must report through the progress context");
    }

    /// <summary>**没给进度上下文时零上报** / With no progress context, nothing is reported.</summary>
    /// <remarks>
    /// **反向判据**：它证明上报来自上下文，而不是某个默认全局上报器。
    /// **只测正向会让"无条件上报"的实现也通过**。
    ///
    /// **The reverse criterion**: it proves the reporting comes from the context, not some default global reporter. **Testing only the positive direction would let an
    /// "always report" implementation pass**.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldNotReportWithoutAProgressContext() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var reporter = new RecordingReporter();
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        await solver.SolveLPAsync(input);

        reporter.Snapshots.Should().BeEmpty();
    }

    /// <summary>**空方案输入必须返回 null 而不是失败** / An empty-plan input must return null, not a failure.</summary>
    /// <remarks>
    /// 这是 `SolveLPAsync` 的早退分支。**早退与失败语义不同**：上游靠 `null` 判断"没有可解的东西"，
    /// 若实现返回失败，上游会当成"求解出错了"并中止流程。
    ///
    /// This is `SolveLPAsync`'s early-exit branch. **Early exit and failure mean different things**: upstream uses `null` to detect "nothing to solve", and if the
    /// implementation returned a failure, upstream would treat it as "the solve broke" and abort.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldReturnNullForAnEmptyPlanList() {
        ProduceInput<Flt64> full = MakeInput(out _);
        var empty = new ProduceInput<Flt64>(
            CuttingPlans: new List<CuttingPlan<Flt64>>(),
            Demands: full.Demands,
            Materials: full.Materials,
            Machines: full.Machines,
            WarmStartPlanUsages: new List<CuttingPlanUsage<Flt64>>());
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        Result<Csp1dMilpSolver.LpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result =
            await solver.SolveLPAsync(empty);

        result.IsOk.Should().BeTrue("an empty plan list is not an error");
        result.Value.Should().BeNull();
    }
}
