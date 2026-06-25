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
/// 任务时间抽象基类 / Task time abstract base class
/// </summary>
public abstract class TaskTime {
    /// <summary>是否启用提前 / Whether advance is enabled</summary>
    public abstract bool AdvanceEnabled { get; }

    /// <summary>是否启用延迟 / Whether delay is enabled</summary>
    public abstract bool DelayEnabled { get; }

    /// <summary>是否启用提前最早结束时间 / Whether advance earliest end time is enabled</summary>
    public virtual bool AdvanceEarliestEndTimeEnabled => false;

    /// <summary>是否启用延迟最晚结束时间 / Whether delay last end time is enabled</summary>
    public virtual bool DelayLastEndTimeEnabled => false;

    /// <summary>是否启用超最大提前 / Whether over-max advance is enabled</summary>
    public virtual bool OverMaxAdvanceEnabled => false;

    /// <summary>是否启用超最大延迟 / Whether over-max delay is enabled</summary>
    public virtual bool OverMaxDelayEnabled => false;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public abstract Try Register(object model);
}

/// <summary>
/// 任务调度任务时间 / Task scheduling task time
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskSchedulingTaskTime<T, E, A> : TaskTime
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly ICompilation _compilation;
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly bool _advanceEnabled;
    private readonly bool _delayEnabled;

    /// <summary>
    /// 任务调度任务时间构造 / Task scheduling task time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="advanceEnabled">是否启用提前 / Whether advance is enabled</param>
    /// <param name="delayEnabled">是否启用延迟 / Whether delay is enabled</param>
    public TaskSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        bool advanceEnabled = false,
        bool delayEnabled = false) {
        _tasks = tasks;
        _compilation = compilation;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _advanceEnabled = advanceEnabled;
        _delayEnabled = delayEnabled;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="advanceEnabled">是否启用提前 / Whether advance is enabled</param>
    /// <param name="delayEnabled">是否启用延迟 / Whether delay is enabled</param>
    public TaskSchedulingTaskTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        bool advanceEnabled = false,
        bool delayEnabled = false) {
        _tasks = tasks;
        _compilation = compilation;
        _timeBoundary = timeBoundary;
        _advanceEnabled = advanceEnabled;
        _delayEnabled = delayEnabled;
    }

    /// <inheritdoc/>
    public override bool AdvanceEnabled => _advanceEnabled;

    /// <inheritdoc/>
    public override bool DelayEnabled => _delayEnabled;

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary => _timeBoundary;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public override Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 迭代任务调度任务时间 / Iterative task scheduling task time
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class IterativeTaskSchedulingTaskTime<IT, T, E, A> : TaskTime
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<IT> _tasks;
    private readonly IterativeTaskCompilation<IT, T, E, A> _compilation;
    private readonly SolverTimeWindowBoundary _timeBoundary;

    /// <summary>
    /// 迭代任务调度任务时间构造 / Iterative task scheduling task time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">迭代编译结果 / Iterative compilation result</param>
    /// <param name="advanceEnabled">是否启用提前 / Whether advance is enabled</param>
    /// <param name="delayEnabled">是否启用延迟 / Whether delay is enabled</param>
    public IterativeTaskSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<IT> tasks,
        IterativeTaskCompilation<IT, T, E, A> compilation,
        bool advanceEnabled = false,
        bool delayEnabled = false) {
        _tasks = tasks;
        _compilation = compilation;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        AdvanceEnabled = advanceEnabled;
        DelayEnabled = delayEnabled;
    }

    /// <inheritdoc/>
    public override bool AdvanceEnabled { get; }

    /// <inheritdoc/>
    public override bool DelayEnabled { get; }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<IT> Tasks => _tasks;

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary => _timeBoundary;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public override Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
