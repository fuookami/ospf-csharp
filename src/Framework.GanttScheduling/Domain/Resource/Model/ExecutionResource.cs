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
/// 执行资源 / Execution resource.
/// </summary>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public abstract class ExecutionResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算任务在指定时间范围内的资源消耗量 / Calculate resource consumption of a task in the given time range.
    /// </summary>
    public abstract V UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time)
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) {
        var counter = default(V);
        foreach (T task in bunch.Tasks) {
            counter = (dynamic)counter + UsedBy(task, time);
        }
        return counter;
    }
}

/// <summary>
/// 执行资源时间槽 / Execution resource time slot.
/// </summary>
/// <typeparam name="R">执行资源类型 / Execution resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public class ExecutionResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : ExecutionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public ExecutionResourceTimeSlot(ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
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

    /// <summary>
    /// 计算任务在此时槽的资源消耗量 / Calculate resource consumption of a task at this time slot.
    /// </summary>
    public V UsedBy<E, A>(IAbstractTask<E, A> task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => Resource.UsedBy(task, Time);

    /// <inheritdoc/>
    public V RelationTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => task is not null ? UsedBy(task) : default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度执行资源使用 / Bunch scheduling execution resource usage.
/// </summary>
/// <typeparam name="R">执行资源类型 / Execution resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public class BunchSchedulingExecutionResourceUsage<R, C, V>
    : IResourceUsage<ExecutionResourceTimeSlot<R, C, V>, R, C, V>
    where R : ExecutionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window.</param>
    /// <param name="resources">资源列表 / List of resources.</param>
    /// <param name="name">名称 / Name.</param>
    public BunchSchedulingExecutionResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<R> resources,
        string name) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ExecutionResourceTimeSlot<R, C, V>> TimeSlots { get; }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;

    /// <inheritdoc/>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    private static IReadOnlyList<ExecutionResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<ExecutionResourceTimeSlot<R, C, V>>();
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
                    timeSlots.Add(new ExecutionResourceTimeSlot<R, C, V>(
                        time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
