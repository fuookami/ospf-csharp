// 任务-执行器分配变量的值域收紧 / Value-range tightening of the task-executor assignment variables.
// 与固定 Kotlin 的 `register` 里那段 `x[task, executor].range.eq(...)` 对应。
// One range constraint per (task, executor) pair: `x[task, executor].range.eq(...)`.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 分配变量的值域 / The assignment variable's value range.
/// </summary>
/// <param name="Low">下界 / The lower bound.</param>
/// <param name="High">上界 / The upper bound.</param>
public readonly record struct AssignmentRange(double Low, double High) {
    /// <summary>允许（0..1）/ Free (0..1).</summary>
    public static AssignmentRange Free => new(0.0, 1.0);

    /// <summary>禁止（0..0）/ Forbidden (0..0).</summary>
    public static AssignmentRange Forbidden => new(0.0, 0.0);

    /// <summary>必须（1..1）/ Required (1..1).</summary>
    public static AssignmentRange Required => new(1.0, 1.0);

    /// <summary>是否被固定 / Whether the range is fixed to a single value.</summary>
    public bool IsFixed => Low == High;
}

/// <summary>
/// 分配变量的值域收紧 / Value-range tightening of the assignment variables.
/// </summary>
/// <remarks>
/// 模型的规模主要由 `任务数 × 执行器数` 决定，而**大部分组合本来就不可能**：任务只接某几个执行器，
/// 或者执行器已经由外部排班定死。把这些组合的值域收紧到固定值，等于告诉求解器"这里不用搜"。
///
/// 规则来自基线 `register`，共三条，但**顺序有讲究**：
///
/// 1. 执行器不在任务的可用执行器集合里 → 固定为 `0`（禁止）。
/// 2. 任务的执行器已定且不允许改变：
///    - 就是那个执行器 → 若任务不允许取消则固定为 `1`（必须），否则保持 `0..1`（可取消，交给优化决定）；
///    - 其它执行器 → 固定为 `0`。
///
/// **第 2 条会覆盖第 1 条**：写法是先按可用集合写 `0`，再按"执行器已定"写 `1`，**后写的生效**。
/// 一个已定执行器即使不在任务的可用集合里，最终也会被钉成 `1`。若实现把两条当成交集处理，
/// 那个任务会**既被禁止又被要求**——模型直接不可解，而错误信息只会说"不可行"。
///
/// Model size is dominated by `task count × executor count`, and **most combinations are impossible to begin with**: a task accepts only certain executors, or the executor has
/// been fixed by an external roster. Tightening those combinations to a single value tells the solver "no search here".
///
/// The rules come from its `register`, three of them, but **order matters**:
///
/// 1. The executor is not among the task's enabled executors → fix to `0` (forbidden).
/// 2. The task's executor is fixed and may not change: the one that matches → fix to `1` (required) when the task cannot be cancelled, otherwise leave `0..1` (cancellable,
///    decided by the optimisation); every other executor → fix to `0`.
///
/// **Rule 2 overrides rule 1**: the code writes `0` from the enabled set first, then writes `1` for "executor is fixed", so **the later write wins**. A fixed executor absent
/// from the task's enabled set still ends up pinned to `1`. An implementation treating the two as an intersection would leave that task **both forbidden and required** — the model
/// becomes infeasible, and the only message is "infeasible".
/// </remarks>
public static class TaskAssignmentRange {
    /// <summary>
    /// 计算一个组合的值域 / Compute the range for one combination.
    /// </summary>
    /// <param name="executorEnabled">执行器是否在任务的可用集合里 / Whether the executor is among the task's enabled executors.</param>
    /// <param name="executorFixed">任务的执行器是否已定且不允许改变 / Whether the task's executor is fixed and may not change.</param>
    /// <param name="isTaskExecutor">该执行器是否就是任务已定的那个 / Whether this executor is the one the task is fixed to.</param>
    /// <param name="cancelEnabled">任务是否允许取消 / Whether the task allows cancelling.</param>
    /// <returns>值域 / The range.</returns>
    public static AssignmentRange For(
        bool executorEnabled,
        bool executorFixed,
        bool isTaskExecutor,
        bool cancelEnabled) {
        AssignmentRange range = executorEnabled ? AssignmentRange.Free : AssignmentRange.Forbidden;

        // 第 2 条后写，覆盖第 1 条。
        // Rule 2 is written second and overrides rule 1.
        if (executorFixed) {
            if (!isTaskExecutor) {
                range = AssignmentRange.Forbidden;
            } else if (!cancelEnabled) {
                range = AssignmentRange.Required;
            }
        }
        return range;
    }

    /// <summary>
    /// 判断一个组合是否还需要参与搜索 / Whether a combination still needs to be searched.
    /// </summary>
    /// <param name="range">值域 / The range.</param>
    /// <returns>是否仍需搜索 / Whether it still needs searching.</returns>
    public static bool NeedsSearch(AssignmentRange range) => !range.IsFixed;
}
