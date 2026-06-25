#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 编译接口 / Compilation interface
/// </summary>
public interface ICompilation {
    /// <summary>是否启用任务取消 / Whether task cancellation is enabled</summary>
    bool TaskCancelEnabled { get; }

    /// <summary>是否包含执行器空闲 / Whether to include executor leisure</summary>
    bool WithExecutorLeisure { get; }

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);
}

/// <summary>
/// 任务编译 / Task compilation
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCompilation<T, E, A> : ICompilation
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockCancelTasks;

    /// <summary>
    /// 任务编译构造 / Task compilation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="taskCancelEnabled">是否启用任务取消 / Whether task cancellation is enabled</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    public TaskCompilation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool taskCancelEnabled = false,
        bool withExecutorLeisure = false) {
        _tasks = tasks;
        _executors = executors;
        _lockCancelTasks = lockCancelTasks ?? new HashSet<T>();
        TaskCancelEnabled = taskCancelEnabled;
        WithExecutorLeisure = withExecutorLeisure;
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled { get; }

    /// <inheritdoc/>
    public bool WithExecutorLeisure { get; }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 迭代任务编译 / Iterative task compilation
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class IterativeTaskCompilation<IT, T, E, A> : ICompilation
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _originTasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockedCancelTasks;
    private readonly TaskAggregation<IT, E, A> _aggregation = new();

    /// <summary>
    /// 迭代任务编译构造 / Iterative task compilation constructor
    /// </summary>
    /// <param name="originTasks">原始任务列表 / List of origin tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockedCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    public IterativeTaskCompilation(
        IReadOnlyList<T> originTasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockedCancelTasks = null) {
        _originTasks = originTasks;
        _executors = executors;
        _lockedCancelTasks = lockedCancelTasks ?? new HashSet<T>();
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => true;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => true;

    /// <summary>原始任务列表 / List of origin tasks</summary>
    public IReadOnlyList<T> OriginTasks => _originTasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>任务聚合 / Task aggregation</summary>
    public TaskAggregation<IT, E, A> Aggregation => _aggregation;

    /// <summary>任务迭代列表 / Task iteration list</summary>
    public IReadOnlyList<IReadOnlyList<IT>> TasksIteration => _aggregation.TasksIteration;

    /// <summary>所有任务列表 / All tasks list</summary>
    public IReadOnlyList<IT> Tasks => _aggregation.Tasks;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
