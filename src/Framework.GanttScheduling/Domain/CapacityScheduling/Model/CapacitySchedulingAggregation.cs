#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能调度聚合类 / Capacity scheduling aggregation class
/// </summary>
/// <remarks>
/// 聚合产能调度相关的数据和逻辑。
/// Aggregates data and logic related to capacity scheduling.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacitySchedulingAggregation<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;

    /// <summary>
    /// 产能调度聚合构造 / Capacity scheduling aggregation constructor
    /// </summary>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    public CapacitySchedulingAggregation(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow) {
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        ActionsByExecutor = actions.GroupBy(a => a.Executor.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<A>)g.ToList());
    }

    /// <summary>生产动作列表 / List of production actions</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / List of time slots</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>时间窗口 / Time window</summary>
    public TimeWindow<Flt64> TimeWindow => _timeWindow;

    /// <summary>按执行器分组的动作 / Actions grouped by executor</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<A>> ActionsByExecutor { get; }

    /// <summary>时隙数量 / Number of time slots</summary>
    public int SlotCount => _slots.Count;

    /// <summary>动作数量 / Number of actions</summary>
    public int ActionCount => _actions.Count;

    /// <summary>执行器数量 / Number of executors</summary>
    public int ExecutorCount => ActionsByExecutor.Count;

    /// <summary>获取指定执行器的动作列表 / Get actions for specified executor</summary>
    public IReadOnlyList<A> ActionsForExecutor(string executorId)
        => ActionsByExecutor.TryGetValue(executorId, out IReadOnlyList<A>? list) ? list : Array.Empty<A>();

    /// <summary>
    /// 所有动作与时隙上的总产能值 / The total capacity value over every action and slot.
    /// </summary>
    /// <returns>总产能值 / The total capacity value.</returns>
    /// <remarks>
    /// `Σ 单位产能 × 上界`，按基线逐格累加。**不做任何折算**：单位产能与上界各是各的口径，折算属于求解层。
    ///
    /// 基线里这是个私有函数、只被 `totalCapacityQuantity` 调用；C# 侧直接公开——它本身就是一个有意义的口径，
    /// 而"只有物理量、没有裸值"会让每次要比较都得先造一个单位。
    ///
    /// `Sum of unit capacity x upper bound`, accumulated cell by cell as in the baseline. **No conversion**: unit capacity and the upper bound keep their own scales; converting belongs
    /// to the solving layer. In the baseline this is private and only serves `totalCapacityQuantity`; here it is public — the bare value is a meaningful figure on its own, and exposing
    /// only the quantity forces callers to invent a unit just to compare.
    /// </remarks>
    public Flt64 TotalCapacityValue() {
        Flt64 total = Flt64.Zero;
        foreach (A action in _actions) {
            foreach (TimeRange slot in _slots) {
                ulong upper = action.UpperBound(slot, _timeWindow);
                total += new Flt64(action.UnitCapacity(_timeWindow).ToDouble() * (double)upper);
            }
        }
        return total;
    }

    /// <summary>
    /// 总产能物理量 / The total capacity as a quantity.
    /// </summary>
    /// <param name="unit">产能单位，缺省为无量纲 / The capacity unit; dimensionless when omitted.</param>
    /// <returns>总产能物理量 / The total capacity quantity.</returns>
    public Quantity<Flt64> TotalCapacityQuantity(PhysicalUnit? unit = null) =>
        new(TotalCapacityValue(), unit ?? NoneUnit.Instance);

    /// <summary>获取指定时隙的索引 / Get index for specified time slot</summary>
    public int IndexOfSlot(TimeRange slot) {
        for (int i = 0; i < _slots.Count; i++) {
            if (Equals(_slots[i], slot)) {
                return i;
            }
        }
        return -1;
    }

    /// <summary>获取指定动作的索引 / Get index for specified action</summary>
    public int IndexOfAction(A action) {
        for (int i = 0; i < _actions.Count; i++) {
            if (Equals(_actions[i], action)) {
                return i;
            }
        }
        return -1;
    }
}
