#nullable enable

using System.Collections.Generic;
using System.Linq;
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
/// `Csp1dMilpSolver` 必须把进度上下文透传到框架层，并让最终 MILP 拥有 75—99 带。
/// `Csp1dMilpSolver` must forward the progress context to the framework layer, giving the final MILP the 75—99 band.
/// </summary>
/// <remarks>
/// 加了透传但没有判据**：`Csp1dMilpSolver` 在 `tests/` 下**零覆盖**，而 CSP1D 的完整流程
/// 在 `Register` 就失败、走不到求解器。这批用一个**能真正建模成功的输入**在 MILP 层直接验证。
///
/// **判据靠带内值 vs 独立值区分**：框架层的求解回调本来报
/// - 注册：`overallProgress = 10 + 100/5 = 30`
/// - 求解：`gap = 0 → progress = 99`，`Milp` 档 → `30 + 99*7/10 = 99`
///
/// 套上 `Phase(75, 99)` 后变成 **99**（注册，阶段内 100）与 **98**（求解，阶段内 99）。
/// **两个数字都与未重映射的 30/99 不同**（注册那条恰好也落在 99，见下方判据说明）。
///
/// added the forwarding but no criterion**: `Csp1dMilpSolver` has **zero coverage** under `tests/`, and CSP1D's full flow fails at `Register` and never reaches the
/// solver. verifies directly at the MILP layer with an input that **actually registers**.
///
/// **The criterion discriminates banded vs standalone values**: the framework-layer callbacks natively report registration `10 + 100/5 = 30` and solving
/// `30 + 99*7/10 = 99`. Under `Phase(75, 99)` these become **99** (registration, in-stage 100) and **98** (solving, in-stage 99) — see the criterion note about the
/// accidental overlap at 99.
/// </remarks>
public class Csp1dMilpSolverProgressTests {
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

    /// <summary>**最终 MILP 的进度必须落在 75—99 带里** / The final MILP's progress must land in the 75—99 band.</summary>
    /// <remarks>
    /// 基线 把 `progressContext?.phase(75, 99)` 传给 `solveFinalMilp`。
    ///
    /// **判据用注册回调那条（阶段内 100 → 带内 99）**，理由是实测出来的：
    /// 求解回调的阶段内 99 在带内是 98，**但注册回调先上报、带内已到 99**，
    /// 于是 `AcceptMonotonic` 把后面的 98 **抬回 99**——**求解那条无法区分**。
    ///
    /// 注册那条**可区分**：未重映射时是 `10 + 100/5 = **30**`，带内是 **99**。
    ///
    /// **这是第三次撞上"单调化掩盖了判据想测的东西"**`、 的取消检查点、 的带下界）。
    /// 每次的表现都是"判据绿/红得莫名其妙"，每次都要回到"谁先把这个值改了"。
    ///
    /// The code's passes `progressContext?.phase(75, 99)` to `solveFinalMilp`.
    ///
    /// **The criterion uses the registration callback (in-stage 100 → banded 99)**, for an empirically found reason: the solving callback's in-stage 99 would band to 98,
    /// **but the registration callback reports first and already reaches 99 in the band**, so `AcceptMonotonic` **lifts the later 98 back to 99** — **the solving one cannot
    /// discriminate**.
    ///
    /// The registration one **can**: standalone it is `10 + 100/5 = **30**`, banded it is **99**.
    ///
    /// **This is the third time "monotonic clamping masked what the criterion wanted to test"** (the earlier `Normalized`, the earlier cancellation checkpoints, the earlier band
    /// lower bound). Every time it looks like "the criterion is inexplicably green/red", and every time the answer is "who changed this value first".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldReportTheFinalMilpProgressInsideItsBand() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        await solver.SolveAsync(input, isFinalMilp: true, progressContext: context.Phase(75, 99));

        reporter.Snapshots.Should().NotBeEmpty("the MILP path must report through the progress context");
        SolverProgressSnapshot registration = reporter.Snapshots.First(
            snapshot => snapshot.Stage.Id.Value == "REGISTRATION");
        registration.OverallProgress.Should().Be(99,
            "standalone this is 30; inside the 75—99 band it is 99");
    }

    /// <summary>**没有进度上下文时零上报** / With no progress context, nothing is reported.</summary>
    [Fact]
    public async Task SolveAsync_ShouldNotReportWithoutAProgressContext() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var reporter = new RecordingReporter();
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        await solver.SolveAsync(input, isFinalMilp: true);

        reporter.Snapshots.Should().BeEmpty();
    }

    /// <summary>**透传的参数确实到达了框架层** / The forwarded parameter really reaches the framework layer.</summary>
    /// <remarks>
    /// **这条与前两条不同**：它不看快照，而是看**框架层的回调被触发了**——
    /// 若 `Csp1dMilpSolver` 忘了把 `progressContext` 塞进 `FrameworkSolveOptions`，
    /// 框架层就不会包装回调，**快照一条都不会有**（前两条会红），但**回调本身仍会被调用**。
    /// 所以需要一条独立判据确认"求解真的跑过"，避免把"求解提前返回"误判成"进度没接"。
    ///
    /// **This differs from the other two**: it does not look at snapshots but at **the framework-layer callbacks firing** — if `Csp1dMilpSolver` forgot to put
    /// `progressContext` into `FrameworkSolveOptions`, the framework would not wrap the callbacks and there would be **no snapshots** (the other two would be red),
    /// yet **the callbacks themselves would still be invoked**. So an independent criterion confirms "the solve really ran", avoiding mistaking "the solve returned
    /// early" for "progress was not wired".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldReachTheSolverWithTheGivenInput() {
        ProduceInput<Flt64> input = MakeInput(out _);
        var solver = new Csp1dMilpSolver(new MockColumnGenerationSolver());

        Result<Csp1dMilpSolver.MilpResult<Flt64>?, ErrorCode, Error<ErrorCode>> result =
            await solver.SolveAsync(input, isFinalMilp: true);

        result.IsOk.Should().BeTrue("a valid input must reach and complete the solve");
        result.Value.Should().NotBeNull("the solve must return a result, not null, for a non-empty input");
    }
}
