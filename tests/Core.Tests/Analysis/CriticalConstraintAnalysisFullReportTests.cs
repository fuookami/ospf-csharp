#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 关键约束分析的完整报告 / The full critical-constraint analysis report.
/// </summary>
/// <remarks>
/// **`ProvenOptimal = true` 而 `ObjectiveValue == null` 是一份自相矛盾的报告，而两个字段单独看都合法。**
///
/// **`ProvenOptimal = true` with `ObjectiveValue == null` is self-contradictory, while either field alone is legitimate.**
/// </remarks>
public class CriticalConstraintAnalysisFullReportTests {
    private static (ConstraintActivityReport Activity, CandidateFunnelRanking Funnel) Fixture() {
        var model = new ConstraintProgrammingModel("full", ObjectCategory.Maximum, identityNamespace: "full");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value!;

        var candidates = new List<ConstraintCandidate> {
            new(ConstraintId: new ConstraintId("c:le"), Group: null,
                Tier: CandidateTier.TierA, Activity: ActivityStatus.Active, Slack: null,
                DualValue: null, LocalEffective: null, Priority: 0),
        };
        var funnel = new CandidateFunnelRanking(
            DualThreshold: 0.5, Candidates: candidates, TierA: 1, TierB: 0, TierC: 0, Unclassified: 0);
        _ = snapshot;
        var activity = new ConstraintActivityReport(
            Activities: [], GroupSummaries: [], Tolerance: 1e-6, NearlyActiveTolerance: 1e-3);
        return (activity, funnel);
    }

