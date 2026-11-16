// 换型发生条件 / When a switch occurs.
// 有 `taskTime` 时取 `x[e,t1] AND x[e,t2] AND frontOf[t1,t2] AND NOT betweenIn[t3,t1,t2]`（对每个 t3）；
// 无 `taskTime` 时取 `t1.start < t2.start` 且无第三个任务的起点严格落在两者之间。
// Two branches build `switch`: 
// with `taskTime`, `x[e,t1] AND x[e,t2] AND frontOf[t1,t2] AND NOT betweenIn[t3,t1,t2]` for every t3;
// without it, `t1.start < t2.start` and no third task's start strictly between the two.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 换型发生条件 / When a switch occurs.
/// </summary>
/// <remarks>
/// `switch[e, t1, t2]` 说的是"执行器 e 干完 t1 紧接着干 t2"——**相邻**，不是"任意先后"。
/// 让这句话成立的是最后那一串 `NOT betweenIn[t3, t1, t2]`：**中间不能夹着别的任务**。
///
/// 漏掉这一串的后果不是"少算一点"，而是**同一个换型被数很多遍**：
/// 执行器上排了 `A → B → C`，若只看"指派了 + 先后"，`A→C` 也会被当成一次换型，
/// 于是换型时间被计入四次（AB、AC、BA 之类的组合），而排程本身完全正常。
///
/// **两支的严格性不同，这是原样**：
///
/// | 分支 | 先后判定 | 夹在中间的判定 |
/// | --- | --- | --- |
/// | 有 `taskTime`（可移） | `frontOf` = `≤` | `betweenIn`（含端点贴边） |
/// | 无 `taskTime`（已定） | `&lt;` **严格** | 第三个起点**严格**落在两者之间 |
///
/// 于是一个"同时开始"的排布在已定分支里**不算换型**，在可移分支里**算**。
///
/// `switch[e, t1, t2]` says "executor e finishes t1 and immediately does t2" — **adjacent**, not merely ordered. What makes that true is the trailing
/// `NOT betweenIn[t3, t1, t2]` chain: **nothing may sit in between**.
///
/// Dropping that chain does not merely under-count — it **counts the same switch many times**. With `A → B → C` on one executor, "assigned and ordered" alone would also
/// treat `A→C` as a switch, charging switch time four times over, while the schedule itself looks perfectly normal.
///
/// **The two branches differ in strictness, and that is the implementation verbatim:**
///
/// | Branch | Ordering test | Betweenness test |
/// | --- | --- | --- |
/// | with `taskTime` (movable) | `frontOf` = `≤` | `betweenIn` (touching endpoints counts) |
/// | without it (fixed) | `&lt;` **strict** | a third start **strictly** between the two |
///
/// So a "starts together" arrangement is **not** a switch in the fixed branch and **is** one in the movable branch.
/// </remarks>
public static class SwitchOccurrence {
    /// <summary>
    /// 可移分支的换型判定 / The switch test of the movable branch.
    /// </summary>
    /// <param name="sameTask">两侧是否同一任务 / Whether both sides are the same task.</param>
    /// <param name="bothAssigned">两个任务是否都指派给该执行器 / Whether both tasks are assigned to the executor.</param>
    /// <param name="frontOf">左任务是否不晚于右任务 / Whether the left task starts no later than the right.</param>
    /// <param name="thirdTasksBetween">每个第三个任务是否居中 / Whether each third task sits in between.</param>
    /// <returns>是否发生换型 / Whether a switch occurs.</returns>
    public static bool Flexible(
        bool sameTask,
        bool bothAssigned,
        bool frontOf,
        IReadOnlyList<bool> thirdTasksBetween) {
        ArgumentNullException.ThrowIfNull(thirdTasksBetween);

        if (sameTask || !bothAssigned || !frontOf) {
            return false;
        }
        foreach (bool between in thirdTasksBetween) {
            if (between) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 已定分支的换型判定 / The switch test of the fixed branch.
    /// </summary>
    /// <param name="sameTask">两侧是否同一任务 / Whether both sides are the same task.</param>
    /// <param name="bothAssigned">两个任务是否都指派给该执行器 / Whether both tasks are assigned to the executor.</param>
    /// <param name="leftStart">左任务固定开始 / The left task's fixed start.</param>
    /// <param name="rightStart">右任务固定开始 / The right task's fixed start.</param>
    /// <param name="thirdStarts">其余任务的固定开始 / The other tasks' fixed starts.</param>
    /// <returns>是否发生换型 / Whether a switch occurs.</returns>
    public static bool Fixed(
        bool sameTask,
        bool bothAssigned,
        double leftStart,
        double rightStart,
        IReadOnlyList<double> thirdStarts) {
        ArgumentNullException.ThrowIfNull(thirdStarts);

        // 严格不等：同时开始在同一执行器上不构成"干完一件再干下一件"。
        // Strict: equal starts do not make "finish one, then the next" on one executor.
        if (sameTask || !bothAssigned || leftStart >= rightStart) {
            return false;
        }
        foreach (double start in thirdStarts) {
            if (leftStart < start && start < rightStart) {
                return false;
            }
        }
        return true;
    }
}
