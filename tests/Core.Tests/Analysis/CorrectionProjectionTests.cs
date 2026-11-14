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
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突解释投影成修正集合 / Projecting a conflict explanation into correction sets.
/// </summary>
/// <remarks>
/// **九个条件里有两个最容易漏**：`!BackgroundBlocking` 与"解释目标 == 可行性目标"。
/// 前者会让"背景挡着"的解释声称每个成员都可删；**后者会让解释去描述一个它没检查过的目标。**
///
/// **Two of the nine conditions are the easiest to miss**: `!BackgroundBlocking` and target agreement. The former lets a "blocked by background" explanation claim every member is
/// deletable; **the latter lets an explanation describe a target it never checked.**
/// </remarks>
public class CorrectionProjectionTests {
    private static readonly ObjectiveTarget Target =
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0));

    private static readonly DiagnosticSource Constraint = new DiagnosticSource.Constraint(new ConstraintId("c:le"));

    private static readonly DiagnosticSource LowerBound =
        new DiagnosticSource.VariableLowerBound(new VariableId("source:x"));

    /// <summary>**一份够格的冲突解释** / A qualifying conflict explanation.</summary>
    private static ConflictExplanation Qualifying(
        IReadOnlyList<DiagnosticSource>? members = null,
        ObjectiveTarget? target = null,
        ObjectiveTarget? feasibilityTarget = null,
        bool backgroundBlocking = false,
        ConflictMinimality minimality = ConflictMinimality.Irreducible,
        ConflictValidity validity = ConflictValidity.Verified) {
        DiagnosticSource[] evidence = [Constraint, LowerBound];
        ObjectiveTarget explanationTarget = target ?? Target;

        return new ConflictExplanation(
            Target: explanationTarget,
            TargetFeasibility: new TargetFeasibilityReport(
                Target: feasibilityTarget ?? explanationTarget,
                Status: AnalysisStatus.Unreachable),
            ExtractionTier: new ConflictExtractionTier(),
            Status: AnalysisStatus.Unreachable,
            AvailableEvidence: evidence,
            Members: members ?? evidence,
            TargetSource: null,
            Validity: validity,
            Minimality: minimality,
            BackgroundBlocking: backgroundBlocking);
    }

    /// <summary>**够格的解释投影成每个成员一条** / A qualifying explanation projects one set per member.</summary>
    [Fact]
    public void TheQualifyingConflict_ShouldProjectOneSetPerMember() {
        ConflictExplanation conflict = Qualifying();

        CorrectionProjection.IsVerifiedMinimalConflict(conflict).Should().BeTrue("all nine conditions hold");

        IReadOnlyList<CorrectionSet> sets = CorrectionProjection.CorrectionSetsFromConflict(conflict);

        sets.Should().HaveCount(2, "one singleton correction set per member");
        sets.Should().OnlyContain(set => set.Minimal, "each set is minimal by construction");
        sets.Should().OnlyContain(set => set.TotalCost == 1.0);
        sets.SelectMany(set => set.Members).Select(member => member.Source)
            .Should().BeEquivalentTo(conflict.Members, "every member appears exactly once");
    }

    /// <summary>**背景阻塞时一条都不给** / Background blocking yields nothing.</summary>
    /// <remarks>
    /// **这是最容易漏的条件之一。** 背景挡着的时候，删掉哪条成员都不会让目标可达，
    /// **于是"删一个就够"这句话对每个成员都是假的。**
    ///
    /// **This is one of the two easiest conditions to miss.** When the background blocks, deleting any member leaves the target unreachable, **so "deleting one is enough" is false for
    /// every member.**
    /// </remarks>
    [Fact]
    public void TheBackgroundBlockedConflict_ShouldProjectNothing() {
        ConflictExplanation conflict = Qualifying(backgroundBlocking: true);

        CorrectionProjection.IsVerifiedMinimalConflict(conflict).Should().BeFalse(
            "deleting a member cannot help when the background is what blocks the target");
        CorrectionProjection.CorrectionSetsFromConflict(conflict).Should().BeEmpty();
        CorrectionProjection.WeightedCorrectionSetsFromConflict(conflict).Should().BeEmpty();
    }

    /// <summary>**解释的目标与可行性检查的目标不是同一个时，一条都不给** / Mismatched targets yield nothing.</summary>
    /// <remarks>
    /// **另一个最容易漏的条件。** 目标不一致时，解释描述的是 A，而可行性检查说的是 B，
    /// **它等于在用一个没检查过的目标下结论。**
    ///
    /// **The other easiest condition.** With mismatched targets the explanation describes A while the feasibility check is about B, **so it concludes about a target nobody checked.**
    /// </remarks>
    [Fact]
    public void TheMismatchedTarget_ShouldProjectNothing() {
        ConflictExplanation conflict = Qualifying(
            feasibilityTarget: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:other"), new Flt64(9.0)));

        CorrectionProjection.IsVerifiedMinimalConflict(conflict).Should().BeFalse(
            "the explanation and the feasibility check are about different targets");
        CorrectionProjection.CorrectionSetsFromConflict(conflict).Should().BeEmpty();
    }

    /// <summary>**部分最小性不给 MCS 结论** / Partial minimality makes no MCS claim.</summary>
    [Fact]
    public void ThePartialMinimality_ShouldProjectNothing() {
        ConflictExplanation conflict = Qualifying(minimality: ConflictMinimality.Partial);

        CorrectionProjection.IsVerifiedMinimalConflict(conflict).Should().BeFalse(
            "a partial deletion sweep never established irreducibility");
        CorrectionProjection.CorrectionSetsFromConflict(conflict).Should().BeEmpty();
    }

    /// <summary>**重复成员会让"删一个就够"变成假话** / Duplicate members falsify "one is enough".</summary>
    [Fact]
    public void TheDuplicateMembers_ShouldProjectNothing() {
        ConflictExplanation conflict = Qualifying(members: [Constraint, LowerBound, Constraint]);

        CorrectionProjection.IsVerifiedMinimalConflict(conflict).Should().BeFalse(
            "a source listed twice makes the singleton projection claim something untrue");
        CorrectionProjection.CorrectionSetsFromConflict(conflict).Should().BeEmpty();
    }

    /// <summary>**排序在截断之前** / Sorting happens before truncation.</summary>
    /// <remarks>
    /// **反向对照**：先 `take` 再 `sort` 会留下"前几条"里恰好是最贵的那几条。
    /// 这里给的输入顺序是**最贵的在前**，一个"先截断"的实现会留下贵的那两条。
    ///
    /// **The reverse control**: `take` then `sort` keeps whichever expensive ones came first. The input here is **most expensive first**, so a "truncate first" implementation keeps the
    /// expensive pair.
    /// </remarks>
    [Fact]
    public void TheWeightedSets_ShouldSortBeforeTruncating() {
        ConflictExplanation conflict = Qualifying();
        var costs = new Dictionary<string, RelaxationCost> {
            [Constraint.StableId] = new RelaxationCost(Weight: 9.0, Relaxation: 1.0),
            [LowerBound.StableId] = new RelaxationCost(Weight: 1.0, Relaxation: 1.0),
        };

        IReadOnlyList<CorrectionSet> sets = CorrectionProjection.WeightedCorrectionSetsFromConflict(
            conflict, costs, new RelaxabilityPolicy(MaxPlans: 1));

        sets.Should().HaveCount(1);
        sets[0].TotalCost.Should().Be(1.0,
            "the cheapest plan survives the truncation, not the first one in member order");
    }

    /// <summary>**非正权重被拒** / A non-positive weight is refused.</summary>
    [Fact]
    public void TheNonPositiveWeight_ShouldBeRefused() {
        ConflictExplanation conflict = Qualifying();
        var costs = new Dictionary<string, RelaxationCost> {
            [Constraint.StableId] = new RelaxationCost(Weight: 0.0),
        };

        Action act = () => CorrectionProjection.WeightedCorrectionSetsFromConflict(conflict, costs);

        act.Should().Throw<ArgumentException>("a zero-weight relaxation makes the cost ordering meaningless");
    }
}
