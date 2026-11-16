#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 冲突解释投影成修正集合的判定 / Verdicts that project a conflict explanation into correction sets.
/// </summary>
/// <remarks>
/// **这是 里三个纯函数的移植。** 三个都不碰求解器：
/// 一个判定"这份解释够不够格谈 MCS"，另两个把够格的解释投影成单例修正集合。
///
/// **These are three pure functions.** None touches a solver: one decides whether the explanation qualifies as an MCS, the other two project a
/// qualifying explanation into singleton correction sets.
/// </remarks>
public static class CorrectionProjection
{
    /// <summary>
    /// 是否是一个已证实的不可约阻塞集合 / Whether this is a verified irreducible blocking set.
    /// </summary>
    /// <param name="conflict">冲突解释 / The conflict explanation.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    /// <remarks>
    /// **九个条件，缺一不可。** 这不是防御性编程，**每一条排除的都是"看起来像 MCS 但其实不是"的东西：**
    ///
    /// | 条件 | 排除的假阳性 |
    /// | --- | --- |
    /// | `Status == Unreachable` | **目标其实是可达的** |
    /// | `Validity == Verified` | 后端给的、没复验过的冲突 |
    /// | `MinimalityVerified` | **只跑了部分删除检查** |
    /// | `!BackgroundBlocking` | 阻塞来自固定背景，**删掉哪条成员都没用** |
    /// | 目标一致 + 可行性目标不可达 | **解释的目标和可行性检查的目标不是同一个** |
    /// | `Members` 非空 | 空集合会让"每个成员生成一个修正集合"变成空操作 |
    /// | `Members` 去重后数量不变 | **同一来源出现两次，会让"删一个就够"变成假话** |
    /// | 每个成员都能转成不可行成员 | 目标源这类根本不能"删" |
    /// | 每个成员都在 `AvailableEvidence` 里 | **成员来自另一份证据集，删它无从谈起** |
    ///
    /// **`!BackgroundBlocking` 与"目标一致"这两条最容易漏**：前者会让一份"背景挡着"的解释声称每个成员都是可删的；
    /// **后者会让解释去描述一个它没有检查过的目标。**
    ///
    /// **Nine conditions, none optional.** This is not defensive coding — **each one excludes a specific false positive.** **`!BackgroundBlocking` and the target agreement are the two
    /// easiest to miss**: the former lets a "blocked by background" explanation claim every member is deletable; **the latter lets an explanation describe a target it never checked.**
    /// </remarks>
    public static bool IsVerifiedMinimalConflict(ConflictExplanation conflict) {
        ArgumentNullException.ThrowIfNull(conflict);

        if (conflict.Status != AnalysisStatus.Unreachable
            || conflict.Validity != ConflictValidity.Verified
            || !conflict.MinimalityVerified
            || conflict.BackgroundBlocking) {
            return false;
        }

        // **解释的目标与可行性检查的目标必须是同一个。**
        // **The explanation target and the feasibility target have to be the same one.**
        if (!Equals(conflict.TargetFeasibility.Target, conflict.Target)
            || conflict.TargetFeasibility.Status != AnalysisStatus.Unreachable) {
            return false;
        }

        IReadOnlyList<DiagnosticSource> members = conflict.Members;
        if (members.Count == 0) {
            return false;
        }

        // **去重后数量不变**：同一来源出现两次会让"删一个就够"变成假话。
        // **The de-duplicated count is unchanged**: a source listed twice would make "deleting one is enough" false.
        if (members.Select(source => source.StableId).Distinct().Count() != members.Count) {
            return false;
        }

        return members.All(source =>
            source.AsInfeasibilityMember() is not null
            && conflict.AvailableEvidence.Contains(source)
            && source is not DiagnosticSource.ObjectiveTargetSource);
    }

    /// <summary>
    /// 不可约阻塞集合投影成单例修正集合 / Project an irreducible blocking set into singleton correction sets.
    /// </summary>
    /// <param name="conflict">冲突解释 / The conflict explanation.</param>
    /// <returns>每个成员一个修正集合；不成立时为空 / One correction set per member; empty when the predicate does not hold.</returns>
    /// <remarks>
    /// **不成立时返回空列表，而不是抛异常**：这是"这个冲突不足以支撑 MCS 结论"，
    /// **不是"调用方用错了 API"。** 两者要分开。
    ///
    /// **Returns an empty list rather than throwing**: this is "the conflict does not support an MCS claim", **not "the caller misused the API".** The two must stay separate.
    /// </remarks>
    public static IReadOnlyList<CorrectionSet> CorrectionSetsFromConflict(ConflictExplanation conflict) {
        ArgumentNullException.ThrowIfNull(conflict);

        if (!IsVerifiedMinimalConflict(conflict)) {
            return [];
        }

        return conflict.Members
            .Select(source => new CorrectionSet(
                Members: [new CorrectionCandidate(source, Weight: 1.0, Relaxation: 1.0)],
                TotalCost: 1.0,
                Minimal: true))
            .ToArray();
    }

