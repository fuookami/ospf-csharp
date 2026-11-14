#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 列生成**流程本身**必须自洽：终止原因、定价结果、方案数变化三者要对得上。
/// The column-generation **flow itself** must be coherent: termination reason, pricing outcome and plan-count change must agree.
/// </summary>
/// <remarks>
/// **这是 CSP1D 列生成第一组流程判据。** 此前只有"接线了"（B322）、"可取消"（B323）、
/// "进度上报了"（B329—B335）——**没有一条验证流程算得对不对**。
///
/// 判据的设计原则：**不钉具体数字，钉三者之间的一致关系**。
/// 钉死 `Iterations.Count == 1` 会让判据在 fixture 变化时碎，而且**说明不了流程对不对**；
/// 钉"`AllDuplicates` ⇔ 定价 0 条 ⇔ 方案数不变"才能表达"收敛判据是靠什么下的"。
///
/// **This is CSP1D column generation's first set of flow criteria.** Before this there was only "it is wired" (B322), "it can be cancelled" (B323) and "it reports
/// progress" (B329—B335) — **not one that checked whether the flow computes the right thing**.
///
/// The design rule: **pin the consistency between the three, not specific numbers**. Pinning `Iterations.Count == 1` would break with the fixture and **would not
/// show whether the flow is right**; pinning "`AllDuplicates` ⇔ 0 priced plans ⇔ unchanged plan count" expresses **what the convergence decision rests on**.
/// </remarks>
public class Csp1dColumnGenerationFlowTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Csp1dProblem<Flt64> MakeProblem() {
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
        return builder.Build();
    }

    private static Csp1dColumnGeneration<Flt64> MakeColumnGeneration() =>
        new(new MockColumnGenerationSolver());

    /// <summary>**流程必须真的产生初始方案并收敛** / The flow must produce initial plans and converge.</summary>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldProduceInitialPlansAndTerminate() {
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await MakeColumnGeneration().SolveWithTraceAsync(MakeProblem());

        result.Trace.InitialPlanCount.ToULong().Should().BePositive("initial column generation must yield plans");
        result.Trace.Iterations.Should().NotBeEmpty("the master/pricing loop must run at least once");
    }

    /// <summary>**终止原因必须与定价结果一致** / The termination reason must match the pricing outcome.</summary>
    /// <remarks>
    /// 这是流程里最容易"看起来对"的地方：终止原因是**由定价结果推出来的**，
    /// 若两者不一致（比如定价了 0 条却说 `IterationLimit`），说明收敛判据接错了线。
    ///
    /// **判据对每个终止原因检查它应当对应的事实**，而不是只查一个。
    ///
    /// This is where a flow most easily "looks right": the termination reason is **derived from** the pricing outcome, and if the two disagree (say 0 plans priced
    /// yet `IterationLimit` reported), the convergence decision is wired wrong.
    ///
    /// **The criterion checks the fact each reason implies**, rather than only one of them.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldKeepTheTerminationReasonConsistentWithPricing() {
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await MakeColumnGeneration().SolveWithTraceAsync(MakeProblem());
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationTrace trace = result.Trace;
        Csp1dIterationRecord last = trace.Iterations[^1];

        switch (trace.TerminationReason) {
            case Csp1dTerminationReason.AllDuplicates:
                last.PricedPlanCount.ToULong().Should().Be(0UL,
                    "AllDuplicates means every priced plan was already present, so none were added");
                last.PlanCountAfter.ToLong().Should().Be(last.PlanCountBefore.ToLong(),
                    "with no new plans the pool cannot grow");
                break;
            case Csp1dTerminationReason.PricingConverged:
                last.PricedPlanCount.ToULong().Should().Be(0UL,
                    "PricingConverged means pricing produced nothing new");
                break;
            default:
                // 其它原因（迭代上限、LP 失败等）本判据不假设其定价结果，只要求流程有记录。
                // Other reasons (iteration limit, LP failure) imply no particular pricing outcome; only require a record.
                trace.Iterations.Should().NotBeEmpty();
                break;
        }
    }

    /// <summary>**方案池在迭代中不得缩小** / The plan pool must never shrink across iterations.</summary>
    /// <remarks>
    /// 列生成只加列不减列。若某轮 `PlanCountAfter < PlanCountBefore`，说明**方案池被误重建**——
    /// 而下游目标值仍然"正常"，**不会被别的判据发现**。
    ///
    /// Column generation only adds columns. If some iteration has `PlanCountAfter < PlanCountBefore`, the pool was **mistakenly rebuilt** — while downstream objective
    /// values still look **normal**, so **no other criterion would notice**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNeverShrinkThePlanPool() {
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await MakeColumnGeneration().SolveWithTraceAsync(MakeProblem());

        foreach (Csp1dIterationRecord record in result.Trace.Iterations) {
            record.PlanCountAfter.ToLong().Should().BeGreaterThanOrEqualTo(
                record.PlanCountBefore.ToLong(),
                "iteration {0} shrank the pool", record.Iteration.ToLong());
        }
    }

    /// <summary>**最终方案数必须与最后一轮一致，且最终 MILP 必须给出解** / The final count must match the last iteration, and the final MILP must solve.</summary>
    /// <remarks>
    /// 这三个数**由三个不同环节各自产生**：`FinalPlanCount` 来自收尾统计，`PlanCountAfter` 来自迭代记录，
    /// `FinalMilpStatus` 来自最终 MILP。**它们对不上，说明某个环节用了旧的方案池。**
    ///
    /// The three numbers **come from three different stages**: `FinalPlanCount` from the closing statistics, `PlanCountAfter` from the iteration record, and
    /// `FinalMilpStatus` from the final MILP. **If they disagree, some stage used a stale plan pool.**
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldAgreeOnTheFinalPlanCountAndMilpStatus() {
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await MakeColumnGeneration().SolveWithTraceAsync(MakeProblem());
        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationTrace trace = result.Trace;

        trace.Iterations.Should().NotBeEmpty();
        trace.FinalPlanCount.ToULong().Should().Be(
            (ulong)trace.Iterations[^1].PlanCountAfter.ToLong(),
            "the closing count must equal the last iteration's pool");
        trace.FinalMilpStatus.Should().Be(Csp1dFinalMilpStatus.Solved);
    }
}
