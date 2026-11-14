// 先后指示量 / Precedence indicator.
// 与固定 Kotlin 基线 `task_compilation/model/Switch.kt` 的 `frontOf` 构造对应：
// `if (task1 == task2) LinearIntermediateSymbol.empty(...)` —— 对角线是**空符号**，即恒零。
// Mirrors the `frontOf` construction in the pinned baseline's `task_compilation/model/Switch.kt`:
// `if (task1 == task2) LinearIntermediateSymbol.empty(...)` — the diagonal is an **empty symbol**, i.e. identically zero.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 先后指示量 / The precedence indicator.
/// </summary>
/// <remarks>
/// 基线的 `frontOf[t1, t2]` 是一个 `IfFunction`，判定 `estimateStartTime[t1] ≤ estimateStartTime[t2]`，
/// 即"任务 1 不晚于任务 2 开始"。但**对角线例外**：`task1 == task2` 时直接给空符号（恒零）。
///
/// 这个例外不能省。`t ≤ t` 恒真，走正常构造会得到 `front_of[t, t] = 1`——
/// 于是**每个任务都声称排在自己前面**。这个多出来的"先后关系"会：
///
/// - 让参与先后关系的对数多出 `任务数` 个；
/// - 在换型统计里给每个任务一条凭空的自换型；
/// - 而不产生任何错误或警告——数值全都算得出来。
///
/// The baseline's `frontOf[t1, t2]` is an `IfFunction` testing `estimateStartTime[t1] ≤ estimateStartTime[t2]`, i.e. "task 1 starts no later than task 2". But the
/// **diagonal is an exception**: `task1 == task2` yields an empty symbol (identically zero).
///
/// That exception cannot be dropped. `t ≤ t` is always true, so the normal construction would give `front_of[t, t] = 1` — **every task claiming to precede itself**. That
/// spurious relation would:
///
/// - add `task count` extra pairs to the precedence relations;
/// - give every task a self-switch in the switch statistics;
/// - and raise no error or warning — every number still computes.
/// </remarks>
public static class SwitchPrecedence {
    /// <summary>
    /// 判断左任务是否排在右任务之前 / Whether the left task precedes the right task.
    /// </summary>
    /// <param name="sameTask">两侧是否同一个任务 / Whether both sides are the same task.</param>
    /// <param name="leftStart">左任务的估计开始 / The left task's estimated start.</param>
    /// <param name="rightStart">右任务的估计开始 / The right task's estimated start.</param>
    /// <returns>是否成立；对角线恒为 `false` / Whether it holds; always `false` on the diagonal.</returns>
    /// <remarks>
    /// **首尾相接（`leftStart == rightStart`）算"不晚于"，即成立**：基线的判定是 `leq`，不是 `lt`。
    /// 两个任务同时可以开始，也算左在右前——若写成 `&lt;`，同一时刻开始的先后关系会**全部消失**。
    ///
    /// **Touching starts (`leftStart == rightStart`) count as "no later than", i.e. true**: the baseline tests `leq`, not `lt`. Two tasks that may start at the same moment
    /// still have left before right — writing `&lt;` would make **every** same-instant precedence relation vanish.
    /// </remarks>
    public static bool IsFrontOf(bool sameTask, double leftStart, double rightStart)
        => !sameTask && leftStart <= rightStart;

    /// <summary>
    /// 构造全部先后指示量 / Build every precedence indicator.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="startOf">取任务的估计开始 / Reads a task's estimated start.</param>
    /// <returns>（左，右）到指示量的映射，含对角线（恒 `false`）/ The map from (left, right) to the indicator, including the diagonal (always `false`).</returns>
    public static IReadOnlyDictionary<(T Left, T Right), bool> Indicators<T>(
        IReadOnlyList<T> tasks,
        Func<T, double> startOf) {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(startOf);

        var table = new Dictionary<(T, T), bool>();
        foreach (T left in tasks) {
            foreach (T right in tasks) {
                bool same = EqualityComparer<T>.Default.Equals(left, right);
                table[(left, right)] = IsFrontOf(same, startOf(left), startOf(right));
            }
        }
        return table;
    }
}
