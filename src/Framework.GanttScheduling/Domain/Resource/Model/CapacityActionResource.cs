#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 支持生产动作的资源接口 / Resource interface that supports production actions.
/// </summary>
public interface ICapacityActionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>计算动作在指定时间范围内的资源消耗 / Calculate resource consumption per unit operation time.</summary>
    V UsedBy(object action, TimeRange time);
}

/// <summary>
/// CapacityActionResource 的时间槽 / Time slot for CapacityActionResource.
/// </summary>
public class CapacityActionResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>, ICapacityActionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public CapacityActionResourceTimeSlot(ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
        Origin = origin;
        Resource = resource;
        ResourceCapacity = resourceCapacity;
        IndexInRule = indexInRule;
        Index = AutoIndexed.AutoIndexedImpl.NextIndex(GetType());
    }

    /// <inheritdoc/>
    public ITimeSlot Origin { get; }

    /// <inheritdoc/>
    public R Resource { get; }

    /// <inheritdoc/>
    public C ResourceCapacity { get; }

    /// <inheritdoc/>
    public ulong IndexInRule { get; }

    /// <inheritdoc/>
    public TimeRange Time => Origin.Time;

    /// <inheritdoc/>
    public int Index { get; }

    /// <inheritdoc/>
    public ITimeSlot? SubOf(TimeRange subTime) => Origin.SubOf(subTime);

    /// <inheritdoc/>
    public V RelationTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => false;

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}
