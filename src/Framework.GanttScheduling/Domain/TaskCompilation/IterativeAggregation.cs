#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation;
/// <summary>
/// 抽象迭代任务调度聚合 / Abstract iterative task scheduling aggregation
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public abstract class AbstractIterativeTaskSchedulingAggregation<IT, T, E, A>
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 抽象迭代任务调度聚合构造 / Abstract iterative task scheduling aggregation constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="originTasks">原始任务列表 / List of origin tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockedCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    protected AbstractIterativeTaskSchedulingAggregation(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> originTasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockedCancelTasks = null) {
        TimeBoundary = new SolverTimeWindowBoundary(timeWindow);
        Compilation = new IterativeTaskCompilation<IT, T, E, A>(originTasks, executors, lockedCancelTasks);
        TaskTime = CreateTaskTime(timeWindow, Compilation);
    }

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary { get; }

    /// <summary>迭代任务编译 / Iterative task compilation</summary>
    public IterativeTaskCompilation<IT, T, E, A> Compilation { get; }

    /// <summary>迭代任务时间 / Iterative task time</summary>
    public IterativeTaskSchedulingTaskTime<IT, T, E, A> TaskTime { get; }

    /// <summary>创建迭代任务时间 / Create iterative task time</summary>
    protected virtual IterativeTaskSchedulingTaskTime<IT, T, E, A> CreateTaskTime(
        TimeWindow<Flt64> timeWindow,
        IterativeTaskCompilation<IT, T, E, A> compilation) => new IterativeTaskSchedulingTaskTime<IT, T, E, A>(timeWindow, compilation.Tasks, compilation);
}
