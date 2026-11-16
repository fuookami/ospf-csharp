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
/// 超最大偏移的约束行 / The over-max deviation rows.
/// </summary>
/// <remarks>
/// 两个约束（`TaskOverMaxDelayTimeConstraint` / `TaskOverMaxAdvanceTimeConstraint`）生出的是**同一种行**：
/// `偏移量 ≤ 最大允许偏移`，名字 `{前缀}_{任务}`。
///
/// 行写的是**偏移量**本身而不是 `over_max_*`：`over_max_*` 是"超过多少"，把它约束到某个值是另一件事；
/// 意思是"偏移不许超过最大允许量"，即 `delayTime ≤ maxDelay`——`over_max_*` 是它的**违反量**。
///
/// Both constraints emit **the same row**: `deviation ≤ maximum`, named `{prefix}_{task}`. The row constrains the **deviation** itself, not `over_max_*`: `over_max_*` is
/// "by how much the limit was exceeded", and bounding that is a different statement. The code means "the deviation must not exceed the maximum", i.e.
/// `delayTime ≤ maxDelay`; `over_max_*` is its violation amount.
/// </remarks>
public static class OverMaxConstraintRows {
    /// <summary>
    /// 添加超最大偏移的约束行 / Add the over-max deviation rows.
    /// </summary>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="timeBoundary">时间边界 / The time boundary.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="taskTime">任务时间对象 / The task time.</param>
    /// <param name="delay">`true` 走延迟方向，`false` 走提前方向 / `true` for delay, `false` for advance.</param>
    /// <param name="name">约束名前缀 / The constraint-name prefix.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **哪些任务被约束**（包括它看起来反了的地方）：给了最大允许量，并且——只有在对应方向全局开关**开着**时——
    /// **不**允许移动的那些。也就是说开关打开时真正被约束的是"本来就不许动"的任务，而对它们偏移量恒为零，行是**恒真的**；
    /// 开关关着时反而约束所有给了上限的任务。这个不对称是刻意的，不是可以顺手改的地方——改了会让约束悄悄多绑一批任务。
    ///
    /// **Which tasks are constrained** (including the part that looks inverted): those carrying a maximum, and — only when the corresponding global
    /// switch is **on** — the ones that may **not** move. So with the switch on the constrained tasks are exactly the immovable ones, whose deviation is identically zero and
    /// whose row is **always true**; with the switch off every task carrying a maximum is constrained. The asymmetry is deliberate, and not something to "fix" quietly —
    /// changing it would silently bind an extra set of tasks.
    /// </remarks>
    public static Try Add<E, A>(
        IAbstractLinearMetaModel<Flt64> linear,
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        TaskTime taskTime,
        bool delay,
        string name)
        where E : Executor
        where A : IAssignmentPolicy<E> {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(timeBoundary);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskTime);

        bool globalEnabled = delay ? taskTime.OverMaxDelayEnabled : taskTime.OverMaxAdvanceEnabled;
        foreach (IAbstractTask<E, A> task in tasks) {
            TimeSpan? limit = delay ? task.MaxDelay : task.MaxAdvance;
            if (limit is not { } maximum) {
                continue;
            }
            bool mayMove = delay ? task.DelayEnabled : task.AdvanceEnabled;
            if (globalEnabled && mayMove) {
                continue;
            }
            if (!taskTime.TryReadDeviation(task, delay, out object? symbol)
                || symbol is not ILinearIntermediateSymbol<Flt64> deviation) {
                if (!mayMove) {
                    // 不许移动的任务偏移量**恒为零**，行 `0 ≤ 上限` 恒真——不加。
                    // 全局开关开着时保留下来的正是这类任务，所以那一支**一行都不会生成**；这不是漏接线，是行本身没有内容。
                    //
                    // A task that may not move has an identically zero deviation, so `0 ≤ limit` is always true — not added. With the global switch on every kept task is of that
                    // kind, so that branch emits **no rows at all**; that is not a missing wiring, the rows simply have no content.
                    continue;
                }
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务时间尚未注册该任务的偏移量：" + task.Id
                    + " / the task time has not registered this task's deviation: " + task.Id));
            }

            string rowName = name + "_" + task.Id;
            // **行必须带影子价格键**：对偶是求解器按行返回的，管线要认回自己那一行只能靠行上的 `args`。
            // 这里给行挂 `args = TaskOverMax{Delay,Advance}ShadowPriceKey(task)`；少了它，提取器读的是一张永远空的表，
            // 于是列生成按零价格挑列而且不报错——**行加了、键没挂**是最难发现的那种半接线。
            //
            // **The row must carry a shadow-price key**: duals come back per row, so recognising its own row requires the `args` on the row. The row carries
            // `args = TaskOverMax{Delay,Advance}ShadowPriceKey(task)` here; without it the extractor reads a forever-empty table and column generation selects at zero prices without raising —
            // **the row is added but the key is not attached**, which is the hardest kind of half-wiring to notice.
            Fuookami.Ospf.Framework.Model.ShadowPriceKey key = delay
                ? new TaskOverMaxDelayShadowPriceKey<E, A>(task)
                : new TaskOverMaxAdvanceShadowPriceKey<E, A>(task);
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    deviation.Polynomial,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), timeBoundary.ValueOf(maximum)),
                    Comparison.LE,
                    rowName),
                group: null,
                name: rowName,
                args: key);
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
