// 列生成入口判据 / Criteria for the BPP1D column-generation entry.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Application;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **一维列生成入口** / The BPP1D column-generation entry.
/// </summary>
/// <remarks>
/// 对应的 `Bpp1dColumnGeneration`。
/// **这里的判据全部需要真实求解器**：LP 定价循环的对偶解只能由求解器给出，
/// 桩求解器能编造一个对偶，但那样测的是"桩对不对"，不是"定价循环对不对"。
///
/// **代价是默认全量测试覆盖不到这条路径**，这一弱点已写进计划文档。
///
/// Mirrors `Bpp1dColumnGeneration`. **Every criterion here needs a real solver**: the duals that drive pricing can only come from one, and a stub's fabricated duals
/// would test the stub rather than the loop. **The cost is that the default full suite does not cover this path** — recorded as a weakness in the plan document.
/// </remarks>
public class Bpp1dColumnGenerationTests {
#if SCIP_INTEGRATION
    /// <summary>**预算非法时在建模型之前就被拒** / An invalid budget is rejected before any model is built.</summary>
    /// <remarks>
    /// 预算为 `0` 时循环**一轮都不跑**，求解器静默退化成普通 MILP——调用方看到的只是"结果差一点"。
    /// 判据同时确认**没有调用求解器**：`Calls` 无法观察，改为断言返回的第一时间就失败。
    ///
    /// A zero budget would make the loop **run no iterations at all**, silently degrading to a plain MILP while the caller only sees "a slightly worse answer".
    ///
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRejectAnInvalidBudget() {
        PackingProblem<Flt64> problem = MakeProblem();
        Bpp1dColumnGeneration<Flt64> entry = MakeEntry();
        var config = new Bpp1dSolveConfig<Flt64>(IterationLimit: 0);

        Result<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await entry.SolveAsync(problem, config);

        result.IsFailed.Should().BeTrue("a zero iteration budget is invalid");
        ((Failed<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**每轮列数预算非法时同样被拒** / A zero per-iteration column budget is rejected too.</summary>
    [Fact]
    public async Task SolveAsync_ShouldRejectAnInvalidColumnBudget() {
        PackingProblem<Flt64> problem = MakeProblem();
        Bpp1dColumnGeneration<Flt64> entry = MakeEntry();
        var config = new Bpp1dSolveConfig<Flt64>(ColumnsPerIteration: 0);

        Result<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await entry.SolveAsync(problem, config);

        result.IsFailed.Should().BeTrue("a zero per-iteration column budget is invalid");
        ((Failed<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**真实 SCIP 跑通定价循环并得到一箱** / Real SCIP runs the pricing loop and finds one bin.</summary>
    /// <remarks>
    /// 需求 4 件、每件 100 mm、箱长 500 mm ⇒ 每箱 5 件 ⇒ **一箱足够**。
    /// 判据钉三件事：目标 **1**、**至少跑过一轮定价**（`PricingIterations > 0`）、
    /// 最终模型用的是"已生成池 + 种子"（`ColumnCount` **大于**种子数说明确实加了列，或至少非空）。
    ///
    /// **`PricingIterations` 是最要紧的那条**：它是"LP 定价循环真的跑了"的唯一外证。
    /// 少了它，一个**从不跑循环**的实现（直接走 `Bpp1dMilp`）也能给出目标 1 而判据全绿。
    ///
    /// Demand 4 pieces at 100 mm in a 500 mm bin (5 per bin) ⇒ **one bin suffices**. The criterion pins three things: objective **1**, **at least one pricing round run**
    /// (`PricingIterations > 0`), and a final model built on the generated pool plus seeds. **`PricingIterations` is the most important of the three**: it is the only
    /// external evidence that the loop actually ran. Without it, an implementation that **never runs the loop** (delegating straight to `Bpp1dMilp`) would still report
    /// objective 1 with every criterion green.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRunThePricingLoopWithRealScip() {
        PackingProblem<Flt64> problem = MakeProblem();
        Bpp1dColumnGeneration<Flt64> entry = MakeEntry();

        Result<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>> result = await entry.SolveAsync(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        Bpp1dSolution<Flt64> solution = ((Ok<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        solution.Objective.ToDouble().Should().BeApproximately(
            1.0, 1e-9, "four pieces at 100 mm fit one 500 mm bin and the objective counts bins");
        solution.PricingIterations.Should().BeGreaterThan(
            0, "next to nothing else proves the LP pricing loop actually ran");
        solution.Packing.Plans.Should().NotBeEmpty();
        solution.ColumnCount.Should().BeGreaterThan(0);
    }
#endif

    /// <summary>构造入口 / Build the entry.</summary>
    /// <returns>入口 / The entry.</returns>
    private static Bpp1dColumnGeneration<Flt64> MakeEntry() =>
        new(new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver(),
            Flt64PackingValueAdapter.Instance);

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem() =>
        new(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), 4UL) },
            // **箱长 500 mm、每件 100 mm ⇒ 一箱装 5 件**，需求 4 件时一箱够。
            // **A 500 mm bin holds 5 pieces of 100 mm**, so one bin serves 4 pieces.
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(500.0), Kilograms(10.0), Flt64.One, null),
            },
            Objective: PackingObjective.BinCount,
            Spacing: Millimetres(0.0));

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
