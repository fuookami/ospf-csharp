// 时间偏移量的任务筛选 / Task selection for time-deviation objectives.
// 与`task_compilation/service/limits/` 里的两处筛选对应：
//（**文件名拼错，类名正确**）的
// `tasks.filter { it.advanceEnabled && it.scheduledTime != null }`，
// 以及 的 `tasks.filter { it.delayEnabled && it.scheduledTime != null }`。
// The two filters in the: 
// `tasks.filter { it.advanceEnabled && it.scheduledTime != null }`
// (the **file name is misspelled, the class name is not**) and the `delayEnabled` counterpart.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 时间偏移量的任务筛选 / Task selection for time-deviation objectives.
/// </summary>
/// <remarks>
/// 提前与延迟两条目标的筛选**结构完全相同**，差别只在那个单任务开关（`advanceEnabled` / `delayEnabled`）。
/// 所以这里只写一份，开关作为参数传进来——**同一个规则写两遍，改一处忘另一处就是选错任务集**。
///
/// 三个条件缺一不可：
///
/// | 条件 | 缺了的后果 |
/// | --- | --- |
/// | 全局开关 | 整个问题没在建模这个偏移量，加进目标只会平白多出变量 |
/// | 任务自身的开关 | 不允许这个方向偏移的任务被允许 |
/// | **有排定时间** | 偏移量**相对什么**？没有基准，这个量无定义 |
///
/// 第三条最容易被漏掉：它看起来像冗余校验，其实是这个量的**定义前提**。
/// 一个任务可以声明"允许提前"却还没有排定时间（例如排在某个未定的班次里），此时它的偏移量**不是一个数**。
///
/// 这种情况**排除**而不是当成零——当成零会让"偏移了但没有基准"安静地通过，
/// 而真正的结论应该是"这个任务的偏移量还不可知"。
///
/// 两个开关**互不影响**：一个任务可以同时允许提前与延迟，也可以只允许其中一个。
/// 把延迟误用成 `advanceEnabled` 会得到**看起来同样合理**的任务集，而偏移量算在了错误的任务上。
///
/// The advance and delay objectives share **exactly the same filter structure**, differing only in the per-task switch (`advanceEnabled` / `delayEnabled`). So there is one
/// implementation here with the switch passed in — **writing the same rule twice and forgetting one copy selects the wrong task set**.
///
/// All three conditions are required:
///
/// | Condition | Consequence of dropping it |
/// | --- | --- |
/// | the global switch | the problem is not modelling this deviation, so the term only adds variables |
/// | the task's own switch | a task that forbids that direction gets it |
/// | **having a scheduled time** | deviation **relative to what**? With no reference the quantity is undefined |
///
/// The third is the easiest to skip: it looks like a redundant validation but is really the **precondition for the definition**. A task may declare "advancing allowed" before a
/// scheduled time exists (say it sits in an undecided shift), and then its deviation is not a number.
///
/// The code **excludes** that case rather than treating it as zero: zero would let "deviated, but there is no reference" through quietly, when the honest conclusion is "this
/// task's deviation is not knowable yet".
///
/// The two switches are **independent**: a task may allow both directions, or only one. Using `advanceEnabled` for the delay objective yields a **plausible-looking** task set
/// with the deviation charged to the wrong tasks.
/// </remarks>
public static class TimeDeviationSelection {
    /// <summary>
    /// 选出参与偏移量目标的任务 / Select the tasks taking part in a time-deviation objective.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <param name="globalEnabled">该方向的全局开关 / That direction's global switch.</param>
    /// <param name="tasks">候选任务 / The candidate tasks.</param>
    /// <param name="directionEnabled">该任务是否允许这个方向的偏移 / Whether the task allows deviating in that direction.</param>
    /// <param name="hasScheduledTime">该任务是否已有排定时间 / Whether the task already has a scheduled time.</param>
    /// <returns>参与的任务 / The tasks taking part.</returns>
    public static IReadOnlyList<T> Select<T>(
        bool globalEnabled,
        IReadOnlyList<T> tasks,
        Func<T, bool> directionEnabled,
        Func<T, bool> hasScheduledTime) {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(directionEnabled);
        ArgumentNullException.ThrowIfNull(hasScheduledTime);

        if (!globalEnabled) {
            return Array.Empty<T>();
        }

        var selected = new List<T>();
        foreach (T task in tasks) {
            if (directionEnabled(task) && hasScheduledTime(task)) {
                selected.Add(task);
            }
        }
        return selected;
    }
}
