// "居中"指示量 / The "in between" indicator.
// 与固定 Kotlin 基线 `task_compilation/model/Switch.kt` 的 `betweenIn` 构造对应：
// `if (task1 == task2 || task1 == task3 || task2 == task3) LinearIntermediateSymbol.empty(...)`，
// 否则取 `frontOf[task1, task3] AND frontOf[task3, task2]`。
// Mirrors the `betweenIn` construction in the pinned baseline's `task_compilation/model/Switch.kt`:
// `if (task1 == task2 || task1 == task3 || task2 == task3) LinearIntermediateSymbol.empty(...)`,
// otherwise `frontOf[task1, task3] AND frontOf[task3, task2]`.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// "居中"指示量 / The "in between" indicator.
/// </summary>
/// <remarks>
/// `betweenIn[t3, t1, t2]` 说的是"任务 3 排任务 1 与任务 2 之间"，展开为
/// `frontOf[t1, t3] AND frontOf[t3, t2]`，即 `start[t1] ≤ start[t3] ≤ start[t2]`。
///
/// 三条排除是 **B512 那条对角线的三维推广**：三个任务里有任意两个相同，结果就是恒零。
/// 两条排除各有用处，漏掉哪条都算得出来：
///
/// | 漏掉的排除 | 后果 |
/// | --- | --- |
/// | 不排除 `t1 == t2` | `start ≤ start ≤ start` 恒真，一个任务成了**自己与自己之间** |
/// | 不排除 `t1 == t3` 或 `t2 == t3` | 端点被当成中间点，`betweenIn` 为真但**没有中间** |
///
/// 第二种尤其安静：`t3` 与 `t1` 同时开始时，`start[t1] ≤ start[t3]` 成立、`start[t3] ≤ start[t2]` 也成立，
/// 于是三元组凭空成立——**数值算得出来，排程看起来正常**。
///
/// `betweenIn[t3, t1, t2]` says "task 3 sits between task 1 and task 2", expanding to
/// `frontOf[t1, t3] AND frontOf[t3, t2]`, i.e. `start[t1] ≤ start[t3] ≤ start[t2]`.
///
/// The three exclusions are the three-dimensional generalisation of B512's diagonal: if any two of the three tasks coincide, the result is identically zero. Each exclusion
/// matters, and dropping either still computes:
///
/// | Dropped exclusion | Consequence |
/// | --- | --- |
/// | not excluding `t1 == t2` | `start ≤ start ≤ start` holds trivially, a task ends up **between itself** |
/// | not excluding `t1 == t3` or `t2 == t3` | an endpoint counts as the middle, `betweenIn` is true but **there is no middle** |
///
/// The second is especially quiet: when `t3` and `t1` start together, `start[t1] ≤ start[t3]` holds and `start[t3] ≤ start[t2]` holds too, so the triple holds out of nowhere —
/// **the numbers compute and the schedule looks normal**.
/// </remarks>
public static class SwitchBetweenIn {
    /// <summary>
    /// 判断中间任务是否排在两端之间 / Whether the middle task sits between the two ends.
    /// </summary>
    /// <param name="allDistinct">三个任务是否两两不同 / Whether all three tasks are distinct.</param>
    /// <param name="leftStart">左端估计开始 / The left end's estimated start.</param>
    /// <param name="middleStart">中间任务估计开始 / The middle task's estimated start.</param>
    /// <param name="rightStart">右端估计开始 / The right end's estimated start.</param>
    /// <returns>是否成立；任两任务相同则恒为 `false` / Whether it holds; always `false` when any two coincide.</returns>
    /// <remarks>
    /// **同一时刻可以"居中"**：`start[middle] == start[left]` 在 `≤` 下成立，于是中间点落在左端点上仍算居中。
    /// 这与 `frontOf` 的 `leq` 一致——若这里改成严格不等，同时开始的排布会**全部失去居中关系**。
    ///
    /// **Touching starts may still be "in between"**: `start[middle] == start[left]` holds under `≤`, so a middle landing on the left end still counts. That is consistent with
    /// `frontOf`'s `leq` — making it strict here would **strip every such arrangement of its between-relations**.
    /// </remarks>
    public static bool Holds(bool allDistinct, double leftStart, double middleStart, double rightStart)
        => allDistinct && leftStart <= middleStart && middleStart <= rightStart;

    /// <summary>
    /// 构造全部居中指示量 / Build every in-between indicator.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="startOf">取任务的估计开始 / Reads a task's estimated start.</param>
    /// <returns>（中间，左，右）到指示量的映射 / The map from (middle, left, right) to the indicator.</returns>
    public static IReadOnlyDictionary<(T Middle, T Left, T Right), bool> Table<T>(
        IReadOnlyList<T> tasks,
        Func<T, double> startOf) {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(startOf);

        var table = new Dictionary<(T, T, T), bool>();
        foreach (T left in tasks) {
            foreach (T right in tasks) {
                foreach (T middle in tasks) {
                    bool allDistinct =
                        !EqualityComparer<T>.Default.Equals(left, middle)
                        && !EqualityComparer<T>.Default.Equals(middle, right)
                        && !EqualityComparer<T>.Default.Equals(left, right);

                    table[(middle, left, right)] = Holds(
                        allDistinct, startOf(left), startOf(middle), startOf(right));
                }
            }
        }
        return table;
    }
}
