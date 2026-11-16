// 不读取模型行的独立排程复核 / Independent schedule validation without reading generated model rows.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Flt64 = Fuookami.Ospf.Math.Algebra.Number.Flt64;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 活动排程的静态输入 / The static input of an activity schedule.
/// </summary>
/// <param name="Activities">活动清单，只含需要被排上的 / The activities, only those that must be scheduled.</param>
/// <param name="CancellableActivities">允许取消的活动 / The activities that may be cancelled.</param>
/// <param name="Dependencies">依赖 / The dependencies.</param>
/// <param name="ReservedResourceIds">需要校验的具名资源 / The named resources to validate.</param>
/// <remarks>
/// **`CancellableActivities` 与 `Activities` 分开而不是给活动一个 `bool`**：一个活动"要不要排"和"能不能取消"是两个独立的问题，
/// 合并成一个标志位后，只有两种组合能被表达，而实际有四种——"必须排且不可取消"、"必须排但可取消"、"可不排"。
///
/// **`CancellableActivities` is separate from `Activities` instead of a `bool` on the activity**: "must it be scheduled" and "may it be cancelled" are two independent
/// questions, and folding them into one flag permits only two combinations where four exist.
/// </remarks>
public sealed record ActivityScheduleInput(
    IReadOnlyList<string> Activities,
    IReadOnlySet<string> CancellableActivities,
    IReadOnlyList<ActivityDependency> Dependencies,
    IReadOnlySet<string> ReservedResourceIds);

/// <summary>
/// 不读取模型行的独立排程复核 / Independent schedule validation that does not read generated model rows.
/// </summary>
/// <remarks>
/// **"不读模型行"是这类校验的全部意义**：如果校验器去读求解器写出的行，那么求解器的建模错误会被同一个人用同一份数据确认，
/// 复核就退化成"再跑一遍相同的推理"。所以它只吃**外部输入 + 选中的候选**，从零重新推一遍。
///
/// **"Does not read model rows" is the whole point**: if the validator read the rows the solver wrote, a modelling error would be confirmed by the same code from the same
/// data and the review would degenerate into "run the same reasoning again". It therefore takes **external input plus the chosen candidates** and re-derives everything.
/// </remarks>
public static class ActivityScheduleValidator {
    /// <summary>
    /// 复核一组候选 / Validate a set of choices.
    /// </summary>
    /// <param name="input">静态输入 / The static input.</param>
    /// <param name="placements">选中候选的放置 / The placements of the chosen candidates.</param>
    /// <returns>校验结果 / The validation result.</returns>
    /// <remarks>
    /// 每条违规带**定位信息**：活动名、依赖下标或资源名。只报"有冲突"等于让使用者自己去几百个活动里找。
    ///
    /// Every violation carries **locating information**: the activity name, the dependency index or the resource name. Reporting only "there is a conflict" leaves the user
    /// to search hundreds of activities by hand.
    /// </remarks>
    public static ActivityScheduleValidation Validate(
        ActivityScheduleInput input,
        IReadOnlyList<ActivityPlacement<Flt64>> placements) {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(placements);

        var violations = new List<string>();

        // 同一候选不能出现两次：重复选择会让后续按任务分组时把同一模式算两遍。
        // The same choice must not appear twice: a duplicate would make the per-task grouping count one mode twice.
        var keys = new HashSet<ActivityChoiceKey>();
        foreach (ActivityPlacement<Flt64> placement in placements) {
            if (!keys.Add(placement.Key)) {
                violations.Add("duplicate_choice");
                break;
            }
        }

        // 按任务分组，统计每个活动被排了几次。
        // Group by task and count how many times each activity was scheduled.
        var countByTask = new Dictionary<string, int>();
        foreach (ActivityPlacement<Flt64> placement in placements) {
            string task = placement.Key.Task;
            countByTask[task] = countByTask.TryGetValue(task, out int known) ? known + 1 : 1;
        }

        foreach (string activity in input.Activities) {
            int count = countByTask.TryGetValue(activity, out int scheduled) ? scheduled : 0;
            if (count > 1) {
                violations.Add("multiple_modes:" + activity);
            }
            if (count == 0 && !input.CancellableActivities.Contains(activity)) {
                violations.Add("missing_required:" + activity);
            }
        }

        // 依赖：先按任务取实际区间，再逐条判存在性与时间关系。
        // Dependencies: resolve each task's actual interval first, then check presence and timing one rule at a time.
        var timeByTask = new Dictionary<string, (System.DateTimeOffset Start, System.DateTimeOffset End)>();
        foreach (ActivityPlacement<Flt64> placement in placements) {
            timeByTask[placement.Key.Task] = (placement.Time.Start, placement.Time.End);
        }

        for (int index = 0; index < input.Dependencies.Count; index++) {
            ActivityDependency dependency = input.Dependencies[index];
            bool hasPredecessor = timeByTask.TryGetValue(dependency.Predecessor, out (System.DateTimeOffset Start, System.DateTimeOffset End) predecessor);
            bool hasSuccessor = timeByTask.TryGetValue(dependency.Successor, out (System.DateTimeOffset Start, System.DateTimeOffset End) successor);

            if ((hasSuccessor && !hasPredecessor && dependency.Presence != ActivityDependencyPresence.WhenBothPresent)
                || (!hasSuccessor && hasPredecessor && dependency.Presence == ActivityDependencyPresence.RequireBoth)) {
                violations.Add("dependency_presence:" + index);
            }

            if (hasPredecessor && hasSuccessor) {
                System.DateTimeOffset from = dependency.From == ActivityEndpoint.Start ? predecessor.Start : predecessor.End;
                System.DateTimeOffset to = dependency.To == ActivityEndpoint.Start ? successor.Start : successor.End;
                System.TimeSpan lag = to - from;
                if (lag < dependency.MinimumLag
                    || (dependency.Maximum is { } maximum && lag > maximum)) {
                    violations.Add("dependency_time:" + index);
                }
            }
        }

        // 资源容量：逐资源检查是否超容。
        // Resource capacity: check every resource for overuse.
        foreach (string resource in input.ReservedResourceIds) {
            bool overused = false;
            for (int left = 0; left < placements.Count && !overused; left++) {
                for (int right = 0; right < placements.Count; right++) {
                    if (left == right) {
                        continue;
                    }
                    if (!Overlaps(placements[left].Time, placements[right].Time)) {
                        continue;
                    }
                    if (UsesResource(placements[left], resource) && UsesResource(placements[right], resource)) {
                        overused = true;
                        break;
                    }
                }
            }
            if (overused) {
                violations.Add("capacity:" + resource);
            }
        }

        return new ActivityScheduleValidation(violations);
    }

    /// <summary>两个区间是否重叠 / Whether two intervals overlap.</summary>
    /// <param name="lhs">左区间 / The left interval.</param>
    /// <param name="rhs">右区间 / The right interval.</param>
    /// <returns>是否重叠 / Whether they overlap.</returns>
    private static bool Overlaps(
        Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange lhs,
        Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange rhs)
        => lhs.Start < rhs.End && rhs.Start < lhs.End;

    /// <summary>一次放置是否占用某资源 / Whether a placement uses a resource.</summary>
    /// <param name="placement">放置 / The placement.</param>
    /// <param name="resource">资源身份 / The resource identity.</param>
    /// <returns>是否占用 / Whether it uses the resource.</returns>
    private static bool UsesResource(ActivityPlacement<Flt64> placement, string resource) {
        foreach (ActivityReservation<Flt64> reservation in placement.Reservations) {
            if (string.Equals(reservation.Resource, resource, System.StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}
