#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 偏移方向 / The deviation direction.
/// </summary>
public enum DeviationDirection {
    /// <summary>不允许比计划晚 / Must not start later than planned.</summary>
    Delay,
    /// <summary>不允许比计划早 / Must not start earlier than planned.</summary>
    Advance,
}

/// <summary>
/// 单任务的偏移约束行 / The per-task deviation rows.
/// </summary>
/// <remarks>
/// 两个约束（`TaskDelayTimeConstraint` / `TaskAdvanceTimeConstraint`）生出的是**同一种行**，只差比较方向：
/// <list type="bullet">
/// <item>不允许延迟 → `est &lt;= 计划开始`（不能晚于计划）；</item>
/// <item>不允许提前 → `est &gt;= 计划开始`（不能早于计划）。</item>
/// </list>
/// **方向写反不会让模型不可行**——它只是把"不许晚"变成"不许早"，而两个方向通常都有可行解，于是计划悄悄偏到另一边。
///
/// Both constraints emit **the same row**, differing only in direction: "may not delay" → `est &lt;= planned start`, "may not advance" → `est &gt;= planned start`.
/// **Swapping the directions does not make the model infeasible** — it merely turns "not later" into "not earlier", both of which usually have feasible solutions, and the plan
/// quietly slides to the other side.
/// </remarks>
public static class DeviationConstraintRows {
    /// <summary>
    /// 按方向添加约束行 / Add the rows for one direction.
    /// </summary>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="timeBoundary">时间边界 / The time boundary.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="taskTime">任务时间对象 / The task time.</param>
    /// <param name="direction">偏移方向 / The direction.</param>
    /// <param name="name">约束名前缀 / The constraint-name prefix.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **哪些任务被约束**：有排定时间（否则"相对什么"没有定义），并且——只有在对应方向的全局开关**开着**时才据此过滤——
    /// 该任务**自身**在该方向上不允许移动。全局开关关着时所有任务都被钉住（它们本来就钉住了，约束是冗余但无害）。
    ///
    /// **Which tasks are constrained**: those with a scheduled time (otherwise "relative to what" is undefined) and — only when the corresponding global switch is **on** does
    /// the per-task switch filter them — those that may not move in that direction themselves. With the global switch off every task is pinned anyway, so the row is redundant
    /// but harmless.
    /// </remarks>
    public static Try Add<E, A>(
        IAbstractLinearMetaModel<Flt64> linear,
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        DeviationDirection direction,
        string name)
        where E : Executor
        where A : IAssignmentPolicy<E> {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(timeBoundary);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskTime);

        bool globalEnabled = direction == DeviationDirection.Delay ? taskTime.DelayEnabled : taskTime.AdvanceEnabled;
        IEnumerable<IAbstractTask<E, A>> selected = tasks.Where(task => task.ScheduledTime is not null);
        if (globalEnabled) {
            selected = selected.Where(task =>
                direction == DeviationDirection.Delay ? !task.DelayEnabled : !task.AdvanceEnabled);
        }

        foreach (IAbstractTask<E, A> task in selected) {
            TimeRange scheduled = task.ScheduledTime!;
            if (!taskTime.TryEstimateStart(task, out object? symbol)
                || symbol is not ILinearIntermediateSymbol<Flt64> start) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务时间尚未注册该任务的预估开始时间：" + task.Id
                    + " / the task time has not registered this task's estimated start: " + task.Id));
            }
            Flt64 planned = timeBoundary.ValueOf(scheduled.Start);
            // **行必须带影子价格键**：对偶是求解器按行返回的，管线要认回自己那一行只能靠行上的 `args`。
            // 基线这里是 `args = Task{Delay,Advance}TimeShadowPriceKey(task)`；少了它，提取器读一张永远空的表，
            // 列生成按零价格挑列**且不报错**。
            //
            // **The row must carry a shadow-price key**: duals come back per row, so recognising its own row requires the `args` on the row. The baseline passes
            // `args = Task{Delay,Advance}TimeShadowPriceKey(task)`; without it the extractor reads a forever-empty table and column generation selects at zero prices **without raising**.
            Fuookami.Ospf.Framework.Model.ShadowPriceKey key = direction == DeviationDirection.Delay
                ? new TaskDelayTimeShadowPriceKey<E, A>(task)
                : new TaskAdvanceTimeShadowPriceKey<E, A>(task);
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    start.Polynomial,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), planned),
                    direction == DeviationDirection.Delay ? Comparison.LE : Comparison.GE,
                    name + "_" + task.Id),
                group: null,
                name: name + "_" + task.Id,
                args: key);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
