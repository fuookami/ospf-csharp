#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using FunnelRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.CandidateFunnelRanking, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 候选漏斗 / The candidate funnel.
/// </summary>
/// <remarks>
/// **B969 补 `CriticalConstraintAnalysisReport` 的漏斗块**（基线 872 行里的前 340 行左右）。
/// 基线的其余两块要 `ConstraintPerturbationReport` 与 `ConflictExplanation`，这两个都住在**带后端求解**的文件里，
/// 所以这一批只搬漏斗，**没搬的部分不写空跑版本**。
///
/// 判据钉的是**"没有证据就不许分层"**：这一层最容易犯的错是把 `Unknown` 读成"不重要"。
///
/// **B969 ported the funnel block of `CriticalConstraintAnalysisReport`** — roughly the first 340 of its 872 baseline lines. The other two blocks need `ConstraintPerturbationReport`
/// and `ConflictExplanation`, both of which live inside files that **do solve on a backend**, so this batch ported the funnel only and **stubbed nothing**. The criteria pin
/// **"no evidence, no tier"**: the mistake this layer is most likely to make is reading `Unknown` as "unimportant".
/// </remarks>
public class CriticalConstraintAnalysisTests {
    private static ConstraintActivity Constraint(string id, string? group, ActivityStatus status) => new(
        new ConstraintId(id),
        group,
        status,
        new ActivityEvidence(
            new DiagnosticSource.Constraint(new ConstraintId(id)),
            ActivityEvidenceKind.Comparison,
            Satisfied: status == ActivityStatus.Active));

    private static ConstraintActivityReport ActivityReport(params ConstraintActivity[] records) =>
        new(records, [], Tolerance: 1e-6, NearlyActiveTolerance: 1e-3);

