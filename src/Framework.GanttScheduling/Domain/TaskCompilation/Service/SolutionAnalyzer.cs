#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;
/// <summary>
/// 任务调度解分析器 / Task scheduling solution analyzer
/// </summary>
public static class SolutionAnalyzer {
    /// <summary>
    /// 分析任务调度解（无时间） / Analyze task scheduling solution (without time)
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="assignedPolicyGenerator">分配策略生成器 / Assignment policy generator</param>
    /// <param name="solution">备选求解向量 / Optional solution vector</param>
    /// <returns>任务解 / Task solution</returns>
    public static Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> Analyze<T, E, A>(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        object model,
        Func<E?, A?> assignedPolicyGenerator,
        IReadOnlyList<Flt64>? solution = null)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var assignedTasks = new List<T>();
        var canceledTasks = new List<T>();

        foreach (T task in tasks) {
            A? policy = assignedPolicyGenerator(default);
            if (policy != null) {
                assignedTasks.Add(task);
            }
            else {
                canceledTasks.Add(task);
            }
        }

        return Results.Ok(new TaskSolution<T, E, A>(assignedTasks, canceledTasks));
    }

    /// <summary>
    /// 分析任务调度解（带时间） / Analyze task scheduling solution (with time)
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="results">求解结果 / Solver results</param>
    /// <param name="model">线性模型 / Linear model</param>
    /// <param name="assignedPolicyGenerator">分配策略生成器 / Assignment policy generator</param>
    /// <param name="solution">备选求解向量 / Optional solution vector</param>
    /// <returns>任务解 / Task solution</returns>
    public static Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> Analyze<T, E, A>(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskSchedulingTaskTime<T, E, A> taskTime,
        IReadOnlyList<Flt64> results,
        object model,
        Func<TimeRange?, E?, A?> assignedPolicyGenerator,
        IReadOnlyList<Flt64>? solution = null)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        return Analyze(timeBoundary, tasks, executors, compilation, taskTime, results, model, assignedPolicyGenerator, solution);
    }

    /// <summary>
    /// 通过 solver 时间窗口边界分析任务调度解 / Analyze task scheduling solution from a solver time-window boundary
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="results">求解结果 / Solver results</param>
    /// <param name="model">线性模型 / Linear model</param>
    /// <param name="assignedPolicyGenerator">分配策略生成器 / Assignment policy generator</param>
    /// <param name="solution">备选求解向量 / Optional solution vector</param>
    /// <returns>任务解 / Task solution</returns>
    public static Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> Analyze<T, E, A>(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskSchedulingTaskTime<T, E, A> taskTime,
        IReadOnlyList<Flt64> results,
        object model,
        Func<TimeRange?, E?, A?> assignedPolicyGenerator,
        IReadOnlyList<Flt64>? solution = null)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var assignedTasks = new List<T>();
        var canceledTasks = new List<T>();

        foreach (T task in tasks) {
            A? policy = assignedPolicyGenerator(null, default);
            if (policy != null) {
                assignedTasks.Add(task);
            }
            else {
                canceledTasks.Add(task);
            }
        }

        return Results.Ok(new TaskSolution<T, E, A>(assignedTasks, canceledTasks));
    }

    /// <summary>
    /// 分析迭代任务调度解 / Analyze iterative task scheduling solution
    /// </summary>
    /// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="originTasks">原始任务列表 / List of origin tasks</param>
    /// <param name="tasks">任务迭代列表 / Task iteration list</param>
    /// <param name="compilation">迭代任务编译结果 / Iterative task compilation result</param>
    /// <param name="model">线性模型 / Linear model</param>
    /// <param name="solution">备选求解向量 / Optional solution vector</param>
    /// <returns>任务解 / Task solution</returns>
    public static Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeIterative<IT, T, E, A>(
        ulong iteration,
        IReadOnlyList<T> originTasks,
        IReadOnlyList<IReadOnlyList<IT>> tasks,
        IterativeTaskCompilation<IT, T, E, A> compilation,
        object model,
        IReadOnlyList<Flt64>? solution = null)
        where IT : IIterativeAbstractTask<E, A>
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var assignedTasks = new List<T>();
        var canceledTasks = new List<T>();

        foreach (T task in originTasks) {
            canceledTasks.Add(task);
        }

        return Results.Ok(new TaskSolution<T, E, A>(assignedTasks, canceledTasks));
    }
}
