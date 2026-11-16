// 任务取消代价 / Task cancellation cost.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 任务取消代价 / Task cancellation cost.
/// </summary>
/// <remarks>
/// 取消一个任务要付 `coefficient` 的代价，目标是最小化总代价。两处边界值得单独钉住：
///
/// 1. **提不出系数不是"系数为一"**。取 `infinity`——该任务的取消**不可定价**，即禁止取消。
/// C# 的 `Flt64` 基于 `decimal`，没有无穷；静默取 `1` 会把"禁止取消"变成"取消一次罚 1"，
///    于是本该被拒绝的排程被轻松接受，而**多取消一个任务只是目标值小一点点**。
/// 2. **全局开关关闭时一个任务都不进目标**。此时"能不能取消"根本不是一个被建模的问题，
///    把任务留在目标里只会让多项式变长，并给求解器一个它不该有的自由度。
///
/// Cancelling a task costs `coefficient`, and the objective minimises the total. Two boundaries are worth pinning:
///
/// 1. **A missing coefficient is not "one".** The implementation takes `infinity` — that task's cancellation **cannot be priced**, i.e. it is forbidden. C#'s `Flt64` is built on
///    `decimal` and has no infinity; defaulting to `1` turns "cancellation forbidden" into "cancelling once costs 1", so a schedule that should be rejected is accepted cheaply —
///    **one extra cancellation merely lowers the objective a little**.
/// 2. **With the global switch off, no task enters the objective.** Whether a task can be cancelled is simply not modelled then; keeping tasks in the objective only lengthens
///    the polynomial and hands the solver a degree of freedom it should not have.
/// </remarks>
public static class TaskCancelCost {
    /// <summary>
    /// 计算一个任务的取消代价 / Compute one task's cancellation cost.
    /// </summary>
    /// <param name="coefficient">可选系数 / The optional coefficient.</param>
    /// <returns>代价或失败 / The cost, or a failure.</returns>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Cost(Flt64? coefficient) {
        if (coefficient is not { } weight) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "取消系数提取失败，该任务的取消不可定价 / Cancellation coefficient extraction failed; this task's cancellation cannot be priced"));
        }
        return Results.Ok(weight);
    }

    /// <summary>
    /// 选出参与取消最小化的任务 / Select the tasks taking part in cancellation minimisation.
    /// </summary>
    /// <param name="globalCancelEnabled">全局开关 / The global switch.</param>
    /// <param name="tasks">候选任务 / The candidate tasks.</param>
    /// <param name="taskCancelEnabled">任务本身是否允许取消 / Whether the task itself allows cancelling.</param>
    /// <returns>参与的任务 / The tasks taking part.</returns>
    public static IReadOnlyList<T> Select<T>(
        bool globalCancelEnabled,
        IReadOnlyList<T> tasks,
        Func<T, bool> taskCancelEnabled) {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(taskCancelEnabled);

        if (!globalCancelEnabled) {
            return Array.Empty<T>();
        }

        var selected = new List<T>();
        foreach (T task in tasks) {
            if (taskCancelEnabled(task)) {
                selected.Add(task);
            }
        }
        return selected;
    }
}
