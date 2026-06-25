#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 存储资源 / Storage resource.
/// </summary>
public abstract class StorageResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>计算指定时长内的固定成本 / Calculate fixed cost in the given duration.</summary>
    public virtual V FixedCostIn(TimeSpan time) => default(V);

    /// <summary>计算指定时间范围内的固定成本 / Calculate fixed cost in the given time range.</summary>
    public virtual V FixedCostIn(TimeRange time) => FixedCostIn(time.Duration);

    /// <summary>计算任务在指定时长内的资源消耗量 / Calculate resource consumption of a task in the given duration.</summary>
    public abstract V CostBy<T, E, A>(T task, TimeSpan time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <summary>计算任务在指定时间范围内的资源消耗量 / Calculate resource consumption of a task in the given time range.</summary>
    public virtual V CostBy<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        TimeRange intersectionTime = task.Time?.IntersectionWith(time) ?? time;
        return CostBy<T, E, A>(task, intersectionTime?.Duration ?? time.Duration);
    }

    /// <summary>计算指定时长内的固定供给量 / Calculate fixed supply in the given duration.</summary>
    public virtual V FixedSupplyIn(TimeSpan time) => default(V);

    /// <summary>计算指定时间范围内的固定供给量 / Calculate fixed supply in the given time range.</summary>
    public virtual V FixedSupplyIn(TimeRange time) => FixedSupplyIn(time.Duration);

    /// <summary>计算任务在指定时长内的资源供给量 / Calculate resource supply of a task in the given duration.</summary>
    public abstract V SupplyBy<T, E, A>(T task, TimeSpan time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <summary>计算任务在指定时间范围内的资源供给量 / Calculate resource supply of a task in the given time range.</summary>
    public virtual V SupplyBy<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        TimeRange intersectionTime = task.Time?.IntersectionWith(time) ?? time;
        return SupplyBy<T, E, A>(task, intersectionTime?.Duration ?? time.Duration);
    }

    /// <summary>计算任务在指定时间范围内的净使用量 / Calculate net usage of a task in the given time range.</summary>
    public V UsedQuantity<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>
        => (dynamic)SupplyBy<T, E, A>(task, time) - CostBy<T, E, A>(task, time);

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        => (dynamic)SupplyBy(bunch, time) - CostBy(bunch, time);

    /// <summary>计算任务束在指定时间范围内的资源消耗量 / Calculate resource consumption of a task bunch.</summary>
    public virtual V CostBy<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var sum = default(V);
        foreach (T task in bunch.Tasks) {
            sum = (dynamic)sum + CostBy<T, E, A>(task, time);
            if (task.Time is not null && task.Time.End >= time.End) {
                break;
            }
        }
        return sum;
    }

    /// <summary>计算任务束在指定时间范围内的资源供给量 / Calculate resource supply of a task bunch.</summary>
    public virtual V SupplyBy<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var sum = default(V);
        foreach (T task in bunch.Tasks) {
            sum = (dynamic)sum + SupplyBy<T, E, A>(task, time);
            if (task.Time is not null && task.Time.End >= time.End) {
                break;
            }
        }
        return sum;
    }
}

/// <summary>
/// 存储资源时间槽 / Storage resource time slot.
/// </summary>
public class StorageResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : StorageResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public StorageResourceTimeSlot(TimeWindow<V> timeWindow, ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
        TimeWindow = timeWindow;
        Origin = origin;
        Resource = resource;
        ResourceCapacity = resourceCapacity;
        IndexInRule = indexInRule;
        Index = AutoIndexed.AutoIndexedImpl.NextIndex(GetType());
    }

    /// <summary>时间窗口 / Time window.</summary>
    public TimeWindow<V> TimeWindow { get; }

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
        => task is not null ? default(V) : default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度存储资源使用 / Bunch scheduling storage resource usage.
/// </summary>
public class BunchSchedulingStorageResourceUsage<E, R, C, V>
    : IResourceUsage<StorageResourceTimeSlot<R, C, V>, R, C, V>
    where E : Executor
    where R : StorageResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public BunchSchedulingStorageResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<E> executors,
        IReadOnlyList<R> resources,
        string name) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<StorageResourceTimeSlot<R, C, V>> TimeSlots { get; }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    private static IReadOnlyList<StorageResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<StorageResourceTimeSlot<R, C, V>>();
        foreach (R resource in resources) {
            foreach (C capacity in resource.Capacities) {
                ulong index = 0;
                DateTimeOffset beginTime = capacity.Time.Start > timeWindow.Start ? capacity.Time.Start : timeWindow.Start;
                DateTimeOffset endTime = capacity.Time.End < timeWindow.End ? capacity.Time.End : timeWindow.End;
                while (beginTime < endTime) {
                    TimeSpan thisInterval = endTime - beginTime;
                    if (thisInterval > capacity.Interval) {
                        thisInterval = capacity.Interval;
                    }

                    if (thisInterval > timeWindow.Interval) {
                        thisInterval = timeWindow.Interval;
                    }

                    var time = new TimeRange(beginTime, beginTime + thisInterval);
                    timeSlots.Add(new StorageResourceTimeSlot<R, C, V>(timeWindow, time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
