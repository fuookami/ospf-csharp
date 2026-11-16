// 时间冲突约束的任务筛选与冲突对枚举 / Task filtering and conflicting-pair enumeration for the time conflict constraint.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 时间冲突约束的任务筛选与冲突对枚举 / Task filtering and conflicting-pair enumeration for the time conflict constraint.
/// </summary>
/// <remarks>
/// 这条约束说"同一个执行器在同一时刻最多干一件事"。所以它要两件事：**哪些任务有确定的时段**，
/// 以及**哪些任务对在时间上真的重叠**。
///
/// 漏一对的后果是**双重占用**：两个任务被排给同一个执行器、时段重叠，容量被算成两份而实际只有一份。
/// 而多算一对的后果只是多一条恒真的约束——白写。所以判据的重点在"不漏"。
///
/// This constraint says "one executor does at most one thing at a time". It needs two things: **which tasks have a definite interval**, and **which pairs actually overlap in time**.
///
/// Missing a pair causes **double booking**: two tasks assigned to the same executor over overlapping intervals, with capacity counted twice where only one exists. An extra pair
/// only adds an always-true constraint — wasted. So the criteria focus on not missing any.
/// </remarks>
public static class TaskTimeConflictPairs {
    /// <summary>一个可判定时段的任务 / A task with a decidable interval.</summary>
    /// <param name="Id">身份 / The identity.</param>
    /// <param name="Time">时段 / The interval.</param>
    public sealed record Candidate(string Id, TimeRange Time);

    /// <summary>
    /// 筛选有确定时段的任务 / Select the tasks with a definite interval.
    /// </summary>
    /// <param name="tasks">候选任务，时段可为空 / The candidates; the interval may be null.</param>
    /// <param name="timeOf">取任务时段 / Reads a task's interval.</param>
    /// <param name="idOf">取任务身份 / Reads a task's identity.</param>
    /// <param name="advanceEnabled">该任务是否允许提前 / Whether the task allows advancing.</param>
    /// <param name="delayEnabled">该任务是否允许延迟 / Whether the task allows delaying.</param>
    /// <returns>按时段起点排序的候选 / The candidates sorted by interval start.</returns>
    /// <remarks>
    /// **允许提前或延迟的任务必须排除**。它们的时段是**决策变量**，不是给定事实：
    /// 两条"可能重叠"的区间不构成冲突，因为求解器可以把它们推开。
    /// 把它们写进互斥约束等于**替求解器做了决定**，而那些决定本来该由优化给出。
    ///
    /// **Tasks that allow advancing or delaying must be excluded.** Their intervals are **decision variables**, not given facts: two "possibly overlapping" ranges are not a
    /// conflict because the solver can push them apart. Writing them into a mutual-exclusion constraint **makes the decision on the solver's behalf**, when optimisation was
    /// supposed to produce it.
    /// </remarks>
    public static IReadOnlyList<Candidate> Select<T>(
        IReadOnlyList<T> tasks,
        Func<T, TimeRange?> timeOf,
        Func<T, string> idOf,
        Func<T, bool> advanceEnabled,
        Func<T, bool> delayEnabled) {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(timeOf);
        System.ArgumentNullException.ThrowIfNull(idOf);
        System.ArgumentNullException.ThrowIfNull(advanceEnabled);
        System.ArgumentNullException.ThrowIfNull(delayEnabled);

        var selected = new List<Candidate>();
        foreach (T task in tasks) {
            if (timeOf(task) is not { } time || advanceEnabled(task) || delayEnabled(task)) {
                continue;
            }
            selected.Add(new Candidate(idOf(task), time));
        }

        selected.Sort((lhs, rhs) => lhs.Time.Start.CompareTo(rhs.Time.Start));
        return selected;
    }

    /// <summary>
    /// 枚举时间上重叠的任务对 / Enumerate the pairs that overlap in time.
    /// </summary>
    /// <param name="candidates">已筛选并排序的候选 / The filtered and sorted candidates.</param>
    /// <returns>重叠的任务对，每对只出现一次 / The overlapping pairs, each appearing once.</returns>
    /// <remarks>
    /// **每对只出现一次**（`j` 从 `i + 1` 起）：同一条互斥约束写两遍不会出错，但会让模型白长一截。
    /// 而漏掉一对就是双重占用——所以判据两头都要断言。
    ///
    /// **Each pair appears once** (`j` starts at `i + 1`): writing the same exclusion twice is not wrong, just wasted model size. Missing a pair is double booking — so the criteria
    /// assert both ends.
    /// </remarks>
    public static IReadOnlyList<(Candidate Left, Candidate Right)> IntersectingPairs(
        IReadOnlyList<Candidate> candidates) {
        System.ArgumentNullException.ThrowIfNull(candidates);

        // 判定交给通用枚举：时段相交只是"冲突"的一个特例，循环结构不该有第二份。
        // The predicate is delegated to the generic enumeration: interval overlap is one special case of "conflict", and the loop structure should not exist twice.
        IReadOnlyList<(Candidate Left, Candidate Right)> pairs =
            TaskConflictPairs.Pairs(candidates, (lhs, rhs) => lhs.Time.WithIntersection(rhs.Time));
        return pairs;
    }
}
