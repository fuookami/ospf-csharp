#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;
using Success = Fuookami.Ospf.Utils.Functional.Success;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 有效性排序报告 / The effectiveness ranking report.
/// </summary>
/// <remarks>
/// 补了 `CriticalConstraintAnalysisReport.kt` 的报告侧**： 当时只补了漏斗那一块，
/// 这里补有效性条目与排序。`buildEffectivenessRanking`、`summarizeGroups` 与报告构造器仍缺。
///
/// **The report side of `CriticalConstraintAnalysisReport.kt` is ported**: only ported the funnel block, so the effectiveness entry and ranking.
/// `buildEffectivenessRanking`, `summarizeGroups` and the report builder remain missing.
/// </remarks>
public class EffectivenessRankingTests {
    private static EffectivenessEntry Entry(
        string id,
        Flt64? maxImprovement = null,
        Flt64? dual = null,
        bool? localEffective = null,
        Flt64? firstEffectiveDelta = null,
        Flt64? marginal = null) => new(
        new ConstraintId(id),
        Group: null,
        Tier: CandidateTier.TierA,
        Activity: ActivityStatus.Active,
        DualValue: dual,
        LocalEffective: localEffective,
        RemovalEffective: null,
        FirstEffectiveDelta: firstEffectiveDelta,
        MaxImprovement: maxImprovement,
        MarginalEffect: marginal,
        Status: AnalysisStatus.Reachable);

    /// <summary>**"没测出来"不是"没有改善"** / "Not measured" is not "no improvement".</summary>
    /// <remarks>
    /// `MaxImprovement = null` 与 `MaxImprovement = 0` 都必须**不**算全局有效，但原因完全不同：
    /// 前者是没测，后者是测了没改善。**把"没测"报成"没改善"，会让一份缺失证据的报告看起来结论明确。**
    ///
    /// Both `null` and `0` must be not-globally-effective, but for completely different reasons: the first was never measured, the second was measured and showed nothing.
    /// **Reporting "not measured" as "no improvement" makes a report with missing evidence look conclusive.**
    /// </remarks>
    [Fact]
    public void TheGlobalEffectiveness_ShouldNeedAValueAboveZero() {
        Entry("unmeasured").IsGloballyEffective.Should().BeFalse("no value was measured at all");
        Entry("zero", maxImprovement: new Flt64(0.0)).IsGloballyEffective.Should().BeFalse(
            "measured, and the improvement is exactly nothing");
        Entry("negative", maxImprovement: new Flt64(-1.0)).IsGloballyEffective.Should().BeFalse(
            "a worse objective is not an improvement");
        Entry("positive", maxImprovement: new Flt64(0.5)).IsGloballyEffective.Should().BeTrue();

        // **`UnavailableReason` 与 `MaxImprovement = null` 必须能分开表达**：上一条判据只证明了
        // "null 不算有效"，还要证明它**不代表"已测且无效"**。
        // **`UnavailableReason` and `MaxImprovement = null` have to stay distinguishable**: the criterion above only proves "null is not effective", not that it **does not mean
        // "measured and ineffective"**.
        var unavailable = new EffectivenessEntry(
            new ConstraintId("unavailable"), null, CandidateTier.TierA, ActivityStatus.Active,
            DualValue: null, LocalEffective: null, RemovalEffective: null,
            FirstEffectiveDelta: null, MaxImprovement: null, MarginalEffect: null,
            Status: AnalysisStatus.Unsupported, UnavailableReason: "no perturbation backend");
        unavailable.IsGloballyEffective.Should().BeFalse();
        unavailable.Status.Should().Be(AnalysisStatus.Unsupported,
            "the reason travels with the status; a bare zero improvement would erase it");
    }

    /// <summary>**没有对偶值就不许有局部判决** / No dual value means no local verdict.</summary>
    /// <remarks>
    /// 没有对偶值却给出"局部有效"，是**凭空断言**——而它读起来完全像一条测出来的结论。
    ///
    /// "Locally effective" with no dual value is **an assertion from nothing** — and it reads exactly like a measured conclusion.
    /// </remarks>
    [Fact]
    public void TheLocalVerdict_ShouldNeedADualValue() {
        var fabricated = new EffectivenessRanking(
            new Flt64(10.0),
            [Entry("c1", localEffective: true)]);
        fabricated.Validate().IsOk.Should().BeFalse(
            "a local verdict with no dual value has nothing behind it");

        var measured = new EffectivenessRanking(
            new Flt64(10.0),
            [Entry("c1", localEffective: true, dual: new Flt64(1.0))]);
        measured.Validate().Value.Should().BeOfType<Success>();

        // **反向对照**：`LocalEffective = null` 在有对偶值时也是合法的（"测得了对偶值，但没做局部判决"）。
        // **The reverse control**: `LocalEffective = null` is legal even with a dual ("the dual was measured, no local verdict was made").
        new EffectivenessRanking(new Flt64(10.0), [Entry("c1", dual: new Flt64(1.0))])
            .Validate().Value.Should().BeOfType<Success>();
    }

