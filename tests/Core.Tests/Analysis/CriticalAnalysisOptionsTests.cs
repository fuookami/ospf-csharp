#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 流水线与冲突分析的选项 / The pipeline and conflict-analysis options.
/// </summary>
/// <remarks>
/// 补的是选项层，没补编排器。**
/// 编排器要依次调扰动、目标可行性、冲突三个分析器，**而这三个的分析器本体都还没搬**。
///
/// **The option layer is implemented, the orchestrator is not**: it calls the perturbation, target-feasibility and conflict analyzers,
/// **none of whose bodies is implemented**.
/// </remarks>
public class CriticalAnalysisOptionsTests {
    /// <summary>**零不是错误，负数才是** / Zero is not an error; negative is.</summary>
    /// <remarks>
    /// `MaxDeletionChecks = 0` 是"保留完整已证明冲突、不缩减"的合法请求；
    /// `CandidateLimit = 0` 是"只做筛选、不做昂贵扰动"；`TimeBudget = 0` 是"不花时间"。
    ///
    /// **把零当非法拒掉，等于让调用方连一次便宜或完整的分析都拿不到。**
    ///
    /// `MaxDeletionChecks = 0` is a legitimate "keep the full proven conflict unchanged"; `CandidateLimit = 0` is "screen only, skip expensive perturbation"; `TimeBudget = 0` is
    /// "spend no time". **Rejecting zero as invalid denies the caller even the cheap or complete analysis.**
    /// </remarks>
    [Fact]
    public void TheZeroValues_ShouldBeAccepted() {
        new ConflictAnalysisOptions(MaxDeletionChecks: 0).Validate()
            .Should().BeNull("zero deletion checks means 'do not shrink', which is a real request");
        new ConflictAnalysisOptions(TimeBudget: TimeSpan.Zero).Validate()
            .Should().BeNull("a zero budget means 'spend no time', not 'misconfigured'");
        new CriticalConstraintAnalysisOptions(CandidateLimit: 0).Validate()
            .Should().BeNull("zero candidates means 'screen only', the cheapest useful run");

        // **反向对照**：只断言"零合法"会漏掉"什么都合法"。负数必须被拒。
        // **The reverse control**: asserting only "zero is legal" would miss "everything is legal". Negatives must be refused.
        new ConflictAnalysisOptions(MaxDeletionChecks: -1).Validate().Should().NotBeNull();
        new ConflictAnalysisOptions(TimeBudget: TimeSpan.FromSeconds(-1)).Validate().Should().NotBeNull();
        new CriticalConstraintAnalysisOptions(CandidateLimit: -1).Validate().Should().NotBeNull();
    }

    /// <summary>**流水线的默认扰动策略不能顺手换成策略类型自己的默认** / The pipeline's default perturbation must not be swapped for the policy's own default.</summary>
    /// <remarks>
    /// 流水线默认 `deltas = emptyList, removalTest = true`——只做删除测试；
    /// 策略类型自己默认 `deltas = [1.0]`——要跑一串 RHS 扰动。**两者代价差一个数量级**，
    /// 而流水线是自动跑的，**取重的那个会让"跑一次分析"变成一件很贵的事。**
    ///
    /// The pipeline defaults to removals only; the policy type's own default runs a series of RHS perturbations. **The two differ by an order of magnitude in cost**,
    /// and the pipeline runs automatically, so **the heavier default makes "one analysis run" expensive.**
    /// </remarks>
    [Fact]
    public void TheDefaultPerturbation_ShouldStayRemovalOnly() {
        var options = new CriticalConstraintAnalysisOptions();
        ConstraintPerturbationPolicy policy = options.PerturbationValue;

        policy.DeltaList.Should().BeEmpty(
            "the pipeline default does no RHS perturbation, and a default delta would start a series");
        policy.RemovalTest.Should().BeTrue(
            "without removals too, the default policy would do nothing at all");
        policy.Validate().Should().BeNull(
            "removals-only is a valid policy; 'at least one operation' is satisfied by the removal test");

        // **对照**：策略类型自己的默认确实不同，否则这条判据什么也没区分。
        // **Control**: the policy type's own default really is different, or this criterion distinguishes nothing.
        new ConstraintPerturbationPolicy().DeltaList.Should().NotBeEmpty(
            "the policy's own default does start with one delta, which is why copying it here would be a change");
    }

    /// <summary>**其余默认值不得漂移** / The remaining defaults must not drift.
    /// </summary>
    /// <remarks>
    /// `CandidateLimit` 的默认是漏斗配置的候选上限（32），不是零。
    /// 上一版我把它写成了 0——**那会让默认的流水线只做筛选，永远不做扰动分析，而且没有任何报错。**
    ///
    /// `CandidateLimit` defaults to the funnel's candidate limit (32), not zero. An earlier draft of wrote 0 — **which would make the default pipeline screen only and
    /// never run perturbation analysis, with no error at all.**
    /// </remarks>
    [Fact]
    public void TheRemainingDefaults_ShouldNotDrift() {
        var options = new CriticalConstraintAnalysisOptions();

        options.CandidateLimit.Should().Be(CandidateFunnelConfig.DefaultCandidateLimitValue,
            "a zero default would silently turn off every expensive stage");
        options.RunPerturbation.Should().BeTrue();
        options.RunTargetAnalysis.Should().BeTrue();
        options.Validate().Should().BeNull("the shipped defaults have to be valid");

        options.ConflictValue.MaxDeletionChecks.Should().Be(int.MaxValue,
            "the default keeps the full proven conflict");
        options.ConflictValue.TimeBudget.Should().BeNull("no default budget");
    }

    /// <summary>**子选项的问题必须冒泡上来** / A problem in a nested option has to surface.</summary>
    /// <remarks>
    /// 校验只看自己那一层、把坏掉的子选项放过去，等于**外层说"配置没问题"、里层每次调用都失败**。
    ///
    /// If validation checks only its own layer and lets a broken nested option through, **the outer object says "configured fine" while every inner call fails.**
    /// </remarks>
    [Fact]
    public void TheNestedProblems_ShouldSurface() {
        var brokenConflict = new CriticalConstraintAnalysisOptions(
            Conflict: new ConflictAnalysisOptions(MaxDeletionChecks: -5));
        brokenConflict.Validate().Should().Be("删除复验次数不得为负 / Maximum deletion checks must not be negative",
            "the outer validation has to report the inner problem, not swallow it");

        var brokenPerturbation = new CriticalConstraintAnalysisOptions(
            Perturbation: new ConstraintPerturbationPolicy(
                Deltas: Array.Empty<Fuookami.Ospf.Math.Algebra.Number.Flt64>()));
        brokenPerturbation.Validate().Should().NotBeNull(
            "a policy with no operation is invalid wherever it is nested");
    }
}
