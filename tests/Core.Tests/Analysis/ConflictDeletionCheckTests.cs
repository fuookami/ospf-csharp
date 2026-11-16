#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突删除复验的判定 / The verdict of a conflict deletion check.
/// </summary>
/// <remarks>
/// **删除后可达，意味着这个来源是必要的，所以要保留。** 这跟直觉相反，而反过来写会让整份冲突恰好取补集。
///
/// **Reachable after removal means the source was necessary, so it is kept.** That is counter-intuitive, and flipping it makes the whole conflict come out as its complement.
/// </remarks>
public class ConflictDeletionCheckTests {
    private static DiagnosticSource Source(string id) => new DiagnosticSource.Constraint(new ConstraintId(id));

    /// <summary>**删除后可达 → 保留；删除后不可达 → 移除** / Reachable keeps, unreachable drops.</summary>
    /// <remarks>
    /// 两条都要查。只查一条，一个"永远保留"或"永远移除"的实现照样通过——**而后者会把整份冲突清空。**
    ///
    /// Both are needed. With one, an always-keep or always-drop implementation passes — **and the latter empties the whole conflict.**
    /// </remarks>
    [Fact]
    public void TheDeletionVerdict_ShouldFollowTheDirection() {
        DiagnosticSource source = Source("c:le");

        (ConflictVerification reachable, bool dropReachable) =
            ConflictDeletionCheck.Check(source, AnalysisStatus.Reachable);
        reachable.Retained.Should().BeTrue("without this source the target becomes reachable, so it is part of the conflict");
        reachable.Removed.Should().BeFalse();
        dropReachable.Should().BeFalse();

        (ConflictVerification unreachable, bool dropUnreachable) =
            ConflictDeletionCheck.Check(source, AnalysisStatus.Unreachable);
        unreachable.Removed.Should().BeTrue("removing it leaves the target unreachable, so it is not part of the conflict");
        unreachable.Retained.Should().BeFalse();
        dropUnreachable.Should().BeTrue();
    }

    /// <summary>**没测出来既不移除也不保留** / An inconclusive check neither removes nor retains.</summary>
    /// <remarks>
    /// 把"没测出来"当成 `Unreachable`，会**把一个真实原因从冲突里删掉**——
    /// 于是报告给出的那份"最小冲突"少了一条，而它看起来完全正常。
    ///
    /// Treating "not measured" as `Unreachable` **deletes a real cause from the conflict**, so the "minimal conflict" the report hands back is missing a member and looks entirely
    /// normal.
    /// </remarks>
    [Fact]
    public void TheInconclusiveCheck_ShouldKeepBothFlagsFalse() {
        foreach (AnalysisStatus status in new[] { AnalysisStatus.Unknown, AnalysisStatus.Unsupported }) {
            (ConflictVerification verification, bool drop) =
                ConflictDeletionCheck.Check(Source("c:le"), status);

            verification.Removed.Should().BeFalse($"a {status} check measured nothing");
            verification.Retained.Should().BeFalse($"a {status} check measured nothing, so it also cannot retain");
            drop.Should().BeFalse($"a {status} check must not remove a real cause");
            ConflictDeletionCheck.MakesPartial(verification).Should().BeTrue();
        }
    }

    /// <summary>**一次没复验就不能说"已是最小"** / Zero checks cannot claim irreducibility.</summary>
    /// <remarks>
    /// `Irreducible` 的意思是"每一条都试过了、每一条都必要"——**一次没试过就说它，是凭空断言。**
    ///
    /// `Irreducible` means "every member was tried and every member proved necessary" — **saying that having tried nothing is an assertion from nothing.**
    /// </remarks>
    [Fact]
    public void TheZeroChecks_ShouldNotClaimIrreducibility() {
        ConflictDeletionCheck.SummarizeMinimality([], maxDeletionChecks: 0)
            .Should().Be(ConflictMinimality.NotChecked, "the cap was zero, so nothing was checked");
        ConflictDeletionCheck.SummarizeMinimality([], maxDeletionChecks: 5)
            .Should().Be(ConflictMinimality.NotChecked, "the cap allowed checks but none ran");
    }

    /// <summary>**部分与最小要分开** / Partial and irreducible are distinct.</summary>
    [Fact]
    public void TheMinimality_ShouldDistinguishPartial() {
        var allDecided = new List<ConflictVerification> {
            ConflictDeletionCheck.Check(Source("a"), AnalysisStatus.Reachable).Verification,
            ConflictDeletionCheck.Check(Source("b"), AnalysisStatus.Unreachable).Verification,
        };
        ConflictDeletionCheck.SummarizeMinimality(allDecided, maxDeletionChecks: 2)
            .Should().Be(ConflictMinimality.Irreducible, "every member was decided");

        var withInconclusive = new List<ConflictVerification>(allDecided) {
            ConflictDeletionCheck.Check(Source("c"), AnalysisStatus.Unknown).Verification,
        };
        ConflictDeletionCheck.SummarizeMinimality(withInconclusive, maxDeletionChecks: 3)
            .Should().Be(ConflictMinimality.Partial, "one member was never decided");
    }

