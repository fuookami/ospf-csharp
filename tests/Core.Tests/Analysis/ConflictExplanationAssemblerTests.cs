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
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突解释的组装 / Assembling a conflict explanation.
/// </summary>
/// <remarks>
/// **`AvailableEvidence` 是全集，`Members` 是收缩后的成员。** 两者混用，
/// **一份"最小冲突"看起来会像整个模型。**
///
/// **`AvailableEvidence` is the whole universe and `Members` is what survived the shrink.** Confused, **a minimal conflict looks like the entire model.**
/// </remarks>
public class ConflictExplanationAssemblerTests {
    private static ConstraintProgrammingModelSnapshot Snapshot() {
        var model = new ConstraintProgrammingModel("assembly", ObjectCategory.Maximum, identityNamespace: "assembly");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, new Int64(6)).Value,
            id: new ConstraintId("c:ge"), name: "ge").Value.Should().NotBeNull();
        var objective = new ObjectiveId("objective:main");
        model.Maximize(expression: expression, id: objective).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    private static TargetFeasibilityReport Unreachable() =>
        new(Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Unreachable);

    /// <summary>**全集是全集，会员是会员** / The universe stays the universe.</summary>
    /// <remarks>
    /// 用 `Members` 当 `AvailableEvidence`，读者会以为"这些就是全部证据"，
    /// **于是那份"最小冲突"看起来像整个模型。**
    ///
    /// Using `Members` as `AvailableEvidence` makes the reader think "this is all the evidence", so **a minimal conflict looks like the entire model.**
    /// </remarks>
    [Fact]
    public void TheAvailableEvidence_ShouldStayTheWholeUniverse() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var kept = new DiagnosticSource.Constraint(new ConstraintId("c:le"));
        var dropped = new DiagnosticSource.Constraint(new ConstraintId("c:ge"));

        ConflictExplanation explanation = ConflictExplanationAssembler.AssembleInfeasible(
            snapshot, Unreachable().Target, Unreachable(), new ConflictExtractionTier(),
            [kept, dropped], targetSource: null,
            // c:ge 删除后仍不可达 → 移除；c:le 删除后可达 → 保留。
            ConflictDeletionCheck.Shrink([kept, dropped], AnalysisStatus.Unreachable,
                maxDeletionChecks: 4, timeBudget: null, elapsed: TimeSpan.Zero,
                // 候选里还剩 c:le → 刚删的是 c:ge → 仍不可达 → c:ge 移除；
                // 候选里只剩 c:ge → 刚删的是 c:le → 可达 → c:le 保留。
                probe: candidate => candidate.Contains(kept) ? AnalysisStatus.Unreachable : AnalysisStatus.Reachable));

        explanation.AvailableEvidence.Should().BeEquivalentTo([kept, dropped],
            "the universe does not shrink just because a shrink ran");
        explanation.Members.Should().ContainSingle("only the shrunken membership is the conflict");
    }

    /// <summary>**三条消息互斥且背景阻塞优先** / The messages are exclusive, background first.</summary>
    /// <remarks>
    /// 先判"部分"的话，背景阻塞那份会带着一条"复验未完成"的消息，**而真正的原因在别处。**
    ///
    /// Checking "partial" first would leave the background-blocking case carrying a "some checks did not run" message, **while the real cause is elsewhere.**
    /// </remarks>
    [Fact]
    public void TheBackgroundBlockingMessage_ShouldWinOverPartial() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var bound = new DiagnosticSource.VariableLowerBound(new VariableId("source:x"));
        var other = new DiagnosticSource.VariableUpperBound(new VariableId("source:x"));

        // **两个标志必须同时为真，判据才有意义。** 第一次我只给了一条来源、上限为 0——
        // 那样 `Members` 会是空的，而"约束投影为空"在空集合上也成立，于是背景阻塞掩盖了别的分支。
        //
        // **Both flags have to be true for the criterion to mean anything.** The first version used one source and a cap of zero: `Members` came out empty, and "no constraint in the
        // projection" holds trivially on an empty set, so background blocking masked the other branch.
        ConflictDeletionCheck.ShrinkResult shrink = ConflictDeletionCheck.Shrink(
            [bound, other], AnalysisStatus.Unreachable, maxDeletionChecks: 4, timeBudget: null,
            elapsed: TimeSpan.Zero,
            // 两条复验都没结论（→ partial），两条都留在 `active`；
            // 两条都不是约束 → 剩下的 `Members` 里仍无约束 → 背景阻塞。
            probe: _ => AnalysisStatus.Unknown);

        shrink.Partial.Should().BeTrue("one check reached no conclusion");
        shrink.BackgroundBlocking.Should().BeTrue("no constraint survives in the projection");

        ConflictExplanation explanation = ConflictExplanationAssembler.AssembleInfeasible(
            snapshot, Unreachable().Target, Unreachable(), new ConflictExtractionTier(),
            [bound, other], targetSource: null, shrink);

        explanation.Message.Should().Contain("固定背景", "background blocking is the real cause");
        explanation.Message.Should().NotContain("删除复验未知", "the partial message would name the wrong cause");
        explanation.BackgroundBlocking.Should().BeTrue();
    }

    /// <summary>**目标可达时仍然 `Verified`，但最小性未查** / Reachable stays verified, with nothing checked.</summary>
    /// <remarks>
    /// **"没有冲突"不等于"没验证过"。** 填 `Unknown` 会让读者怀疑一份恰恰被完全证明的报告。
    ///
    /// **"No conflict" does not mean "nothing was verified".** `Unknown` makes the reader doubt the one kind of conclusion that was fully proven.
    /// </remarks>
    [Fact]
    public void TheReachableExplanation_ShouldStayVerified() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var report = new TargetFeasibilityReport(
            Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Reachable);

        ConflictExplanation explanation = ConflictExplanationAssembler.AssembleReachable(
            snapshot, report.Target, report, new ConflictExtractionTier(),
            [new DiagnosticSource.Constraint(new ConstraintId("c:le"))]);

        explanation.Validity.Should().Be(ConflictValidity.Verified,
            "reachability was proven; the absence of a conflict is not doubt about the proof");
        explanation.Members.Should().BeEmpty();
        explanation.Minimality.Should().Be(ConflictMinimality.NotChecked);
        explanation.Message.Should().BeNull("nothing needs explaining");
    }

    /// <summary>**按组汇总只覆盖收缩后的成员** / Group summaries cover the shrunken members only.</summary>
    [Fact]
    public void TheGroupSummaries_ShouldCoverOnlyMembers() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var kept = new DiagnosticSource.Constraint(new ConstraintId("c:le"));

        ConflictExplanation explanation = ConflictExplanationAssembler.AssembleInfeasible(
            snapshot, Unreachable().Target, Unreachable(), new ConflictExtractionTier(),
            [kept], targetSource: null,
            ConflictDeletionCheck.Shrink([kept], AnalysisStatus.Unreachable,
                maxDeletionChecks: 0, timeBudget: null, elapsed: TimeSpan.Zero,
                probe: _ => AnalysisStatus.Unknown));

        explanation.Members.Should().ContainSingle();
        // 夹具里两条约束都没有组名，所以汇总为空——**但这与"成员为空"是两件事**。
        explanation.GroupSummaries.Should().BeEmpty("no constraint in this fixture carries a group name");
    }

    /// <summary>**未知与不受支持不能声称"已验证"** / Unknown and unsupported cannot claim verification.</summary>
    /// <remarks>
    /// 把这两种当成"和可达一样"，会给出 `Members` 为空 + `Verified` 的一份解释，
    /// **而它同时声称"没有冲突"和"已证明"——两句话都是从没做过的分析里编出来的。**
    ///
    /// Treating them like reachable yields empty members with `Verified`, **claiming both "no conflict" and "proven" — two statements invented from an analysis that never ran.**
    /// </remarks>
    [Fact]
    public void TheUndecidedTarget_ShouldKeepItsEvidenceAndStayUnknown() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var evidence = new List<DiagnosticSource> {
            new DiagnosticSource.Constraint(new ConstraintId("c:le")),
            new DiagnosticSource.VariableLowerBound(new VariableId("source:x")),
        };

        foreach (AnalysisStatus status in new[] { AnalysisStatus.Unknown, AnalysisStatus.Unsupported }) {
            var report = new TargetFeasibilityReport(
                Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
                Status: status);

            ConflictExplanation explanation = ConflictExplanationAssembler.AssembleNotUnreachable(
                snapshot, report.Target, report, new ConflictExtractionTier(), evidence);

            explanation.Validity.Should().Be(ConflictValidity.Unknown,
                $"a {status} check established nothing");
            explanation.Members.Should().BeEquivalentTo(evidence,
                $"a {status} check has no basis to drop any source");
            explanation.Minimality.Should().Be(ConflictMinimality.NotChecked);
        }
    }

    /// <summary>**可达时成员为空且仍是被证过的** / Reachable has no members and is still verified.</summary>
    /// <remarks>
    /// **反向对照**：只查上一条，一个"永远返回全部证据 + Unknown"的实现照样通过，
    /// **而它会把一份完全证明的"目标可达"降级成不确定。**
    ///
    /// **The reverse control**: with only the previous criterion, an implementation that always returns all evidence with `Unknown` would pass, **downgrading a fully proven
    /// reachability to uncertainty.**
    /// </remarks>
    [Fact]
    public void TheReachableTarget_ShouldStayVerifiedWithNoMembers() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var report = new TargetFeasibilityReport(
            Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Reachable);

        ConflictExplanation explanation = ConflictExplanationAssembler.AssembleNotUnreachable(
            snapshot, report.Target, report, new ConflictExtractionTier(),
            [new DiagnosticSource.Constraint(new ConstraintId("c:le"))]);

        explanation.Validity.Should().Be(ConflictValidity.Verified, "reachability was proven");
        explanation.Members.Should().BeEmpty("nothing blocks a reachable target");
        explanation.AvailableEvidence.Should().NotBeEmpty("the universe is still reported");
    }

    /// <summary>**不可达不走这条路** / Unreachable does not come through here.</summary>
    [Fact]
    public void TheUnreachableTarget_ShouldBeRefusedHere() {
        ConstraintProgrammingModelSnapshot snapshot = Snapshot();
        var report = new TargetFeasibilityReport(
            Target: ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(9.0)),
            Status: AnalysisStatus.Unreachable);

        Action act = () => ConflictExplanationAssembler.AssembleNotUnreachable(
            snapshot, report.Target, report, new ConflictExtractionTier(),
            [new DiagnosticSource.Constraint(new ConstraintId("c:le"))]);

        act.Should().Throw<ArgumentException>("an unreachable target has to go through the deletion checks");
    }
}
