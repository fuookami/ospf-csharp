#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Success = Fuookami.Ospf.Utils.Functional.Success;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 修正集代数 / The correction-set algebra.
/// </summary>
/// <remarks>
/// **B971 补了 `CorrectionRecommendation` 的前 325 行**：策略、候选、成本、修正集合、改进方案与四个构造器。
/// 尾部约 110 行要 `ConflictExplanation`，**它住在带后端求解的 `ConflictAnalyzer.kt` 里，所以没搬，也没写空跑版本。**
///
/// 判据钉的是**两件"说了就不能反悔"的事**：最小性只在没被截断时成立；总成本必须与成员一致。
///
/// **B971 ported the first 325 lines of `CorrectionRecommendation`**. The last ~110 need `ConflictExplanation`, which lives inside the backend-solving `ConflictAnalyzer.kt`, so they
/// were neither ported nor stubbed. The criteria pin **two things a claim cannot take back**: minimality holds only without truncation, and the total has to agree with the members.
/// </remarks>
public class CorrectionRecommendationTests {
    private static CorrectionCandidate Candidate(string id, double weight = 1.0, double relaxation = 1.0) =>
        new(new DiagnosticSource.Constraint(new ConstraintId(id)), weight, relaxation);

    /// <summary>**策略截断后不得声称最小性** / Minimality may not be claimed after the policy truncates.</summary>
    /// <remarks>
    /// 删除验证只覆盖被截断前的候选全集，而返回的集合已经不是那个全集。**保留加权建议，但不声称最小。**
    ///
    /// Deletion verification covered the untruncated universe; the returned set is no longer it. **Keep the recommendation, drop the claim.**
    /// </remarks>
    [Fact]
    public void TheMinimality_ShouldNotSurviveTruncation() {
        CorrectionCandidate[] three = [Candidate("c1"), Candidate("c2"), Candidate("c3")];

        // 上限 3：没被截断，最小性成立。
        CorrectionSet intact = CorrectionRecommendations.MinimalCorrectionSet(
            three, verified: true, new RelaxabilityPolicy(MaxCandidates: 3));
        intact.Minimal.Should().BeTrue("nothing was truncated, so the verification still covers the set");
        intact.DeletionMinimalityVerified().Should().BeTrue();

        // 上限 2：被截断，最小性必须降级。
        CorrectionSet truncated = CorrectionRecommendations.MinimalCorrectionSet(
            three, verified: true, new RelaxabilityPolicy(MaxCandidates: 2));
        truncated.Members.Should().HaveCount(2);
        truncated.Minimal.Should().BeFalse(
            "the deletion verification covered three candidates and the returned set is not those three");
        truncated.DeletionMinimalityVerified().Should().BeFalse();

        // **没有删除验证时必须抛**，而不是默默给一个"最小"集合。
        Action unverified = () => CorrectionRecommendations.MinimalCorrectionSet(three, verified: false);
        unverified.Should().Throw<ArgumentException>("an unverified set may not be called minimal");
    }

    /// <summary>**总成本必须与成员一致，不一致的集合要被拒** / The total has to agree with the members.</summary>
    /// <remarks>
    /// 不一致的集合会让"按成本排序"给出一个成员成本解释不了的顺序——**而排序恰好是这一层的全部输出。**
    ///
    /// An inconsistent set makes a cost ranking produce an order its members cannot explain — **and that ordering is this layer's entire output.**
    /// </remarks>
    [Fact]
    public void TheTotalCost_ShouldAgreeWithTheMembers() {
        var consistent = new CorrectionSet([Candidate("c1", 2.0, 3.0)], TotalCost: 6.0);
        consistent.Validate().Value.Should().BeOfType<Success>("a consistent set validates to the success value");

        var inconsistent = new CorrectionSet([Candidate("c1", 2.0, 3.0)], TotalCost: 5.0);
        inconsistent.Validate().IsOk.Should().BeFalse(
            "a total the members cannot explain makes the ranking meaningless");

        var duplicate = new CorrectionSet([Candidate("c1"), Candidate("c1")], TotalCost: 2.0);
        duplicate.Validate().IsOk.Should().BeFalse("the same evidence twice would double-count one relaxation");

        var empty = new CorrectionSet([], TotalCost: 0.0);
        empty.Validate().IsOk.Should().BeFalse("an empty correction set corrects nothing");
    }