    /// <summary>**没结论的来源必须留在 active 里** / An inconclusive source stays active.</summary>
    /// <remarks>
    /// `members` 是从 `active` 推出来的。**把没结论的来源删掉，报告会少一个真实原因，
    /// 而整份报告一致地错。**
    ///
    /// `members` is derived from `active`. **Dropping an inconclusive source loses a real cause, and since the whole report follows from it, the whole report is consistently
    /// wrong.**
    /// </remarks>
    [Fact]
    public void TheInconclusiveSource_ShouldStayInMembers() {
        DiagnosticSource a = Source("a");
        DiagnosticSource b = Source("b");

        ConflictDeletionCheck.ShrinkResult result = ConflictDeletionCheck.Shrink(
            [a, b], AnalysisStatus.Unreachable, maxDeletionChecks: 4, timeBudget: null,
            elapsed: TimeSpan.Zero,
            // **每一次复验都没结论**：两条都必须留在 `active` 里。
            probe: _ => AnalysisStatus.Unknown);

        result.Members.Should().BeEquivalentTo([a, b], "an undecided source must not be dropped");
        result.Partial.Should().BeTrue();
        result.Minimality.Should().Be(ConflictMinimality.Partial);
        result.Checks.Should().Be(2);
    }

    /// <summary>**可移除的来源真的被移除** / A droppable source is really dropped.</summary>
    /// <remarks>
    /// **反向对照**：上一条证明"没结论的不动"，这条证明"有结论的会动"。
    /// 只查前者，一个"什么都不删"的实现照样通过。
    ///
    /// **The reverse control**: the previous one proves undecided sources stay; this one proves decided ones move. With only the former, an implementation that never drops
    /// anything would pass.
    /// </remarks>
    [Fact]
    public void TheDroppableSource_ShouldBeRemoved() {
        DiagnosticSource a = Source("a");
        DiagnosticSource b = Source("b");

        // 删掉 a 仍不可达 → a 移除；之后 active 只剩 b，删掉 b 后可达 → b 保留。
        ConflictDeletionCheck.ShrinkResult result = ConflictDeletionCheck.Shrink(
            [a, b], AnalysisStatus.Unreachable, maxDeletionChecks: 4, timeBudget: null,
            elapsed: TimeSpan.Zero,
            // 删掉 a 后候选是 [b]，仍不可达 → a 移除；再删 b 后候选为空、可达 → b 保留。
            probe: candidate => candidate.Count == 0 ? AnalysisStatus.Reachable : AnalysisStatus.Unreachable);

        result.Members.Should().ContainSingle("a was proven unnecessary and b was proven necessary");
        result.Members[0].Should().Be(b);
        result.Minimality.Should().Be(ConflictMinimality.Irreducible,
            "every remaining member has its own proven target-fixed deletion check");
    }

    /// <summary>**背景阻塞不能报 Irreducible** / Background blocking is never irreducible.</summary>
    /// <remarks>
    /// 约束投影为空时，阻塞来自布尔/稀疏值域这类固定背景。**这种结果有用，
    /// 但不是约束级 MUS。**
    ///
    /// With an empty constraint projection the blocking comes from fixed background such as Boolean or sparse domains. **That is useful, but it is not a constraint-level MUS.**
    /// </remarks>
    [Fact]
    public void TheBackgroundBlocking_ShouldNotClaimIrreducibility() {
        var bound = new DiagnosticSource.VariableLowerBound(new VariableId("x"));

        ConflictDeletionCheck.ShrinkResult result = ConflictDeletionCheck.Shrink(
            [bound], AnalysisStatus.Unreachable, maxDeletionChecks: 4, timeBudget: null,
            elapsed: TimeSpan.Zero,
            // 删掉它仍不可达 → 移除 → 约束投影为空 → 背景阻塞。
            probe: _ => AnalysisStatus.Unreachable);

        result.Members.Should().BeEmpty("the only source was proven unnecessary");
        result.BackgroundBlocking.Should().BeTrue();
        result.Minimality.Should().Be(ConflictMinimality.Partial,
            "an empty constraint projection is background blocking, never a constraint-level MUS");
    }

