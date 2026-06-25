#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
/// <summary>
/// 抽象任务束编译聚合 / Abstract bunch compilation aggregation
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public abstract class AbstractBunchCompilationAggregation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 抽象任务束编译聚合构造 / Abstract bunch compilation aggregation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    protected AbstractBunchCompilationAggregation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true) {
        Compilation = new BunchCompilation<B, T, E, A>(tasks, executors, lockCancelTasks, withExecutorLeisure);
    }

    /// <summary>任务束编译 / Bunch compilation</summary>
    public BunchCompilation<B, T, E, A> Compilation { get; }

    /// <summary>任务束迭代列表 / Bunch iteration list</summary>
    public IReadOnlyList<IReadOnlyList<B>> BunchesIteration => Compilation.BunchesIteration;

    /// <summary>所有任务束列表 / All bunches list</summary>
    public IReadOnlyList<B> Bunches => Compilation.Bunches;

    /// <summary>已移除任务束集合 / Removed bunches set</summary>
    public IReadOnlySet<B> RemovedBunches => Compilation.RemovedBunches;

    /// <summary>最后迭代的任务束列表 / Last iteration bunches</summary>
    public IReadOnlyList<B> LastIterationBunches => Compilation.LastIterationBunches;

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try Register(object model) => Compilation.Register(model);

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
        object model) => Compilation.AddColumns(iteration, newBunches, model);

    /// <summary>
    /// 添加列（异步版本） / Add columns (async version)
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    public virtual async Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> AddColumnsAsync(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) => await Compilation.AddColumnsAsync(iteration, newBunches, model);

    /// <summary>
    /// 移除列 / Remove columns
    /// </summary>
    /// <param name="maximumReducedCost">最大约简成本 / Maximum reduced cost</param>
    /// <param name="maximumColumnAmount">最大列数 / Maximum column amount</param>
    /// <param name="reducedCost">约简成本函数 / Reduced cost function</param>
    /// <param name="fixedBunches">固定任务束集合 / Set of fixed bunches</param>
    /// <param name="keptBunches">保留任务束集合 / Set of kept bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>更新后的最大约简成本 / Updated maximum reduced cost</returns>
    public virtual Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Func<B, Flt64> reducedCost,
        ISet<B> fixedBunches,
        ISet<B> keptBunches,
        object model) {
        foreach (B bunch in Bunches) {
            if (RemovedBunches.Contains(bunch)) {
                continue;
            }

            if (reducedCost(bunch) >= maximumReducedCost
                && !fixedBunches.Contains(bunch)
                && !keptBunches.Contains(bunch)) {
                Compilation.Aggregation.RemoveColumn(bunch);
            }
        }

        ulong remainingAmount = (ulong)Bunches.Count;
        if (remainingAmount > maximumColumnAmount) {
            return Results.Ok<Flt64>(NextReducedCostCutoff(maximumReducedCost));
        }
        return Results.Ok(maximumReducedCost);
    }

    /// <summary>
    /// 提取固定任务束 / Extract fixed bunches
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>固定任务束集合 / Set of fixed bunches</returns>
    public virtual Result<ISet<B>, ErrorCode, Error<ErrorCode>> ExtractFixedBunches(ulong iteration, object model) => Results.Ok<ISet<B>>(new HashSet<B>());

    /// <summary>
    /// 提取保留任务束 / Extract kept bunches
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>保留任务束集合 / Set of kept bunches</returns>
    public virtual Result<ISet<B>, ErrorCode, Error<ErrorCode>> ExtractKeptBunches(ulong iteration, object model) => Results.Ok<ISet<B>>(new HashSet<B>());

    /// <summary>
    /// 提取保留任务束及比率 / Extract kept bunches with ratio
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>任务束到比率的映射 / Map of bunch to ratio</returns>
    public virtual Result<IReadOnlyDictionary<B, Flt64>, ErrorCode, Error<ErrorCode>> ExtractKeptBunchesWithRatio(
        ulong iteration, object model) => Results.Ok<IReadOnlyDictionary<B, Flt64>>(new Dictionary<B, Flt64>());

    /// <summary>
    /// 提取隐藏执行器 / Extract hidden executors
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>隐藏执行器集合 / Set of hidden executors</returns>
    public virtual Result<ISet<E>, ErrorCode, Error<ErrorCode>> ExtractHiddenExecutors(
        IReadOnlyList<E> executors, object model) => Results.Ok<ISet<E>>(new HashSet<E>());

    /// <summary>
    /// 全局固定 / Globally fix
    /// </summary>
    /// <param name="fixedBunches">固定任务束集合 / Set of fixed bunches</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try GloballyFix(ISet<B> fixedBunches) => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 局部固定 / Locally fix
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="bar">阈值 / Threshold</param>
    /// <param name="fixedBunches">固定任务束集合 / Set of fixed bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="withFixNot">固定非选中项 / Whether to fix not-selected items</param>
    /// <returns>新固定的任务束集合 / Set of newly fixed bunches</returns>
    public virtual Result<ISet<B>, ErrorCode, Error<ErrorCode>> LocallyFix(
        ulong iteration,
        Flt64 bar,
        ISet<B> fixedBunches,
        object model,
        bool withFixNot = false) => Results.Ok<ISet<B>>(new HashSet<B>());

    /// <summary>
    /// 记录结果 / Log result
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try LogResult(ulong iteration, object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 记录任务束成本 / Log bunch cost
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try LogBunchCost(ulong iteration, object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 刷新变量范围 / Flush variable ranges
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try Flush(ulong iteration, IReadOnlyList<T>? tasks = null, IReadOnlySet<T>? lockCancelTasks = null) => Results.Ok<Success>(Results.SuccessInstance);

    private static Flt64 NextReducedCostCutoff(Flt64 maximumReducedCost) {
        long reducedCostCutoff = (long)(maximumReducedCost.ToDouble() * 2.0 / 3.0);
        return new Flt64(System.Math.Max(reducedCostCutoff, 5L));
    }
}

/// <summary>
/// 任务束编译聚合 / Bunch compilation aggregation
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchCompilationAggregation<B, T, E, A> : AbstractBunchCompilationAggregation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 任务束编译聚合构造 / Bunch compilation aggregation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    public BunchCompilationAggregation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true)
        : base(tasks, executors, lockCancelTasks, withExecutorLeisure) {
    }
}

