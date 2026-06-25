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
/// 连接资源 / Connection resource.
/// </summary>
public abstract class ConnectionResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算任务间连接在指定时间范围内的资源消耗量 / Calculate resource consumption of a task connection in the given time range.
    /// </summary>
    public abstract V UsedBy<T, E, A>(T? prevTask, T? task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) {
        var counter = default(V);
        for (int i = 0; i < bunch.Tasks.Count; i++) {
            V usage = i == 0
                ? UsedBy<T, E, A>(bunch.LastTask, bunch.Tasks[i], time)
                : UsedBy<T, E, A>(bunch.Tasks[i - 1], bunch.Tasks[i], time);
            counter = (dynamic)counter + usage;
        }
        return counter;
    }
}

/// <summary>
/// 连接资源时间槽 / Connection resource time slot.
/// </summary>
public class ConnectionResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : ConnectionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public ConnectionResourceTimeSlot(ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
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
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度连接资源使用 / Bunch scheduling connection resource usage.
/// </summary>
public class BunchSchedulingConnectionResourceUsage<R, C, V>
    : IResourceUsage<ConnectionResourceTimeSlot<R, C, V>, R, C, V>
    where R : ConnectionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public BunchSchedulingConnectionResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<R> resources,
        string name) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ConnectionResourceTimeSlot<R, C, V>> TimeSlots { get; }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    private static IReadOnlyList<ConnectionResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<ConnectionResourceTimeSlot<R, C, V>>();
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
                    timeSlots.Add(new ConnectionResourceTimeSlot<R, C, V>(time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
