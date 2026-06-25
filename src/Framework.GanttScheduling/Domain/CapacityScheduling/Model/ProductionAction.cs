#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 生产动作接口
/// Production action interface
/// </summary>
/// <remarks>
/// 生产动作表示产能的生产方式，分为离散型（有固定批次时长）和连续型（无固定批次时长）。
/// A production action represents a way to produce capacity. It can be discrete (fixed batch duration) or continuous.
/// </remarks>
public interface IProductionAction {
    /// <summary>动作唯一标识 / Unique identifier for the action</summary>
    string Id { get; }

    /// <summary>动作名称 / Name of the action</summary>
    string Name { get; }

    /// <summary>显示名称 / Display name</summary>
    string DisplayName => Name;

    /// <summary>执行者 / The executor that performs this action</summary>
    Executor Executor { get; }

    /// <summary>
    /// 是否为离散型动作 / Whether the action is discrete
    /// </summary>
    /// <remarks>
    /// true: 离散型，x 表示批次数；false: 连续型，x 表示时长单位数。
    /// true: discrete, x represents batch count; false: continuous, x represents duration units.
    /// </remarks>
    bool Discrete { get; }

    /// <summary>批次时长（仅离散型有效）/ Batch duration (only for discrete actions)</summary>
    TimeSpan? BatchDuration => null;

    /// <summary>
    /// 单位成本 / Unit cost
    /// </summary>
    /// <param name="time">时间点 / Time instant</param>
    /// <param name="fromDouble">Double 到 Flt64 的转换函数 / Double to Flt64 converter</param>
    /// <returns>单位成本 / Unit cost</returns>
    Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble);

    /// <summary>
    /// 求解器单位成本值 / Solver unit cost value
    /// </summary>
    /// <param name="time">时间点 / Time instant</param>
    /// <returns>求解器单位成本 / Solver unit cost</returns>
    Flt64 UnitCostSolverValue(DateTimeOffset time) => UnitCost(time, d => new Flt64(d));

    /// <summary>
    /// x 变量的上界 / Upper bound for x variable
    /// </summary>
    /// <param name="slot">时隙 / Time slot</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <returns>上界值 / Upper bound value</returns>
    ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow);

    /// <summary>
    /// 单位产能 / Unit capacity per x value
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <returns>单位产能 / Unit capacity</returns>
    Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow);
}

/// <summary>
/// 动作分配结果 / Action allocation result
/// </summary>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <param name="Action">生产动作 / The production action</param>
/// <param name="Slot">时隙 / The time slot</param>
/// <param name="SlotIndex">时隙索引 / Slot index</param>
/// <param name="Amount">分配数量 / Allocated amount</param>
/// <param name="Duration">分配时长 / Allocated duration</param>
/// <param name="Order">顺序位置（可选）/ Order position (optional)</param>
public sealed record ActionAllocation<A>(
    A Action,
    TimeRange Slot,
    int SlotIndex,
    ulong Amount,
    TimeSpan Duration,
    int Order = 0)
    where A : IProductionAction;

/// <summary>
/// 执行器产能结果 / Executor capacity result
/// </summary>
/// <param name="Executor">执行器 / The executor</param>
/// <param name="Slot">时隙 / The time slot</param>
/// <param name="SlotIndex">时隙索引 / Slot index</param>
/// <param name="TotalDuration">总使用时长 / Total used duration</param>
public sealed record ExecutorCapacityResult(
    Executor Executor,
    TimeRange Slot,
    int SlotIndex,
    TimeSpan TotalDuration);

/// <summary>
/// 产能调度解 / Capacity scheduling solution
/// </summary>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <param name="Actions">所有动作 / All actions</param>
/// <param name="ActionAllocations">所有动作分配 / All action allocations</param>
/// <param name="ExecutorCapacities">所有执行器产能结果 / All executor capacity results</param>
public sealed record CapacitySchedulingSolution<A>(
    IReadOnlyList<A> Actions,
    IReadOnlyList<ActionAllocation<A>> ActionAllocations,
    IReadOnlyList<ExecutorCapacityResult> ExecutorCapacities)
    where A : IProductionAction {
    /// <summary>所有动作分配（按时隙分组）/ Action allocations grouped by slot</summary>
    public IReadOnlyDictionary<TimeRange, List<ActionAllocation<A>>> AllocationsBySlot
        => ActionAllocations.GroupBy(a => a.Slot).ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>所有执行器产能结果（按时隙分组）/ Executor capacities grouped by slot</summary>
    public IReadOnlyDictionary<TimeRange, List<ExecutorCapacityResult>> CapacitiesBySlot
        => ExecutorCapacities.GroupBy(c => c.Slot).ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>获取指定时隙的动作分配 / Get action allocations for specified slot</summary>
    public IReadOnlyList<ActionAllocation<A>> AllocationsInSlot(TimeRange slot)
        => AllocationsBySlot.TryGetValue(slot, out List<ActionAllocation<A>>? list) ? list : Array.Empty<ActionAllocation<A>>();

    /// <summary>获取指定时隙的执行器产能结果 / Get executor capacity results for specified slot</summary>
    public IReadOnlyList<ExecutorCapacityResult> CapacitiesInSlot(TimeRange slot)
        => CapacitiesBySlot.TryGetValue(slot, out List<ExecutorCapacityResult>? list) ? list : Array.Empty<ExecutorCapacityResult>();

    /// <summary>获取指定动作的所有分配 / Get all allocations for specified action</summary>
    public IReadOnlyList<ActionAllocation<A>> AllocationsForAction(A action)
        => ActionAllocations.Where(a => Equals(a.Action, action)).ToList();

    /// <summary>获取指定执行器的所有产能结果 / Get all capacity results for specified executor</summary>
    public IReadOnlyList<ExecutorCapacityResult> CapacitiesForExecutor(Executor executor)
        => ExecutorCapacities.Where(c => ReferenceEquals(c.Executor, executor) || c.Executor.Id == executor.Id).ToList();
}

/// <summary>
/// 产能列 / Capacity column
/// </summary>
/// <remarks>
/// 一个产能列代表某台设备在某个时隙某个顺序位置的完整分配方案。
/// A column represents a complete allocation plan for an executor at a specific slot and order.
/// </remarks>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <param name="Executor">执行该列的设备 / Executor that performs this column</param>
/// <param name="SlotIndex">时隙索引 / Slot index</param>
/// <param name="Order">顺序位置 / Order position</param>
/// <param name="Allocations">动作分配 / Action allocations</param>
/// <param name="ColumnCost">列成本 / Column cost</param>
public sealed record CapacityColumn<E, A>(
    E Executor,
    int SlotIndex,
    int Order,
    IReadOnlyDictionary<A, ulong> Allocations,
    Flt64 ColumnCost)
    where A : IProductionAction {
    /// <summary>获取指定动作的分配数量 / Get allocation amount for a specific action</summary>
    public ulong AmountFor(A action) => Allocations.TryGetValue(action, out ulong amount) ? amount : 0UL;

    /// <summary>总分配数量 / Total allocation amount</summary>
    public ulong TotalAmount => Allocations.Values.Aggregate(0UL, (acc, amount) => acc + amount);

    /// <summary>是否为空列 / Whether this is an empty column</summary>
    public bool IsEmpty => Allocations.Count == 0 || Allocations.Values.All(a => a == 0);
}
