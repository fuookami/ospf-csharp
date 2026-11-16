// 组合求解报告支撑的判据 / Criteria for the combinatorial solve report support.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **组合求解报告支撑** / The combinatorial solve report support.
/// </summary>
/// <remarks>
/// 判据的重点：**"有 incumbent"需要两个条件**、**终止错误只映射三种状态**、
/// **`Unknown` 不算终局**、**空报告不许伪装成成功**。
///
/// The criteria focus on **"has an incumbent" needing both conditions**, on **the stop error mapping covering only three statuses**, on **`Unknown` not counting as terminal**, and on **an
/// empty report never masquerading as success**.
/// </remarks>
public class CombinatorialSolveSupportTests {
    private static SolveReport<Flt64> Report(
        ProblemStatus status = ProblemStatus.Feasible,
        TerminationReason reason = TerminationReason.Completed,
        SolutionPresence presence = SolutionPresence.Optimal,
        bool withSolution = true,
        IReadOnlyList<SolveIssue>? errors = null) =>
        new() {
            ProblemStatus = status,
            TerminationReason = reason,
            SolutionPresence = presence,
            Solution = withSolution
                ? new SolveSolution<Flt64>(new Solution<Flt64>(new[] { new Flt64(1.0) }))
                : null,
            Diagnostics = new SolveDiagnostics<Flt64>(Errors: errors),
        };

    private static SolveAttemptTrace<Flt64> Attempt(string id, SolveReport<Flt64>? report) =>
        new(new SolveAttemptId(id), id, Report: report);

    /// <summary>
    /// **有 incumbent 需要"有解对象"且"状态不是 None"** / An incumbent needs both a solution object and a non-`None` presence.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 有解对象 + `Optimal` → **有**。
    /// 2. 有解对象但 `Presence` 是 `None` → **没有**——解对象可能只是个占位，不能当 incumbent。
    /// 3. 没有解对象 → 没有。
    ///
    /// Three paired points: a solution object with `Optimal` **has one**; a solution object whose presence is `None` **does not**, since the object may be a placeholder; and no solution object
    /// does not either.
    /// </remarks>
    [Fact]
    public void HasCombinatorialIncumbent_ShouldNeedBothConditions() {
        Report().HasCombinatorialIncumbent().Should().BeTrue();
        Report(presence: SolutionPresence.None).HasCombinatorialIncumbent()
            .Should().BeFalse("a placeholder solution object is not an incumbent");
        Report(withSolution: false).HasCombinatorialIncumbent().Should().BeFalse();
    }

    /// <summary>
    /// **终止错误只映射三种状态，其余返回 null** / The stop error maps only three statuses, returning null for the rest.
    /// </summary>
    /// <remarks>
    /// 四点成对：不可行 → `ORModelInfeasible`；无界 → `ORModelUnbounded`；两者皆是 → `ORModelInfeasibleOrUnbounded`；
    /// 可行 → **null**（不是"随便给一个错误码"）。
    ///
    /// Four paired points: infeasible maps to `ORModelInfeasible`, unbounded to `ORModelUnbounded`, either to `ORModelInfeasibleOrUnbounded`, and feasible to **null** rather than some
    /// convenient error code.
    /// </remarks>
    [Fact]
    public void CombinatorialStopErrorCode_ShouldMapOnlyTerminalStatuses() {
        Report(status: ProblemStatus.Infeasible).CombinatorialStopErrorCode()
            .Should().Be(ErrorCode.ORModelInfeasible);
        Report(status: ProblemStatus.Unbounded).CombinatorialStopErrorCode()
            .Should().Be(ErrorCode.ORModelUnbounded);
        Report(status: ProblemStatus.InfeasibleOrUnbounded).CombinatorialStopErrorCode()
            .Should().Be(ErrorCode.ORModelInfeasibleOrUnbounded);
        Report(status: ProblemStatus.Feasible).CombinatorialStopErrorCode().Should().BeNull();
    }

