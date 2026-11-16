using System;
using System.Collections.Generic;
// 任务提前时间最小化的代价核心：两个分支，按"是否超过允许提前量"分别计价。
// Cost core of task advance time minimization: two branches, priced by whether the advance exceeds the allowance.

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 任务提前时间的代价核心 / The cost core of task advance time.
/// </summary>
/// <remarks>
/// `invoke` 里走两条分支：阈值恰为零时直接线性计入 `coefficient * advanceTime`；
/// 阈值非零时引入一个松弛变量，只罚超出阈值的部分。**两条分支的取值必须相同**——
/// 零阈值只是省掉一个变量，不是换一套语义。若它们哪怕差一点点，"设了阈值"和"没设阈值"就会给出不同的解，
/// 而这在配置层面完全看不出。
///
/// The code has two branches in `invoke`: with a threshold of exactly zero it charges `coefficient * advanceTime` linearly; with a non-zero threshold it introduces a slack
/// variable and penalises only the excess. **Both branches must give the same value** — a zero threshold merely saves one variable, it does not swap semantics. If they diverge
/// even slightly, "configured a threshold" and "no threshold" produce different solutions, and that is invisible at the configuration level.
///
/// **提不出系数不是"系数为一"**。`coefficient(task)` 返回空时取 `infinity`，即该任务的提前时间**不可定价**。
/// C# 的 `Flt64` 基于 `decimal`，没有无穷；静默取 `1` 会把"禁止提前"变成"每单位罚 1"，
/// 于是本该被拒绝的排程被轻松接受。所以这里返回失败。
///
/// **A missing coefficient is not "one"**. The code takes `infinity` when `coefficient(task)` is null, meaning that task's advance **cannot be priced**. C#'s `Flt64` is built on
/// `decimal` and has no infinity; silently defaulting to `1` turns "advance prohibited" into "penalty 1 per unit", so a schedule that should be rejected is accepted cheaply. Hence
/// the failure here.
/// </remarks>
public static class TaskAdvanceTimeCost {
    /// <summary>
    /// 计算一个任务的提前时间代价 / Compute one task's advance-time cost.
    /// </summary>
    /// <param name="advance">提前时间（时间单位）/ The advance amount in time units.</param>
    /// <param name="threshold">可选阈值 / The optional threshold.</param>
    /// <param name="coefficient">可选系数 / The optional coefficient.</param>
    /// <returns>代价或失败 / The cost, or a failure.</returns>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Cost(
        Flt64 advance,
        Flt64? threshold,
        Flt64? coefficient) {
        if (coefficient is not { } weight) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "提前时间系数提取失败，该任务的提前时间不可定价"
                + " / Advance-time coefficient extraction failed; this task's advance cannot be priced"));
        }

        Flt64 bar = threshold ?? Flt64.Zero;
        Flt64 excess = advance - bar;
        if (excess.ToDouble() <= 0.0) {
            return Results.Ok(Flt64.Zero);
        }
        return Results.Ok(weight * excess);
    }

    /// <summary>
    /// 判断一个任务是否参与提前时间最小化 / Whether a task takes part in advance-time minimization.
    /// </summary>
    /// <param name="globalAdvanceEnabled">全局开关 / The global switch.</param>
    /// <param name="tasks">候选任务 / The candidate tasks.</param>
    /// <param name="taskAdvanceEnabled">任务本身是否允许提前 / Whether the task itself allows advancing.</param>
    /// <param name="scheduled">任务是否已排定 / Whether the task is scheduled.</param>
    /// <returns>是否参与 / Whether it takes part.</returns>
    /// <remarks>
    /// 在**全局开关打开时**才筛选，且筛选条件是"任务允许提前 **且** 已排定"。
    /// 未排定的任务没有提前时间可言；不允许提前的任务就算算进去也恒为零。两者都算进去只会让目标多项式变长。
    ///
    /// The code filters only when the **global switch is on**, and the condition is "the task allows advancing **and** is scheduled". An unscheduled task has no advance to
    /// speak of; a task that forbids advancing contributes a constant zero. Including either only lengthens the objective polynomial.
    /// </remarks>
    public static IReadOnlyList<T> Select<T>(
        bool globalAdvanceEnabled,
        IReadOnlyList<T> tasks,
        Func<T, bool> taskAdvanceEnabled,
        Func<T, bool> scheduled) {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(taskAdvanceEnabled);
        System.ArgumentNullException.ThrowIfNull(scheduled);

        if (!globalAdvanceEnabled) {
            return Array.Empty<T>();
        }

        var selected = new List<T>();
        foreach (T task in tasks) {
            if (taskAdvanceEnabled(task) && scheduled(task)) {
                selected.Add(task);
            }
        }
        return selected;
    }
}
