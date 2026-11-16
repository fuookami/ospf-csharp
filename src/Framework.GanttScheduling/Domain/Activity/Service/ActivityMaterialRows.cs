// 库存累积行 / Cumulative stock rows.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 库存累积行 / Cumulative stock rows.
/// </summary>
/// <remarks>
/// **每个时点的库存行只包含此前的事件，绝不包含之后的**。这是"未来产出不能补过去的缺料"的唯一实现方式：
/// 若把整条时间轴的事件都放进同一个行系数，一个十天后才到货的批次会立刻让今天的库存看起来充足，
/// 于是**排程允许在前九天持续缺料**，而模型报告一切可行。
///
/// 这条性质不能靠"检查结果"来兜底：缺料的那九天在解里看着完全正常，只是那一天真的没有料。
///
/// **Each instant's stock row contains only the events up to that instant, never later ones.** That is the only way to implement "future supply cannot cover past shortages": if
/// the whole time axis' events went into one row's coefficients, a batch arriving in ten days would immediately make today's stock look sufficient, letting **the schedule run
/// short for nine days** while the model reports everything feasible.
///
/// The property cannot be caught by inspecting the answer: those nine days look perfectly normal in the solution; there simply was no material on them.
/// </remarks>
public static class ActivityMaterialRows {
    /// <summary>一个物料事件 / One material event.</summary>
    /// <param name="Choice">候选下标，`-1` 表示固定事件 / The candidate index; -1 means a fixed event.</param>
    /// <param name="Order">原始求和顺序 / The original summation order.</param>
    /// <param name="Time">事件时间 / The event time.</param>
    /// <param name="Quantity">有符号变化量 / The signed change.</param>
    /// <param name="Material">物料身份 / The material identity.</param>
    public sealed record Event(int Choice, int Order, DateTimeOffset Time, Flt64 Quantity, string Material);

    /// <summary>一个时点的库存行 / One instant's stock row.</summary>
    /// <param name="Coefficients">候选下标到累计量的映射，只含非零项 / The map from candidate index to accumulated amount, non-zero entries only.</param>
    /// <param name="Lower">下界 / The lower bound.</param>
    /// <param name="Upper">上界 / The upper bound.</param>
    public sealed record RowSnapshot(IReadOnlyDictionary<int, Flt64> Coefficients, Flt64? Lower, Flt64? Upper);

    /// <summary>
    /// 生成一种物料的库存行 / Generate the stock rows of one material.
    /// </summary>
    /// <param name="events">该物料的全部事件 / Every event of that material.</param>
    /// <param name="initial">期初库存 / The opening stock.</param>
    /// <param name="minimum">最低库存 / The minimum stock.</param>
    /// <param name="maximum">可选上限 / The optional maximum.</param>
    /// <param name="horizonStart">优化区间起点 / The horizon start.</param>
    /// <param name="horizonEnd">优化区间终点 / The horizon end.</param>
    /// <returns>按时点排列的库存行 / The stock rows in time order.</returns>
    /// <remarks>
    /// **固定事件进界、不进系数**（与存在性行同一原则）：它们没有决策变量，写进系数表会引用一列并不存在。
    ///
    /// **区间两端始终出现在时点表里**：最低库存常落在"什么都没发生的长时间段"内，只报有事件的时点会让
    /// "整整三天库存为零"不可见。
    ///
    /// **Fixed events go into the bounds, not the coefficients** (the same principle as the presence rows): they have no decision column, and writing them into the coefficient
    /// table references one that does not exist.
    ///
    /// **Both horizon endpoints always appear in the instant list**: the minimum stock usually falls in a long quiet stretch, and reporting only the instants that carry events
    /// makes "stock sat at zero for three days" invisible.
    /// </remarks>
    public static IReadOnlyList<RowSnapshot> Rows(
        IReadOnlyList<Event> events,
        Flt64 initial,
        Flt64 minimum,
        Flt64? maximum,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd) {
        System.ArgumentNullException.ThrowIfNull(events);

        var times = new SortedSet<DateTimeOffset> { horizonStart, horizonEnd };
        foreach (Event materialEvent in events) {
            times.Add(materialEvent.Time);
        }
        var ordered = new List<DateTimeOffset>(times);
        var orderedSorted = new List<Event>(events);
        orderedSorted.Sort((lhs, rhs) => lhs.Order.CompareTo(rhs.Order));

        var snapshots = new List<RowSnapshot>(ordered.Count);
        Flt64 fixedTotal = initial;
        var accumulators = new Dictionary<int, Flt64>();
        var coefficients = new Dictionary<int, Flt64>();

        int cursor = 0;
        foreach (DateTimeOffset time in ordered) {
            // 只推进到当前时点为止的事件——之后的到货不会进入这一行。
            // Advance only through the events up to this instant; later receipts must not enter this row.
            while (cursor < orderedSorted.Count && orderedSorted[cursor].Time <= time) {
                Event materialEvent = orderedSorted[cursor];
                if (materialEvent.Choice < 0) {
                    fixedTotal += materialEvent.Quantity;
                } else {
                    Flt64 current = accumulators.TryGetValue(materialEvent.Choice, out Flt64 known)
                        ? known + materialEvent.Quantity
                        : materialEvent.Quantity;
                    // 零系数必须移除，否则每一列都出现在每一行里。
                    // A zero coefficient must be removed, otherwise every column appears in every row.
                    if (current.ToDouble() == 0.0) {
                        accumulators.Remove(materialEvent.Choice);
                    } else {
                        accumulators[materialEvent.Choice] = current;
                    }
                }
                cursor++;
            }

            coefficients = new Dictionary<int, Flt64>(accumulators);
            snapshots.Add(new RowSnapshot(
                coefficients,
                Lower: minimum - fixedTotal,
                Upper: maximum is { } upper ? upper - fixedTotal : null));
        }
        return snapshots;
    }
}
