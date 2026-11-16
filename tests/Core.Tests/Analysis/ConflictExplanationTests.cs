#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Success = Fuookami.Ospf.Utils.Functional.Success;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突解释 / The conflict explanation.
/// </summary>
/// <remarks>
/// 只搬报告侧**：基线第 630 行有 `solver.solve(...)`、第 755 行有 `session.solve(assumptions =...)`，
/// `ConflictAnalyzer` 与 `MinimalConflictAnalyzer` 都在其后。**那部分要真实 CP 后端，没搬也没空跑。**
///
/// 判据钉的每一条，**都是"报告看起来有结论，但证据撑不住"的形状**。
///
/// **The report side only is ported**: baseline line 630 has `solver.solve(...)` and line 755 has `session.solve(assumptions =...)`, with both analyzers after them.
/// **That needs a real CP backend, so it was neither ported nor stubbed.** Every criterion here blocks a shape where **the report looks concluded but the evidence does not
/// carry it**.
/// </remarks>
public class ConflictExplanationTests {
    private static ObjectiveTarget Target() =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(10.0));

    private static DiagnosticSource.Constraint Constraint(string id) => new(new ConstraintId(id));

    private static TargetFeasibilityReport Feasibility(ObjectiveTarget target, AnalysisStatus status) =>
        new(target, Status: status, ProofStatus: ProofStatus.Verified);

    private static ConflictExplanation Explanation(
        AnalysisStatus status,
        IReadOnlyList<DiagnosticSource> members,
        ConflictValidity validity = ConflictValidity.Verified,
        ConflictMinimality minimality = ConflictMinimality.NotChecked,
        IReadOnlyList<ConflictVerification>? verifications = null,
        bool backgroundBlocking = false,
        IReadOnlyList<DiagnosticSource>? available = null) {
        ObjectiveTarget target = Target();
        return new ConflictExplanation(
            target,
            Feasibility(target, status),
            ConflictExtractionTier.RepeatedSolving,
            status,
            available ?? [Constraint("c1"), Constraint("c2"), Constraint("c3")],
            members,
            null,
            validity,
            minimality,
            verifications,
            null,
            backgroundBlocking);
    }

    /// <summary>**目标不得出现在被阻塞的成员里** / The target may not appear among the blocking members.</summary>
    /// <remarks>
    /// 目标自己进了成员表，**删掉它"冲突就没了"**——那是一条永远成立、也永远无用的结论。
    ///
    /// With the target itself in the member list, **deleting it "resolves" the conflict** — a conclusion that always holds and never means anything.
    /// </remarks>
    [Fact]
    public void TheMembers_ShouldNotContainTheTarget() {
        ConflictExplanation withTarget = Explanation(
            AnalysisStatus.Unreachable,
            [Constraint("c1"), new DiagnosticSource.ObjectiveTargetSource(Target())]);
        withTarget.Validate().IsOk.Should().BeFalse(
            "the target among the members would make the conflict trivially resolvable");

        Explanation(AnalysisStatus.Unreachable, [Constraint("c1")]).Validate().Value
            .Should().BeOfType<Success>("a member list of real original evidence validates");
    }

    /// <summary>**成员必须是可用证据的子集，且不得重复** / Members must be a unique subset of the available evidence.</summary>
    /// <remarks>
    /// 同一个来源算两次，**会让最小性看起来更强**；一条从未激活的证据出现在成员里，
    /// 则意味着这份冲突涉及了模型里不存在的东西。
    ///
    /// Counting the same source twice **makes minimality look stronger**; a member that was never active means the conflict involves something the model does not have.
    /// </remarks>
    [Fact]
    public void TheMembers_ShouldBeAUniqueSubsetOfTheEvidence() {
        Explanation(AnalysisStatus.Unreachable, [Constraint("c1"), Constraint("c1")])
            .Validate().IsOk.Should().BeFalse("a duplicate member would be counted twice");

        Explanation(AnalysisStatus.Unreachable, [Constraint("never-active")])
            .Validate().IsOk.Should().BeFalse(
                "a member outside the available evidence was never part of this model");

        Explanation(
            AnalysisStatus.Unreachable,
            [Constraint("c1")],
            available: [Constraint("c1"), Constraint("c1")])
            .Validate().IsOk.Should().BeFalse("the evidence universe itself must be duplicate-free");
    }

    /// <summary>**可达目标不得带阻塞成员** / A reachable target carries no blocking members.</summary>
    [Fact]
    public void TheReachableTarget_ShouldCarryNoMembers() {
        Explanation(AnalysisStatus.Reachable, [Constraint("c1")])
            .Validate().IsOk.Should().BeFalse("a reachable target with blockers contradicts itself");
        Explanation(AnalysisStatus.Reachable, []).Validate().Value.Should().BeOfType<Success>();

        // **不可达必须同时是 Verified**：一个"不可达但未证明"的冲突是最容易被当成结论的那种。
        // **Unreachable has to be Verified too**: "unreachable but unproven" is the one most easily read as a conclusion.
        Explanation(AnalysisStatus.Unreachable, [Constraint("c1")], validity: ConflictValidity.Unknown)
            .Validate().IsOk.Should().BeFalse("an unverified unreachable conflict may not pass as proven");
    }

    /// <summary>**背景阻塞不得宣称约束级最小性** / Background blocking may not claim constraint-level minimality.</summary>
    /// <remarks>
    /// 阻塞来自背景时，**删掉任何一条原始约束都解不开它**，所以"每个成员都验过删除"这件事无从谈起。
    ///
    /// When the block comes from the background, **removing any original constraint cannot lift it**, so "every member passed a deletion check" cannot even be asked.
    /// </remarks>
    [Fact]
    public void TheBackgroundBlocking_ShouldNotClaimMinimality() {
        ConflictVerification[] proof = [
            // **删掉 c1 后目标变成可达**——这正是"它是冲突成员"的含义。
            // **Removing c1 makes the target reachable** — that is what being a conflict member means.
            new ConflictVerification(Constraint("c1"), AnalysisStatus.Reachable, Removed: true, Retained: true),
        ];

        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1")],
            minimality: ConflictMinimality.Irreducible,
            verifications: proof,
            backgroundBlocking: true)
            .Validate().IsOk.Should().BeFalse(
                "a background block cannot be minimal in terms of original constraints");

        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1")],
            minimality: ConflictMinimality.Irreducible,
            verifications: proof,
            backgroundBlocking: false)
            .Validate().Value.Should().BeOfType<Success>("the same set without background blocking is minimal");
    }

    /// <summary>**不可约要求逐成员的 target-fixed 删除证明** / Irreducibility needs a per-member target-fixed proof.</summary>
    /// <remarks>
    /// 只要求"有过复验"，会让一份**只验了一条**的记录声称整组最小。
    ///
    /// Requiring only "some verification happened" lets a record that checked **one** member claim the whole set minimal.
    /// </remarks>
    [Fact]
    public void TheIrreducibility_ShouldNeedEveryMemberProven() {
        // 没有任何复验：拒。
        Explanation(AnalysisStatus.Unreachable, [Constraint("c1")], minimality: ConflictMinimality.Irreducible)
            .Validate().IsOk.Should().BeFalse("claiming irreducibility with no verification at all");

        // 只验了 c1，但成员有 c1 和 c2：拒。
        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1"), Constraint("c2")],
            minimality: ConflictMinimality.Irreducible,
            verifications: [new ConflictVerification(Constraint("c1"), AnalysisStatus.Reachable, true, true)])
            .Validate().IsOk.Should().BeFalse("one proven member does not make a two-member set irreducible");

        // 两个成员都验过：通过。
        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1"), Constraint("c2")],
            minimality: ConflictMinimality.Irreducible,
            verifications: [
                new ConflictVerification(Constraint("c1"), AnalysisStatus.Reachable, true, true),
                new ConflictVerification(Constraint("c2"), AnalysisStatus.Reachable, true, true),
            ])
            .Validate().Value.Should().BeOfType<Success>();

        // **`TargetFixed = false` 的复验不算**：目标没固定时，"删掉这条就不不可达了"说明不了任何事。
        // **A `TargetFixed = false` check does not count**: with the target unfixed, "removing this made it not-unreachable" says nothing.
        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1")],
            minimality: ConflictMinimality.Irreducible,
            verifications: [new ConflictVerification(Constraint("c1"), AnalysisStatus.Reachable, true, true, TargetFixed: false)])
            .Validate().IsOk.Should().BeFalse("an unfixed-target check proves nothing about this target");

        // **`Retained = false` 的复验不算**：候选已经不在冲突里，它证明不了当前成员。
        // **A `Retained = false` check does not count**: the candidate is no longer in the conflict, so it proves nothing about the current member.
        Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1")],
            minimality: ConflictMinimality.Irreducible,
            verifications: [new ConflictVerification(Constraint("c1"), AnalysisStatus.Reachable, true, false)])
            .Validate().IsOk.Should().BeFalse("a candidate that was not retained is not a member");
    }

    /// <summary>**目标不一致要被拒** / A mismatched target is refused.</summary>
    [Fact]
    public void TheTarget_ShouldAgreeOnBothSides() {
        ObjectiveTarget left = Target();
        ObjectiveTarget right = ObjectiveTarget.CreateAtMost(new ObjectiveId("objective:main"), new Flt64(5.0));

        var mismatched = new ConflictExplanation(
            left,
            Feasibility(right, AnalysisStatus.Unreachable),
            ConflictExtractionTier.RepeatedSolving,
            AnalysisStatus.Unreachable,
            [Constraint("c1")],
            [Constraint("c1")],
            null,
            ConflictValidity.Verified,
            ConflictMinimality.NotChecked);
        mismatched.Validate().IsOk.Should().BeFalse(
            "the conflict would point at a target nobody analysed");
    }

    /// <summary>**三个别名各自成立** / The three aliases each hold.</summary>
    [Fact]
    public void TheAliases_ShouldTrackTheirFields() {
        ConflictExplanation explanation = Explanation(
            AnalysisStatus.Unreachable, [Constraint("c1"), Constraint("c2")],
            minimality: ConflictMinimality.Partial);

        explanation.Tier.Should().Be(explanation.ExtractionTier);
        explanation.MinimalBlockingSet.Should().BeEquivalentTo(explanation.Members);
        explanation.DeletionChecks.Should().BeEmpty("no verifications were supplied");
        explanation.Groups.Should().BeEmpty();
        explanation.ConstraintIds.Should().BeEquivalentTo(new[] { new ConstraintId("c1"), new ConstraintId("c2") });
        explanation.InfeasibilityMembers.Should().HaveCount(2, "both constraint sources map to infeasibility members");
        explanation.MinimalityVerified.Should().BeFalse("Partial is not Irreducible");
        explanation.EvidenceTargetSource.Should().BeOfType<DiagnosticSource.ObjectiveTargetSource>(
            "the target source is derived from the target when not supplied");
    }
}
