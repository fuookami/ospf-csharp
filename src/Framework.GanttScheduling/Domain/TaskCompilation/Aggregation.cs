#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation;
/// <summary>
/// 抽象任务调度聚合 / Abstract task scheduling aggregation
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public abstract class AbstractTaskSchedulingAggregation<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 抽象任务调度聚合构造 / Abstract task scheduling aggregation constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="taskCancelEnabled">是否启用任务取消 / Whether task cancellation is enabled</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    protected AbstractTaskSchedulingAggregation(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool taskCancelEnabled = false,
        bool withExecutorLeisure = false) {
        TimeBoundary = new SolverTimeWindowBoundary(timeWindow);
        Compilation = new TaskCompilation<T, E, A>(
            tasks, executors, lockCancelTasks, taskCancelEnabled, withExecutorLeisure);
        TaskTime = CreateTaskTime(timeWindow, tasks, Compilation);
    }

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary { get; }

    /// <summary>任务编译 / Task compilation</summary>
    public TaskCompilation<T, E, A> Compilation { get; }

    /// <summary>任务时间 / Task time</summary>
    public TaskSchedulingTaskTime<T, E, A> TaskTime { get; }

    /// <summary>创建任务时间 / Create task time</summary>
    protected virtual TaskSchedulingTaskTime<T, E, A> CreateTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        TaskCompilation<T, E, A> compilation) => new TaskSchedulingTaskTime<T, E, A>(timeWindow, tasks, compilation);
}

/// <summary>
/// 任务调度聚合 / Task scheduling aggregation
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskSchedulingAggregation<T, E, A> : AbstractTaskSchedulingAggregation<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 任务调度聚合构造 / Task scheduling aggregation constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="taskCancelEnabled">是否启用任务取消 / Whether task cancellation is enabled</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    public TaskSchedulingAggregation(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool taskCancelEnabled = false,
        bool withExecutorLeisure = false)
        : base(timeWindow, tasks, executors, lockCancelTasks, taskCancelEnabled, withExecutorLeisure) {
    }
}
