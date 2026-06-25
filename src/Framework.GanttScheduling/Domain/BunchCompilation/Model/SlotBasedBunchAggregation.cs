#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 分时隙任务束聚合 / Slot-based bunch aggregation
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class SlotBasedBunchAggregation<B, T, E, A> : BunchAggregation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>, ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <inheritdoc/>
    protected override bool SameColumnAs(B lhs, B rhs) {
        return lhs.Eq(rhs)
            && lhs.SlotIndex == rhs.SlotIndex
            && lhs.Slot == rhs.Slot;
    }
}
