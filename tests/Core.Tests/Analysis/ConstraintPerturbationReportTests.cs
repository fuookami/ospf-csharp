#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 扰动报告与策略 / The perturbation report and policies.
/// </summary>
/// <remarks>
/// 补了 前 348 行的纯数据类型**（策略、请求、结果、观测、报告）。
/// 第 349 行起是分析器本体，含 `activeBackend!!.solve(request)` 与 `backend!!.solve(request)`，
/// **要真实重新优化后端，这里不实现，也不写空跑版本。**
///
/// **implemented the first 348 lines of** — the policies, request, result, observation and report. From line 349 on is the analyzer itself, with
/// `activeBackend!!.solve(request)` and `backend!!.solve(request)`; **that needs a real reoptimization backend, so it was neither implemented nor stubbed.**
/// </remarks>
public class ConstraintPerturbationReportTests {
    private static ConstraintPerturbationObservation Observation(
        ConstraintPerturbationKind kind,
        PerturbationOutcome outcome) =>
        new(
            new ConstraintId("c1"),
            kind,
            Delta: kind == ConstraintPerturbationKind.Removal ? null : new Flt64(1.0),
            PerturbedRhs: null,
            Status: AnalysisStatus.Reachable,
            BaselineObjective: new Flt64(10.0),
            NewObjective: new Flt64(12.0),
            ObjectiveImprovement: new Flt64(2.0),
            IntegerPatternChanged: null,
            SolveTime: null,
            Outcome: outcome);

    /// <summary>**没有操作的策略是空跑，必须被拒** / A policy with no operation is a no-op and is refused.</summary>
    /// <remarks>
    /// 没有显式 delta、没有自适应、也不做删除测试的策略**什么也不会发生**，
    /// 而报告会是一份"分析完成、无发现"。**那是最坏的一种空跑。**
    ///
    /// **A policy with no explicit deltas, no adaptive sequence and no removal test does nothing at all**, and the report comes back as "analysis complete, nothing found".
    /// **That is the worst kind of no-op.**
    /// </remarks>
    [Fact]
    public void ThePolicy_ShouldRefuseAnEmptyOperationSet() {
        var empty = new ConstraintPerturbationPolicy(Deltas: Array.Empty<Flt64>());
        empty.Validate().Should().NotBeNull("a policy with no operation would run and find nothing");

        // **但自适应或删除测试单独存在时是合法的**，否则上面那条只是"一律拒绝"。
        // **An adaptive sequence or a removal test alone is legal**, or the check above only proves "everything is refused".
        new ConstraintPerturbationPolicy(Deltas: Array.Empty<Flt64>(), Adaptive: new AdaptivePerturbationPolicy())
            .Validate().Should().BeNull();
        new ConstraintPerturbationPolicy(Deltas: Array.Empty<Flt64>(), RemovalTest: true)
            .Validate().Should().BeNull();
        new ConstraintPerturbationPolicy().Validate().Should().BeNull();
    }

    /// <summary>**增长因子必须严格大于 1** / The growth factor has to be strictly greater than one.</summary>
    /// <remarks>
    /// 等于 1 时 delta 不变，序列会在预算内反复试同一个点，**而报告只会说"没有观察到效果"**。
    ///
    /// At exactly one the delta never changes and the sequence retries the same point until the budget runs out, **while the report simply says "no effect observed"**.
    /// </remarks>
    [Fact]
    public void TheAdaptivePolicy_ShouldRefuseAUnitGrowthFactor() {
        new AdaptivePerturbationPolicy(GrowthFactor: 1.0).Validate()
            .Should().NotBeNull("a growth factor of one retries the same delta and reports 'no effect'");
        new AdaptivePerturbationPolicy(GrowthFactor: 1.5).Validate().Should().BeNull();

        new AdaptivePerturbationPolicy(MaxDelta: new Flt64(0.5)).Validate()
            .Should().NotBeNull("a max delta below the initial delta makes the first step illegal");
        new AdaptivePerturbationPolicy(InitialDelta: new Flt64(0.0)).Validate()
            .Should().NotBeNull("a zero initial delta perturbs nothing");
        new AdaptivePerturbationPolicy(RefinementIterations: -1).Validate()
            .Should().NotBeNull("a negative refinement count is not a budget");

        new AdaptivePerturbationPolicy().Validate().Should().BeNull();
        new AdaptivePerturbationPolicy().InitialDeltaValue.ToDouble().Should().Be(1.0,
            "the default initial delta is part of the protocol");
        new AdaptivePerturbationPolicy().MaxDeltaValue.ToDouble().Should().Be(16.0);
    }

