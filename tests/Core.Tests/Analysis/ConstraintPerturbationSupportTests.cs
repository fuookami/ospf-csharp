#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 扰动分析的纯函数 / The pure part of perturbation analysis.
/// </summary>
/// <remarks>
/// 这四个函数决定"一次扰动算不算证据"。**它们自己不会被求解器掩盖，所以在这里钉死代价最低。**
///
/// These four decide whether a perturbation counts as evidence. **Nothing masks them, so pinning them here is the cheapest place.**
/// </remarks>
public class ConstraintPerturbationSupportTests {
    private static ConstraintPerturbationObservation Observation(string id, AnalysisStatus status) => new(
        new ConstraintId(id),
        ConstraintPerturbationKind.Rhs,
        Delta: new Flt64(1.0),
        PerturbedRhs: new Flt64(1.0),
        Status: status,
        BaselineObjective: new Flt64(10.0),
        NewObjective: null,
        ObjectiveImprovement: null,
        IntegerPatternChanged: null,
        SolveTime: null,
        Outcome: PerturbationOutcome.Unknown);

    /// <summary>**"没测出结论"不等于"没效果"** / "No conclusion" is not "no effect".</summary>
    /// <remarks>
    /// 求解状态不是 `Reachable` 时，改善量即使有值也不算数；**报成 `NoObservedEffect` 会让一份缺证据的报告看起来结论明确。**
    ///
    /// When the solve status is not `Reachable` the improvement does not count, even if present; **reporting `NoObservedEffect` would make a report with missing evidence look
    /// conclusive.**
    /// </remarks>
    [Fact]
    public void TheInconclusiveRun_ShouldBeUnknownNotNoEffect() {
        ConstraintPerturbationSupport.ClassifyOutcome(AnalysisStatus.Unknown, new Flt64(5.0), 1e-9)
            .Should().Be(PerturbationOutcome.Unknown, "a run with no conclusion measured nothing, whatever number it carries");
        ConstraintPerturbationSupport.ClassifyOutcome(AnalysisStatus.Reachable, null, 1e-9)
            .Should().Be(PerturbationOutcome.Unknown, "with no improvement there is nothing to classify");

        ConstraintPerturbationSupport.ClassifyOutcome(AnalysisStatus.Reachable, new Flt64(0.0), 1e-9)
            .Should().Be(PerturbationOutcome.NoObservedEffect, "this one really was measured, and the answer was zero");
    }

    /// <summary>**阈值是严格大于** / The tolerance is strictly greater-than.</summary>
    [Fact]
    public void TheToleranceBoundary_ShouldNotCount() {
        ConstraintPerturbationSupport.ClassifyOutcome(AnalysisStatus.Reachable, new Flt64(0.5), 0.5)
            .Should().Be(PerturbationOutcome.NoObservedEffect, "an improvement exactly at the tolerance is still noise");
        ConstraintPerturbationSupport.ClassifyOutcome(AnalysisStatus.Reachable, new Flt64(0.5000001), 0.5)
            .Should().Be(PerturbationOutcome.Effective);
    }

    /// <summary>**方向搞反，变差的解看起来是改善** / The wrong direction makes a worse solution look better.</summary>
    /// <remarks>
    /// 同一个差值（10 → 12），最大化下是 +2、最小化下是 −2。**两条都要查，只查一条会让"永远 current - baseline"通过。**
    ///
    /// The same difference (10 to 12) is +2 under maximisation and 2 under minimisation. **Both have to be checked, or an always `current - baseline` implementation passes.**
    /// </remarks>
    [Fact]
    public void TheImprovement_ShouldFollowTheDirection() {
        ConstraintPerturbationSupport.ObjectiveImprovement(ObjectCategory.Maximum, new Flt64(10.0), new Flt64(12.0))!.Value
            .ToDouble().Should().Be(2.0, "going from 10 to 12 is an improvement when maximising");
        ConstraintPerturbationSupport.ObjectiveImprovement(ObjectCategory.Minimum, new Flt64(10.0), new Flt64(12.0))!.Value
            .ToDouble().Should().Be(-2.0, "the same move is a regression when minimising");
    }