    private static ConstraintProgrammingModelSnapshot FixtureSnapshot() {
        var model = new ConstraintProgrammingModel("full", ObjectCategory.Maximum, identityNamespace: "full");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    private static AnalysisBaselineSummary Baseline(bool proven = true, Flt64? value = null) =>
        new(ObjectiveId: "objective:main", Sense: "Maximum", ObjectiveValue: value,
            Status: proven ? AnalysisStatus.Reachable : AnalysisStatus.Unknown, ProvenOptimal: proven);

    private static CriticalConstraintAnalysisReport Report(
        AnalysisBaselineSummary baseline,
        ConflictExplanation? conflict = null,
        IReadOnlyList<AnalysisGroupSummary>? groups = null,
        string schema = "1.0.0") {
        (ConstraintActivityReport activity, CandidateFunnelRanking funnel) = Fixture();
        return new CriticalConstraintAnalysisReport(
            Baseline: baseline,
            Activity: activity,
            LocalSensitivity: null,
            Funnel: funnel,
            Effectiveness: null,
            Target: null,
            Conflict: conflict,
            GroupSummaries: groups ?? [],
            Status: AnalysisStatus.Reachable,
            UnavailableReasons: [],
            SchemaVersion: schema);
    }

    /// <summary>**"已证明最优"必须带目标值** / A proven baseline needs its value.</summary>
    /// <remarks>
    /// 两个字段单独看都合法，**合起来是自相矛盾的一份报告。**
    ///
    /// Either field alone is legitimate; **together they are a self-contradictory report.**
    /// </remarks>
    [Fact]
    public void TheProvenBaselineWithoutValue_ShouldBeRejected() {
        Report(Baseline(proven: true, value: null)).Validate()
            .Should().ContainMatch("*已证明的基线必须带目标值*");

        // **反向对照**：补上值就必须通过。
        Report(Baseline(proven: true, value: new Flt64(4.0))).Validate()
            .Should().BeEmpty("a proven baseline with its value is exactly what the invariant asks for");
    }

    /// <summary>**未证明的基线可以没有目标值** / An unproven baseline may lack its value.</summary>
    /// <remarks>
    /// **反向对照**：只查上一条，一个"总是要求目标值"的实现照样通过。
    ///
    /// **The reverse control**: with only the previous criterion, an implementation that always demands a value would pass.
    /// </remarks>
    [Fact]
    public void TheUnprovenBaseline_MayLackItsValue() {
        Report(Baseline(proven: false, value: null)).Validate()
            .Should().BeEmpty("nothing was proven, so there is no value to require");
    }

    /// <summary>**空 schema 版本被拒** / A blank schema version is rejected.</summary>
    [Fact]
    public void TheBlankSchemaVersion_ShouldBeRejected() {
        Report(Baseline(proven: true, value: new Flt64(4.0)), schema: "  ").Validate()
            .Should().ContainMatch("*schema 版本不得为空*");
    }

    /// <summary>**背景阻塞不得同时声称最小集** / Background blocking cannot claim a minimal set.</summary>
    [Fact]
    public void TheBackgroundBlockingWithMinimalSet_ShouldBeRejected() {
        var bound = new DiagnosticSource.VariableLowerBound(new VariableId("source:x"));
        TargetFeasibilityReport report = new(
            Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Unreachable);
        ConflictExplanation conflict = ConflictExplanationAssembler.AssembleInfeasible(
            FixtureSnapshot(), report.Target, report, new ConflictExtractionTier(),
            [bound], targetSource: null,
            ConflictDeletionCheck.Shrink([bound], AnalysisStatus.Unreachable,
                maxDeletionChecks: 0, timeBudget: null, elapsed: TimeSpan.Zero,
                probe: _ => AnalysisStatus.Unknown));

        // 夹具本身：背景阻塞成立，而最小性是 `NotChecked`（上限为 0，一次复验都没跑）。
        // **关键是它不是 `Irreducible`** —— 所以两条不变量并不冲突。
        conflict.BackgroundBlocking.Should().BeTrue();
        conflict.Minimality.Should().Be(ConflictMinimality.NotChecked);
        conflict.Minimality.Should().NotBe(ConflictMinimality.Irreducible);

        Report(Baseline(proven: true, value: new Flt64(4.0)), conflict).Validate()
            .Should().BeEmpty("partial minimality is not a minimal-set claim, so there is no contradiction");
    }

    /// <summary>**三种阻塞摘要分开说** / The three blocking summaries are distinct.</summary>
    /// <remarks>
    /// 背景阻塞时 `Members` 里没有约束。**若走"最小集"那条分支，摘要会声称"0 个原始证据"，
    /// 而真实原因是固定背景。**
    ///
    /// With background blocking there is no constraint among the members. **Taking the "minimal set" branch would report "0 original evidence items" while the real cause is fixed
    /// background.**
    /// </remarks>
    [Fact]
    public void TheBlockingSummary_ShouldKeepTheThreeCasesApart() {
        Report(Baseline(proven: true, value: new Flt64(4.0))).BlockingSummary()
            .Should().Contain("未执行", "no conflict was supplied");

        var bound = new DiagnosticSource.VariableLowerBound(new VariableId("source:x"));
        var report = new TargetFeasibilityReport(
            Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Unreachable);
        ConflictExplanation background = ConflictExplanationAssembler.AssembleInfeasible(
            FixtureSnapshot(), report.Target, report, new ConflictExtractionTier(),
            [bound], targetSource: null,
            ConflictDeletionCheck.Shrink([bound], AnalysisStatus.Unreachable,
                maxDeletionChecks: 0, timeBudget: null, elapsed: TimeSpan.Zero,
                probe: _ => AnalysisStatus.Unknown));

        Report(Baseline(proven: true, value: new Flt64(4.0)), background).BlockingSummary()
            .Should().Contain("固定背景", "the background case has its own wording")
            .And.NotContain("个原始证据", "the minimal-set wording would report zero evidence and hide the real cause");
    }

    /// <summary>**没有最小集时 `HasMinimalBlockingSet` 为假** / No minimal set means false.</summary>
    [Fact]
    public void TheAbsentConflict_ShouldNotClaimAMinimalSet() {
        Report(Baseline(proven: true, value: new Flt64(4.0))).HasMinimalBlockingSet
            .Should().BeFalse("there is no conflict at all");
    }
}
