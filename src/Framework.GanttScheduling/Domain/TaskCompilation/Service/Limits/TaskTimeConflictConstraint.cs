#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务时间冲突约束 / Task time conflict constraint.
/// 检测并约束任务在时间维度上的冲突。
/// Detects and constrains task conflicts in the time dimension.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskTimeConflictConstraint<T, E, A> : IPipeline<object>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly TaskCompilation<T, E, A> _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务时间冲突约束构造 / Task time conflict constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskTimeConflictConstraint(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        string name = "task_time_conflict") {
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _name = name;
        Tasks = tasks
            .Where(task => task.Time is not null && !task.AdvanceEnabled && !task.DelayEnabled)
            .OrderBy(task => task.Time!.Start)
            .ToArray();
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <summary>
    /// 筛选并排序后的任务列表 / The filtered and sorted task list.
    /// </summary>
    /// <remarks>
    /// 只收**时间固定**的任务：`Time != null` 且**不允许提前也不允许延迟**。允许移动的任务"是否重叠"本身是决策，
    /// 静态时间窗重叠不代表一定重叠——把它们当成冲突会**凭空禁掉合法的并行**。
    ///
    /// 按开始时间排序不是装饰：排好序之后"时间窗相交"在几何上只剩相邻候选，而判据（和变异测试）都靠这个顺序才看得出
    /// "把 `i+1` 写成 `i`"这种错。
    ///
    /// Only **fixed-time** tasks are collected: `Time != null` with neither advance nor delay allowed. Whether a movable task overlaps is itself a decision, and a static window
    /// overlap does not mean a real one — treating those as conflicts would **forbid legal parallelism**. The start-time order is not decoration either: once sorted, an
    /// overlapping window is only among neighbours, which is what lets a criterion (and a mutation) notice an `i + 1` written as `i`.
    /// </remarks>
    public IReadOnlyList<T> Tasks { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// 两条**时间固定**且时间窗相交的任务不能在同一台机器上同时做：`x[左, e] + x[右, e] &lt;= 1`。
    ///
    /// Two **fixed-time** tasks with intersecting windows cannot share a machine: `x[left, e] + x[right, e] &lt;= 1`.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务时间冲突约束只能施加到线性元模型 / the time-conflict constraint can only be applied to a linear meta model"));
        }
        return TaskConflictRows.Add(
            linear,
            _executors,
            Tasks,
            _compilation,
            (_, left, right) => left.Time is { } leftTime
                && right.Time is { } rightTime
                && leftTime.WithIntersection(rightTime),
            _name);
    }
}
