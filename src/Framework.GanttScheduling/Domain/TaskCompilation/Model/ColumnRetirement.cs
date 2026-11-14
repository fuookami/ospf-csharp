// 列回收的两条规则 / The two rules of column retirement.
// 与固定 Kotlin 基线 `task_compilation/IterativeAggregation.kt` 的
// `xi[task].range.eq(false); model.remove(xi[task])` 对应。
// Mirrors `xi[task].range.eq(false); model.remove(xi[task])` in the pinned baseline's `task_compilation/IterativeAggregation.kt`.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 列回收的两条规则 / The two rules of column retirement.
/// </summary>
/// <remarks>
/// 列生成把无望的列从模型里撤掉。基线只有两行，但两行都有不报错的错法：
///
/// | 规则 | 错法的后果 |
/// | --- | --- |
/// | **先固定为 0，再移除** | 若移除静默失败（变量仍在），没有固定的话该列**还能被选中**——"已撤掉的列"出现在解里 |
/// | **按任务自己的迭代号找变量** | 按下标找会固定/移除**别的轮次**的变量：想撤的列仍可选，不该撤的被固定成 0 |
///
/// 第二条尤其隐蔽：`x` 是每轮一个 `BinVariable1`，同一下标在第 0 轮和第 1 轮指向**不同的列**。
/// 按下标取，两轮列数相同的时候**完全看不出来**。
///
/// Column generation retires hopeless columns from the model. The baseline is two lines, and both have silent failure modes:
///
/// | Rule | Consequence of getting it wrong |
/// | --- | --- |
/// | **pin to 0 first, then remove** | if removal silently fails (the variable is still there), without the pin that column **can still be selected** — a "retired" column shows up in the solution |
/// | **find the variable by the task's own iteration** | indexing by position pins/removes **another round's** variable: the column you meant to retire stays selectable, and one you did not mean to retire gets pinned to 0 |
///
/// The second is especially subtle: `x` is one `BinVariable1` per round, and the same index points at **different columns** in rounds 0 and 1. Indexing by position is
/// **completely invisible** when both rounds happen to hold the same number of columns.
/// </remarks>
public static class ColumnRetirement {
    /// <summary>
    /// 是否该回收这一列 / Whether this column should be retired.
    /// </summary>
    /// <param name="reducedCost">该列的检验数 / The column's reduced cost.</param>
    /// <param name="maximumReducedCost">检验数上限 / The reduced-cost cutoff.</param>
    /// <param name="fixed">是否被固定保留 / Whether it is fixed.</param>
    /// <param name="kept">是否被要求保留 / Whether it is kept.</param>
    /// <param name="alreadyRemoved">是否已被移除 / Whether it was already removed.</param>
    /// <returns>是否回收 / Whether to retire it.</returns>
    /// <remarks>
    /// 基线是 `!(reducedCost ls maximumReducedCost) &amp;&amp; !fixed &amp;&amp; !kept`。
    /// **`fixed` 与 `kept` 都会否决回收**：它们存在的意义就是"这一列无论多贵都留着"。
    /// 只判其中一个，另一类列会被撤掉——而撤掉的列**不会报错，只是从解里消失**。
    ///
    /// The baseline is `!(reducedCost ls maximumReducedCost) &amp;&amp; !fixed &amp;&amp; !kept`. **Both `fixed` and `kept` veto retirement**: their whole point is "keep this column no matter how
    /// expensive". Testing only one of them retires the other kind — and a retired column **raises nothing, it merely disappears from the solution**.
    /// </remarks>
    public static bool ShouldRetire(
        double reducedCost,
        double maximumReducedCost,
        bool @fixed,
        bool kept,
        bool alreadyRemoved) =>
        !alreadyRemoved && !(reducedCost < maximumReducedCost) && !@fixed && !kept;

    /// <summary>
    /// 取该任务所在轮次的变量 / Take the variable of the round the task belongs to.
    /// </summary>
    /// <typeparam name="TVariable">变量类型 / Variable type.</typeparam>
    /// <param name="rounds">每轮的变量，按轮次排列 / The per-round variables, in round order.</param>
    /// <param name="iteration">任务的迭代号 / The task's iteration number.</param>
    /// <returns>该轮的变量 / That round's variable.</returns>
    /// <remarks>
    /// **按 `iteration` 取，不按下标取**。下标与迭代号在"每轮都加满列"时恰好相等，
    /// 于是写错了也看不出来——直到某一轮只加了一部分列。
    ///
    /// **Indexed by `iteration`, not by position.** The two coincide whenever every round fills completely, so the mistake is invisible — until some round adds only part of its
    /// columns.
    /// </remarks>
    public static TVariable ForIteration<TVariable>(
        System.Collections.Generic.IReadOnlyList<TVariable> rounds,
        long iteration) {
        ArgumentNullException.ThrowIfNull(rounds);
        if (iteration < 0 || iteration >= rounds.Count) {
            throw new ArgumentOutOfRangeException(
                nameof(iteration),
                iteration,
                "任务所属轮次超出已建变量范围 / the task's round is outside the built variables");
        }
        return rounds[(int)iteration];
    }
}