    /// <summary>**缺任一端就没有改善量** / A missing endpoint means no improvement.</summary>
    [Fact]
    public void TheImprovement_ShouldNeedBothEnds() {
        ConstraintPerturbationSupport.ObjectiveImprovement(ObjectCategory.Maximum, null, new Flt64(1.0))
            .Should().BeNull();
        ConstraintPerturbationSupport.ObjectiveImprovement(ObjectCategory.Maximum, new Flt64(1.0), null)
            .Should().BeNull();
        ConstraintPerturbationSupport
            .ObjectiveImprovement(ObjectCategory.Maximum, new Flt64(double.NaN), new Flt64(1.0))
            .Should().BeNull("NaN is not a measurement");
    }

    /// <summary>**只有全部观测都不可达才叫不可达** / Unreachable needs every observation unreachable.</summary>
    /// <remarks>
    /// 一个可能可达的东西被报成不可达，是最难被后续工作纠正的错误。**只要有一个观测没结论，就必须退到 `Unknown`。**
    ///
    /// Reporting something possibly reachable as unreachable is the error later work finds hardest to undo. **One inconclusive observation forces `Unknown`.**
    /// </remarks>
    [Fact]
    public void TheAggregate_ShouldBeUnreachableOnlyWhenEverythingIs() {
        ConstraintPerturbationSupport.AggregateStatus(
            [Observation("a", AnalysisStatus.Unreachable), Observation("b", AnalysisStatus.Unreachable)],
            budgetStopped: false).Should().Be(AnalysisStatus.Unreachable);

        ConstraintPerturbationSupport.AggregateStatus(
            [Observation("a", AnalysisStatus.Unreachable), Observation("b", AnalysisStatus.Unknown)],
            budgetStopped: false).Should().Be(AnalysisStatus.Unknown,
            "one inconclusive observation is enough to deny the word unreachable");

        ConstraintPerturbationSupport.AggregateStatus(
            [Observation("a", AnalysisStatus.Unreachable), Observation("b", AnalysisStatus.Reachable)],
            budgetStopped: false).Should().Be(AnalysisStatus.Reachable);
    }

    /// <summary>**不支持混着可达，整体给 `Unknown`** / Unsupported mixed with reachable is `Unknown`.</summary>
    /// <remarks>
    /// 一部分证据没适用，整体既不能算可达也不能算不支持。
    ///
    /// Part of the evidence did not apply, so the whole is neither reachable nor unsupported.
    /// </remarks>
    [Fact]
    public void TheAggregate_ShouldMixUnsupportedWithCare() {
        ConstraintPerturbationSupport.AggregateStatus(
            [Observation("a", AnalysisStatus.Unsupported), Observation("b", AnalysisStatus.Unreachable)],
            budgetStopped: false).Should().Be(AnalysisStatus.Unsupported);

        ConstraintPerturbationSupport.AggregateStatus(
            [Observation("a", AnalysisStatus.Unsupported), Observation("b", AnalysisStatus.Reachable)],
            budgetStopped: false).Should().Be(AnalysisStatus.Unknown,
            "part of the evidence never applied, so neither answer is licensed");
    }

    /// <summary>**空观测：预算中止是 `Unknown`，不是 `Unsupported`** / Empty under a budget stop is `Unknown`.</summary>
    /// <remarks>
    /// **没跑完和跑不了是两件事。** 把前者报成后者，会让一次因预算中止的分析看起来像"这个后端不支持"。
    ///
    /// **Not finishing and not being able to are two different things.** Reporting the first as the second makes a budget stop look like an unsupported backend.
    /// </remarks>
    [Fact]
    public void TheEmptyAggregate_ShouldDistinguishBudgetFromUnsupported() {
        ConstraintPerturbationSupport.AggregateStatus([], budgetStopped: true)
            .Should().Be(AnalysisStatus.Unknown, "the run was stopped, not refused");
        ConstraintPerturbationSupport.AggregateStatus([], budgetStopped: false)
            .Should().Be(AnalysisStatus.Unsupported);
    }

    /// <summary>**重复的 delta 不是两条证据** / A duplicate delta is not two pieces of evidence.</summary>
    [Fact]
    public void TheDeltas_ShouldBeDeduplicatedInOrder() {
        var policy = new ConstraintPerturbationPolicy(Deltas: [new Flt64(1.0), new Flt64(2.0), new Flt64(1.0)]);
        IReadOnlyList<Flt64> deltas = ConstraintPerturbationSupport.RequestedDeltas(policy);

        deltas.Should().HaveCount(2, "1.0 appears twice but is one delta");
        deltas.Select(delta => delta.ToDouble()).Should().Equal(1.0, 2.0);
    }
}
