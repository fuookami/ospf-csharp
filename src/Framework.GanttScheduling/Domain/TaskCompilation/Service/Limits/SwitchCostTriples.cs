// 换型代价的三元组枚举 / Triple enumeration for switch cost.
// 与固定 Kotlin 基线 `task_compilation/Service/.../SwitchCostMinimization.kt` 的
// `for executor / for task1 / for task2` 三重循环对应。
// Mirrors the `for executor / for task1 / for task2` triple loop in the pinned baseline's `SwitchCostMinimization.kt`.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 换型代价的三元组枚举 / Triple enumeration for switch cost.
/// </summary>
/// <remarks>
/// 基线是 `executors × tasks × tasks`，**包含 `task1 == task2` 的对角线**：
/// 它没有写 `if (task1 == task2) continue`。
///
/// 一个"自然"的实现会顺手跳过对角线，因为"从某个任务换到它自己"听起来没有意义。但这改变了模型：
/// 若 `switch[e, t, t]` 在编译结果里非零（比如它包括准备时间），跳过它就等于**少罚了一部分换型时间**，
/// 而排程结果看起来完全正常。
///
/// **对角线是否为零应由换型数据决定，不该由循环替它决定。**
///
/// The baseline is `executors × tasks × tasks`, **including the `task1 == task2` diagonal**: it never writes `if (task1 == task2) continue`.
///
/// A "natural" implementation would skip the diagonal, since "switching from a task to itself" sounds meaningless. But that changes the model: if `switch[e, t, t]` is
/// non-zero in the compilation (say it includes setup time), skipping it **under-penalises part of the switch time**, while the schedule looks entirely normal.
///
/// **Whether the diagonal is zero is for the switch data to decide, not for the loop.**
/// </remarks>
public static class SwitchCostTriples {
    /// <summary>
    /// 枚举全部（执行器，任务，任务）三元组 / Enumerate every (executor, task, task) triple.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <returns>三元组序列，含对角线 / The triples, including the diagonal.</returns>
    public static IReadOnlyList<(E Executor, T Left, T Right)> All<T, E>(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks) {
        System.ArgumentNullException.ThrowIfNull(executors);
        System.ArgumentNullException.ThrowIfNull(tasks);

        var triples = new List<(E, T, T)>();
        foreach (E executor in executors) {
            foreach (T left in tasks) {
                foreach (T right in tasks) {
                    triples.Add((executor, left, right));
                }
            }
        }
        return triples;
    }

    /// <summary>
    /// 枚举换型时间所需的（任务，任务）对 / Enumerate the (task, task) pairs needed for switch time.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <returns>任务对序列，含对角线 / The pairs, including the diagonal.</returns>
    /// <remarks>
    /// 换型时间不带执行器——基线的 `switch.switchTime[task1, task2]` 只有两个下标。
    ///
    /// Switch time carries no executor — the baseline's `switch.switchTime[task1, task2]` has only two indices.
    /// </remarks>
    public static IReadOnlyList<(T Left, T Right)> AllPairs<T>(IReadOnlyList<T> tasks) {
        System.ArgumentNullException.ThrowIfNull(tasks);

        var pairs = new List<(T, T)>();
        foreach (T left in tasks) {
            foreach (T right in tasks) {
                pairs.Add((left, right));
            }
        }
        return pairs;
    }
}
