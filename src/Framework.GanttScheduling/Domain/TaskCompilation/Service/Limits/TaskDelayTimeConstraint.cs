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
/// 任务延迟时间影子价格键 / Task delay time shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskDelayTimeShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    public TaskDelayTimeShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskDelayTimeShadowPriceKey<E, A>)) {
        Task = task;
    }
    public IAbstractTask<E, A> Task { get; }
    public override bool Equals(object? obj) => obj is TaskDelayTimeShadowPriceKey<E, A> other && Equals(Task, other.Task);
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务延迟时间约束 / Task delay time constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskDelayTimeConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly TaskTime _taskTime;
    private readonly string _name;

    /// <summary>
    /// 任务延迟时间约束构造 / Task delay time constraint constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskDelayTimeConstraint(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_delay_time") {
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
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskDelayTimeConstraint(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_delay_time") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task != null) {
                var key = new TaskDelayTimeShadowPriceKey<E, A>(args.Task);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <inheritdoc/>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}
