#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 定价**产出新列**时，流程必须真的加列、方案池增长，然后才收敛。
/// When pricing **produces new columns**, the flow must actually add them, grow the pool, and only then converge.
/// </summary>
/// <remarks>
/// 记录了一条覆盖盲区**：默认 fixture 的定价产不出新列，于是
/// `addedPlans.Count > 0` → `AddColumns` → 方案池增长这条路径**完全没有判据**。
/// 那一批的变异"收尾方案数改用陈旧初始值"**存活**，正是因为 `initialCount == finalCount`。
///
/// 这批用一个**可控的定价替身**（第一轮给一条新列，之后不给）把这条路径打通。
///
/// **这是"替身比真实实现更好用"的一处**：真实定价依赖对偶价格驱动，难以稳定构造；
/// 而加列逻辑本身与"列从哪来"无关——**替身正好隔离了这两件事**。
///
/// **recorded a coverage gap**: the default fixture's pricing produces no new columns, so the path
/// `addedPlans.Count > 0` → `AddColumns` → pool growth **had no criterion at all**. That batch's mutation "closing count uses the stale initial value" **survived**,
/// precisely because `initialCount == finalCount`.
///
/// opens that path with a **controllable pricing double** (one new column on the first call, none afterwards).
///
/// **This is a case where a double beats the real implementation**: real pricing is driven by dual prices and is hard to construct stably, while the column-adding
/// logic itself is independent of "where the column came from" — **the double separates exactly those two concerns**.
/// </remarks>
public class Csp1dColumnAddingFlowTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Csp1dProblem<Flt64> MakeProblem() {
        Material<Flt64> material = MakeMaterial();
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

    /// <summary>第一轮给一条新列、之后不给的定价替身 / A pricing double yielding one new column, then none.</summary>
    private sealed class OneShotPricingGenerator : ICsp1dPricingGenerator<CuttingPlan<Flt64>, Csp1dPricingInput<Flt64>> {
        private readonly Material<Flt64> _material;

        public OneShotPricingGenerator(Material<Flt64> material) => _material = material;

        public int Calls { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(Csp1dPricingInput<Flt64> input) {
            ++Calls;
            if (Calls > 1) {
                return new List<CuttingPlan<Flt64>>();
            }
            var product = new Product<Flt64>(
                id: "A",
                name: "product-a",
                width: new List<Quantity<Flt64>> { Millimetres(50.0) });
            return new List<CuttingPlan<Flt64>> {
                new(
                    id: "priced-1",
                    material: _material,
                    slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) }),
            };
        }
    }

    /// <summary>**定价产出新列时方案池必须增长** / The pool must grow when pricing yields a new column.</summary>
    /// <remarks>
    /// 这是 那条存活变异针对的分支。断言**第一轮 `PlanCountAfter > PlanCountBefore`**——
    /// 若加列被跳过（例如 `AddColumns` 的结果被丢弃），`After == Before`，判据红。
    ///
    /// This is the branch the earlier surviving mutation targeted. It asserts **iteration one has `PlanCountAfter > PlanCountBefore`** — if adding were skipped (say
    /// `AddColumns`' result dropped), `After == Before` and the criterion would be red.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldGrowThePlanPoolWhenPricingYieldsAColumn() {
        Material<Flt64> material = MakeMaterial();
        var pricing = new OneShotPricingGenerator(material);
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: pricing);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(MakeProblem());

        pricing.Calls.Should().BeGreaterThan(1, "the loop must run again after a column was added");
        Csp1dIterationRecord first = result.Trace.Iterations[0];
        first.PlanCountAfter.ToLong().Should().BeGreaterThan(
            first.PlanCountBefore.ToLong(),
            "a priced column must actually enter the pool");
        first.PricedPlanCount.ToULong().Should().BePositive();
    }

    /// <summary>**加列之后必须继续迭代，直到定价无新列** / After adding, iterate until pricing yields nothing.</summary>
    /// <remarks>
    /// 这条钉"**加了列就继续**"：若实现加完列就退出，`Iterations.Count` 会是 1 而 `Calls` 也是 1。
    /// 断言 **`Calls == 2` 且 `Iterations.Count >= 2`**，两者一起才能说明"第二轮确实因为没新列而收敛"。
    ///
    /// This pins "**added a column so continue**": if the implementation exited right after adding, `Iterations.Count` would be 1 and so would `Calls`. Asserting
    /// **`Calls == 2` and `Iterations.Count >= 2`** together is what shows "the second round genuinely converged because nothing was new".
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldIterateAgainAfterAddingAColumn() {
        Material<Flt64> material = MakeMaterial();
        var pricing = new OneShotPricingGenerator(material);
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: pricing);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(MakeProblem());

        pricing.Calls.Should().Be(2);
        result.Trace.Iterations.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Trace.TerminationReason.Should().BeOneOf(
            Csp1dTerminationReason.AllDuplicates,
            Csp1dTerminationReason.PricingConverged);
    }

    /// <summary>**收尾方案数必须是加列后的真实值，不是初始值** / The closing count must be the post-column value, not the initial one.</summary>
    /// <remarks>
    /// **这正是 存活的那条变异**：把收尾计数换成 `initialCount` 时，上一个 fixture 里两者相等，
    /// 判据看不出来。这里两者**必然不等**（差一条新列），所以能区分。
    ///
    /// **This is exactly the earlier surviving mutation**: swapping the closing count for `initialCount` was invisible in the previous fixture because the two were equal.
    /// Here they **necessarily differ** (by one added column), so it becomes detectable.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldReportThePostColumnFinalCount() {
        Material<Flt64> material = MakeMaterial();
        var pricing = new OneShotPricingGenerator(material);
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: pricing);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(MakeProblem());

        result.Trace.FinalPlanCount.ToULong().Should().BeGreaterThan(
            result.Trace.InitialPlanCount.ToULong(),
            "pricing added a column, so the final count must exceed the initial count");
        result.Trace.FinalPlanCount.ToULong().Should().Be(
            (ulong)result.Trace.Iterations[^1].PlanCountAfter.ToLong());
    }
}
