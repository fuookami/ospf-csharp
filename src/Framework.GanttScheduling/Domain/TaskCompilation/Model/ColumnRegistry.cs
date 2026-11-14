// 列变量登记 / Registration of column variables.
// 与固定 Kotlin 基线 `task_compilation/model/Compilation.kt` 的 `_x.add(xi)` 与
// `x[otherIteration][task2]` 两级访问对应。
// Mirrors `_x.add(xi)` and the two-level `x[otherIteration][task2]` access in the pinned baseline's `task_compilation/model/Compilation.kt`.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 列变量登记 / Registration of column variables.
/// </summary>
/// <remarks>
/// 迭代编译每加一轮列就新增一组变量（基线的 `xi = BinVariable1("x_$iteration", ...)`），
/// 并且**历史轮次不会被丢弃**：跨轮冲突约束要写成 `xi[task1] + x[otherIteration][task2] ≤ 1`，
/// 也就是回头访问**任意一轮**的变量。
///
/// 于是"按轮次分组"这件事不能省：一个扁平的 `Dictionary&lt;task, var&gt;` 会让**同名任务在不同轮次互相覆盖**，
/// 跨轮冲突就会约束到同一个变量上（`xi + xi ≤ 1`），于是**本该互斥的两个任务安静地共处**。
///
/// Iterative compilation adds a fresh variable set every round (the baseline's `xi = BinVariable1("x_$iteration", ...)`) and **never discards earlier rounds**: cross-round
/// conflicts are written as `xi[task1] + x[otherIteration][task2] ≤ 1`, which reaches back into **any** earlier round.
///
/// So grouping by round cannot be skipped: a flat `Dictionary&lt;task, var&gt;` lets **same-named tasks in different rounds overwrite each other**, and a cross-round conflict would
/// then constrain one variable to itself (`xi + xi ≤ 1`), letting **two tasks that should exclude each other coexist quietly**.
/// </remarks>
public class ColumnRegistry<TIteration, TTask>
    where TIteration : notnull
    where TTask : notnull {
    private readonly List<TIteration> _order = new();
    private readonly Dictionary<TIteration, HashSet<TTask>> _rounds = new();

    /// <summary>已登记的轮次数 / How many rounds are registered.</summary>
    public int RoundCount => _order.Count;

    /// <summary>按登记顺序的轮次 / The rounds in registration order.</summary>
    public IReadOnlyList<TIteration> Rounds => _order;

    /// <summary>
    /// 登记一轮列变量 / Register one round of column variables.
    /// </summary>
    /// <param name="iteration">轮次标识 / The round's identity.</param>
    /// <param name="tasks">该轮的列 / That round's columns.</param>
    /// <returns>本轮实际新增的列 / The columns actually added this round.</returns>
    public IReadOnlyList<TTask> AddRound(TIteration iteration, IReadOnlyList<TTask> tasks) {
        ArgumentNullException.ThrowIfNull(tasks);

        if (!_rounds.TryGetValue(iteration, out HashSet<TTask>? round)) {
            round = new HashSet<TTask>();
            _rounds[iteration] = round;
            _order.Add(iteration);
        }

        var added = new List<TTask>();
        foreach (TTask task in tasks) {
            if (round.Add(task)) {
                added.Add(task);
            }
        }
        return added;
    }

    /// <summary>
    /// 查某轮是否有该列 / Whether a round holds that column.
    /// </summary>
    /// <param name="iteration">轮次 / The round.</param>
    /// <param name="task">列 / The column.</param>
    /// <returns>是否存在 / Whether it is present.</returns>
    /// <remarks>
    /// **只在同一轮内查**。同名任务在别的轮次里存在**不算**——这正是跨轮冲突约束要分开写的原因。
    ///
    /// **Scoped to that round only.** The same task existing in another round **does not count** — exactly why cross-round conflicts must be written separately.
    /// </remarks>
    public bool Contains(TIteration iteration, TTask task) =>
        _rounds.TryGetValue(iteration, out HashSet<TTask>? round) && round.Contains(task);

    /// <summary>
    /// 查某轮的全部列 / Every column of a round.
    /// </summary>
    /// <param name="iteration">轮次 / The round.</param>
    /// <returns>该轮的列；轮次不存在时为空 / That round's columns; empty when the round is unknown.</returns>
    public IReadOnlyCollection<TTask> Round(TIteration iteration) =>
        _rounds.TryGetValue(iteration, out HashSet<TTask>? round)
            ? round
            : Array.Empty<TTask>();

    /// <summary>
    /// 找出包含该列的所有轮次 / Every round holding that column.
    /// </summary>
    /// <param name="task">列 / The column.</param>
    /// <returns>轮次序列 / The rounds.</returns>
    /// <remarks>
    /// 跨轮冲突要遍历的正是这个集合：对每个"别的轮次"生成一条 `xi[task1] + x[other][task2] ≤ 1`。
    ///
    /// This is exactly the set a cross-round conflict iterates: one `xi[task1] + x[other][task2] ≤ 1` per other round.
    /// </remarks>
    public IReadOnlyList<TIteration> RoundsContaining(TTask task) {
        var rounds = new List<TIteration>();
        foreach (TIteration iteration in _order) {
            if (_rounds[iteration].Contains(task)) {
                rounds.Add(iteration);
            }
        }
        return rounds;
    }
}