    /// <summary>
    /// 带成本的单例修正集合 / Singleton correction sets with costs.
    /// </summary>
    /// <param name="conflict">冲突解释 / The conflict explanation.</param>
    /// <param name="costs">按来源稳定 ID 给出的成本 / Costs keyed by the source stable id.</param>
    /// <param name="policy">策略 / The policy.</param>
    /// <returns>按总成本升序、最多 `MaxPlans` 条 / Sorted by total cost, at most `MaxPlans`.</returns>
    /// <remarks>
    /// **排序在截断之前**：先 `take` 再 `sort` 会留下"最小的几条"里恰好是最贵的那几条。
    /// **排序键第二段是稳定 ID**，因为总成本相同的结果必须有确定顺序，
    /// **否则同一输入两次运行给出不同的计划表。**
    ///
    /// **Sort before truncating**: `take` then `sort` would keep whichever expensive ones happened to come first.
    /// **The second sort key is the stable id**, because equal-cost results need a deterministic order —
    /// **otherwise the same input yields a different plan list on two runs.**
    /// </remarks>
    public static IReadOnlyList<CorrectionSet> WeightedCorrectionSetsFromConflict(
        ConflictExplanation conflict,
        IReadOnlyDictionary<string, RelaxationCost>? costs = null,
        RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(conflict);
        policy ??= new RelaxabilityPolicy();
        costs ??= new Dictionary<string, RelaxationCost>();

        if (!IsVerifiedMinimalConflict(conflict)) {
            return [];
        }

        var sets = new List<CorrectionSet>();
        foreach (DiagnosticSource source in conflict.Members) {
            RelaxationCost cost = costs.TryGetValue(source.StableId, out RelaxationCost? found)
                ? found
                : new RelaxationCost();
            if (policy.RequirePositiveWeight && cost.Weight <= 0.0) {
                throw new ArgumentException(
                    $"修正权重必须为正：{source.StableId} / correction weight must be positive: {source.StableId}",
                    nameof(costs));
            }

            var candidate = new CorrectionCandidate(source, cost.Weight, cost.Relaxation);
            sets.Add(new CorrectionSet(
                Members: [candidate],
                TotalCost: candidate.Weight * candidate.Relaxation,
                Minimal: true));
        }

        return sets
            .OrderBy(set => set.TotalCost)
            .ThenBy(set => set.Members[0].Source.StableId, StringComparer.Ordinal)
            .Take(policy.MaxPlans)
            .ToArray();
    }

    /// <summary>
    /// 把多份报告的改进计划聚合成一个排序过的清单 / Aggregate the improvement plans of several reports into one ordered list.
    /// </summary>
    /// <param name="reports">每份目标报告 / One report per target.</param>
    /// <param name="costs">松弛成本 / Relaxation costs.</param>
    /// <param name="policy">策略 / The policy.</param>
    /// <returns>跨目标排序并重新编号的计划 / Plans ordered across targets and renumbered.</returns>
    /// <remarks>
    /// **这是 `analyzeCriticalityTargets` 里那段纯聚合的移植。** 三道门缺一不可：
    /// **目标状态必须是 `Unreachable`**、**报告的目标必须就是冲突的目标**、**冲突必须已验证最小。**
    ///
    /// **第二道门容易被当成冗余删掉。** 它不是：一个报告的 `Conflict` 可以来自另一个目标
    /// （局部敏感性与目标分析共用一份报告），**于是"这份报告里有冲突"不等于"这个目标有冲突"**——
    /// **删掉它就会把一个目标的修复建议挂到另一个目标名下。**
    ///
    /// **This is the implementation of the pure aggregation inside `analyzeCriticalityTargets`.** Three gates are all needed: **the target status must be `Unreachable`**, **the report's target must be the conflict's target**, and **the
    /// conflict must be verified minimal.** **The second gate looks redundant and is not**: a report's `Conflict` may belong to another target (local sensitivity and target analysis share a report), **so "this
    /// report has a conflict" is not "this target has a conflict" — dropping it files one target's repair advice under another target's name.**
    /// </remarks>
    public static IReadOnlyList<AlternativeImprovementPlan> AggregatePlans(
        IReadOnlyList<CriticalConstraintAnalysisReport> reports,
        IReadOnlyDictionary<string, RelaxationCost>? costs = null,
        RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(reports);

        RelaxabilityPolicy effective = policy ?? new RelaxabilityPolicy();
        var aggregated = new List<AlternativeImprovementPlan>();

        foreach (CriticalConstraintAnalysisReport report in reports) {
            if (report.Conflict is not { } conflict
                || report.Target is not { } target
                || target.Status != AnalysisStatus.Unreachable
                || !Equals(target.Target, conflict.Target)
                || !IsVerifiedMinimalConflict(conflict)) {
                continue;
            }

            aggregated.AddRange(CorrectionRecommendations.AlternativeImprovementPlansFromConflict(
                conflict, costs ?? new Dictionary<string, RelaxationCost>(), effective));
        }

        return aggregated
            .OrderBy(plan => plan.CorrectionSet.TotalCost)
            .ThenBy(plan => plan.Target.StableId, StringComparer.Ordinal)
            .ThenBy(
                plan => string.Join(',', plan.CorrectionSet.Members.Select(member => member.Source.StableId)),
                StringComparer.Ordinal)
            .Take(effective.MaxPlans)
            .Select((plan, index) => plan with { Rank = index })
            .ToArray();
    }
}