    /// <summary>**溢出成 `Infinity` 的成本要被拒** / A cost that overflows to `Infinity` is refused.</summary>
    /// <remarks>
    /// 权重与松弛各自有限、乘积溢出时，成本变成 `Infinity`，而按成本排序会**把它排到最后**——
    /// 看起来像"最便宜的方案"。
    ///
    /// When each is finite but the product overflows the cost becomes `Infinity`, and a cost ranking puts it **last** — which reads as "the cheapest option".
    /// </remarks>
    [Fact]
    public void TheCost_ShouldRefuseAnOverflowingProduct() {
        var overflowing = new CorrectionCandidate(
            new DiagnosticSource.Constraint(new ConstraintId("c1")),
            Weight: 1e200,
            Relaxation: 1e200);
        overflowing.Validate().IsOk.Should().BeFalse("1e200 * 1e200 is Infinity, not a cost");

        CorrectionCandidate zeroRelaxation = Candidate("c1", relaxation: 0.0);
        zeroRelaxation.Validate().IsOk.Should().BeFalse("relaxing nothing is not a correction");

        CorrectionCandidate negative = Candidate("c1", weight: -1.0);
        negative.Validate().IsOk.Should().BeFalse("a negative weight would reward removing evidence");

        // **策略允许时负权重是合法的**：否则上面那条只是"一律拒绝"。
        // **A negative weight is legal when the policy allows it**, or the check above only proves "everything is refused".
        new CorrectionCandidate(new DiagnosticSource.Constraint(new ConstraintId("c1")), -1.0, 1.0)
            .Validate(new RelaxabilityPolicy(RequirePositiveWeight: false))
            .Value.Should().BeOfType<Success>("the policy can allow it, and then it is genuinely valid");
    }

    /// <summary>**加权构造器按成本升序取前 N 个，结果确定** / The weighted builder takes the cheapest N, deterministically.</summary>
    [Fact]
    public void TheWeightedSet_ShouldBeDeterministic() {
        CorrectionCandidate[] candidates = [
            Candidate("c3", 3.0, 1.0),
            Candidate("c1", 1.0, 1.0),
            Candidate("c2", 2.0, 1.0),
        ];

        CorrectionSet set = CorrectionRecommendations.WeightedCorrectionSet(
            candidates, new RelaxabilityPolicy(MaxCandidates: 2));
        set.Members.Select(member => member.Source.StableId).Should().Equal("constraint:c1", "constraint:c2");
        set.TotalCost.Should().Be(3.0, "the total is the sum of the two kept members, not of all three");
        set.Validate().Value.Should().BeOfType<Success>("the built set has to be self-consistent");

        // **等成本时按稳定身份排序**，否则同一份输入两次可能给出两种顺序。
        // **Equal costs fall back to the stable identity**, or the same input could give two orders.
        CorrectionSet tied = CorrectionRecommendations.WeightedCorrectionSet(
            [Candidate("cb"), Candidate("ca")], new RelaxabilityPolicy());
        tied.Members.Select(member => member.Source.StableId).Should().Equal("constraint:ca", "constraint:cb");
    }

    /// <summary>**本协议从不声称完整 MCS，也从不声称应用后目标可达** / The protocol never claims a complete MCS or a reachable target.</summary>
    /// <remarks>
    /// 这两条是"说了就收不回"的承诺：数值放宽**只是建议**，必须由调用方重新校验。
    ///
    /// These are promises that cannot be taken back: a numeric relaxation is **only a recommendation** and has to be revalidated by the caller.
    /// </remarks>
    [Fact]
    public void TheProtocol_ShouldNotOverclaim() {
        var set = new CorrectionSet([Candidate("c1")], TotalCost: 1.0);
        set.IsCompleteMcs().Should().BeFalse("this protocol never proves a model-wide MCS");
        set.RequiresRevalidation.Should().BeTrue("a numeric relaxation is a recommendation");
        set.RequiresRevalidationMethod().Should().Be(set.RequiresRevalidation);

        var plan = new AlternativeImprovementPlan(
            ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(1.0)),
            set,
            Rank: 0);
        plan.IsCompleteMcs().Should().BeFalse();
        plan.RequiresRevalidation.Should().BeTrue(
            "applying a numeric candidate never proves the target became reachable");
        plan.Validate().Value.Should().BeOfType<Success>("a rank-0 plan under the default policy is valid");

        // **名次超出策略要被拒**，否则"最多 N 个方案"这条上限是空话。
        AlternativeImprovementPlan outOfRange = plan with { Rank = 8 };
        outOfRange.Validate(new RelaxabilityPolicy(MaxPlans: 8)).IsOk.Should().BeFalse(
            "a rank at the plan limit is outside the policy");
    }

    /// <summary>**Result 形式把异常转成失败结果，而不是抛出** / The Result forms turn the exception into a failure.</summary>
    [Fact]
    public void TheResultForms_ShouldNotThrow() {
        CorrectionSet good = CorrectionRecommendations.TryWeightedCorrectionSet([Candidate("c1")]).Value;
        good.Members.Should().ContainSingle();

        CorrectionCandidate[] badCandidates = [Candidate("c1", weight: -1.0)];
        CorrectionRecommendations.TryWeightedCorrectionSet(badCandidates).IsOk.Should().BeFalse(
            "a negative weight fails, and the Result form reports it instead of throwing");

        CorrectionRecommendations.TryMinimalCorrectionSet([Candidate("c1")], verified: false).IsOk
            .Should().BeFalse("an unverified minimal set fails through the Result form too");
    }
}
