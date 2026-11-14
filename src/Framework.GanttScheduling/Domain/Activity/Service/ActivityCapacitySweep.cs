// 资源占用扫描 / Resource occupancy sweep.
// 与固定 Kotlin 基线 `gantt-scheduling-domain-activity-context/.../ActivityCapacityRows.kt` 的扫描核心对应：
// 按端点推进增删占用，只重算发生变化的选择，并保留各组原求和顺序。
// Mirrors the sweep core of the pinned baseline's `ActivityCapacityRows.kt`: advance through endpoints, recompute only the
// changed choices, and keep each group's original summation order.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 资源占用扫描 / The resource occupancy sweep.
/// </summary>
/// <remarks>
/// 每块资源在每个时段一条上界行，行系数是**该时段内每个候选的占用量**。要得到它，需要在时间轴上推进：
/// 每个占用区间的起点加、终点减。
///
/// **零系数必须从系数表移除，不能留下 0**。留下零会让每一列出现在每一块资源的每一行里——
/// 行的结构因此变成"全连接"，而它真正的稀疏结构恰恰是求解器性能的全部来源。
/// 更糟的是这种膨胀**不在数值上改变任何东西**，所以只在规模上表现出来。
///
/// One upper-bound row per resource per interval, whose coefficients are **each candidate's occupancy within that interval**. Obtaining them requires advancing along the time
/// axis: add at each reservation's start, subtract at its end.
///
/// **A zero coefficient must be removed from the table, not left as 0.** Leaving zeros puts every column into every row of every resource, turning the row structure fully
/// connected while its genuine sparsity is the entire source of solver performance. Worse, the inflation **changes nothing numerically**, so it only shows up in scale.
/// </remarks>
public static class ActivityCapacitySweep {
    /// <summary>一段占用 / One reservation.</summary>
    /// <param name="Choice">候选下标，`-1` 表示固定占用 / The candidate index; -1 means a fixed reservation.</param>
    /// <param name="Order">原始求和顺序 / The original summation order.</param>
    /// <param name="Time">占用区间 / The reservation interval.</param>
    /// <param name="Quantity">占用量 / The reserved amount.</param>
    /// <param name="Resource">资源身份 / The resource identity.</param>
    public sealed record Reservation(int Choice, int Order, TimeRange Time, Flt64 Quantity, string Resource);

    /// <summary>一个时段的上界行 / One interval's upper-bound row.</summary>
    /// <param name="Coefficients">候选下标到占用量的映射，只含非零项 / The map from candidate index to occupancy, non-zero entries only.</param>
    /// <param name="Fixed">固定占用的总量 / The total fixed occupancy.</param>
    /// <param name="Upper">该时段的上界 / The upper bound for the interval.</param>
    public sealed record RowSnapshot(IReadOnlyDictionary<int, Flt64> Coefficients, Flt64 Fixed, Flt64 Upper);

    /// <summary>
    /// 扫描一块资源 / Sweep one resource.
    /// </summary>
    /// <param name="reservations">该资源的全部占用 / Every reservation on that resource.</param>
    /// <param name="horizonStart">优化区间起点 / The horizon start.</param>
    /// <param name="horizonEnd">优化区间终点 / The horizon end.</param>
    /// <param name="capacityAt">时段容量函数 / The capacity function per interval.</param>
    /// <returns>按时段排列的上界行 / The upper-bound rows in time order.</returns>
    /// <remarks>
    /// **区间端点只保留严格在优化区间内部的时刻**：区间之外的占用不会出现在任何行里，因为它们在排程范围外，
    /// 生成相关行只是浪费。而**优化区间起点那一刻已经在进行中的占用必须进初值**——否则"排程一开始就有东西占着资源"
    /// 这件事会丢失，容量被高估。
    ///
    /// **Only instants strictly inside the horizon become endpoints**: reservations outside the scheduling window appear in no row because they are closed later or earlier,
    /// and generating rows for them is waste. But **reservations already in progress at the horizon start must enter the initial state** — otherwise "the resource was already
    /// occupied when scheduling began" is lost and capacity is overstated.
    /// </remarks>
    public static IReadOnlyList<RowSnapshot> Sweep(
        IReadOnlyList<Reservation> reservations,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd,
        Func<DateTimeOffset, Flt64> capacityAt) {
        System.ArgumentNullException.ThrowIfNull(reservations);
        System.ArgumentNullException.ThrowIfNull(capacityAt);

        var events = new SortedSet<DateTimeOffset> { horizonStart, horizonEnd };
        foreach (Reservation reservation in reservations) {
            if (reservation.Time.Start > horizonStart && reservation.Time.Start < horizonEnd) {
                events.Add(reservation.Time.Start);
            }
            if (reservation.Time.End > horizonStart && reservation.Time.End < horizonEnd) {
                events.Add(reservation.Time.End);
            }
        }

        var times = new List<DateTimeOffset>(events);
        var snapshots = new List<RowSnapshot>();
        if (times.Count < 2) {
            return snapshots;
        }

        var active = new Dictionary<int, Flt64>();
        Flt64 fixedQuantity = Flt64.Zero;
        foreach (Reservation reservation in reservations) {
            if (reservation.Time.Start <= horizonStart && reservation.Time.End > horizonStart) {
                if (reservation.Choice < 0) {
                    fixedQuantity += reservation.Quantity;
                } else {
                    active[reservation.Choice] = active.TryGetValue(reservation.Choice, out Flt64 known)
                        ? known + reservation.Quantity
                        : reservation.Quantity;
                }
            }
        }

        for (int index = 0; index < times.Count - 1; index++) {
            DateTimeOffset time = times[index];

            // 推进到该时段：加上此刻开始的，减去此刻结束的。
            // Advance into this interval: add what starts here, remove what ends here.
            foreach (Reservation reservation in reservations) {
                bool startsHere = reservation.Time.Start == time && time > horizonStart;
                bool endsHere = reservation.Time.End == time && time > horizonStart;
                if (!startsHere && !endsHere) {
                    continue;
                }
                Flt64 delta = startsHere ? reservation.Quantity : (Flt64.Zero - reservation.Quantity);
                if (reservation.Choice < 0) {
                    fixedQuantity += delta;
                } else {
                    Flt64 current = active.TryGetValue(reservation.Choice, out Flt64 known)
                        ? known + delta
                        : delta;
                    // 零系数必须移除，否则每一列都出现在每一行里。
                    // A zero coefficient must be removed, otherwise every column appears in every row.
                    if (current.ToDouble() == 0.0) {
                        active.Remove(reservation.Choice);
                    } else {
                        active[reservation.Choice] = current;
                    }
                }
            }

            if (active.Count > 0 || fixedQuantity.ToDouble() != 0.0) {
                var coefficients = new Dictionary<int, Flt64>(active);
                snapshots.Add(new RowSnapshot(coefficients, fixedQuantity, capacityAt(time) - fixedQuantity));
            }
        }
        return snapshots;
    }
}
