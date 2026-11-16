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
/// 任务提前时间影子价格键 / Task advance time shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskAdvanceTimeShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>构造 / Constructor</summary>
    /// <param name="task">任务 / Task</param>
    public TaskAdvanceTimeShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskAdvanceTimeShadowPriceKey<E, A>)) {
        Task = task;
    }

    /// <summary>任务 / Task</summary>
    public IAbstractTask<E, A> Task { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TaskAdvanceTimeShadowPriceKey<E, A> other && Equals(Task, other.Task);

    /// <inheritdoc/>
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务提前时间约束 / Task advance time constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskAdvanceTimeConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly TaskTime _taskTime;
    private readonly string _name;

    /// <summary>
    /// 任务提前时间约束构造 / Task advance time constraint constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskAdvanceTimeConstraint(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_advance_time") {
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
    public TaskAdvanceTimeConstraint(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        string name = "task_advance_time") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 这里接线：对每个"该钉住"的任务加一行 `est {op} 计划开始`。哪些任务该钉见 `DeviationConstraintRows` 的注释——
    /// 关键是**全局开关开着时才按任务开关过滤**：全局关着时任务本来就被钉在计划开始上，这行是冗余但无害的；
    /// 全局开着时若不过滤，就会把"允许晚一点"的任务也钉死，那等于把延迟开关作废。
    ///
    /// Wired here: one row `est {op} planned start` per task that must be pinned. Which tasks those are is documented on `DeviationConstraintRows` — the point is that
    /// the **per-task switch only filters while the global switch is on**: with it off the task is pinned anyway (redundant but harmless), and with it on, not filtering would pin
    /// even the tasks allowed to move, cancelling the delay switch outright.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "偏移约束只能施加到线性元模型 / the deviation constraint can only be applied to a linear meta model"));
        }
        return DeviationConstraintRows.Add(
            linear, _timeBoundary, _tasks, _taskTime, DeviationDirection.Advance, _name);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task != null) {
                var key = new TaskAdvanceTimeShadowPriceKey<E, A>(args.Task);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。走约束分组的默认实现一行都找不到——`DeviationConstraintRows.Add` 用 `group: null` 加行。
    /// **筛选条件必须是键类型**：两条偏移约束（提前 / 延后）同时存在，靠分组或名字都会串味。
    ///
    /// Recognises this pipeline's rows in the dual solution by **the key type carried on the row**. A group-based default finds none, since `DeviationConstraintRows.Add` adds rows with
    /// `group: null`. **The filter must be the key type**: the two deviation constraints (advance and delay) coexist, and filtering by group or name would let their prices cross.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is TaskAdvanceTimeShadowPriceKey<E, A> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