    /// <summary>**没有对偶证据就只能 `Unclassified`** / Without dual evidence the answer is `Unclassified`.</summary>
    /// <remarks>
    /// active 但没有双数 → `Unclassified`（不是 TierB）；`SatisfiedWithoutSlackMetric` 与 `Inactive` → `TierC`；
    /// `Violated`/`Unknown` → `Unclassified`。**把"未知"当成"不重要"会让一份没有证据的报告给出结论。**
    ///
    /// Active without a dual is `Unclassified` (not TierB); satisfied-without-metric and inactive are `TierC`; violated and unknown stay `Unclassified`. **Reading "unknown" as
    /// "unimportant" lets a report with no evidence produce a verdict.**
    /// </remarks>
    [Fact]
    public void TheClassification_ShouldNotInventATier() {
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Active, dualValue: null)
            .Should().Be(CandidateTier.Unclassified,
                "active with no dual is not evidence of a small dual, it is no evidence at all");
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Active, new Flt64(1.0))
            .Should().Be(CandidateTier.TierA, "a dual above the threshold is tier A");
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Active, new Flt64(0.0))
            .Should().Be(CandidateTier.TierB, "a dual at the threshold is tier B");

        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.NearlyActive, null)
            .Should().Be(CandidateTier.Unclassified);
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Inactive, null)
            .Should().Be(CandidateTier.TierC);
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.SatisfiedWithoutSlackMetric, null)
            .Should().Be(CandidateTier.TierC);
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Violated, null)
            .Should().Be(CandidateTier.Unclassified,
                "a violated constraint is not unimportant, it is unclassified");
        CriticalCandidateFunnel.ClassifyCandidate(ActivityStatus.Unknown, null)
            .Should().Be(CandidateTier.Unclassified);
    }

    /// <summary>**没有敏感性报告时，所有候选都停在 `Unclassified`** / With no sensitivity report every candidate stays unclassified.</summary>
    [Fact]
    public void TheFunnel_ShouldClaimNoTierWithoutASensitivityReport() {
        FunnelRet report = CriticalCandidateFunnel.BuildCandidateFunnel(
            ActivityReport(
                Constraint("c1", "g1", ActivityStatus.Active),
                Constraint("c2", "g1", ActivityStatus.Inactive)),
            sensitivity: null);

        // **说清结果是什么，而不是只说"没失败"**：成功时携带的是漏斗本身。
        // **Say what the result is, not just that nothing failed**: success carries the funnel itself.
        CandidateFunnelRanking funnel = report.Value;
        funnel.Should().NotBeNull("a successful build carries the ranking itself");
        funnel.Candidates.Should().HaveCount(2, "Tier C is still derived from activity and retained");
        funnel.TierA.Should().Be(0);
        funnel.TierB.Should().Be(0);
        funnel.Unclassified.Should().Be(1, "the active record has no dual, so it cannot be tiered");
        funnel.TierC.Should().Be(1, "the inactive record still gets its tier from activity alone");
    }

    /// <summary>**层级、`|dual|` 降序、稳定身份三层排序** / Tier, then descending `|dual|`, then stable identity.</summary>
    [Fact]
    public void TheOrdering_ShouldBeDeterministic() {
        var sensitivity = new FixedIntegerLpSensitivityReport(
            AnalysisStatus.Reachable,
            Sensitivities: [
                new LocalConstraintSensitivity(new ConstraintId("c1"), DualValue: new Flt64(2.0)),
                new LocalConstraintSensitivity(new ConstraintId("c2"), DualValue: new Flt64(9.0)),
                new LocalConstraintSensitivity(new ConstraintId("c3"), DualValue: new Flt64(0.0)),
            ]);
        FunnelRet report = CriticalCandidateFunnel.BuildCandidateFunnel(
            ActivityReport(
                Constraint("c1", "g1", ActivityStatus.Active),
                Constraint("c2", "g1", ActivityStatus.Active),
                Constraint("c3", "g1", ActivityStatus.Active)),
            sensitivity);

        // **说清结果是什么，而不是只说"没失败"**：成功时携带的是漏斗本身。
        // **Say what the result is, not just that nothing failed**: success carries the funnel itself.
        CandidateFunnelRanking funnel = report.Value;
        funnel.Should().NotBeNull("a successful build carries the ranking itself");
        // **`Equal(params T[])` 的最后一个字符串不是 reason**：它会被当成第 4 个元素。
        // **The trailing string in `Equal(params T[])` is not a reason**: it becomes a fourth element.
        funnel.Candidates.Select(candidate => candidate.ConstraintId.Value).Should()
            .Equal("c2", "c1", "c3");
        funnel.Candidates[0].Tier.Should().Be(CandidateTier.TierA,
            "tier A first, and within it the larger dual comes first");
        // **`Equal(params T[])` 的最后一个字符串不是 reason**——第二次撞上了，连同 B967 那次一起记在文档里。
        // **The trailing string in `Equal(params T[])` is not a reason** — hit twice now, recorded in the document alongside the B967 case.
        funnel.Candidates.Select(candidate => candidate.Priority).Should()
            .Equal(0, 1, 2).And.Subject.Should().BeEquivalentTo(new[] { 0, 1, 2 });
        funnel.Candidates.Select(candidate => candidate.Priority).Should().NotBeEmpty(
            "priorities have to be dense or the validation rejects the report");
    }

    /// <summary>**`TierA`/`TierB`/`TierC` 与证据矛盾时校验要拒** / Validation refuses a tier that contradicts its evidence.</summary>
    /// <remarks>
    /// 三条合起来保证"分层"和"证据"不互相矛盾：`TierA` 必须有超过阈值的对偶，`TierB` 必须有不超过阈值的对偶，
    /// `TierC` 必须不是 active。**少了任何一条，一份自相矛盾的报告都能校验通过。**
    ///
    /// Together these keep the tier and the evidence consistent. **Drop any one and a self-contradictory report validates clean.**
    /// </remarks>
    [Fact]
    public void TheValidation_ShouldRefuseAContradictoryTier() {
        // TierA 却没有对偶值。
        var missingDual = new CandidateFunnelRanking(1e-9,
            [new ConstraintCandidate(new ConstraintId("c1"), null, CandidateTier.TierA, ActivityStatus.Active, null, null, null, 0)],
            1, 0, 0, 0);
        missingDual.Validate().IsOk.Should().BeFalse("tier A without a dual contradicts itself");

        // TierB 却超过阈值。
        var overThreshold = new CandidateFunnelRanking(1e-9,
            [new ConstraintCandidate(new ConstraintId("c1"), null, CandidateTier.TierB, ActivityStatus.Active, null, new Flt64(1.0), true, 0)],
            0, 1, 0, 0);
        overThreshold.Validate().IsOk.Should().BeFalse();

        // TierC 却是 active。
        var activeTierC = new CandidateFunnelRanking(1e-9,
            [new ConstraintCandidate(new ConstraintId("c1"), null, CandidateTier.TierC, ActivityStatus.Active, null, null, null, 0)],
            0, 0, 1, 0);
        activeTierC.Validate().IsOk.Should().BeFalse();

        // 计数与列表不一致。
        var wrongCount = new CandidateFunnelRanking(1e-9,
            [new ConstraintCandidate(new ConstraintId("c1"), null, CandidateTier.Unclassified, ActivityStatus.Active, null, null, null, 0)],
            1, 0, 0, 0);
        wrongCount.Validate().IsOk.Should().BeFalse("the tier counts have to match the list");

        // **一份自洽的报告必须给过**，否则上面四条只是"全都拒绝"。
        // **A consistent ranking has to pass**, or the four above only prove that everything is refused.
        var sound = new CandidateFunnelRanking(1e-9,
            [new ConstraintCandidate(new ConstraintId("c1"), null, CandidateTier.TierA, ActivityStatus.Active, null, new Flt64(1.0), true, 0)],
            1, 0, 0, 0);
        sound.Validate().Value.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Success>();
    }

    /// <summary>**短名单不会把 Tier C 永久丢弃** / The shortlist never drops tier C for good.</summary>
    [Fact]
    public void TheShortlist_ShouldPreferTheTieredButBackfill() {
        var ranking = new CandidateFunnelRanking(1e-9,
            [
                new ConstraintCandidate(new ConstraintId("a"), null, CandidateTier.TierA, ActivityStatus.Active, null, new Flt64(1.0), true, 0),
                new ConstraintCandidate(new ConstraintId("c"), null, CandidateTier.TierC, ActivityStatus.Inactive, null, null, null, 1),
            ],
            1, 0, 1, 0);

        ranking.Shortlist(limit: 1).Select(candidate => candidate.ConstraintId.Value).Should().Equal("a");
        ranking.Shortlist(limit: 1).Should().ContainSingle(
            "the preferred tier takes the only slot first");
        ranking.Shortlist(limit: 2).Select(candidate => candidate.ConstraintId.Value).Should().Equal("a", "c");
        ranking.Shortlist(limit: 2).Should().HaveCount(2,
            "tier C is backfilled while slots remain, not dropped");
        ranking.Shortlist(limit: 0).Should().BeEmpty("a zero limit means no work, not the whole list");
        CandidateTier.TierA.IsShortlistDefault().Should().BeTrue();
        CandidateTier.TierC.IsShortlistDefault().Should().BeFalse();
        CandidateTier.Unclassified.TierName().Should().Be("UNCLASSIFIED");
    }

    /// <summary>**配置不合法时漏斗拒绝构造** / A bad configuration refuses the whole funnel.</summary>
    [Fact]
    public void TheConfiguration_ShouldBeCheckedBeforeBuilding() {
        CandidateFunnelConfig bad = new(DualThreshold: double.NaN);
        bad.Validate().Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>(
            "a NaN threshold would tier nothing, so validation has to fail");

        FunnelRet report = CriticalCandidateFunnel.BuildCandidateFunnel(
            ActivityReport(Constraint("c1", "g1", ActivityStatus.Active)),
            sensitivity: null,
            config: bad);
        report.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<CandidateFunnelRanking, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>(
            "the funnel must not be built on a configuration it rejected");

        new CandidateFunnelConfig().Invoking(config => config.Validate()).Should().NotThrow();
        CandidateFunnelConfig.DefaultDualThreshold.Should().Be(1e-9);
        CandidateFunnelConfig.DefaultCandidateLimitValue.Should().Be(32);
    }
}
