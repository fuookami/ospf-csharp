// 物料库存审计的判据 / Criteria for the material inventory audit.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **同一时刻先合并再结算，区间两端必须出现在轨迹里，期初量也要判** / Simultaneous events net first, both horizon endpoints appear, and the opening stock is checked too.
/// </summary>
/// <remarks>
/// 三处都能给出"看着合理但错"的结果：逐事件判会报出不存在的缺料、只报有事件的时刻会让长时间零库存不可见、
/// 只看事件后库存会漏掉"一开始就不够"。
///
/// All three can produce a "plausible but wrong" answer: per-event checking reports a shortage that is not there, reporting only event instants hides long zero-stock stretches,
/// and looking only at post-event levels misses "it was never enough".
/// </remarks>
public class ActivityMaterialAuditTests {
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **同一时刻的消耗与到货按净额结算，不报缺料** / A consumption and a receipt at one instant settle to a net, and no shortage is reported.
    /// </summary>
    /// <remarks>
    /// 期初 `0`，同一时刻消耗 `5`、到货 `5`。**逐事件累加会经过 `−5`** 并报出 `stock:M`，
    /// 而按净额结算全程不低于 `0`，不该报任何违规。判据还**刻意把消耗放在数组前面**：
    /// 顺序反过来时，即便是逐事件实现也可能碰巧通过。
    ///
    /// Opening `0`, with a `5` consumption and a `5` receipt at the same instant. **Accumulating per event passes through `−5`** and reports `stock:M`,
    /// while netting never goes below `0` and should report nothing. The consumption is deliberately **first in the array**: with the reverse order even a per-event
    /// implementation might pass by luck.
    /// </remarks>
    [Fact]
    public void Levels_ShouldNetSimultaneousEvents() {
        ActivityMaterial<Flt64> material = Material("M", initial: 0.0);
        List<ActivityMaterialEvent<Flt64>> events = new() {
            Event("M", Start.AddHours(1), -5.0),
            Event("M", Start.AddHours(1), 5.0),
        };

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels =
            ActivityMaterialAudit.Levels(new[] { material }, events, Start, End);

        // 该时刻的库存必须是 -5 + 5 = 0：逐事件累加的实现会**先写到 -5 再写回 0**，
        // 于是轨迹里出现两个同一时刻的记录，其中一个为负。断言"恰好一条且为零"才能区分。
        // The stock at that instant must be -5 + 5 = 0: a per-event implementation **writes -5 first and then 0**, producing two records at the same instant, one negative.
        // Asserting "exactly one record and it is zero" is what tells the two apart.
        levels["M"].Should().HaveCount(3, "the two horizon endpoints plus one record for the single instant");
        levels["M"][1].Time.Should().Be(Start.AddHours(1));
        levels["M"][1].Quantity.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
        levels["M"].Should().NotContain(level => level.Quantity.Value.ToDouble() < 0.0,
            "no intermediate negative state may appear in the trajectory");

        ActivityMaterialAudit.Violations(new[] { material }, levels)
            .Should().BeEmpty("the net at that instant is zero, so there is no shortage");
    }

    /// **区间的两端都出现在轨迹里** / Both horizon endpoints appear in the trajectory.
    [Fact]
    public void Levels_ShouldIncludeBothHorizonEndpoints() {
        ActivityMaterial<Flt64> material = Material("M", initial: 10.0);

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels =
            ActivityMaterialAudit.Levels(
                new[] { material },
                new[] { Event("M", Start.AddHours(5), -1.0) },
                Start,
                End);

        levels["M"].Should().HaveCount(3, "two endpoints plus the one event instant");
        levels["M"][0].Time.Should().Be(Start);
        levels["M"][2].Time.Should().Be(End);
    }

