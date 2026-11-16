// 通用冲突对枚举 / Generic conflicting-pair enumeration.
// 双循环为每个执行器上的每对任务生成冲突项：`conflict(executor, tasks[i], tasks[j])`。
// `TaskTimeConflictConstraint` 是它在时间维度上的特例。
//
// A double loop emits one conflict term per executor and per task pair: `conflict(executor, tasks[i], tasks[j])`.
// `TaskTimeConflictConstraint` is its special case along the time axis.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 通用冲突对枚举 / Generic conflicting-pair enumeration.
/// </summary>
/// <remarks>
/// 冲突族里有**两份几乎一样的双循环**：`TaskConflictConstraint` 用一个注入的 `conflict` 判定，
/// `TaskTimeConflictConstraint` 直接把判定写成"时段相交"。两者的循环结构、下标范围、
/// "每对只出现一次"的约定完全相同，差别只在那一行判定。
///
/// 所以这里只实现一份：判定作为参数传进来。**同一条规则写两遍，改一处忘另一处就是漏一对，
/// 而漏一对是双重占用。**
///
/// The code's conflict family has **two almost identical double loops**: `TaskConflictConstraint` takes an injected `conflict` predicate, and `TaskTimeConflictConstraint`
/// hard-codes the predicate as "intervals intersect". The loop structure, index ranges and the "each pair once" convention are identical; only that one line differs.
///
/// So only one implementation lives here, with the predicate as a parameter. **Writing the same rule twice and forgetting the other copy means missing a pair, and a missed pair
/// is double booking.**
/// </remarks>
public static class TaskConflictPairs {
    /// <summary>
    /// 枚举满足判定的任务对 / Enumerate the pairs satisfying the predicate.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="conflict">冲突判定 / The conflict predicate.</param>
    /// <returns>冲突任务对，每对只出现一次 / The conflicting pairs, each appearing once.</returns>
    /// <remarks>
    /// **每对只出现一次**（`j` 从 `i + 1` 起）：同一条互斥约束写两遍不会出错，只会让模型白长一截。
    /// 而 `j` 从 `0` 起会写出 `(i,j)` 与 `(j,i)` 两份——判据必须能分开这两种。
    ///
    /// **Each pair appears once** (`j` starts at `i + 1`): writing the same exclusion twice is not wrong, just wasted model size. Starting `j` at `0` emits both `(i,j)` and
    /// `(j,i)` — the criteria must be able to tell these apart.
    /// </remarks>
    public static IReadOnlyList<(T Left, T Right)> Pairs<T>(
        IReadOnlyList<T> tasks,
        Func<T, T, bool> conflict) {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(conflict);

        var pairs = new List<(T, T)>();
        for (int i = 0; i < tasks.Count; i++) {
            for (int j = i + 1; j < tasks.Count; j++) {
                if (conflict(tasks[i], tasks[j])) {
                    pairs.Add((tasks[i], tasks[j]));
                }
            }
        }
        return pairs;
    }

    /// <summary>
    /// 为每个执行器枚举冲突任务对 / Enumerate the conflicting pairs for every executor.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="conflict">带执行器的冲突判定 / The conflict predicate taking an executor.</param>
    /// <returns>（执行器，任务对）序列 / The (executor, pair) sequence.</returns>
    public static IReadOnlyList<(E Executor, T Left, T Right)> PairsForEach<T, E>(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        Func<E, T, T, bool> conflict) {
        System.ArgumentNullException.ThrowIfNull(executors);
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(conflict);

        // 复用 `Pairs`：锁住执行器之后就是无执行器的判定，循环结构不必再写一遍。
        // Reuses `Pairs`: once the executor is bound the predicate is executor-free, so the loop need not be written again.
        var pairs = new List<(E, T, T)>();
        foreach (E executor in executors) {
            foreach ((T left, T right) in Pairs(tasks, (lhs, rhs) => conflict(executor, lhs, rhs))) {
                pairs.Add((executor, left, right));
            }
        }
        return pairs;
    }
}
