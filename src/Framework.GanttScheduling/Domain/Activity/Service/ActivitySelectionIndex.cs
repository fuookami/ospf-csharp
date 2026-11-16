// 活动候选索引与选择校验 / Activity choice index and selection validation.
// `variable` / `cancellationVariable` 查询与 `extractSolution` 里的"恰好选一"校验对应。
// Mirrors the `variable` / `cancellationVariable` lookups and the "exactly one" check inside `extractSolution`
//'s.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 活动候选索引与选择校验 / The activity choice index and selection validation.
/// </summary>
/// <remarks>
/// 索引把"完整候选身份"映射到列下标，并把"没有放置的候选"识别为**显式取消**。
/// 两个查询在查不到时都返回 `null`，而不是抛异常或返回零：
/// `null` 有明确语义——"这不是一个候选"或"这个任务不能取消"，调用方据此决定是否追加约束。
/// 返回零会让调用方以为拿到了第 0 列，于是把约束加到了别的任务上。
///
/// The index maps a complete choice identity to a column index and recognises a choice without a placement as an **explicit cancellation**. Both lookups return `null` when
/// nothing matches rather than throwing or returning zero: `null` has a definite meaning — "this is not a candidate" or "this task cannot be cancelled" — and the caller
/// decides from it whether to add a constraint. Returning zero would make the caller believe it received column 0 and attach the constraint to a different task.
/// </remarks>
public sealed class ActivitySelectionIndex {
    private readonly Dictionary<ActivityChoiceKey, int> _indices = new();
    private readonly Dictionary<string, int> _cancellations = new(StringComparer.Ordinal);

    /// <summary>
    /// 由候选与放置构造索引 / Build the index from choices and their placements.
    /// </summary>
    /// <param name="choices">候选的放置，`null` 表示该候选是显式取消 / The placements; null means the choice is an explicit cancellation.</param>
    public ActivitySelectionIndex(IReadOnlyList<(ActivityChoiceKey Key, ActivityPlacement<Flt64>? Placement)> choices) {
        System.ArgumentNullException.ThrowIfNull(choices);

        for (int index = 0; index < choices.Count; index++) {
            (ActivityChoiceKey key, ActivityPlacement<Flt64>? placement) = choices[index];
            if (placement is not null) {
                _indices[key] = index;
            } else {
                _cancellations[key.Task] = index;
            }
        }
    }

    /// <summary>全部可执行候选的索引 / The indices of every executable choice.</summary>
    public IReadOnlyCollection<ActivityChoiceKey> Keys => _indices.Keys;

    /// <summary>全部可取消任务的索引 / The indices of every cancellable task.</summary>
    public IReadOnlyCollection<string> CancellableTasks => _cancellations.Keys;

    /// <summary>
    /// 查询候选的列下标 / Look up a choice's column index.
    /// </summary>
    /// <param name="key">完整候选身份 / The complete choice identity.</param>
    /// <returns>列下标，未知候选为空 / The column index, or null for an unknown choice.</returns>
    public int? VariableOf(ActivityChoiceKey key) =>
        key is not null && _indices.TryGetValue(key, out int index) ? index : null;

    /// <summary>
    /// 查询任务的取消列下标 / Look up a task's cancellation column index.
    /// </summary>
    /// <param name="task">待排任务 / The pending task.</param>
    /// <returns>列下标，必做任务为空 / The column index, or null for a mandatory task.</returns>
    public int? CancellationVariableOf(string task) =>
        task is not null && _cancellations.TryGetValue(task, out int index) ? index : null;

    /// <summary>
    /// 校验每个活动恰好选择一次 / Validate that every activity is selected exactly once.
    /// </summary>
    /// <param name="activities">活动清单 / The activities.</param>
    /// <param name="selected">被选中的列下标 / The selected column indices.</param>
    /// <param name="taskOfIndex">列下标到任务的映射 / The mapping from a column index to its task.</param>
    /// <returns>违规项 / The violations.</returns>
    /// <remarks>
    /// **"恰好一次"覆盖执行与显式取消两者**：每列的 `task` 无论代表执行还是取消，都计入该活动的选择次数。
    /// 若只数执行列，一个"既执行又取消"的活动会被判为恰好选一，而它在物理上自相矛盾；
    /// 若只数取消列，一个"执行两次"的活动会被判为没选。
    ///
    /// **"Exactly once" covers both execution and explicit cancellation**: each column's `task`, whether it stands for execution or cancellation, counts towards that
    /// activity's selection count. Counting only execution columns would let an activity that is both executed **and** cancelled pass as exactly-once, though it is physically
    /// contradictory; counting only cancellation columns would read an activity executed twice as never selected.
    /// </remarks>
    public static IReadOnlyList<string> ValidateExactlyOne(
        IReadOnlyList<string> activities,
        IReadOnlyList<int> selected,
        IReadOnlyDictionary<int, string> taskOfIndex) {
        System.ArgumentNullException.ThrowIfNull(activities);
        System.ArgumentNullException.ThrowIfNull(selected);
        System.ArgumentNullException.ThrowIfNull(taskOfIndex);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int index in selected) {
            if (taskOfIndex.TryGetValue(index, out string? task)) {
                counts[task] = counts.TryGetValue(task, out int known) ? known + 1 : 1;
            }
        }

        var violations = new List<string>();
        foreach (string activity in activities) {
            int count = counts.TryGetValue(activity, out int found) ? found : 0;
            if (count != 1) {
                violations.Add("selection:" + activity + ":" + count);
            }
        }
        return violations;
    }
}
