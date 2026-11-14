// 库存累积行的判据 / Criteria for cumulative stock rows.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **每个时点的行只含此前的事件，未来到货不能补过去的缺料** / Each instant's row holds only earlier events; future receipts cannot cover past shortages.
/// </summary>
/// <remarks>
/// 若把整条时间轴的事件都放进同一个行系数，一个十天后才到货的批次会立刻让今天的库存看起来充足，
/// 于是**排程允许在前九天持续缺料**，而模型报告一切可行。缺料的那九天在解里看着完全正常，只是那一天真的没有料。
///
/// If the whole time axis' events went into one row's coefficients, a batch arriving in ten days would immediately make today's stock look sufficient, letting **the schedule run
/// short for nine days** while the model reports everything feasible. Those nine days look perfectly normal in the solution; there simply was no material on them.
/// </remarks>
public class ActivityMaterialRowsTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **未来的到货不进入更早的行** / A future receipt does not enter an earlier row.
    /// </summary>
    /// <remarks>
    /// 第 1 小时消耗 `5`（候选 0），第 9 小时到货 `5`。第 1 小时那一行的系数必须是 `-5`，
    /// **不能是 `0`**——若把到货也提前计入，这一行就变成零，缺料被完全抹平。
    ///
    /// Hour 1 consumes `5` (candidate 0) and hour 9 receives `5`. The hour-1 row's coefficient must be `-5`, **not `0`** — counting the receipt early makes that row zero and
    /// erases the shortage entirely.
    /// </remarks>
    [Fact]
    public void Rows_ShouldNotLetFutureSupplyCoverAPastShortage() {
        var events = new List<ActivityMaterialRows.Event> {
            new(Choice: 0, Order: 0, Time: Origin.AddHours(1), Quantity: new Flt64(-5.0), Material: "M"),
            new(Choice: -1, Order: 1, Time: Origin.AddHours(9), Quantity: new Flt64(5.0), Material: "M"),
        };

        IReadOnlyList<ActivityMaterialRows.RowSnapshot> rows = ActivityMaterialRows.Rows(
            events, initial: new Flt64(0.0), minimum: new Flt64(0.0), maximum: null,
            HorizonFrom(), Origin.AddHours(10));

        // 时点：起点 0、1、9、10。第 1 小时对应下标 1。
        // Instants: start 0, 1, 9, 10. Hour 1 is index 1.
        rows.Should().HaveCount(4);
        rows[1].Coefficients.Should().ContainKey(0);
        rows[1].Coefficients[0].ToDouble().Should().BeApproximately(-5.0, 1e-12,
            "the receipt nine hours later must not be visible here");
        rows[1].Lower!.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// **固定事件进界、不进系数** / Fixed events go into the bounds, not the coefficients.
    [Fact]
    public void Rows_ShouldPutFixedEventsIntoTheBounds() {
        var events = new List<ActivityMaterialRows.Event> {
            new(Choice: -1, Order: 0, Time: Origin.AddHours(2), Quantity: new Flt64(10.0), Material: "M"),
        };

        IReadOnlyList<ActivityMaterialRows.RowSnapshot> rows = ActivityMaterialRows.Rows(
            events, initial: new Flt64(5.0), minimum: new Flt64(3.0), maximum: new Flt64(20.0),
            HorizonFrom(), Origin.AddHours(3));

        rows.Should().HaveCount(3, "two horizon endpoints plus the event instant");
        rows[0].Coefficients.Should().BeEmpty();
        rows[0].Lower!.Value.ToDouble().Should().BeApproximately(-2.0, 1e-12, "3 - 5");
        rows[2].Lower!.Value.ToDouble().Should().BeApproximately(-12.0, 1e-12, "3 - (5 + 10)");
        rows[2].Upper!.Value.ToDouble().Should().BeApproximately(5.0, 1e-12, "20 - (5 + 10)");
    }

    /// <summary>
    /// **库存归零后系数被移除，不留下零** / A coefficient is removed once its accumulation reaches zero.
    /// </summary>
    /// <remarks>
    /// 候选 0 在第 1 小时 `-5`、第 2 小时 `+5`，累计为零。第 2 小时之后那一行不该再提到候选 0，
    /// 否则每一列都会连到每一行——**数值上不改变任何东西，只在规模上表现出来**。
    ///
    /// Candidate 0 consumes `5` at hour 1 and receives `5` at hour 2, accumulating to zero. Rows after hour 2 must not mention candidate 0 again; otherwise every column connects
    /// to every row — **changing nothing numerically, showing up only in scale**.
    /// </remarks>
    [Fact]
    public void Rows_ShouldRemoveCoefficientsThatAccumulateToZero() {
        var events = new List<ActivityMaterialRows.Event> {
            new(Choice: 0, Order: 0, Time: Origin.AddHours(1), Quantity: new Flt64(-5.0), Material: "M"),
            new(Choice: 0, Order: 1, Time: Origin.AddHours(2), Quantity: new Flt64(5.0), Material: "M"),
        };

        IReadOnlyList<ActivityMaterialRows.RowSnapshot> rows = ActivityMaterialRows.Rows(
            events, initial: new Flt64(0.0), minimum: new Flt64(0.0), maximum: null,
            HorizonFrom(), Origin.AddHours(3));

        rows[2].Coefficients.Should().NotContainKey(0);
        rows.Should().NotContain(row => row.Coefficients.ContainsKey(0) && row.Coefficients[0].ToDouble() == 0.0);
    }

    /// **区间两端始终出现在时点表里** / Both horizon endpoints always appear.
    [Fact]
    public void Rows_ShouldAlwaysIncludeBothHorizonEndpoints() {
        IReadOnlyList<ActivityMaterialRows.RowSnapshot> rows = ActivityMaterialRows.Rows(
            new List<ActivityMaterialRows.Event>(),
            initial: new Flt64(7.0), minimum: new Flt64(0.0), maximum: null,
            HorizonFrom(), Origin.AddHours(10));

        rows.Should().HaveCount(2);
        rows[0].Lower!.Value.ToDouble().Should().BeApproximately(-7.0, 1e-12);
        rows[1].Lower!.Value.ToDouble().Should().BeApproximately(-7.0, 1e-12);
    }

    /// **没有上限时上界为空** / Without a maximum the upper bound is null.
    [Fact]
    public void Rows_ShouldLeaveTheUpperBoundNullWithoutAMaximum() {
        IReadOnlyList<ActivityMaterialRows.RowSnapshot> rows = ActivityMaterialRows.Rows(
            new List<ActivityMaterialRows.Event>(),
            initial: new Flt64(7.0), minimum: new Flt64(0.0), maximum: null,
            HorizonFrom(), Origin.AddHours(10));

        rows.Should().OnlyContain(row => row.Upper == null);
    }

    /// <summary>构造优化区间起点 / Build the horizon start.</summary>
    /// <returns>起点 / The start.</returns>
    private static DateTimeOffset HorizonFrom() => Origin;
}
