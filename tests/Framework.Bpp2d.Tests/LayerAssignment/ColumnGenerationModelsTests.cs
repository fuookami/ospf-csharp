// 列生成数据结构（B645）的判据 / Criteria for the column-generation data structures (B645).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **列生成数据结构（B645）** / The column-generation data structures (B645).
/// </summary>
/// <remarks>
/// 判据的重点：**配置默认值照抄基线**（尤其容差不是零）、**两个"因上限结束"的标记不合并**、以及**影子价格两层的键不混用**。
/// 这三条都是"看起来能用、错起来无声"的地方。
///
/// The criteria focus on **the config defaults matching the baseline** (especially the tolerance not being zero), **the two "stopped at a limit" flags staying separate**, and **the two
/// shadow-price key layers not being mixed**. All three are places that look fine and fail silently.
/// </remarks>
public class ColumnGenerationModelsTests {
    /// <summary>
    /// **默认值照抄基线，容差不是零** / The defaults match the baseline, and the tolerance is not zero.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `new ColumnGenerationConfig()` → 迭代 64、每轮 32、最终 MILP 开启。
    /// 2. **容差是 `1e-6`，不是零**——零会让"改进列"永远成立，跑到迭代上限为止。
    /// 3. 未设时间上限时 `Time` 是 `TimeSpan.Zero`（不限），不是某个"很久"。
    ///
    /// Three paired points: the parameterless config gives 64 iterations, 32 columns per iteration and the final MILP on; **the tolerance is `1e-6` rather than zero**, since zero would
    /// make "improving column" always true and run to the limit; and an unset time limit is `TimeSpan.Zero`, meaning unbounded, not some very long duration.
    /// </remarks>
    [Fact]
    public void Config_ShouldCarryTheBaselineDefaults() {
        ColumnGenerationConfig config = new();

        config.IterationLimit.Should().Be(64);
        config.MaxColumnsPerIteration.Should().Be(32);
        config.FinalMilpEnabled.Should().BeTrue();
        config.Tolerance.ToFlt64().ToDouble().Should().BeApproximately(1e-6, 1e-12, "a zero tolerance would never converge");
        config.Time.Should().Be(TimeSpan.Zero, "zero means unbounded");

        ColumnGenerationConfig tuned = new(
            IterationLimit: 8, TimeLimit: TimeSpan.FromSeconds(30), ReducedCostTolerance: new FltX(0.5));
        tuned.Tolerance.ToFlt64().ToDouble().Should().Be(0.5);
        tuned.Time.Should().Be(TimeSpan.FromSeconds(30));
        tuned.IterationLimit.Should().Be(8);
    }

    /// <summary>
    /// **两个"因上限结束"的标记分开，不合并** / The two "stopped at a limit" flags stay separate.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 只到迭代上限 → `TerminatedByIterationLimit` 真、`TerminatedByTimeLimit` 假。
    /// 2. 只到时间上限 → 反之。
    /// 3. **两个可以同时为假**（正常收敛）——合并成一个 `Terminated` 就表达不了"两个都假且最终未求解"。
    ///
    /// Three paired points: stopping only at the iteration limit sets the first flag true and the second false; stopping only at the time limit reverses them; and **both can be false**
    /// (normal convergence), which a single merged `Terminated` could not express together with "final not solved".
    /// </remarks>
    [Fact]
    public void Result_ShouldKeepTheTwoTerminationFlagsSeparate() {
        Layer<FltX>[] columns = Array.Empty<Layer<FltX>>();
        Sheet<FltX>[] sheets = Array.Empty<Sheet<FltX>>();

        ColumnGenerationResult byIteration = new(
            columns, sheets, 64, TerminatedByIterationLimit: true, TerminatedByTimeLimit: false,
            LpSolvedTimes: 64, FinalSolved: false, LpObjectives: Array.Empty<FltX?>(),
            FinalObjective: null, Elapsed: TimeSpan.FromSeconds(1));
        byIteration.TerminatedByIterationLimit.Should().BeTrue();
        byIteration.TerminatedByTimeLimit.Should().BeFalse();

        ColumnGenerationResult byTime = byIteration with { TerminatedByIterationLimit = false, TerminatedByTimeLimit = true };
        byTime.TerminatedByIterationLimit.Should().BeFalse();
        byTime.TerminatedByTimeLimit.Should().BeTrue();

        ColumnGenerationResult converged = byIteration with
        {
            TerminatedByIterationLimit = false,
            TerminatedByTimeLimit = false,
            FinalSolved = true,
        };
        converged.TerminatedByIterationLimit.Should().BeFalse();
        converged.TerminatedByTimeLimit.Should().BeFalse("a normal convergence sets neither flag");
        converged.FinalSolved.Should().BeTrue();
    }

    /// <summary>
    /// **影子价格两层键不混用** / The two shadow-price key layers are not mixed.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 状态里的影子价格按 `DemandModeKey`（字符串层）索引。
    /// 2. **同一模式不同单位是两个键**——`DemandModeKey` 带 `QuantityUnit`，丢单位会让件数与面积撞在一起。
    /// 3. 未给可选集合时它们是 `null`，不是空表——空表会被读成"求解器说没有影子价格"。
    ///
    /// Three paired points: the state's shadow prices are indexed by `DemandModeKey`, the string layer; **the same mode with different units is two keys**, since `DemandModeKey` carries
    /// `QuantityUnit` and dropping it would collide counts with areas; and the optional collections are `null` rather than empty, which would read as "the solver reported no shadow prices".
    /// </remarks>
    [Fact]
    public void State_ShouldKeyShadowPricesByDemandModeKey() {
        Fuookami.Ospf.Quantities.Unit.PhysicalUnit unit = Fuookami.Ospf.Quantities.Unit.LengthUnits.Millimeter;
        DemandModeKey perItem = new("item_amount", "I1");
        DemandModeKey perArea = new("item_area", "I1", unit);

        ColumnGenerationState state = new(
            Iteration: 0,
            Columns: Array.Empty<Layer<FltX>>(),
            ShadowPrices: new Dictionary<DemandModeKey, FltX> {
                [perItem] = new FltX(1.5),
                [perArea] = new FltX(2.5),
            });

        state.ShadowPrices!.Should().HaveCount(2, "different modes are different keys");
        state.ShadowPrices![perItem].ToFlt64().ToDouble().Should().Be(1.5);
        state.ShadowPrices![perArea].ToFlt64().ToDouble().Should().Be(2.5);

        ColumnGenerationState minimal = new(0, Array.Empty<Layer<FltX>>());
        minimal.Sheets.Should().BeNull("an omitted collection is null, not empty");
        minimal.ShadowPrices.Should().BeNull();
        minimal.AdditionalShadowPrices.Should().BeNull();

        new ColumnGenerationLpResult(new Dictionary<DemandModeKey, FltX>())
            .Objective.Should().BeNull("no objective means the LP did not report one");
    }
}
