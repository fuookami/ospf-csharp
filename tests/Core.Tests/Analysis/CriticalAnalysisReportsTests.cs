#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 报告侧的两个纯函数 / The two pure functions on the report side.
/// </summary>
/// <remarks>
/// 补完报告侧**：`buildEffectivenessRanking` 与 `summarizeGroups`。
/// 两者都不碰后端。报告构造器仍缺。
///
/// **This completes the report side.** Neither function touches a backend; the report builder is still missing.
/// </remarks>
public class CriticalAnalysisReportsTests {
    private static ConstraintPerturbationObservation Observation(
        string constraint,
        PerturbationOutcome outcome,
        Flt64? improvement = null,
        Flt64? delta = null) => new(
        new ConstraintId(constraint),
        ConstraintPerturbationKind.Rhs,
        Delta: delta,
        PerturbedRhs: null,
        Status: AnalysisStatus.Reachable,
        BaselineObjective: new Flt64(10.0),
        NewObjective: null,
        ObjectiveImprovement: improvement,
        IntegerPatternChanged: null,
        SolveTime: null,
        Outcome: outcome);

    private static ConstraintPerturbationReport Report(
        string constraint,
        params ConstraintPerturbationObservation[] observations) => new(
        new ConstraintId(constraint),
        AnalysisStatus.Reachable,
        Observations: observations);

    /// <summary>**"测了没效果"是证据，"没有结论"不是** / "Measured, no effect" is evidence; "no conclusion" is not.</summary>
    /// <remarks>
    /// 只有 `Unknown` 该被丢掉。**把 `NoObservedEffect` 一起丢，报告就再也分不清"没测"和"测了没用"。**
    ///
    /// Only `Unknown` should be dropped. **Dropping `NoObservedEffect` too would leave the report unable to tell "not measured" from "measured, no effect".**
    /// </remarks>
    [Fact]
    public void TheBuilder_ShouldKeepNoObservedEffectAndDropUnknown() {
        Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> built =
            CriticalAnalysisReports.BuildEffectivenessRanking(
                new Flt64(10.0),
                funnel: null,
                perturbationReports: [
                    Report("measured-none",
                        Observation("measured-none", PerturbationOutcome.NoObservedEffect, improvement: new Flt64(0.0))),
                    Report("no-conclusion",
                        Observation("no-conclusion", PerturbationOutcome.Unknown, improvement: new Flt64(5.0))),
                    Report("effective",
                        Observation("effective", PerturbationOutcome.Effective,
                            improvement: new Flt64(3.0), delta: new Flt64(1.0))),
                ]);

        built.IsOk.Should().BeTrue(built.ToString());
        EffectivenessRanking ranking = built.Value!;
        ranking.EntryList.Should().HaveCount(3, "every report still gets an entry");

        // **`Unknown` 那条的改善值必须被丢掉**：一个没有结论的求解即使报了个数字，那也不是测量结果。
        // **The `Unknown` entry must lose its improvement**: a solve that reached no conclusion is not a measurement even if it reports a number.
        ranking.Entry(new ConstraintId("no-conclusion"))!.MaxImprovement
            .Should().BeNull("a solve with no conclusion measured nothing, whatever number it carried");
        ranking.Entry(new ConstraintId("measured-none"))!.MaxImprovement
            .Should().Be(new Flt64(0.0), "this one really was measured, and the answer was zero");

        ranking.GloballyEffective.Should().ContainSingle()
            .Which.ConstraintId.Value.Should().Be("effective");
    }

    /// <summary>**边际效应要求分母严格为正** / The marginal effect needs a strictly positive divisor.</summary>
    /// <remarks>
    /// 零 delta 会让它变成 `Infinity`，`NaN` 会混进排序。**算不出来就给 `null`，不要给一个假的数。**
    ///
    /// A zero delta turns it into `Infinity` and a `NaN` slips into the ordering. **When it cannot be computed, `null` is the answer, not a fake number.**
    /// </remarks>
    [Fact]
    public void TheMarginalEffect_ShouldNeedAPositiveDelta() {
        Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> built =
            CriticalAnalysisReports.BuildEffectivenessRanking(
                new Flt64(10.0),
                funnel: null,
                perturbationReports: [
                    Report("with-delta",
                        Observation("with-delta", PerturbationOutcome.Effective,
                            improvement: new Flt64(3.0), delta: new Flt64(2.0))),
                ]);
        built.Value!.Entry(new ConstraintId("with-delta"))!.MarginalEffect!.Value.ToDouble()
            .Should().Be(1.5, "3.0 improvement over a delta of 2.0");

        // 没有 delta 的有效观测：边际效应给 null，而不是 Infinity。
        Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> noDelta =
            CriticalAnalysisReports.BuildEffectivenessRanking(
                new Flt64(10.0),
                funnel: null,
                perturbationReports: [
                    Report("no-delta",
                        Observation("no-delta", PerturbationOutcome.Effective, improvement: new Flt64(3.0))),
                ]);
        noDelta.Value!.Entry(new ConstraintId("no-delta"))!.MarginalEffect
            .Should().BeNull("dividing by a missing delta would need an infinity");
    }