    /// <summary>
    /// **长时间零库存必须可见** / A long zero-stock stretch must be visible.
    /// </summary>
    /// <remarks>
    /// 只有到货事件、且到货发生在区间很靠后时，**从区间起点到到货之间库存一直是零**。
    /// 若轨迹只包含有事件的时刻，这段就不在任何一条记录里，最低库存因此永远算不出来。
    ///
    /// With only a late receipt in the horizon, **stock is zero from the start until that receipt**. If the trajectory only carried event instants, that stretch would appear in
    /// no record at all and the minimum stock could never be derived.
    /// </remarks>
    [Fact]
    public void Violations_ShouldReportTheMinimumReachedAtTheHorizonStart() {
        ActivityMaterial<Flt64> material = Material("M", initial: 0.0, minimum: 0.0);
        ActivityMaterialEvent<Flt64>[] events = new[] { Event("M", Start.AddHours(9), 100.0) };

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels =
            ActivityMaterialAudit.Levels(new[] { material }, events, Start, End);

        // 期初恰好 0 = 下限，不算违规；这里断言的是"起点这一点确实在轨迹里被算过"。
        // Opening exactly 0 equals the minimum and is not a violation; what is asserted is that the start point really is part of the trajectory.
        levels["M"][0].Time.Should().Be(Start);
        levels["M"][0].Quantity.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
        ActivityMaterialAudit.Violations(new[] { material }, levels).Should().BeEmpty();

        ActivityMaterial<Flt64> strict = Material("M", initial: 0.0, minimum: 1.0);
        ActivityMaterialAudit.Violations(new[] { strict }, levels)
            .Should().Contain(v => v.StartsWith("stock_opening:M", StringComparison.Ordinal),
                "the opening stock itself is below the minimum");
    }

    /// **期初量本身也要判** / The opening stock is checked on its own.
    [Fact]
    public void Violations_ShouldCheckTheOpeningStockEvenWhenALaterReceiptFixesIt() {
        ActivityMaterial<Flt64> material = Material("M", initial: 0.0, minimum: 1.0);
        ActivityMaterialEvent<Flt64>[] events = new[] { Event("M", Start.AddHours(1), 100.0) };

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels =
            ActivityMaterialAudit.Levels(new[] { material }, events, Start, End);

        ActivityMaterialAudit.Violations(new[] { material }, levels)
            .Should().Contain("stock_opening:M", "checking only post-event levels would miss that it was never enough");
    }

    /// <summary>
    /// **`NaN` 库存必须被报出，不能被静默放过** / A `NaN` stock must be reported rather than silently passing.
    /// </summary>
    /// <remarks>
    /// `NaN` 与任何上下界比较都为假，因此 `stock < min || stock > max` 形式的检查会**静默通过**。
    /// 这条判据的存在就是为了证明实现里有显式的有限性判断。
    ///
    /// `NaN` compares false against every bound, so a check shaped `stock < min || stock > max` **silently passes**. This criterion exists to prove the implementation tests
    /// finiteness explicitly.
    /// </remarks>
    [Fact]
    public void Violations_ShouldReportNonFiniteStock() {
        ActivityMaterial<Flt64> material = Material("M", initial: 10.0, minimum: 0.0, maximum: 100.0);
        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels = new(StringComparer.Ordinal) {
            ["M"] = new List<ActivityInventoryLevel<Flt64>> {
                new(Start, Amount(double.NaN)),
            },
        };

        ActivityMaterialAudit.Violations(new[] { material }, levels)
            .Should().Contain(v => v.StartsWith("stock:M", StringComparison.Ordinal));
    }

    /// **上限同样生效** / The upper bound is enforced as well.
    [Fact]
    public void Violations_ShouldEnforceTheMaximum() {
        ActivityMaterial<Flt64> material = Material("M", initial: 10.0, minimum: 0.0, maximum: 20.0);
        ActivityMaterialEvent<Flt64>[] events = new[] { Event("M", Start.AddHours(1), 100.0) };

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels =
            ActivityMaterialAudit.Levels(new[] { material }, events, Start, End);

        ActivityMaterialAudit.Violations(new[] { material }, levels)
            .Should().Contain(v => v.Contains(":M:", StringComparison.Ordinal), "stock overflows the storage limit");
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="id">身份 / The identity.</param>
    /// <param name="initial">期初 / The opening stock.</param>
    /// <param name="minimum">下限 / The lower bound.</param>
    /// <param name="maximum">上限 / The upper bound.</param>
    /// <returns>物料 / The material.</returns>
    private static ActivityMaterial<Flt64> Material(string id, double initial, double? minimum = null, double? maximum = null) =>
        new(id,
            Amount(initial),
            minimum is null ? null : Amount(minimum.Value),
            maximum is null ? null : Amount(maximum.Value));

    /// <summary>构造物料事件 / Build a material event.</summary>
    /// <param name="material">物料 / The material.</param>
    /// <param name="time">时间 / The time.</param>
    /// <param name="quantity">有符号变化量 / The signed change.</param>
    /// <returns>事件 / The event.</returns>
    private static ActivityMaterialEvent<Flt64> Event(string material, DateTimeOffset time, double quantity) =>
        new(material, time, Amount(quantity));

    /// <summary>构造以千克为单位的量 / Build a quantity in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>量 / The quantity.</returns>
    private static Quantity<Flt64> Amount(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
