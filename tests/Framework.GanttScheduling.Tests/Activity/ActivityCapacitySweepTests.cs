// 资源占用扫描的判据 / Criteria for the resource occupancy sweep.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **零系数必须从系数表移除；区间起点的在途占用必须进初值** / Zero coefficients must leave the table, and in-progress occupancy at the horizon start must enter the initial state.
/// </summary>
/// <remarks>
/// 留下零系数会让每一列出现在每一行里，把行结构变成全连接——而它真正的稀疏结构是求解器性能的全部来源。
/// 这种膨胀**不在数值上改变任何东西**，只在规模上表现。
///
/// 丢掉起点的在途占用会让"排程一开始就占着资源"这件事消失，容量被高估。
///
/// Leaving zeros puts every column into every row and makes the row structure fully connected, while its genuine sparsity is the entire source of solver performance. The
/// inflation **changes nothing numerically** and only shows up in scale. Dropping the in-progress occupancy at the start loses "the resource was already occupied when
/// scheduling began" and overstates capacity.
/// </remarks>
public class ActivityCapacitySweepTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **相邻区间的占用正确推进** / Occupancy advances correctly across adjacent intervals.
    [Fact]
    public void Sweep_ShouldAdvanceOccupancyAcrossIntervals() {
        var reservations = new List<ActivityCapacitySweep.Reservation> {
            new(Choice: 0, Order: 0, Time: Range(1, 3), Quantity: new Flt64(2.0), Resource: "R"),
        };

        IReadOnlyList<ActivityCapacitySweep.RowSnapshot> snapshots =
            ActivityCapacitySweep.Sweep(reservations, Origin, Origin.AddHours(5), _ => new Flt64(10.0));

        snapshots.Should().ContainSingle("the endpoints cut the horizon into exactly one interval where the reservation is active");
        snapshots[0].Coefficients[0].ToDouble().Should().BeApproximately(2.0, 1e-12);
        snapshots[0].Upper.ToDouble().Should().BeApproximately(10.0, 1e-12);
    }

    /// <summary>
    /// **占用结束后系数被移除，不留下零** / The coefficient is removed once occupancy ends, not left as zero.
    /// </summary>
    /// <remarks>
    /// 判据同时断言"这一段没有行"和"没有任何快照含零系数"。只断言前者的话，
    /// 一个生成 `{0: 0}` 行的实现也能通过，而它正是让行结构膨胀的那种。
    ///
    /// The criterion asserts both "there is no row for that stretch" and "no snapshot carries a zero coefficient". Asserting only the former would let an implementation
    /// that emits a `{0: 0}` row pass — and that is precisely the kind that inflates the row structure.
    /// </remarks>
    [Fact]
    public void Sweep_ShouldRemoveZeroCoefficientsRatherThanKeepThem() {
        var reservations = new List<ActivityCapacitySweep.Reservation> {
            new(Choice: 0, Order: 0, Time: Range(1, 2), Quantity: new Flt64(3.0), Resource: "R"),
        };

        IReadOnlyList<ActivityCapacitySweep.RowSnapshot> snapshots =
            ActivityCapacitySweep.Sweep(reservations, Origin, Origin.AddHours(4), _ => new Flt64(10.0));

        snapshots.Should().ContainSingle("the reservation is active on exactly one interval");
        snapshots.Should().NotContain(snapshot => snapshot.Coefficients.ContainsKey(0) && snapshot.Coefficients[0].ToDouble() == 0.0,
            "a zero entry would connect the column to a row it does not affect");
    }

    /// <summary>
    /// **区间开始时已在进行的占用进入第一段** / Occupancy already in progress at the horizon start enters the first interval.
    /// </summary>
    /// <remarks>
    /// 占用区间 `[前 2 小时, 前 1 小时]` 完全在优化区间之外，不该出现；而 `[前 1 小时, 后 1 小时]` 跨过起点，
    /// 必须在第一段就被算上。判据把两种情形放在一起：只判其一会让实现漏掉其中一半。
    ///
    /// A reservation `[-2h, -1h]` lies entirely outside the horizon and must not appear, while `[-1h, +1h]` straddles the start and must be counted in the first interval.
    /// The criterion places both cases side by side: judging only one lets an implementation miss the other half.
    /// </remarks>
    [Fact]
    public void Sweep_ShouldSeedOccupancyInProgressAtTheHorizonStart() {
        var reservations = new List<ActivityCapacitySweep.Reservation> {
            new(Choice: 0, Order: 0, Time: Range(-2, -1), Quantity: new Flt64(5.0), Resource: "R"),
            new(Choice: 1, Order: 1, Time: Range(-1, 1), Quantity: new Flt64(7.0), Resource: "R"),
        };

        IReadOnlyList<ActivityCapacitySweep.RowSnapshot> snapshots =
            ActivityCapacitySweep.Sweep(reservations, Origin, Origin.AddHours(3), _ => new Flt64(10.0));

        snapshots.Should().NotBeEmpty();
        snapshots[0].Coefficients.Should().ContainKey(1, "that reservation is still running when scheduling begins");
        snapshots[0].Coefficients.Should().NotContainKey(0, "that reservation finished before the horizon started");
    }

    /// <summary>
    /// <summary>
    /// **固定占用进上界而不是系数** / Fixed occupancy goes into the bound, not the coefficients.
    /// </summary>
    /// </summary>
    /// <remarks>
    /// 固定占用没有决策变量。写进系数表会引用一列并不存在；写进上界才对。
    /// 判据断言系数表为空且上界已被扣减——只断言其中一条都测不出方向。
    ///
    /// A fixed reservation has no decision column. Writing it into the coefficient table references a column that does not exist; the upper bound is where it belongs. The
    /// criterion asserts the table is empty **and** the bound has been reduced — either alone fails to pin the direction.
    /// </remarks>
    /// <remarks>
    /// 上界由容量减去固定占用得出。
    /// </remarks>
    [Fact]
    public void Sweep_ShouldPutFixedOccupancyIntoTheBound() {
        var reservations = new List<ActivityCapacitySweep.Reservation> {
            new(Choice: -1, Order: 0, Time: Range(0, 2), Quantity: new Flt64(4.0), Resource: "R"),
        };

        IReadOnlyList<ActivityCapacitySweep.RowSnapshot> snapshots =
            ActivityCapacitySweep.Sweep(reservations, Origin, Origin.AddHours(3), _ => new Flt64(10.0));

        snapshots.Should().ContainSingle();
        snapshots[0].Coefficients.Should().BeEmpty();
        snapshots[0].Fixed.ToDouble().Should().BeApproximately(4.0, 1e-12);
        snapshots[0].Upper.ToDouble().Should().BeApproximately(6.0, 1e-12, "capacity minus fixed occupancy");
    }

    /// **无占用时不产生任何行** / No reservations produce no rows at all.
    [Fact]
    public void Sweep_ShouldEmitNothingWhenThereIsNoOccupancy() {
        ActivityCapacitySweep.Sweep(
                new List<ActivityCapacitySweep.Reservation>(),
                Origin,
                Origin.AddHours(3),
                _ => new Flt64(10.0))
            .Should().BeEmpty("a row with no coefficients and no fixed load constrains nothing");
    }

    /// <summary>构造一个相对起点的时段 / Build an interval relative to the origin.</summary>
    /// <param name="startHour">开始小时 / The start hour.</param>
    /// <param name="endHour">结束小时 / The end hour.</param>
    /// <returns>时段 / The interval.</returns>
    private static TimeRange Range(int startHour, int endHour) =>
        new(Origin.AddHours(startHour), Origin.AddHours(endHour));
}
