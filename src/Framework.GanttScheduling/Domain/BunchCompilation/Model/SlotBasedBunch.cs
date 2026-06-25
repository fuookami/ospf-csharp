#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 分时隙任务束接口
/// Slot-based task bunch interface
/// </summary>
/// <remarks>
/// 一个 SlotBasedBunch 只能属于一个时隙。时隙对应关系由 bunch 生成器保证。
/// A SlotBasedBunch can only belong to one time slot. The slot correspondence is ensured by the bunch generator.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public interface ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 所属时隙 / The time slot this bunch belongs to
    /// </summary>
    TimeRange Slot { get; }

    /// <summary>
    /// 时隙索引 / Slot index in the time window
    /// </summary>
    int SlotIndex { get; }
}
