// 跨轮冲突约束的生成条件 / When a cross-round conflict constraint is emitted.
// 与固定 Kotlin 的
// `if (task1 != task2 && conflict(task1, task2)) { xi[task1] + x[other][task2] ≤ 1 }` 对应。
// Mirrors `if (task1 != task2 && conflict(task1, task2)) { xi[task1] + x[other][task2] ≤ 1 }`
//'s.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 跨轮冲突约束 / Cross-round conflict constraints.
/// </summary>
/// <remarks>
/// 列生成每轮新增一批列，而**冲突要在所有轮次之间成立**：本轮的第 1 个任务与历史任意一轮的第 2 个任务
/// 若互斥，就要加一条 `xi[task1] + x[other][task2] ≤ 1`。
///
/// `task1 != task2` 这个判断不能省，而且省掉的后果比"多一条约束"严重得多：
/// 当 `otherIteration` 就是本轮、`task2` 就是 `task1` 自己时，两边是**同一个变量**，
/// 约束退化成 `2x ≤ 1`，于是**每一列都被强制取 0**——排程直接无解，或者解里什么任务都没做。
///
/// 另有一处需要记录的细节：`task1 != task2` 比的是**任务的相等性**，不是在比"是不是同一个变量"。
/// 于是同一个任务出现在**不同轮次**时也会被跳过，那两列之间**不加互斥**。
/// 这是基线原样；见下方 `ponytail:` 说明。
///
/// Column generation adds a batch of columns per round, and **conflicts must hold across all rounds**: if this round's first task excludes any second task from any earlier round,
/// a `xi[task1] + x[other][task2] ≤ 1` row is added.
///
/// The `task1 != task2` test cannot be dropped, and dropping it is far worse than an extra row: when `otherIteration` is the current round and `task2` is `task1` itself, both sides
/// are **the same variable**, the row degenerates to `2x ≤ 1`, and **every column is forced to 0** — the schedule is infeasible, or feasible with nothing done.
///
/// One detail worth recording: `task1 != task2` compares **task equality**, not "is it the same variable". So the same task appearing in **different rounds** is skipped too, and no
/// exclusion is added between those two columns. This is the code verbatim; see the `ponytail:` note below.
/// </remarks>
public static class CrossRoundConflict {
    /// <summary>
    /// 是否生成一条约束 / Whether to emit a constraint.
    /// </summary>
    /// <param name="sameTask">两侧是否是同一个任务 / Whether both sides are the same task.</param>
    /// <param name="conflict">冲突判定是否成立 / Whether the conflict predicate holds.</param>
    /// <returns>是否生成 / Whether to emit.</returns>
    /// <remarks>
    /// **先判相等再判冲突**：顺序不影响结果，但相等必须被排除——
    /// 否则同一条列会和它自己组成 `2x ≤ 1`。
    ///
    /// **Equality is tested first, then the conflict**: the order does not change the outcome, but equality must be excluded — otherwise a column pairs with itself in `2x ≤ 1`.
    /// </remarks>
    public static bool ShouldAdd(bool sameTask, bool conflict) => !sameTask && conflict;

    /// <summary>
    /// 约束的右端常数 / The right-hand-side constant of the row.
    /// </summary>
    /// <returns>恒为 `1` / Always `1`.</returns>
    /// <remarks>
    /// `xi + x ≤ 1` 而不是 `≤ 2`：两个二值列**不能同时为 1**，也就是"互斥"。
    /// 写成 `≤ 2` 会让这条约束恒真——约束看着在，实际什么都没排除。
    ///
    /// `xi + x ≤ 1`, not `≤ 2`: the two binary columns **cannot both be 1**, i.e. they exclude each other. Writing `≤ 2` makes the row always true — it looks present while excluding
    /// nothing.
    /// </remarks>
    public static double RightHandSide => 1.0;
}
