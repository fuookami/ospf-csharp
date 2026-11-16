#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>一个原始约束组的统计 / Statistics for one original constraint group.</summary>
/// <param name="Group">组名称，<c>null</c> 表示未分组 / Group name; null means ungrouped.</param>
/// <param name="InvolvedInstances">组内涉及的约束实例数 / Constraint instances involved in this group.</param>
/// <param name="ActiveInstances">组内活动实例数 / Active instances in this group.</param>
/// <param name="BlockingInstances">组内阻塞实例数 / Blocking instances in this group.</param>
/// <param name="MaxImprovement">组内已证明的最大改善 / Largest proven improvement in this group.</param>
public sealed record AnalysisGroupSummary(
    string? Group,
    int InvolvedInstances,
    int ActiveInstances,
    int BlockingInstances,
    Flt64? MaxImprovement)
{
    /// <summary>组内是否有阻塞约束 / Whether the group holds a blocking constraint.</summary>
    public bool IsBlocking => BlockingInstances > 0;
}

/// <summary>
/// 报告侧的三个纯函数 / The three pure functions on the report side.
/// </summary>
/// <remarks>
/// 补完 `CriticalConstraintAnalysisReport.kt` 的报告侧**：`buildEffectivenessRanking`、`AnalysisGroupSummary`、`summarizeGroups`。
/// 这三者都不碰后端。**报告构造器 `CriticalConstraintAnalysisReportBuilder` 仍缺**——它要把各阶段结果拼成一份完整报告。
///
/// **completes the report side of `CriticalConstraintAnalysisReport.kt`**: the effectiveness builder, the group summary and the group summarizer. None of the three touches a
/// backend. **The report builder itself is still missing** — it assembles the stage results into one complete report.
/// </remarks>
public static class CriticalAnalysisReports
{
    /// <summary>
    /// 从扰动报告构造有效性排序 / Build the effectiveness ranking from perturbation reports.
    /// </summary>
    /// <param name="baselineObjective">基线目标值 / Baseline objective value.</param>
    /// <param name="funnel">候选漏斗排序，可为 <c>null</c> / Candidate funnel ranking, or <c>null</c>.</param>
    /// <param name="perturbationReports">扰动报告 / Perturbation reports.</param>
    /// <returns>有效性排序或错误 / The ranking, or an error.</returns>
    /// <remarks>
    /// **只有 `Unknown` 被排除，`NoObservedEffect` 保留**：一个"测过了、没有效果"的点是一条证据，
    /// 而一次"没有结论"的求解不是。**把两者一起丢掉，报告就无法区分"没测"和"测了没用"。**
    ///
    /// **`MarginalEffect` 要求 `FirstEffectiveDelta` 严格为正**：`0` 会让它变成 `Infinity`，
    /// `NaN` 会混进排序。**两个都拿到了才算得出来，否则宁可给 `null`。**
    ///
    /// **Only `Unknown` is dropped, `NoObservedEffect` is kept**: a point that was measured and showed nothing is evidence, while a solve that reached no conclusion is not.
    /// **Dropping both would erase the difference between "not measured" and "measured, no effect".** **`MarginalEffect` needs a strictly positive `FirstEffectiveDelta`**: a
    /// zero would make it `Infinity` and a `NaN` would slip into the ordering. **Both have to be present to compute it; otherwise `null` is better.**
    /// </remarks>
    public static Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> BuildEffectivenessRanking(
        Flt64 baselineObjective,
        CandidateFunnelRanking? funnel,
        IReadOnlyList<ConstraintPerturbationReport> perturbationReports) {
        ArgumentNullException.ThrowIfNull(perturbationReports);

        if (double.IsNaN(baselineObjective.ToDouble()) || double.IsInfinity(baselineObjective.ToDouble())) {
            return Results.Failed<EffectivenessRanking>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "基线目标值必须有限 / Baseline objective must be finite"));
        }

        var byId = (funnel?.Candidates ?? [])
            .ToDictionary(candidate => candidate.ConstraintId);

        var entries = new List<EffectivenessEntry>(perturbationReports.Count);
        foreach (ConstraintPerturbationReport report in perturbationReports) {
            byId.TryGetValue(report.ConstraintId, out ConstraintCandidate? candidate);

            IReadOnlyList<ConstraintPerturbationObservation> proven = report.ObservationList
                .Concat(report.Removal is { } removal ? [removal] : Array.Empty<ConstraintPerturbationObservation>())
                .Where(observation => observation.Outcome != PerturbationOutcome.Unknown)
                .ToArray();

            Flt64? maxImprovement = proven
                .Select(observation => observation.ObjectiveImprovement)
                .Where(improvement => improvement is { } value && IsFinite(value))
                .Select(improvement => improvement!.Value)
                .OrderByDescending(value => value.ToDouble())
                .Cast<Flt64?>()
                .FirstOrDefault();

            Flt64? firstEffectiveDelta = proven
                .Where(observation => observation.Outcome == PerturbationOutcome.Effective)
                .Select(observation => observation.Delta)
                .Where(delta => delta is not null)
                .OrderBy(delta => delta!.Value.ToDouble())
                .FirstOrDefault();

            Flt64? marginalEffect = maxImprovement is { } improvementValue
                && firstEffectiveDelta is { } deltaValue
                && deltaValue.ToDouble() > 0.0
                ? new Flt64(improvementValue.ToDouble() / deltaValue.ToDouble())
                : null;

            entries.Add(new EffectivenessEntry(
                ConstraintId: report.ConstraintId,
                Group: candidate?.Group,
                Tier: candidate?.Tier ?? CandidateTier.Unclassified,
                Activity: candidate?.Activity ?? ActivityStatus.Unknown,
                DualValue: candidate?.DualValue,
                LocalEffective: candidate?.LocalEffective,
                RemovalEffective: report.RemovalEffective,
                FirstEffectiveDelta: firstEffectiveDelta,
                MaxImprovement: maxImprovement,
                MarginalEffect: marginalEffect,
                Status: report.Status,
                UnavailableReason: report.Message));
        }

        // **排序三级、每级都有理由**：已证明改善降序（这是报告的主排序）；
        // 其次 |对偶| 降序（没测出改善时，对偶值仍说明这一行有多紧）；
        // 最后稳定身份（**否则同一份输入两次运行可能给出不同顺序**）。
        //
        // **Three levels, each with a reason**: proven improvement descending (the report's main order); then |dual| descending (when no improvement was measured, the dual still
        // says how tight the row is); and finally the stable identity (**otherwise the same input could produce a different order on two runs**).
        IReadOnlyList<EffectivenessEntry> sorted = entries
            .OrderByDescending(entry => entry.MaxImprovement?.ToDouble() ?? double.NegativeInfinity)
            .ThenByDescending(entry => entry.DualValue is { } dual ? global::System.Math.Abs(dual.ToDouble()) : double.NegativeInfinity)
            .ThenBy(entry => entry.ConstraintId.Value, StringComparer.Ordinal)
            .ToArray();

        var ranking = new EffectivenessRanking(baselineObjective, sorted);
        return ranking.Validate() is Ok<Success, ErrorCode, Error<ErrorCode>>
            ? Results.Ok(ranking)
            : Results.Failed<EffectivenessRanking>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "有效性排序报告无效 / Effectiveness ranking is invalid"));
    }

    /// <summary>
    /// 按原始约束组汇总 / Summarize by original constraint group.
    /// </summary>
    /// <param name="activity">活动性报告，可为 <c>null</c> / Activity report, or <c>null</c>.</param>
    /// <param name="effectiveness">有效性排序，可为 <c>null</c> / Effectiveness ranking, or <c>null</c>.</param>
    /// <param name="conflict">冲突解释，可为 <c>null</c> / Conflict explanation, or <c>null</c>.</param>
    /// <returns>按组统计，顺序为首次出现顺序 / Per-group statistics in first-seen order.</returns>
    /// <remarks>
    /// **组的归属以活动性报告为准**：冲突解释**刻意只保存稳定身份**，它自己不带组名。
    /// 所以阻塞约束的组必须从活动性报告反查——**查不到就归到 `null` 组，而不是猜一个。**
    ///
    /// **Group membership comes from the activity report**: the conflict explanation **deliberately stores only stable identities** and carries no group name, so a blocking
    /// constraint's group has to be looked up there — **and when it is not found it lands in the `null` group rather than being guessed.**
    /// </remarks>
    public static IReadOnlyList<AnalysisGroupSummary> SummarizeGroups(
        ConstraintActivityReport? activity,
        EffectivenessRanking? effectiveness,
        ConflictExplanation? conflict) {
        // **刻意不用 `Dictionary<string?, …>`**：`null` 是合法的组名（未分组），而字典不接受 null 键。
        // 换成"列表 + 线性查找"——组的数量是个位数，代价可以忽略，**而它不会在未分组时抛异常。**
        //
        // **Deliberately not `Dictionary<string?, …>`**: `null` is a legitimate group name (ungrouped) and dictionaries reject null keys. A list with linear lookup is used
        // instead — groups number in the single digits, so the cost is negligible, **and it does not throw on the ungrouped case.**
        var order = new List<string?>();

        void Touch(string? group) {
            if (!order.Contains(group)) {
                order.Add(group);
            }
        }

        IReadOnlyList<ConstraintActivity> activities = activity?.ConstraintActivities ?? [];
        foreach (ConstraintActivity record in activities) {
            if (record.ConstraintId is null) {
                continue;
            }
            Touch(record.Group);
        }
        foreach (EffectivenessEntry record in effectiveness?.EntryList ?? []) {
            if (record.MaxImprovement is not null) {
                // **活动性报告里有这个约束就用它的组，否则回退到条目自带的组。**
                // **When the activity report knows the constraint its group wins; otherwise the entry's own group is used.**
                string? known = activities
                    .FirstOrDefault(candidate => candidate.ConstraintId == record.ConstraintId)?.Group;
                Touch(known ?? record.Group);
            }
        }
        IReadOnlySet<ConstraintId> conflictIds = conflict?.ConstraintIds
            ?? (IReadOnlySet<ConstraintId>)new HashSet<ConstraintId>();
        foreach (ConstraintId _ in conflictIds) {
            // **冲突解释不带组名**，所以这一轮只保证"有阻塞就有一个组出现"，具体归属在下面按 id 反查。
            // **The conflict explanation carries no group name**, so this only guarantees a group appears; the actual attribution is looked up by id below.
            Touch(null);
        }

        string? GroupOf(ConstraintId id) =>
            activities.FirstOrDefault(candidate => candidate.ConstraintId == id)?.Group;

        var summaries = new List<AnalysisGroupSummary>();
        foreach (string? group in order) {
            int involved = activities.Count(record => record.ConstraintId is not null && record.Group == group);
            int active = activities.Count(record =>
                record.ConstraintId is not null && record.Group == group && record.Status == ActivityStatus.Active);

            Flt64? maxImprovement = null;
            foreach (EffectivenessEntry record in effectiveness?.EntryList ?? []) {
                if (record.MaxImprovement is not { } value) {
                    continue;
                }
                string? recordGroup = GroupOf(record.ConstraintId) ?? record.Group;
                if (recordGroup != group) {
                    continue;
                }
                if (maxImprovement is not { } current || value.ToDouble() > current.ToDouble()) {
                    maxImprovement = value;
                }
            }

            // **阻塞归属只按 id 反查，没有回退**：冲突解释不带组名，反查不到就归 `null` 组，**不猜一个名字**。
            // **Blocking attribution is by id lookup only, with no fallback**: the conflict explanation has no group name, so a miss lands in the `null` group, **never a guessed name**.
            int blocking = conflictIds.Count(id => GroupOf(id) == group);

            summaries.Add(new AnalysisGroupSummary(
                Group: group,
                InvolvedInstances: involved,
                ActiveInstances: active,
                BlockingInstances: blocking,
                MaxImprovement: maxImprovement));
        }
        return summaries;
    }

    private static bool IsFinite(Flt64 value) =>
        !double.IsNaN(value.ToDouble()) && !double.IsInfinity(value.ToDouble());
}
