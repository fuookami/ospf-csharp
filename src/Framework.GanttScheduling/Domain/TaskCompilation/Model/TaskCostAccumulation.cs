// 任务代价的累加 / Accumulation of task cost.
// 与固定 Kotlin 基线 `task_compilation/model/Compilation.kt` 的 `addColumns` 里
// `taskCost.flush` + 只对新列追加 `cost(task) * x[task]` 对应。
// Mirrors `taskCost.flush` followed by appending `cost(task) * x[task]` for the new columns only,
// inside `addColumns` in the pinned baseline's `task_compilation/model/Compilation.kt`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 任务代价的累加 / Accumulation of task cost.
/// </summary>
/// <remarks>
/// 基线的 `addColumns` 每次都先 `taskCost.flush`，然后**只对本次新增的列**追加 `cost × x[task]`。
/// 于是目标里只留下**最近一次迭代**的代价项，之前迭代的列虽然还在模型里（`_x` 累积、约束照旧），
/// 它们的**代价项已经不在目标中了**。
///
/// 这不是笔误，是基线的原样行为；本实现照抄。后果很具体：
///
/// | 表现 | 说明 |
/// | --- | --- |
/// | 目标值偏小 | 只有最后一轮的列计入了代价 |
/// | 不会报错 | 目标仍然是一个正常的多项式 |
/// | 列生成越久越明显 | 早期列越多，缺失的代价越多 |
///
/// ponytail: 照抄基线的 flush 语义；若要让目标覆盖全部历史列，改成**累积字典**（`task → weight`，
/// 每轮 `Add` 而非 `Clear`），并把 `RetainedIterations` 改成"全部"。
///
/// The baseline's `addColumns` always calls `taskCost.flush` first and then appends `cost × x[task]` for **this iteration's new columns only**. So the objective keeps only the
/// **most recent iteration's** cost terms; earlier columns are still in the model (`_x` accumulates, the constraints remain) but their **cost terms are gone from the objective**.
///
/// This is the baseline verbatim, not a typo, and this implementation copies it. The consequences are concrete:
///
/// | Symptom | Detail |
/// | --- | --- |
/// | the objective is too small | only the last round's columns are charged |
/// | nothing is reported | the objective is still a well-formed polynomial |
/// | worse the longer column generation runs | more early columns, more missing cost |
///
/// ponytail: the flush semantics are copied from the baseline; to cover every historical column, switch to an **accumulating dictionary** (`task → weight`, `Add` each round
/// instead of `Clear`) and change `RetainedIterations` to "all".
/// </remarks>
public class TaskCostAccumulation<T> {
    /// <summary>一个代价项 / One cost term.</summary>
    /// <param name="Task">任务 / The task.</param>
    /// <param name="Weight">代价系数 / The cost coefficient.</param>
    public sealed record Term(T Task, Flt64 Weight);

    private readonly List<Term> _terms = new();

    /// <summary>
    /// 当前目标里的代价项 / The cost terms currently in the objective.
    /// </summary>
    /// <remarks>只有最近一轮，与基线的 flush 一致。 / Only the latest round, matching the baseline's flush.</remarks>
    public IReadOnlyList<Term> Terms => _terms;

    /// <summary>目标中保留的迭代数 / How many iterations the objective retains.</summary>
    /// <remarks>基线只保留最近一轮，所以是 `1`。 / The baseline keeps only the latest round, so this is `1`.</remarks>
    public static int RetainedIterations => 1;

    /// <summary>
    /// 加入一轮新列 / Add one round of new columns.
    /// </summary>
    /// <param name="newTasks">本轮新增的任务 / This round's new tasks.</param>
    /// <param name="costOf">代价提取器 / The cost extractor.</param>
    /// <returns>成功或失败 / Success or a failure.</returns>
    /// <remarks>
    /// **先清空再加入**，照抄基线的 `taskCost.flush`。表里于是只剩本轮——
    /// 之前的列还在模型里，但它们的代价项已经不在目标中。
    ///
    /// **Clears first, then adds**, copying the baseline's `taskCost.flush`. Only this round survives; earlier columns remain in the model but their cost terms are gone from
    /// the objective.
    /// </remarks>
    public Result<IReadOnlyList<Term>, ErrorCode, Error<ErrorCode>> AddRound(
        IReadOnlyList<T> newTasks,
        Func<T, Flt64?> costOf) {
        ArgumentNullException.ThrowIfNull(newTasks);
        ArgumentNullException.ThrowIfNull(costOf);

        var next = new List<Term>();
        foreach (T task in newTasks) {
            if (costOf(task) is not { } weight) {
                // 失败时保持原表不动：半轮代价比没有代价更难排查。
                // On failure the previous table is left untouched: a half round is harder to diagnose than none.
                return Results.Failed<IReadOnlyList<Term>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务代价提取失败，该列不可定价 / Task cost extraction failed; this column cannot be priced"));
            }
            next.Add(new Term(task, weight));
        }

        _terms.Clear();
        _terms.AddRange(next);
        return Results.Ok<IReadOnlyList<Term>>(_terms);
    }
}