    /// <summary>**预算耗尽立刻停手** / An exhausted budget stops immediately.</summary>
    [Fact]
    public void TheExhaustedBudget_ShouldStopWithoutRemoving() {
        DiagnosticSource a = Source("a");
        DiagnosticSource b = Source("b");
        int calls = 0;

        ConflictDeletionCheck.ShrinkResult result = ConflictDeletionCheck.Shrink(
            [a, b], AnalysisStatus.Unreachable, maxDeletionChecks: 4,
            timeBudget: TimeSpan.FromSeconds(1), elapsed: TimeSpan.FromSeconds(2),
            probe: _ => { calls++; return AnalysisStatus.Unreachable; });

        calls.Should().Be(0, "the budget was already spent, so no probe should run");
        result.Members.Should().HaveCount(2);
        result.Partial.Should().BeTrue();
        result.Minimality.Should().Be(ConflictMinimality.Partial);
    }

    /// <summary>**上限为 0 时第一次就停** / A cap of zero stops before the first check.</summary>
    /// <remarks>
    /// **`checks >= max` 而不是 `checks > max`。** 用后者会**多做一次复验**，
    /// **而每一次复验都是一次完整求解**——多出来的那一次正好是最贵的那一次。
    ///
    /// **`checks >= max`, not `checks > max`.** The latter **runs one extra check**, **and every check is a full solve** — the extra one is exactly the expensive one.
    /// </remarks>
    [Fact]
    public void TheZeroCap_ShouldStopBeforeTheFirstCheck() {
        ConflictDeletionCheck.ShouldStop(checksDone: 0, maxDeletionChecks: 0, TimeSpan.Zero, budget: null)
            .Should().BeTrue("zero is a request to check nothing, not to check once");
    }

    /// <summary>**恰好做满上限次就停** / It stops after exactly the cap.</summary>
    /// <remarks>
    /// **两侧都要**：`max - 1` 次时不能停（否则做少了），`max` 次时必须停（否则做多了）。
    ///
    /// **Both sides**: at `max - 1` it must not stop (it would under-check), at `max` it must (it would over-check).
    /// </remarks>
    [Fact]
    public void TheCap_ShouldBeExactNotApproximate() {
        ConflictDeletionCheck.ShouldStop(checksDone: 9, maxDeletionChecks: 10, TimeSpan.Zero, budget: null)
            .Should().BeFalse("nine of ten is not done");

        ConflictDeletionCheck.ShouldStop(checksDone: 10, maxDeletionChecks: 10, TimeSpan.Zero, budget: null)
            .Should().BeTrue("ten of ten is done, and an eleventh check is one solve too many");
    }

    /// <summary>**时间预算耗尽也停** / An exhausted budget stops too.</summary>
    /// <remarks>
    /// **反向对照**：只测次数上限会让"忽略预算"通过，**而预算是唯一能挡住无上限配置的东西。**
    ///
    /// **The reverse control**: testing only the count lets "ignore the budget" pass, **and the budget is the only thing that bounds an unbounded configuration.**
    /// </remarks>
    [Fact]
    public void TheExhaustedBudget_ShouldStopEvenUnderALargeCap() {
        ConflictDeletionCheck.ShouldStop(
                1, int.MaxValue, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))
            .Should().BeTrue("a five second elapsed time past a one second budget is a stop");

        ConflictDeletionCheck.ShouldStop(
                1, int.MaxValue, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1))
            .Should().BeFalse("a millisecond into a one second budget is not a stop");
    }

    /// <summary>**`Shrink` 真的走 `ShouldStop`** / Shrink actually goes through ShouldStop.</summary>
    /// <remarks>
    /// **这条挡的是"两份中止条件各自漂移"。** 此前 `Shrink` 里有一份内联的副本，
    /// 与 `ShouldStop` 逐字相同——**而两份相同的代码只要改一份，就再也不相同。**
    ///
    /// **This guards against two stop conditions drifting apart.** `Shrink` used to hold an inline copy identical to `ShouldStop` — **and two identical copies stop being identical as soon as one is
    /// edited.**
    /// </remarks>
    [Fact]
    public void TheShrink_ShouldHonourAZeroCap() {
        DiagnosticSource[] sources = new[] {
            new DiagnosticSource.Constraint(new ConstraintId("constraint:a")),
            new DiagnosticSource.Constraint(new ConstraintId("constraint:b")),
        };
        int probes = 0;

        ConflictDeletionCheck.ShrinkResult result = ConflictDeletionCheck.Shrink(
            evidence: sources,
            initialStatus: AnalysisStatus.Unreachable,
            maxDeletionChecks: 0,
            timeBudget: null,
            elapsed: TimeSpan.Zero,
            probe: _ => { ++probes; return AnalysisStatus.Unreachable; });

        probes.Should().Be(0, "a zero cap means probe nothing at all");
        result.Checks.Should().Be(0);
        result.Partial.Should().BeTrue("asking for no checks leaves the answer partial, not proven");
        result.Minimality.Should().Be(ConflictMinimality.NotChecked);
    }
}