/// <summary>
/// 带时间的任务束编译聚合 / Bunch compilation aggregation with time
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchCompilationAggregationWithTime<B, T, E, A> : AbstractBunchCompilationAggregation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 带时间的任务束编译聚合构造 / Bunch compilation aggregation with time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    /// <param name="makespanExtra">是否额外计算完工时间 / Whether to compute makespan extra</param>
    public BunchCompilationAggregationWithTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true,
        TimeSpan? redundancyRange = null,
        bool makespanExtra = false)
        : base(tasks, executors, lockCancelTasks, withExecutorLeisure) {
        TaskTime = new BunchSchedulingTaskTime<B, T, E, A>(timeWindow, tasks, Compilation, redundancyRange);
        Makespan = new Makespan<T, E, A>(tasks, TaskTime, makespanExtra);
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    /// <param name="makespanExtra">是否额外计算完工时间 / Whether to compute makespan extra</param>
    public BunchCompilationAggregationWithTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true,
        TimeSpan? redundancyRange = null,
        bool makespanExtra = false)
        : base(tasks, executors, lockCancelTasks, withExecutorLeisure) {
        TaskTime = new BunchSchedulingTaskTime<B, T, E, A>(timeBoundary, tasks, Compilation, redundancyRange);
        Makespan = new Makespan<T, E, A>(tasks, TaskTime, makespanExtra);
    }

    /// <summary>任务时间 / Task time</summary>
    public BunchSchedulingTaskTime<B, T, E, A> TaskTime { get; }

    /// <summary>最大完工时间 / Makespan</summary>
    public Makespan<T, E, A> Makespan { get; }

    /// <inheritdoc/>
    public override Try Register(object model) {
        Try result = base.Register(model);
        if (result.IsFailed) {
            return result;
        }

        result = TaskTime.Register(model);
        if (result.IsFailed) {
            return result;
        }

        result = Makespan.Register(model);
        if (result.IsFailed) {
            return result;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public override Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) {
        Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> result = base.AddColumns(iteration, newBunches, model);
        if (result.IsFailed) {
            return result;
        }

        IReadOnlyList<B> undupBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)result).Value;
        TaskTime.AddColumns(iteration, undupBunches, model);

        return Results.Ok(undupBunches);
    }
}
