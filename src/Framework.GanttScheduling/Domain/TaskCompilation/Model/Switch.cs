#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 切换接口 / Switch interface
/// </summary>
public interface ISwitch {
    /// <summary>注册切换到模型 / Register switch to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);
}

/// <summary>
/// 任务调度切换 / Task scheduling switch
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskSchedulingSwitch<T, E, A> : ISwitch
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly TaskCompilation<T, E, A> _compilation;
    private readonly TaskTime? _taskTime;
    private readonly SolverTimeWindowBoundary _timeBoundary;

    /// <summary>
    /// 任务调度切换构造 / Task scheduling switch constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    public TaskSchedulingSwitch(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskTime? taskTime = null) {
        _timeWindow = timeWindow;
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _taskTime = taskTime;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建任务调度切换 / Create task scheduling switch from a solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    public TaskSchedulingSwitch(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskTime? taskTime = null)
        : this(timeBoundary.Source, tasks, executors, compilation, taskTime) {
    }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>注册切换到模型 / Register switch to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
