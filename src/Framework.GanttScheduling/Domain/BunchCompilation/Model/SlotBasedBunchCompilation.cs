#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 分时隙任务束编译类
/// Slot-based bunch compilation class
/// </summary>
/// <remarks>
/// 继承 BunchCompilation，增加时隙相关功能。每个 bunch 只能属于一个时隙，时隙对应关系由 bunch 生成器保证。
/// Extends BunchCompilation with slot-related functionality. Each bunch can only belong to one time slot.
/// </remarks>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class SlotBasedBunchCompilation<B, T, E, A> : BunchCompilation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>, ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<TimeRange> _slots;

    /// <summary>
    /// 分时隙任务束编译构造 / Slot-based bunch compilation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    public SlotBasedBunchCompilation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlyList<TimeRange> slots,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true)
        : base(tasks, executors, lockCancelTasks, withExecutorLeisure, new SlotBasedBunchAggregation<B, T, E, A>()) {
        _slots = slots;
    }

    /// <summary>时隙列表 / List of time slots</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>
    /// 按时隙分组的 bunches / Bunches grouped by slot
    /// </summary>
    public IReadOnlyDictionary<TimeRange, IReadOnlyList<B>> BunchesBySlot =>
        Bunches.GroupBy(b => b.Slot)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<B>)g.ToList());

    /// <summary>
    /// 获取指定时隙的所有 bunch / Get all bunches for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <returns>该时隙的 bunch 列表 / List of bunches in this slot</returns>
    public IReadOnlyList<B> BunchesInSlot(TimeRange slot) => BunchesBySlot.TryGetValue(slot, out IReadOnlyList<B>? bunches) ? bunches : Array.Empty<B>();

    /// <summary>
    /// 获取指定时隙和执行器的所有 bunch / Get all bunches for specified slot and executor
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <param name="executor">执行器 / The executor</param>
    /// <returns>该时隙该执行器的 bunch 列表 / List of bunches in this slot for this executor</returns>
    public IReadOnlyList<B> BunchesInSlot(TimeRange slot, E executor) {
        return BunchesBySlot.TryGetValue(slot, out IReadOnlyList<B>? bunches)
            ? bunches.Where(b => Equals(b.Executor, executor)).ToList()
            : Array.Empty<B>();
    }
}
