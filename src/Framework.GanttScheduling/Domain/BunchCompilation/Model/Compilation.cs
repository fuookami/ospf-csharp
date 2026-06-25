#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束编译 / Bunch compilation
/// </summary>
/// <remarks>
/// 管理 y/z/bunchCost/taskAssignment/taskCompilation/executorCompilation 变量。
/// Manages y/z/bunchCost/taskAssignment/taskCompilation/executorCompilation variables.
/// </remarks>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchCompilation<B, T, E, A> : ICompilation
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockCancelTasks;
    private readonly BunchAggregation<B, T, E, A> _aggregation;

    /// <summary>
    /// 任务束编译构造 / Bunch compilation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    /// <param name="bunchAggregation">任务束聚合 / Bunch aggregation</param>
    public BunchCompilation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true,
        BunchAggregation<B, T, E, A>? bunchAggregation = null) {
        _tasks = tasks;
        _executors = executors;
        _lockCancelTasks = lockCancelTasks ?? new HashSet<T>();
        WithExecutorLeisure = withExecutorLeisure;
        _aggregation = bunchAggregation ?? new BunchAggregation<B, T, E, A>();

        // Ensure executors are indexed
        if (!executors.All(e => e.Indexed)) {
            ManualIndexed.Impl.Flush(typeof(Executor));
            foreach (E executor in executors) {
                executor.SetIndexed();
            }
        }

        // Ensure tasks are indexed
        if (!tasks.All(t => t.Indexed)) {
            ManualIndexed.Impl.Flush(typeof(IAbstractTask<E, A>));
            foreach (T task in tasks) {
                if (task is ManualIndexed manual) {
                    manual.SetIndexed();
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => true;

    /// <inheritdoc/>
    public bool WithExecutorLeisure { get; }

    /// <summary>任务束聚合 / Bunch aggregation</summary>
    public BunchAggregation<B, T, E, A> Aggregation => _aggregation;

    /// <summary>任务束迭代列表 / Bunch iteration list</summary>
    public IReadOnlyList<IReadOnlyList<B>> BunchesIteration => _aggregation.BunchesIteration;

    /// <summary>所有任务束列表 / All bunches list</summary>
    public IReadOnlyList<B> Bunches => _aggregation.Bunches;

    /// <summary>已移除任务束集合 / Removed bunches set</summary>
    public IReadOnlySet<B> RemovedBunches => _aggregation.RemovedBunches;

    /// <summary>最后迭代的任务束列表 / Last iteration bunches</summary>
    public IReadOnlyList<B> LastIterationBunches => _aggregation.LastIterationBunches;

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>锁定取消任务集合 / Set of locked cancel tasks</summary>
    public IReadOnlySet<T> LockCancelTasks => _lockCancelTasks;

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    public virtual Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) {
        IReadOnlyList<B> unduplicated = _aggregation.AddColumns(newBunches);
        return Results.Ok<IReadOnlyList<B>>(unduplicated);
    }

    /// <summary>
    /// 添加列（异步版本） / Add columns (async version)
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    public virtual async System.Threading.Tasks.Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> AddColumnsAsync(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) {
        IReadOnlyList<B> unduplicated = await _aggregation.AddColumnsAsync(newBunches);
        return Results.Ok<IReadOnlyList<B>>(unduplicated);
    }
}
