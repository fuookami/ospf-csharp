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
/// 任务延迟最晚结束时间影子价格键 / Task delay last-end-time shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskDelayLastEndTimeShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    public TaskDelayLastEndTimeShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskDelayLastEndTimeShadowPriceKey<E, A>)) {
        Task = task;
    }
    public IAbstractTask<E, A> Task { get; }
    public override bool Equals(object? obj) => obj is TaskDelayLastEndTimeShadowPriceKey<E, A> other && Equals(Task, other.Task);
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务延迟最晚结束时间约束 / Task delay last-end-time constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskDelayLastEndTimeConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly TaskTime _taskTime;
    private readonly string _name;

    /// <summary>
    /// 任务延迟最晚结束时间约束构造 / Task delay last-end-time constraint constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskDelayLastEndTimeConstraint(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_delay_last_end_time") {
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
    public TaskDelayLastEndTimeConstraint(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_delay_last_end_time") {
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
    /// <remarks>
    /// 每任务一行：`estEnd &lt;= 最晚结束时间`，名字 `task_delay_last_end_time_{任务}`。任务筛选（含基线里看起来反了的那一半）见
    /// `EndConstraintRows.Add` 的注释。
    ///
    /// 影子价格**已接线**：行上带 `args`（本类键类型），`Refresh` 从对偶解里认回这些行，`Extractor` 按任务取价。
    ///
    /// One row per task: `estEnd &lt;= the last allowed end`, named `task_delay_last_end_time_{task}`. Task selection (including the part of the baseline that looks inverted) is documented on `EndConstraintRows.Add`.
    /// The shadow price **is wired**: rows carry `args` (this class's key type), `Refresh` recognises those rows in the dual solution, and `Extractor` reads the price by task.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "结束时间偏移约束只能施加到线性元模型 / the end-time deviation constraint can only be applied to a linear meta model"));
        }
        return EndConstraintRows.Add(
            linear, _timeBoundary, _tasks, _taskTime, delaySide: true, _name,
            argsOf: task => new TaskDelayLastEndTimeShadowPriceKey<E, A>(task));
    }
    /// <inheritdoc/>
    /// <remarks>
    /// 按 `args.Task` 认回该任务的价格；`Task == null`（执行器级询问）返回零，而不是把某个任务的价格当成所有任务的。
    ///
    /// Resolves the price by `args.Task`; a `Task == null` query (executor level) returns zero rather than reusing one task's price for every task.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task is not { } asked) {
                return Flt64.Zero;
            }
            var key = new TaskDelayLastEndTimeShadowPriceKey<E, A>(asked);
            return map.Map.TryGetValue(key, out ShadowPrice? price) ? price.Price : Flt64.Zero;
        };

    /// <inheritdoc/>
    /// <remarks>
    /// 只认 `Args` 是本类键类型的行，别的管线的行一律不碰。这是"对偶 → 价格 → 约简成本"链条里此前缺的一环：
    /// 没有它，`Extractor` 读的是一张**永远为空**的表，约简成本于是恒等于自身的成本——列生成看起来在跑，其实按零价格在挑列。
    ///
    /// Only rows whose `Args` is this class's key type; rows belonging to other pipelines are left alone. This is the link that was missing in "dual → price → reduced cost": without
    /// it, `Extractor` reads a table that is **forever empty**, reduced cost degenerates to the column's own cost, and column generation appears to run while selecting at zero prices.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is TaskDelayLastEndTimeShadowPriceKey<E, A> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
