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
/// 任务超过最大提前时间影子价格键 / Task over-max advance shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskOverMaxAdvanceShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    public TaskOverMaxAdvanceShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskOverMaxAdvanceShadowPriceKey<E, A>)) {
        Task = task;
    }
    public IAbstractTask<E, A> Task { get; }
    public override bool Equals(object? obj) => obj is TaskOverMaxAdvanceShadowPriceKey<E, A> other && Equals(Task, other.Task);
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务超过最大提前时间约束 / Task over-max advance time constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskOverMaxAdvanceTimeConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly TaskTime _taskTime;
    private readonly string _name;

    /// <summary>
    /// 任务超过最大提前时间约束构造 / Task over-max advance time constraint constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOverMaxAdvanceTimeConstraint(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_over_max_advance_time") {
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
    public TaskOverMaxAdvanceTimeConstraint(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_over_max_advance_time") {
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
    /// 每任务一行：`advance_time[任务] ≤ 最大允许提前`，名字 `task_over_max_advance_time_{任务}`。任务筛选（含看起来反了的那一半）见
    /// `OverMaxConstraintRows.Add` 的注释。
    ///
    /// 影子价格提取器按任务读取已回填的价格，见 `Extractor`。
    ///
    /// One row per task: `advance_time[task] ≤ max allowed advance`, named `task_over_max_advance_time_{task}`. Task selection (including the part of the code that looks inverted) is documented on
    /// `OverMaxConstraintRows.Add`. The shadow-price extractor reads the already-filled price per task; see `Extractor`. (The old remark here claimed it needed the solver dual channel,
    /// which was wrong: `ICGPipeline.Refresh` fills the map from the rows' `Args`.)
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "超最大提前约束只能施加到线性元模型 / the over-max advance constraint can only be applied to a linear meta model"));
        }
        return OverMaxConstraintRows.Add(linear, _timeBoundary, _tasks, _taskTime, delay: false, _name);
    }

    /// <summary>
    /// 影子价格提取器 / Shadow-price extractor.
    /// </summary>
    /// <returns>按任务读取 `TaskOverMaxAdvanceShadowPriceKey` 的提取器 / An extractor reading `TaskOverMaxAdvanceShadowPriceKey` per task.</returns>
    /// <remarks>
    /// 与 `TaskDelayTimeConstraint.Extractor` 同形：取**当前任务**那个键上已回填的价格，没有就是零。
    /// 价格由 `Refresh` 写进映射，而 `Refresh` 的默认实现（`ICGPipeline.Refresh`）会按行的 `Args` 收集，
    /// 所以"要等求解器对偶通道"不成立——本仓其它约束早已这样取价。
    ///
    /// Same shape as `TaskDelayTimeConstraint.Extractor`: read the price already filled under **this task's** key, zero when absent. `Refresh` is what fills the map, and its default implementation
    /// (`ICGPipeline.Refresh`) collects by the row's `Args`, so "waiting for the solver's dual channel" does not hold — the other constraints in this repository already read prices this way.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task is IAbstractTask<E, A> task) {
                var key = new TaskOverMaxAdvanceShadowPriceKey<E, A>(task);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <summary>
    /// 从对偶解认回本管线的行 / Recognise this pipeline's rows in the dual solution.
    /// </summary>
    /// <param name="shadowPriceMap">影子价格表 / The shadow-price map.</param>
    /// <param name="model">模型（本实现不用）/ The model (unused here).</param>
    /// <param name="shadowPrices">对偶解 / The dual solution.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 按**行上带的键类型**筛选，不看约束分组：`OverMaxConstraintRows.Add` 用 `group: null` 加行，
    /// 走分组默认实现一行都认不回来。与 `TaskDelayLastEndTimeConstraint.Refresh` 同形。
    ///
    /// Filters by **the key type carried on the row** rather than by constraint group: `OverMaxConstraintRows.Add` adds rows with `group: null`, so a group-based default recognises none of them.
    /// Same shape as `TaskDelayLastEndTimeConstraint.Refresh`.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is TaskOverMaxAdvanceShadowPriceKey<E, A> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
