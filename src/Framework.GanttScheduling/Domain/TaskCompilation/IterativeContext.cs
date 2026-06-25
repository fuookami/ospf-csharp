#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation;
/// <summary>
/// 抽象迭代任务调度上下文 / Abstract iterative task scheduling context
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public abstract class AbstractIterativeTaskSchedulingContext<IT, T, E, A>
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 迭代任务调度上下文构造 / Iterative task scheduling context constructor
    /// </summary>
    /// <param name="aggregation">迭代任务调度聚合 / Iterative task scheduling aggregation</param>
    protected AbstractIterativeTaskSchedulingContext(
        AbstractIterativeTaskSchedulingAggregation<IT, T, E, A> aggregation) {
        Aggregation = aggregation;
    }

    /// <summary>迭代任务调度聚合 / Iterative task scheduling aggregation</summary>
    public AbstractIterativeTaskSchedulingAggregation<IT, T, E, A> Aggregation { get; }

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary => Aggregation.TimeBoundary;

    /// <summary>迭代任务编译 / Iterative task compilation</summary>
    public IterativeTaskCompilation<IT, T, E, A> Compilation => Aggregation.Compilation;

    /// <summary>迭代任务时间 / Iterative task time</summary>
    public IterativeTaskSchedulingTaskTime<IT, T, E, A> TaskTime => Aggregation.TaskTime;
}
