// 迭代上下文的两阶段注册 / Two-phase registration of the iterative context.
// 与固定 Kotlin 基线 `gantt-scheduling-domain-task-compilation-context/.../IterativeContext.kt` 的 `register`
// 与 `columnAmount` 对应。基线的其余方法全是纯转发，逻辑在 `IterativeTaskCompilationAggregation`。
// Mirrors `register` and `columnAmount` in the pinned baseline's `IterativeContext.kt`. Everything else there is pure
// forwarding; the logic lives in `IterativeTaskCompilationAggregation`.

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;

/// <summary>
/// 迭代上下文的两阶段注册 / Two-phase registration of the iterative context.
/// </summary>
/// <remarks>
/// 基线把注册拆成两步：**先 `aggregation.register(model)`，成功后才 `pipelineList(model)`**。
/// 顺序不是风格，是语义——管线会在模型上追加约束。若第一步失败还继续跑第二步，
/// 管线就会把约束加到**一个只注册了一半的模型**上：约束名指向不存在的列，或者互相冲突，
/// 而错误最终会在很久之后以"模型不可解"的形式出现，**与真正的原因隔了好几层**。
///
/// 所以这里用短路：第一步非 `Ok` 就直接返回，第二步根本不执行。
///
/// The baseline splits registration into two steps: **`aggregation.register(model)` first, `pipelineList(model)` only on success**. The order is semantics, not style — the
/// pipelines append constraints to the model. Continuing to step two after step one failed attaches those constraints to **a half-registered model**: constraint names point at
/// columns that do not exist or contradict each other, and the error surfaces much later as "the model is infeasible", **several layers away from its actual cause**.
///
/// Hence the short-circuit here: a non-`Ok` step one returns immediately and step two never runs.
/// </remarks>
public static class IterativeContext {
    /// <summary>
    /// 两阶段注册 / Two-phase registration.
    /// </summary>
    /// <param name="registerAggregation">第一步：注册聚合层 / Step one: register the aggregation.</param>
    /// <param name="runPipelines">第二步：运行管线 / Step two: run the pipeline list.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public static Try Register(Func<Try> registerAggregation, Func<Try> runPipelines) {
        System.ArgumentNullException.ThrowIfNull(registerAggregation);
        System.ArgumentNullException.ThrowIfNull(runPipelines);

        Try first = registerAggregation();
        if (first is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return first;
        }
        return runPipelines();
    }

    /// <summary>
    /// 列数由任务数导出 / The column count is derived from the task count.
    /// </summary>
    /// <param name="taskCount">任务数量 / The task count.</param>
    /// <returns>列数 / The column count.</returns>
    /// <remarks>
    /// 基线的 `columnAmount` 是 `UInt64(aggregation.tasks.size)`——**每次读取都重新计算**，不是缓存字段。
    /// 若缓存下来，列生成过程中追加任务后它就不再有意义，而调用方会按过期的上限做决策。
    ///
    /// The baseline's `columnAmount` is `UInt64(aggregation.tasks.size)` — **recomputed on every read**, not a cached field. Cached, it would stop meaning anything once
    /// column generation adds tasks, and callers would decide against a stale limit.
    /// </remarks>
    public static ulong ColumnAmount(int taskCount) {
        if (taskCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(taskCount), taskCount, "任务数量不能为负 / The task count cannot be negative");
        }
        return (ulong)taskCount;
    }
}
