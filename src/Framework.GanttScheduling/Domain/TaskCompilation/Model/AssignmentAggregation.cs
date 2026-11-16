// 分配/编译聚合的重写规则 / Rewrite rules for the assignment and compilation aggregates.
// `assign.flush += sum(thisTasks.map { xi[it] })` 两段（分别按 `(key, executor)` 与 `key` 分组）对应。
// Mirrors the two `assign.flush += sum(thisTasks.map { xi[it] })` blocks (grouped by `(key, executor)` and by `key`)
//'s.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 分配/编译聚合的重写规则 / Rewrite rules for the assignment and compilation aggregates.
/// </summary>
/// <remarks>
/// 迭代编译里 `taskAssignment` 与 `taskCompilation` 都是**列之和**，每轮被重写：
///
/// | 表 | 分组键 |
/// | --- | --- |
/// | `taskAssignment[origin, e]` | **`(key, executor)`** |
/// | `taskCompilation[origin]` | **只有 `key`** |
///
/// 两者键不同，这不是笔误：一个原始任务可能在不同执行器上有不同列，分配要按执行器分开；
/// 而"这个任务是否达成"是跨执行器的总量。
///
/// **`if (thisTasks.isNotEmpty)` 这个守卫不能省。** 守卫为假时不动聚合；若写成无条件重写，
/// 每个原始任务每轮都会被 `flush` 成空——所有指派关系消失，而**没有任何错误或警告**：
/// 之后的换型、冲突、成本都读到零，模型只是"什么都没做"。
///
/// In iterative compilation `taskAssignment` and `taskCompilation` are both **sums of columns**, rewritten every round:
///
/// | Table | Grouping key |
/// | --- | --- |
/// | `taskAssignment[origin, e]` | **`(key, executor)`** |
/// | `taskCompilation[origin]` | **`key` only** |
///
/// The keys differ on purpose: one origin task may have columns on several executors, so the assignment must be split per executor, while "was this task achieved" is a total
/// across executors.
///
/// **The `if (thisTasks.isNotEmpty)` guard cannot be dropped.** When it is false the aggregate is left alone; an unconditional rewrite would `flush` every origin task into an
/// empty sum every round — all assignment relations vanish with **no error or warning**: switching, conflicts and cost then read zero, and the model merely "does nothing".
/// </remarks>
public static class AssignmentAggregation {
    /// <summary>
    /// 本轮是否重写该聚合 / Whether this round rewrites that aggregate.
    /// </summary>
    /// <param name="newColumnCount">本轮为该原始任务新产生的列数 / Columns produced this round for that origin task.</param>
    /// <returns>是否重写 / Whether to rewrite.</returns>
    public static bool ShouldRewrite(int newColumnCount) {
        ArgumentOutOfRangeException.ThrowIfNegative(newColumnCount);

        return newColumnCount > 0;
    }

    /// <summary>
    /// 判断一列是否计入某执行器的分配聚合 / Whether a column counts towards an executor's assignment aggregate.
    /// </summary>
    /// <typeparam name="TKey">原始任务标识 / The origin task's identity.</typeparam>
    /// <typeparam name="TExecutor">执行器类型 / Executor type.</typeparam>
    /// <param name="originKey">原始任务标识 / The origin task's identity.</param>
    /// <param name="originExecutor">原始任务所在执行器 / The origin task's executor.</param>
    /// <param name="columnKey">列的标识 / The column's identity.</param>
    /// <param name="columnExecutor">列执行器 / The column's executor.</param>
    /// <param name="sameKey">标识是否相等的判定 / The identity equality test.</param>
    /// <param name="sameExecutor">执行器是否相等的判定 / The executor equality test.</param>
    /// <returns>是否计入 / Whether it counts.</returns>
    /// <remarks>
    /// **标识与执行器都要相同**。只比标识会把另一个执行器上的列算进这个执行器的分配，
    /// 于是"该执行器承担了这个任务"在**它并没有**的时候成立。
    ///
    /// **Both the identity and the executor must match.** Comparing only the identity pulls another executor's column into this executor's assignment, so "this executor carries the
    /// task" holds **when it does not**.
    /// </remarks>
    public static bool MatchesAssignment<TKey, TExecutor>(
        TKey originKey,
        TExecutor originExecutor,
        TKey columnKey,
        TExecutor columnExecutor,
        Func<TKey, TKey, bool> sameKey,
        Func<TExecutor, TExecutor, bool> sameExecutor) {
        ArgumentNullException.ThrowIfNull(sameKey);
        ArgumentNullException.ThrowIfNull(sameExecutor);

        return sameKey(originKey, columnKey) && sameExecutor(originExecutor, columnExecutor);
    }

    /// <summary>
    /// 判断一列是否计入编译聚合 / Whether a column counts towards the compilation aggregate.
    /// </summary>
    /// <typeparam name="TKey">原始任务标识 / The origin task's identity.</typeparam>
    /// <param name="originKey">原始任务标识 / The origin task's identity.</param>
    /// <param name="columnKey">列的标识 / The column's identity.</param>
    /// <param name="sameKey">标识是否相等的判定 / The identity equality test.</param>
    /// <returns>是否计入 / Whether it counts.</returns>
    /// <remarks>
    /// **执行器不参与**。把执行器也算进来会让"该任务达成"只统计某一个执行器的列，
    /// 于是达成量偏低——而偏低是安静的：它只是一个更小的数。
    ///
    /// **The executor takes no part.** Including it would count only one executor's columns towards "this task is achieved", understating it — and understating is quiet: it is merely a
    /// smaller number.
    /// </remarks>
    public static bool MatchesCompilation<TKey>(
        TKey originKey,
        TKey columnKey,
        Func<TKey, TKey, bool> sameKey) {
        ArgumentNullException.ThrowIfNull(sameKey);

        return sameKey(originKey, columnKey);
    }
}