    /// <summary>**重复身份会让改善看起来翻倍** / A duplicate identity doubles the apparent improvement.</summary>
    [Fact]
    public void TheDuplicateConstraints_ShouldBeRefused() {
        new EffectivenessRanking(
            new Flt64(10.0),
            [Entry("c1", maxImprovement: new Flt64(2.0)), Entry("c1", maxImprovement: new Flt64(2.0))])
            .Validate().IsOk.Should().BeFalse("the same constraint counted twice doubles the improvement");

        new EffectivenessRanking(new Flt64(10.0), [Entry("")])
            .Validate().IsOk.Should().BeFalse("an entry with no identity cannot be traced");
    }

    /// <summary>**`NaN` 混进排序会让整个降序失去意义** / A `NaN` in the ordering destroys the sort.</summary>
    [Fact]
    public void TheNonFiniteValues_ShouldBeRefused() {
        new EffectivenessRanking(
            new Flt64(10.0), [Entry("c1", maxImprovement: new Flt64(double.NaN))])
            .Validate().IsOk.Should().BeFalse("NaN compares false against everything, so the order is not an order");
        new EffectivenessRanking(
            new Flt64(10.0), [Entry("c1", maxImprovement: new Flt64(double.PositiveInfinity))])
            .Validate().IsOk.Should().BeFalse("an infinite improvement is not a measurement");
        new EffectivenessRanking(
            new Flt64(10.0), [Entry("c1", dual: new Flt64(double.NaN))])
            .Validate().IsOk.Should().BeFalse();

        new EffectivenessRanking(
            new Flt64(double.NaN), [Entry("c1")])
            .Validate().IsOk.Should().BeFalse("a baseline objective with no value makes every comparison meaningless");
        new EffectivenessRanking(new Flt64(10.0), [Entry("c1")], SchemaVersion: "  ")
            .Validate().IsOk.Should().BeFalse("a report with no schema has no provenance");
    }

    /// <summary>**首次有效 delta 必须为正** / The first effective delta has to be positive.</summary>
    /// <remarks>
    /// 零 delta 表示"没有扰动"，负 delta 表示"往反方向扰动"。**两者都不是"首次有效"。**
    ///
    /// A zero delta means "no perturbation" and a negative one means "perturb the other way". **Neither is a "first effective" point.**
    /// </remarks>
    [Fact]
    public void TheFirstEffectiveDelta_ShouldBePositive() {
        new EffectivenessRanking(new Flt64(10.0), [Entry("c1", firstEffectiveDelta: new Flt64(0.0))])
            .Validate().IsOk.Should().BeFalse("a zero delta perturbs nothing");
        new EffectivenessRanking(new Flt64(10.0), [Entry("c1", firstEffectiveDelta: new Flt64(-1.0))])
            .Validate().IsOk.Should().BeFalse("a negative delta perturbs the other way");
        new EffectivenessRanking(new Flt64(10.0), [Entry("c1", firstEffectiveDelta: new Flt64(0.5))])
            .Validate().Value.Should().BeOfType<Success>();
    }

    /// <summary>**三个视图各自只筛自己的那一类** / Each of the three views filters only its own kind.</summary>
    [Fact]
    public void TheViews_ShouldFilterIndependently() {
        var ranking = new EffectivenessRanking(
            new Flt64(10.0),
            [
                Entry("global", maxImprovement: new Flt64(2.0), dual: new Flt64(1.0), localEffective: true),
                Entry("local-only", dual: new Flt64(1.0), localEffective: true),
                Entry("neither", dual: new Flt64(0.5)),
            ]);

        ranking.GloballyEffective.Should().ContainSingle()
            .Which.ConstraintId.Value.Should().Be("global");
        ranking.LocallyEffective.Should().HaveCount(2,
            "the local view includes the globally effective one when it also has a local verdict");
        ranking.EntryList.Should().HaveCount(3);

        ranking.Entry(new ConstraintId("neither")).Should().NotBeNull();
        ranking.Entry(new ConstraintId("absent")).Should().BeNull();
        ranking.Validate().Value.Should().BeOfType<Success>();
    }

    /// <summary>**条目缺省时不炸** / A missing entry list does not throw.</summary>
    [Fact]
    public void TheAbsentEntries_ShouldBeEmpty() {
        var ranking = new EffectivenessRanking(new Flt64(10.0));
        ranking.EntryList.Should().BeEmpty();
        ranking.GloballyEffective.Should().BeEmpty();
        ranking.LocallyEffective.Should().BeEmpty();
        ranking.Validate().Value.Should().BeOfType<Success>("an empty ranking is a valid ranking");
    }
}