    /// <summary>**"删除是否有效"只对删除操作有答案** / "Removal effective" only answers for a removal.</summary>
    /// <remarks>
    /// 给一个 RHS 扰动返回 `false`，会被读成"扰动无效"，而实际含义是"这个字段不适用于本次操作"。
    ///
    /// Returning `false` for an RHS perturbation reads as "the perturbation did nothing", when the real meaning is "this field does not apply here".
    /// </remarks>
    [Fact]
    public void TheRemovalEffective_ShouldBeNullForAnRhsPerturbation() {
        Observation(ConstraintPerturbationKind.Rhs, PerturbationOutcome.Effective)
            .RemovalEffective.Should().BeNull("an RHS perturbation has no removal to be effective about");
        Observation(ConstraintPerturbationKind.Rhs, PerturbationOutcome.NoObservedEffect)
            .RemovalEffective.Should().BeNull("and a no-effect RHS perturbation still has no removal answer");

        Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.Effective)
            .RemovalEffective.Should().BeTrue();
        Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.NoObservedEffect)
            .RemovalEffective.Should().BeFalse();
        // **更正**： 这里断言的是 `false`，理由是"未知不能声称有效"。
        // **那个理由成立，但结论错了**：`false` 不是"没有声称有效"，而是**声称了无效**。
        // 字段是 `bool?`，三态就是为了让"没测"能说出来；把 `Unknown` 压成 `false`，
        // 只看这个字段的调用方就分不清"测了无效"和"没测"。
        //
        // **correction**: asserted `false` here, reasoning "an unknown result cannot claim effectiveness". **The reasoning holds, the conclusion does not**: `false`
        // is not "claims no effectiveness", it **claims ineffectiveness**. The field is `bool?` precisely so "not measured" can be said; collapsing `Unknown` into `false`
        // leaves a caller that reads only this field unable to tell "measured, ineffective" from "not measured".
        Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.Unknown)
            .RemovalEffective.Should().BeNull(
                "an unknown removal measured nothing, so there is no verdict to report either way");

        Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.Effective)
            .ObjectiveDifference.Should().Be(
                Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.Effective).ObjectiveImprovement,
                "the alias has to track the field it aliases");
    }

    /// <summary>**`Unknown` 不是改善** / `Unknown` is not an improvement.</summary>
    /// <remarks>
    /// 一次没有结论的求解被算成改善，会让整份报告偏向"扰动有效"。
    ///
    /// A solve that reached no conclusion being counted as an improvement would bias the whole report toward "perturbation works".
    /// </remarks>
    [Fact]
    public void TheGloballyEffective_ShouldIgnoreUnknown() {
        var unknownOnly = new ConstraintPerturbationReport(
            new ConstraintId("c1"),
            AnalysisStatus.Unknown,
            Observations: [Observation(ConstraintPerturbationKind.Rhs, PerturbationOutcome.Unknown)]);
        unknownOnly.GloballyEffective.Should().BeFalse("no conclusion is not an improvement");

        var noEffect = new ConstraintPerturbationReport(
            new ConstraintId("c1"),
            AnalysisStatus.Reachable,
            Observations: [Observation(ConstraintPerturbationKind.Rhs, PerturbationOutcome.NoObservedEffect)]);
        noEffect.GloballyEffective.Should().BeFalse();

        var effective = new ConstraintPerturbationReport(
            new ConstraintId("c1"),
            AnalysisStatus.Reachable,
            Observations: [Observation(ConstraintPerturbationKind.Rhs, PerturbationOutcome.Effective)]);
        effective.GloballyEffective.Should().BeTrue();

        // **删除观测也算**，否则"只做删除测试"的报告永远是 false。
        // **The removal observation counts too**, or a removal-only report would always read false.
        var removalOnly = new ConstraintPerturbationReport(
            new ConstraintId("c1"),
            AnalysisStatus.Reachable,
            Removal: Observation(ConstraintPerturbationKind.Removal, PerturbationOutcome.Effective));
        removalOnly.GloballyEffective.Should().BeTrue(
            "a removal test that proved an improvement is a global improvement");
        removalOnly.RemovalEffective.Should().BeTrue();

        new ConstraintPerturbationReport(new ConstraintId("c1"), AnalysisStatus.Unknown)
            .ObservationList.Should().BeEmpty("an absent observation list is empty, not null");
    }

    /// <summary>**请求的两个别名各自成立** / The request's two aliases each hold.</summary>
    [Fact]
    public void TheRequest_ShouldExposeItsAliases() {
        var request = new ConstraintPerturbationRequest(
            BaselineSnapshot: null!,
            DerivedSnapshot: null,
            ConstraintId: new ConstraintId("c1"),
            Kind: ConstraintPerturbationKind.Removal);
        request.IsRemoval.Should().BeTrue("the kind says removal");
        request.Snapshot.Should().BeNull("a continuous backend derives the snapshot itself");

        new ConstraintPerturbationRequest(
            BaselineSnapshot: null!,
            DerivedSnapshot: null,
            ConstraintId: new ConstraintId("c1"),
            Kind: ConstraintPerturbationKind.Rhs,
            Delta: new Flt64(1.0))
            .IsRemoval.Should().BeFalse();

        // **非有限目标值要被拒**：`Infinity` 当目标值会让"改善量"变成 `NaN`。
        // **A non-finite objective is refused**: an `Infinity` objective makes every improvement `NaN`.
        new ConstraintPerturbationSolveResult(
            AnalysisStatus.Reachable, ObjectiveValue: new Flt64(double.PositiveInfinity))
            .Validate().Should().NotBeNull();
        new ConstraintPerturbationSolveResult(
            AnalysisStatus.Reachable, ObjectiveValue: new Flt64(10.0))
            .Validate().Should().BeNull();
    }

    /// <summary>**自适应结论四档各自独立** / The four adaptive outcomes are distinct.</summary>
    [Fact]
    public void TheAdaptiveOutcomes_ShouldStayDistinct() {
        AdaptivePerturbationOutcome[] outcomes = Enum.GetValues<AdaptivePerturbationOutcome>();
        outcomes.Should().HaveCount(4, "the baseline has four, and merging any two collapses two answers");

        var result = new AdaptivePerturbationResult(
            AdaptivePerturbationOutcome.ThresholdInterval,
            FirstEffectiveDelta: new Flt64(2.0),
            LowerBound: new Flt64(1.0),
            UpperBound: new Flt64(2.0),
            Attempts: 5);
        result.ThresholdLower!.Value.ToDouble().Should().Be(1.0);
        result.ThresholdUpper!.Value.ToDouble().Should().Be(2.0);
        result.Attempts.Should().Be(5);
    }
}