    /// <summary>**排序三级，最后一级保证可重复** / Three ordering levels, the last guaranteeing repeatability.</summary>
    [Fact]
    public void TheOrdering_ShouldBeStableAcrossRuns() {
        Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> built =
            CriticalAnalysisReports.BuildEffectivenessRanking(
                new Flt64(10.0),
                funnel: null,
                perturbationReports: [
                    Report("b", Observation("b", PerturbationOutcome.NoObservedEffect, improvement: new Flt64(1.0))),
                    Report("a", Observation("a", PerturbationOutcome.NoObservedEffect, improvement: new Flt64(1.0))),
                    Report("big", Observation("big", PerturbationOutcome.Effective, improvement: new Flt64(9.0))),
                ]);

        built.Value!.EntryList.Select(entry => entry.ConstraintId.Value)
            .Should().Equal("big", "a", "b");
        built.Value.EntryList.Should().HaveCount(3,
            "improvement descending, then identity; without the identity level the two equal entries could swap between runs");
    }

    /// <summary>**反查不到组就归 `null` 组，不要猜** / A group that cannot be looked up lands in `null`, not a guess.</summary>
    /// <remarks>
    /// 冲突解释**刻意只保存稳定身份**，它自己不带组名。阻塞约束的组必须从活动性报告反查；
    /// **查不到就归 `null`**——猜一个组名会让"某个组阻塞了模型"变成一条没有依据的结论。
    ///
    /// The conflict explanation **deliberately stores only stable identities** and carries no group name, so a blocking constraint's group has to be looked up in the activity
    /// report. **A miss lands in `null`** — guessing a name would turn "this group blocks the model" into a conclusion with nothing behind it.
    /// </remarks>
    [Fact]
    public void TheSummarizer_ShouldNotGuessAGroup() {
        var activity = new ConstraintActivityReport(
            Activities: [
                new ConstraintActivity(new ConstraintId("known"), "packing", ActivityStatus.Active, new ActivityEvidence(new DiagnosticSource.Constraint(new ConstraintId("probe")), ActivityEvidenceKind.Semantic)),
            ],
            GroupSummaries: [],
            Tolerance: 1e-6,
            NearlyActiveTolerance: 1e-4);

        var conflict = new ConflictExplanation(
            ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(10.0)),
            new TargetFeasibilityReport(
                ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(10.0)),
                Status: AnalysisStatus.Unreachable,
                ProofStatus: ProofStatus.Verified),
            ConflictExtractionTier.RepeatedSolving,
            AnalysisStatus.Unreachable,
            [new DiagnosticSource.Constraint(new ConstraintId("known"))],
            [new DiagnosticSource.Constraint(new ConstraintId("known")), new DiagnosticSource.Constraint(new ConstraintId("stranger"))],
            null,
            ConflictValidity.Verified,
            ConflictMinimality.NotChecked);

        IReadOnlyList<AnalysisGroupSummary> summaries =
            CriticalAnalysisReports.SummarizeGroups(activity, effectiveness: null, conflict: conflict);

        AnalysisGroupSummary packing = summaries.Single(summary => summary.Group == "packing");
        packing.BlockingInstances.Should().Be(1, "the known constraint belongs to the packing group");
        packing.IsBlocking.Should().BeTrue();

        AnalysisGroupSummary ungrouped = summaries.Single(summary => summary.Group is null);
        ungrouped.BlockingInstances.Should().Be(1,
            "the stranger's group is unknown, so it lands in the null group instead of being guessed");
    }

    /// <summary>**活动性报告的组压过条目自带的组** / The activity report's group beats the entry's own.</summary>
    [Fact]
    public void TheSummarizer_ShouldPreferTheActivityGroup() {
        var activity = new ConstraintActivityReport(
            Activities: [
                new ConstraintActivity(new ConstraintId("c1"), "packing", ActivityStatus.Active, new ActivityEvidence(new DiagnosticSource.Constraint(new ConstraintId("probe")), ActivityEvidenceKind.Semantic)),
            ],
            GroupSummaries: [],
            Tolerance: 1e-6,
            NearlyActiveTolerance: 1e-4);

        var ranking = new EffectivenessRanking(
            new Flt64(10.0),
            [new EffectivenessEntry(
                new ConstraintId("c1"), "stale-group", CandidateTier.TierA, ActivityStatus.Active,
                DualValue: null, LocalEffective: null, RemovalEffective: null,
                FirstEffectiveDelta: null, MaxImprovement: new Flt64(4.0), MarginalEffect: null,
                Status: AnalysisStatus.Reachable)]);

        IReadOnlyList<AnalysisGroupSummary> summaries =
            CriticalAnalysisReports.SummarizeGroups(activity, ranking, conflict: null);

        summaries.Should().ContainSingle("only one group is involved");
        summaries[0].Group.Should().Be("packing", "the activity report is the authority on group membership");
        summaries[0].MaxImprovement!.Value.ToDouble().Should().Be(4.0);
        summaries[0].InvolvedInstances.Should().Be(1);
        summaries[0].ActiveInstances.Should().Be(1);
    }

    /// <summary>**基线与缺省都不炸** / A NaN baseline and the empty case do not throw.</summary>
    [Fact]
    public void TheEdgeCases_ShouldBeHandled() {
        CriticalAnalysisReports
            .BuildEffectivenessRanking(new Flt64(double.NaN), null, [])
            .IsOk.Should().BeFalse("a NaN baseline makes every comparison meaningless");

        CriticalAnalysisReports.BuildEffectivenessRanking(new Flt64(10.0), null, []).IsOk
            .Should().BeTrue("an empty report set is a valid, empty ranking");

        CriticalAnalysisReports.SummarizeGroups(null, null, null)
            .Should().BeEmpty("no inputs means no groups");
    }
}
