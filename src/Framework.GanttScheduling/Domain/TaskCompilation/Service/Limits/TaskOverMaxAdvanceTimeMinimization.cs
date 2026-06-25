#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务超过最大提前时间最小化 / Task over-max advance time minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskOverMaxAdvanceTimeMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<T> _tasks;
    private readonly TaskTime _taskTime;
    private readonly string _name;

    /// <summary>
    /// 任务超过最大提前时间最小化构造 / Task over-max advance time minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOverMaxAdvanceTimeMinimization(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        string name = "task_over_max_advance_time_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _tasks = tasks;
        _taskTime = taskTime;
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOverMaxAdvanceTimeMinimization(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        string name = "task_over_max_advance_time_minimization") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => null;

    /// <inheritdoc/>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}