    /// <summary>
    /// **`Unknown` 不算终局，取消算** / `Unknown` is not terminal, cancellation is.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 第一次 `Unknown`、第二次已取消 → **返回第二次**。
    /// 2. 全部 `Unknown` → `null`。
    /// 3. 没有报告的尝试被跳过，不抛。
    ///
    /// Three paired points: an `Unknown` first attempt followed by a cancelled one **returns the second**; all-`Unknown` yields `null`; and attempts without reports are skipped rather than throwing.
    /// </remarks>
    [Fact]
    public void KnownTerminalAttempt_ShouldSkipUnknownStatus() {
        var attempts = new List<SolveAttemptTrace<Flt64>> {
            Attempt("a1", Report(status: ProblemStatus.Unknown, presence: SolutionPresence.None)),
            Attempt("a2", Report(reason: TerminationReason.Cancelled, presence: SolutionPresence.None)),
        };
        attempts.KnownTerminalAttempt()!.AttemptId.Value.Should().Be("a2",
            "Unknown means the backend produced no report, so it cannot be the terminal attempt");

        var allUnknown = new List<SolveAttemptTrace<Flt64>> {
            Attempt("a1", Report(status: ProblemStatus.Unknown, presence: SolutionPresence.None)),
            Attempt("a2", null),
        };
        allUnknown.KnownTerminalAttempt().Should().BeNull();
    }

    /// <summary>
    /// **空报告不许伪装成成功，且错误先取终局再补尝试** / An empty report must not pass as success, and errors come from the terminal report before the attempts.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 没有终局报告、尝试里也没有错误 → **补一条 `combinatorial-no-successful-attempt`**，状态是 `Unknown` 且终止原因是 `BackendFailure`。
    /// 2. 没有终局报告但尝试有错误 → **带上尝试的错误**，不再补那条。
    /// 3. 有终局报告 → 它的诊断错误**原样保留**，尝试的尝试记录与选择信息一并带出。
    /// 4. 有终局报告且两处都无错误 → 诊断里**没有错误**，不是补一条。
    ///
    /// Four paired points: with no terminal report and no attempt errors, **a `combinatorial-no-successful-attempt` issue is added** under `Unknown` status and `BackendFailure` reason; with no
    /// terminal report but attempt errors, **those errors are carried** and the issue is not added; with a terminal report, **its diagnostic errors survive verbatim** alongside the attempts and
    /// selection data; and with a terminal report and no errors anywhere, the diagnostics **stay empty** rather than gaining a synthetic issue.
    /// </remarks>
    [Fact]
    public void ToTerminalReport_ShouldNotFakeSuccess() {
        var empty = new CombinatorialSolveReport<Flt64>(
            FinalReport: null,
            Attempts: new List<SolveAttemptTrace<Flt64>> { Attempt("a1", null) },
            SelectedAttemptId: null,
            SelectionReason: SolveSelectionReason.NoSuccessfulAttempt);
        SolveReport<Flt64> terminal = empty.ToTerminalReport();
        terminal.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        terminal.TerminationReason.Should().Be(TerminationReason.BackendFailure);
        terminal.Diagnostics.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("combinatorial-no-successful-attempt");

        var issue = new SolveIssue("attempt-boom", SolveIssueCategory.Backend, "boom");
        var withAttemptErrors = new CombinatorialSolveReport<Flt64>(
            FinalReport: null,
            Attempts: new List<SolveAttemptTrace<Flt64>> {
                new(new SolveAttemptId("a1"), "a1", Report: null, Errors: new[] { issue }),
            },
            SelectedAttemptId: null,
            SelectionReason: SolveSelectionReason.NoSuccessfulAttempt);
        withAttemptErrors.ToTerminalReport().Diagnostics.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("attempt-boom", "attempt errors are carried instead of a synthetic issue");

        var finalIssue = new SolveIssue("final-warn", SolveIssueCategory.Environment, "warn");
        var healthy = new CombinatorialSolveReport<Flt64>(
            FinalReport: Report(errors: new[] { finalIssue }),
            Attempts: new List<SolveAttemptTrace<Flt64>> { Attempt("a1", null) },
            SelectedAttemptId: new SolveAttemptId("a1"),
            SelectionReason: SolveSelectionReason.NoSuccessfulAttempt);
        SolveReport<Flt64> collapsed = healthy.ToTerminalReport();
        collapsed.Diagnostics.Errors.Should().ContainSingle().Which.Code.Should().Be("final-warn");
        collapsed.Attempts.Should().HaveCount(1, "the attempts are carried along");
        collapsed.SelectedAttemptId!.Value.Value.Should().Be("a1", "the selection is carried along");
        collapsed.SelectionReason.Should().Be(SolveSelectionReason.NoSuccessfulAttempt);

        var clean = new CombinatorialSolveReport<Flt64>(
            FinalReport: Report(),
            Attempts: Array.Empty<SolveAttemptTrace<Flt64>>(),
            SelectedAttemptId: null,
            SelectionReason: SolveSelectionReason.NoSuccessfulAttempt);
        clean.ToTerminalReport().Diagnostics.Errors.Should().BeEmpty(
            "a healthy terminal report gains no synthetic issue");
    }
}
