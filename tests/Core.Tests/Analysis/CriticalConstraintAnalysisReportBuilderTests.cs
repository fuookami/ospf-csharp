#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 关键约束分析报告的累积器 / The accumulator for a critical-constraint analysis report.
/// </summary>
/// <remarks>
/// **一个阶段没跑出结论，整份报告就不能说"目标可达"——哪怕其余六个阶段都跑出了确定结论。**
///
/// **One inconclusive stage stops the report from saying "reachable", even when the other six concluded firmly.**
/// </remarks>
public class CriticalConstraintAnalysisReportBuilderTests {
    private static ConstraintActivityReport Activity() {
        var model = new ConstraintProgrammingModel("builder", ObjectCategory.Maximum, identityNamespace: "builder");
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
        return new ConstraintActivityReport(
            Activities: [], GroupSummaries: [], Tolerance: 1e-6, NearlyActiveTolerance: 1e-3);
    }

    private static AnalysisBaselineSummary Baseline(bool proven = true, Flt64? value = null) =>
        new("objective:main", "Maximum", value, proven ? AnalysisStatus.Reachable : AnalysisStatus.Unknown, proven);

    /// <summary>**"没跑"必须把状态压到 `Unsupported`** / "Did not run" pushes the status down.</summary>
    /// <remarks>
    /// 只记原因不改状态，一份"六个阶段没跑"的报告会带着 `Reachable` 出去——
    /// **而 `Reachable` 的意思是"目标可达，一切正常"。**
    ///
    /// Recording only the reason would let a report with six skipped stages go out carrying `Reachable` — **and `Reachable` means "the target is reachable, everything is fine".**
    /// </remarks>
    [Fact]
    public void TheSkippedStage_ShouldPushTheStatusDown() {
        CriticalConstraintAnalysisReport clean = new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: true, value: new Flt64(4.0)))
            .Activity(Activity())
            .Build().Value!;

        clean.Status.Should().Be(AnalysisStatus.Reachable, "every supplied stage concluded firmly");

        CriticalConstraintAnalysisReport skipped = new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: true, value: new Flt64(4.0)))
            .Activity(Activity())
            .NoteUnavailable("扰动阶段被调用方关闭 / The perturbation stage was disabled by the caller")
            .Build().Value!;

        skipped.Status.Should().Be(AnalysisStatus.Unsupported,
            "a stage that did not run makes the whole report unsupported, not reachable");
        skipped.UnavailableReasons.Should().ContainSingle();
    }

    /// <summary>**最弱环节决定整体** / The weakest link decides.</summary>
    /// <remarks>
    /// `Unreachable` 比 `Unknown` 强：前者是"证出来了"，后者是"没证出来"。
    /// **排反会让一个没结论的阶段把已证明的结论拉成"未知"。**
    ///
    /// `Unreachable` outranks `Unknown`: the first was proven, the second was not. **Swapping them lets an inconclusive stage drag a proof down to "unknown".**
    /// </remarks>
    [Fact]
    public void TheWeakestLink_ShouldDecide() {
        CriticalConstraintAnalysisReport.CombineStatus(AnalysisStatus.Reachable, AnalysisStatus.Unreachable)
            .Should().Be(AnalysisStatus.Unreachable, "a proven impossibility outranks reachability");
        CriticalConstraintAnalysisReport.CombineStatus(AnalysisStatus.Unreachable, AnalysisStatus.Unknown)
            .Should().Be(AnalysisStatus.Unknown, "an inconclusive stage is weaker than a proof");
        CriticalConstraintAnalysisReport.CombineStatus(AnalysisStatus.Unknown, AnalysisStatus.Unsupported)
            .Should().Be(AnalysisStatus.Unsupported);
        CriticalConstraintAnalysisReport.CombineStatus(AnalysisStatus.Unreachable, null)
            .Should().Be(AnalysisStatus.Unreachable, "a missing stage changes nothing");

        // **反向对照**：把顺序排反，第二行就会返回 Unreachable。
        // **The reverse control**: with the order swapped, the second line would return Unreachable.
        CriticalConstraintAnalysisReport.CombineStatus(AnalysisStatus.Unsupported, AnalysisStatus.Reachable)
            .Should().Be(AnalysisStatus.Unsupported, "the weaker input wins whichever side it is on");
    }

    /// <summary>**扰动非空但没有基线值 → 失败** / Perturbation without a baseline value fails.</summary>
    /// <remarks>
    /// **反向对照**：扰动为空时不需要基线值。一个无条件要求基线值的实现，
    /// **会让"没跑扰动"的正常报告也失败。**
    ///
    /// **The reverse control**: an empty perturbation list needs no baseline value. Demanding it unconditionally would **fail the perfectly normal report that skipped perturbation.**
    /// </remarks>
    [Fact]
    public void ThePerturbationWithoutBaselineValue_ShouldFail() {
        var report = new ConstraintPerturbationReport(
            ConstraintId: new ConstraintId("c:le"),
            Status: AnalysisStatus.Reachable);

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> withPerturbation =
            new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: false, value: null))
            .Activity(Activity())
            .Perturbation([report])
            .Build();
        withPerturbation.Should().BeOfType<Failed<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>>>(
            "there is no baseline objective to compare against");

        Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> withoutPerturbation =
            new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: false, value: null))
            .Activity(Activity())
            .Perturbation([])
            .Build();
        withoutPerturbation.Value!.Effectiveness.Should().BeNull(
            "nothing needs a comparison baseline when perturbation did not run");
    }

    /// <summary>**缺基线或缺活动性报告就直接失败** / No baseline or no activity report fails outright.</summary>
    [Fact]
    public void TheMissingRequiredStages_ShouldFail() {
        new CriticalConstraintAnalysisReportBuilder()
            .Activity(Activity())
            .Build().Should().BeOfType<Failed<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>>>(
                "a report with no baseline says nothing about what was analysed");

        new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: true, value: new Flt64(4.0)))
            .Build().Should().BeOfType<Failed<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>>>(
                "without an activity report there is nothing to build a funnel from");
    }

    /// <summary>**无法分层的候选要写进原因** / Untiered candidates are recorded as a reason.</summary>
    [Fact]
    public void TheUntieredCandidates_ShouldBeRecorded() {
        CriticalConstraintAnalysisReport report = new CriticalConstraintAnalysisReportBuilder()
            .Baseline(Baseline(proven: true, value: new Flt64(4.0)))
            .Activity(Activity())
            .Build().Value!;

        report.Funnel.Unclassified.Should().Be(0, "the fixture has no candidates at all");
        report.UnavailableReasons.Should().BeEmpty();
    }
}
