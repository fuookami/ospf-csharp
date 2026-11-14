#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 跨目标的计划聚合 / Cross-target plan aggregation.
/// </summary>
/// <remarks>
/// **三道门缺一不可**：目标 `Unreachable`、**报告的目标就是冲突的目标**、冲突已验证最小。
///
/// **第二道门看起来冗余，而它不是。** 一份报告可以带着另一个目标的冲突
/// （局部敏感性与目标分析共用一份报告），**于是"这份报告里有冲突"不等于"这个目标有冲突"**——
/// **删掉它会把一个目标的修复建议挂到另一个目标名下。**
///
/// **Three gates are all needed**: the target is `Unreachable`, **the report's target is the conflict's target**, and the conflict is verified minimal.
/// **The second gate looks redundant and is not.** A report may carry another target's conflict (local sensitivity and target analysis share a report), **so "this report has a conflict" is not "this
/// target has a conflict" — dropping it files one target's repair advice under another target's name.**
/// </remarks>
public class CriticalityPlanAggregationTests {
    private static ObjectiveTarget Target(string name) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId(name), new Flt64(10.0));

    private static DiagnosticSource Source(string name) => new DiagnosticSource.Constraint(new ConstraintId(name));

    private static ConflictExplanation Conflict(ObjectiveTarget target, string member, ConflictMinimality minimality) {
        var feasibility = new TargetFeasibilityReport(target, Status: AnalysisStatus.Unreachable);
        return new ConflictExplanation(
            Target: target,
            TargetFeasibility: feasibility,
            ExtractionTier: ConflictExtractionTier.RepeatedSolving,
            Status: AnalysisStatus.Unreachable,
            AvailableEvidence: new[] { Source(member) },
            Members: new[] { Source(member) },
            TargetSource: null,
            Validity: ConflictValidity.Verified,
            Minimality: minimality);
    }

    private static CriticalConstraintAnalysisReport Report(
        ObjectiveTarget target,
        ConflictExplanation? conflict,
        AnalysisStatus status) {
        var feasibility = new TargetFeasibilityReport(target, Status: status);
        return new CriticalConstraintAnalysisReport(
            Baseline: new AnalysisBaselineSummary(
                ObjectiveId: "objective:main",
                Sense: "Minimize",
                ObjectiveValue: null,
                Status: status,
                ProvenOptimal: false),
            Activity: new ConstraintActivityReport(
                Activities: Array.Empty<ConstraintActivity>(),
                GroupSummaries: Array.Empty<ActivityGroupSummary>(),
                Tolerance: 1e-9,
                NearlyActiveTolerance: 1e-6),
            LocalSensitivity: null,
            Funnel: new CandidateFunnelRanking(0.0, Array.Empty<ConstraintCandidate>(), 0, 0, 0, 0),
            Effectiveness: null,
            Target: feasibility,
            Conflict: conflict,
            GroupSummaries: Array.Empty<AnalysisGroupSummary>(),
            Status: status,
            UnavailableReasons: Array.Empty<string>());
    }

    /// <summary>**已验证最小且目标一致时产出计划** / A verified, matching conflict produces plans.</summary>
    [Fact]
    public void TheVerifiedConflict_ShouldProduceAPlan() {
        ObjectiveTarget target = Target("objective:a");

        IReadOnlyList<AlternativeImprovementPlan> plans = CorrectionProjection.AggregatePlans(new[]
        {
            Report(target, Conflict(target, "constraint:x", ConflictMinimality.Irreducible), AnalysisStatus.Unreachable),
        });

        plans.Should().ContainSingle("one verified conflict gives one plan");
        plans[0].Target.Should().Be(target);
        plans[0].Rank.Should().Be(0, "ranks are reassigned across the whole list");
    }

    /// <summary>**报告目标与冲突目标不一致时不得产出** / A mismatched target produces nothing.</summary>
    /// <remarks>
    /// **这条盯的就是那道看起来冗余的门。** 报告的 `Target` 指向 A，
    /// 而 `Conflict` 指向 B——**删掉一致性检查，A 名下会出现 B 的修复建议。**
    ///
    /// **This is the criterion for the gate that looks redundant.** The report's `Target` is A while its `Conflict` points at B — **drop the equality check and B's advice appears under A.**
    /// </remarks>
    [Fact]
    public void TheMismatchedTarget_ShouldProduceNothing() {
        ObjectiveTarget reported = Target("objective:a");
        ObjectiveTarget conflicting = Target("objective:b");

        CorrectionProjection.AggregatePlans(new[]
            {
                Report(reported, Conflict(conflicting, "constraint:x", ConflictMinimality.Irreducible),
                    AnalysisStatus.Unreachable),
            })
            .Should().BeEmpty("B's repair advice must not be filed under A");
    }

    /// <summary>**未验证最小的冲突不得产出** / An unverified conflict produces nothing.</summary>
    [Theory]
    [InlineData(ConflictMinimality.Partial)]
    [InlineData(ConflictMinimality.NotChecked)]
    public void TheUnverifiedMinimality_ShouldProduceNothing(ConflictMinimality minimality) {
        ObjectiveTarget target = Target("objective:a");

        CorrectionProjection.AggregatePlans(new[]
            {
                Report(target, Conflict(target, "constraint:x", minimality), AnalysisStatus.Unreachable),
            })
            .Should().BeEmpty("an unverified conflict cannot justify a repair");
    }

    /// <summary>**可达的目标不得产出计划** / A reachable target produces nothing.</summary>
    [Fact]
    public void TheReachableTarget_ShouldProduceNothing() {
        ObjectiveTarget target = Target("objective:a");

        CorrectionProjection.AggregatePlans(new[]
            {
                Report(target, Conflict(target, "constraint:x", ConflictMinimality.Irreducible), AnalysisStatus.Reachable),
            })
            .Should().BeEmpty("there is nothing to repair in a reachable target");
    }

    /// <summary>**跨目标按成本排序并重新编号** / Plans are ordered by cost across targets and renumbered.</summary>
    /// <remarks>
    /// **排序键是（总成本，目标 stableId，成员 stableId）**，`Rank` 按聚合后的位置重排——
    /// **不是每份报告各自从 0 开始**，否则两张表拼在一起会有两个 0 号。
    ///
    /// **The key is (total cost, target stable id, member stable ids)** and `Rank` follows the aggregated position — **not each report restarting at zero**, which would leave two rank zeros in one list.
    /// </remarks>
    [Fact]
    public void TheAggregate_ShouldOrderByCostAndRenumber() {
        ObjectiveTarget early = Target("objective:a");
        ObjectiveTarget late = Target("objective:b");

        IReadOnlyList<AlternativeImprovementPlan> plans = CorrectionProjection.AggregatePlans(new[]
        {
            Report(late, Conflict(late, "constraint:z", ConflictMinimality.Irreducible), AnalysisStatus.Unreachable),
            Report(early, Conflict(early, "constraint:y", ConflictMinimality.Irreducible), AnalysisStatus.Unreachable),
        });

        plans.Should().HaveCount(2);
        plans.Select(plan => plan.Rank).Should().Equal(new[] { 0, 1 },
            "ranks are positional in the aggregate, not per report");
        plans.Select(plan => plan.Target.StableId).Should().BeInAscendingOrder(StringComparer.Ordinal,
            "equal costs fall back to the target identity, so the order is stable across runs");
    }
}
