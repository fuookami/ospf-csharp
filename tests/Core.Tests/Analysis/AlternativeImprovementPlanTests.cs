#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 改进方案的名次 / The rank of an improvement plan.
/// </summary>
/// <remarks>
/// **`Rank` 是列表下标，不是评分。** 它只在"同一个冲突、同一套成本、同一份策略"下可比，
/// **而 `AlternativeImprovementPlan.Validate` 会拿它和 `MaxPlans` 比**——名次越界是真实的配置错误。
///
/// **`Rank` is the list index, not a score.** It is only comparable within one conflict, one cost table and one policy, **and `Validate` compares it against `MaxPlans`** — an out-of-range rank is a real
/// configuration error.
/// </remarks>
public class AlternativeImprovementPlanTests {
    private static readonly ObjectiveTarget Target = ObjectiveTarget.CreateAtLeast(
        new Fuookami.Ospf.Core.Solver.Report.ObjectiveId("obj"),
        new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0));

    private static ConflictExplanation Explanation(params DiagnosticSource[] members) =>
        new(
            Target: Target,
            TargetFeasibility: new TargetFeasibilityReport(Target, Status: AnalysisStatus.Unreachable),
            ExtractionTier: ConflictExtractionTier.Unavailable,
            Status: AnalysisStatus.Unreachable,
            AvailableEvidence: members,
            Members: members,
            TargetSource: null,
            Validity: ConflictValidity.Verified,
            Minimality: ConflictMinimality.Irreducible,
            Verifications: null);

    /// <summary>**每个方案的名次就是它的下标** / Each plan's rank is its index.</summary>
    /// <remarks>
    /// **反向对照**：如果实现恒返回 `Rank: 0`，只断言"第一个是 0"会通过。
    /// **所以要断言整串是 0,1,2…**，而不是首项。
    ///
    /// **The reverse control**: an implementation returning `Rank: 0` always would pass a check of only the first element. **So the whole sequence is asserted to be 0,1,2…**, not just the head.
    /// </remarks>
    [Fact]
    public void TheRanks_ShouldBeTheIndices() {
        var members = new DiagnosticSource[] {
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c1")),
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c2")),
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c3")),
        };

        IReadOnlyList<AlternativeImprovementPlan> plans =
            CorrectionRecommendations.AlternativeImprovementPlansFromConflict(Explanation(members));

        plans.Should().NotBeEmpty("three removable members should yield at least one plan");
        plans.Select(plan => plan.Rank).Should().BeInAscendingOrder();
        plans.Select(plan => plan.Rank).Should().Equal(
            Enumerable.Range(0, plans.Count).ToArray(),
            "the rank is the index, so a duplicated or skipped rank is a bug");
    }

    /// <summary>**每个方案都指向同一个目标** / Every plan points at the same target.</summary>
    /// <remarks>
    /// 一个方案列表只描述**一个**冲突，所以目标必须一致——**混进别的目标的方案会被误读成"另一种改法"。**
    ///
    /// A plan list describes **one** conflict, so the target must agree — **a plan carrying another target reads as "a different fix".**
    /// </remarks>
    [Fact]
    public void ThePlans_ShouldAllPointAtTheSameTarget() {
        var members = new DiagnosticSource[] {
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c1")),
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c2")),
        };

        IReadOnlyList<AlternativeImprovementPlan> plans =
            CorrectionRecommendations.AlternativeImprovementPlansFromConflict(Explanation(members));

        plans.Should().NotBeEmpty();
        plans.Should().OnlyContain(plan => plan.Target == Target);
    }

    /// <summary>**空成员给出空方案表，不是一项** / No members yield no plans, not one.</summary>
    /// <remarks>
    /// 给出一项空修正集会让调用方**把"没什么可放宽"读成"有一个方案"**。
    ///
    /// Returning one empty correction set makes the caller **read "nothing to relax" as "there is one plan".**
    /// </remarks>
    [Fact]
    public void TheEmptyConflict_ShouldYieldNoPlans() {
        CorrectionRecommendations.AlternativeImprovementPlansFromConflict(Explanation())
            .Should().BeEmpty("nothing removable means no plan, and an empty plan is not a plan");
    }

    /// <summary>**"放宽建议"与"改进方案"是同一批东西** / The two names return the same plans.</summary>
    /// <remarks>
    /// 别名如果和主干漂移，**读"放宽建议"的调用方会拿到另一批方案**——而名字暗示它们是同一批。
    ///
    /// If the alias drifts from the trunk, **a caller reading "relaxation recommendations" gets a different set** — while the name implies they are the same.
    /// </remarks>
    [Fact]
    public void TheAlias_ShouldMatchTheTrunk() {
        var members = new DiagnosticSource[] {
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c1")),
            new DiagnosticSource.Constraint(new Fuookami.Ospf.Core.Solver.Report.ConstraintId("c2")),
        };

        IReadOnlyList<AlternativeImprovementPlan> trunk =
            CorrectionRecommendations.AlternativeImprovementPlansFromConflict(Explanation(members));
        IReadOnlyList<AlternativeImprovementPlan> alias =
            CorrectionRecommendations.RelaxationRecommendationsFromConflict(Explanation(members));

        // **比名次序列，不比记录相等**：记录里的成员是列表，列表按引用比较，
        // **两次调用必然得到不同的列表实例——那会让这条判据永远为假。**
        // **Compare the rank sequences, not record equality**: the records hold lists, lists compare by reference, **so two calls always yield different instances and the assertion would always fail.**
        alias.Select(plan => plan.Rank).Should().Equal(trunk.Select(plan => plan.Rank),
            "the alias forwards, so it cannot produce a differently ranked list");
        alias.Should().HaveCount(trunk.Count);
    }
}
