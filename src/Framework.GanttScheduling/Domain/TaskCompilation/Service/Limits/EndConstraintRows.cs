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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 结束时间偏移的约束行 / The end-time deviation rows.
/// </summary>
/// <remarks>
/// 两个约束（`TaskDelayLastEndTimeConstraint` / `TaskAdvanceEarliestEndTimeConstraint`）生成的是一对镜像行：
/// <list type="bullet">
/// <item>不许拖过最晚结束 → `estEnd &lt;= 最晚结束`；</item>
/// <item>不许早过最早结束 → `estEnd &gt;= 最早结束`。</item>
/// </list>
///
/// 行约束的是**预估结束时间**本身，不是 `delay_last_end_time`：后者是"超了多少"，把它约束到某个值是另一件事。
/// **方向写反不会让模型不可行**——两个方向通常都有可行解，计划只会悄悄偏到另一侧。
///
/// The two constraints emit a mirrored pair: "must not finish after the last end" → `estEnd &lt;= lastEnd`, "must not finish before the earliest end" →
/// `estEnd &gt;= earliestEnd`. The row bounds the **estimated end time** itself, not `delay_last_end_time` (the overshoot); bounding that is a different statement.
/// **Swapping the directions does not make the model infeasible** — both directions usually have feasible solutions and the plan merely slides to the other side.
/// </remarks>
public static class EndConstraintRows {
    /// <summary>
    /// 添加结束时间偏移的约束行 / Add the end-time deviation rows.
    /// </summary>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="timeBoundary">时间边界 / The time boundary.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="taskTime">任务时间对象 / The task time.</param>
    /// <param name="delaySide">`true` 走"不许拖过最晚结束"，`false` 走"不许早过最早结束" / See the type remarks.</param>
    /// <param name="name">约束名前缀 / The constraint-name prefix.</param>
    /// <param name="argsOf">行上携带的对偶回程键，`null` 表示该行不参与价格回程 / The dual return-address key carried on each row; `null` means the row takes no part in price return.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **哪些任务被约束**（照做，包括它看起来反了的地方）：给了目标结束时间，并且——只有在对应方向全局开关**开着**时——
    /// **不**允许移动的那些。开关打开时被约束的正是"本来就不许动"的任务；关掉开关才约束所有给了目标的任务。
    ///
    /// 与超最大约束不同，这里**不跳过**任何行：`estEnd` 对每个任务都存在（不动任务的 `estEnd` 只是被窗口夹住），
    /// 所以"不许动"的任务照样能收到一条有内容的行。
    ///
    /// **Which tasks are constrained** (copied from the code, including the part that looks inverted): those carrying a target end, and — only when the corresponding global
    /// switch is **on** — the ones that may **not** move. With the switch on the constrained tasks are exactly the immovable ones; only with it off does every task with a target
    /// get a row. Unlike the over-max rows of，**no row is skipped** here: `estEnd` exists for every task (for an immovable one it is merely pinned by the window), so an
    /// immovable task still receives a row with content.
    /// </remarks>
    public static Try Add<E, A>(
        IAbstractLinearMetaModel<Flt64> linear,
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        bool delaySide,
        string name,
        Func<IAbstractTask<E, A>, object?>? argsOf = null)
        where E : Executor
        where A : IAssignmentPolicy<E> {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(timeBoundary);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskTime);

        bool globalEnabled = delaySide ? taskTime.DelayLastEndTimeEnabled : taskTime.AdvanceEarliestEndTimeEnabled;
        foreach (IAbstractTask<E, A> task in tasks) {
            DateTimeOffset? targetEnd = delaySide ? task.LastEndTime : task.EarliestEndTime;
            if (targetEnd is not { } target) {
                continue;
            }
            bool mayMove = delaySide ? task.DelayEnabled : task.AdvanceEnabled;
            if (globalEnabled && mayMove) {
                continue;
            }
            if (!taskTime.TryEstimateEnd(task, out object? symbol)
                || symbol is not ILinearIntermediateSymbol<Flt64> end) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务时间尚未注册该任务的预估结束时间：" + task.Id
                    + " / the task time has not registered this task's estimated end: " + task.Id));
            }

            string rowName = name + "_" + task.Id;
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    end.Polynomial,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), timeBoundary.ValueOf(target)),
                    delaySide ? Comparison.LE : Comparison.GE,
                    rowName),
                group: null,
                name: rowName,
                // `args` 是**对偶回程的地址**：求解器按 `MathConstraint` 返回价格，管线要认回自己建的那一行，
                // 只能靠行上带着的键。不带 `args` 的行，价格无处可放——这就是 `Refresh` 之前只能返回零的原因。
                // `args` is the **return address for the dual**: the solver returns prices keyed by `MathConstraint`, and a pipeline can only recognise its own row through the key
                // carried on that row. A row without `args` has nowhere to put the price — exactly why `Refresh` could only return zero before.
                args: argsOf?.Invoke(task));
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
