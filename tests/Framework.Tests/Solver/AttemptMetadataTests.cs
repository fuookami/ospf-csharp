// 尝试元数据补齐的判据 / Criteria for filling in attempt metadata.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **尝试元数据补齐** / Filling in attempt metadata.
/// </summary>
/// <remarks>
/// 判据的重点：**只有报告写了 `Cancelled` 才算取消**、**晚于完成时刻的取消记录不算数**、
/// **报告为 null 但有取消记录时仍记原因**、**耗时由起始时间戳现算**。
///
/// The criteria focus on **only a report saying `Cancelled` counting as a cancellation**, on **a cancellation record later than the completion instant not counting**, on **a null report with a
/// cancellation record still recording a reason**, and on **the elapsed time being computed from the start timestamp**.
/// </remarks>
public class AttemptMetadataTests {
    private static SolveReport<Flt64> Report(TerminationReason reason) => new() {
        ProblemStatus = ProblemStatus.Feasible,
        TerminationReason = reason,
        SolutionPresence = SolutionPresence.Optimal,
        Solution = new SolveSolution<Flt64>(new Solution<Flt64>(new[] { new Flt64(1.0) })),
    };

    private static SolveAttemptTrace<Flt64> Attempt(SolveReport<Flt64>? report) =>
        new(new SolveAttemptId("child"), "backend", Report: report);

    private static readonly SolveAttemptId Parent = new("parent");
    private static readonly DateTimeOffset Completed = DateTimeOffset.FromUnixTimeMilliseconds(1_000);

    /// <summary>
    /// **只有报告写了 `Cancelled` 才算取消；报告为 null 但已知取消也记原因** / Only a `Cancelled` report counts as a cancellation, while a null report with a known cancellation still records a reason.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 报告 `Completed` + 有取消记录 → **不记原因**（取消记录是尝试开始时已知的，不代表这次被取消）。
    /// 2. 报告 `Cancelled` + 有取消记录 → **记原因**。
    /// 3. 报告为 **null** + 有取消记录 → **记原因**（没有报告又要解释"为什么没有"，只能靠这条）。
    /// 4. 报告为 null + 无取消记录 → 不记原因。
    ///
    /// Four paired points: a `Completed` report with a cancellation record **records no reason**, since the record was known when the attempt started and does not mean this attempt was cancelled; a
    /// `Cancelled` report with a record **does**; a **null** report with a record **also does**, the only way to explain why there is no report; and a null report without a record does not.
    /// </remarks>
    [Fact]
    public void WithAttemptMetadata_ShouldOnlyTreatCancelledReportsAndMissingReports() {
        var record = new PortableCancellationRecord("caller", 900, "user asked");

        SolveAttemptTrace<Flt64> completed = Attempt(Report(TerminationReason.Completed))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), record, Parent, null, Completed);
        completed.CancellationReason.Should().BeNull("a completed report is not a cancellation");

        SolveAttemptTrace<Flt64> cancelled = Attempt(Report(TerminationReason.Cancelled))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), record, Parent, null, Completed);
        cancelled.CancellationReason.Should().Be("user asked");

        SolveAttemptTrace<Flt64> missing = Attempt(null)
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), record, Parent, null, Completed);
        missing.CancellationReason.Should().Be("user asked",
            "a missing report plus a known cancellation must explain itself");

        SolveAttemptTrace<Flt64> missingNoRecord = Attempt(null)
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), null, Parent, null, Completed);
        missingNoRecord.CancellationReason.Should().BeNull();
    }

    /// <summary>
    /// **晚于完成时刻的取消记录不算数** / A cancellation record later than the completion instant does not count.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 完成时点的记录**不晚于**完成时刻（同一毫秒）→ **用它**，原因是新记录的原因。
    /// 2. 完成时点的记录**晚于**完成时刻 → **回落到**开始时的那条，因为晚于完成的取消属于**下一次**尝试。
    /// 3. 完成时点**没有**记录 → 回落到开始时那条。
    ///
    /// Three paired points: a completion-time record **not later than** the completion instant (the same millisecond) **is used**, with the new reason; one **later than** it **falls back** to the
    /// start-time record, since a cancellation after completion belongs to the next attempt; and no completion-time record falls back too.
    /// </remarks>
    [Fact]
    public void WithAttemptMetadata_ShouldIgnoreACancellationRequestedAfterCompletion() {
        var start = new PortableCancellationRecord("caller", 800, "earlier");
        var atCompletion = new PortableCancellationRecord("watchdog", 1_000, "same instant");
        var afterCompletion = new PortableCancellationRecord("watchdog", 1_001, "too late");

        SolveAttemptTrace<Flt64> sameInstant = Attempt(Report(TerminationReason.Cancelled))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), start, Parent, atCompletion, Completed);
        sameInstant.CancellationReason.Should().Be("same instant",
            "a record at the completion instant is not later than it");

        SolveAttemptTrace<Flt64> later = Attempt(Report(TerminationReason.Cancelled))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), start, Parent, afterCompletion, Completed);
        later.CancellationReason.Should().Be("earlier",
            "a cancellation after completion belongs to the next attempt");

        SolveAttemptTrace<Flt64> none = Attempt(Report(TerminationReason.Cancelled))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(), start, Parent, null, Completed);
        none.CancellationReason.Should().Be("earlier");

        SolveAttemptTrace<Flt64> noReason = Attempt(Report(TerminationReason.Cancelled))
            .WithAttemptMetadata(Stopwatch.GetTimestamp(),
                new PortableCancellationRecord("caller", 800, null), Parent, null, Completed);
        noReason.CancellationReason.Should().Be("cancellation requested",
            "a record without a reason still needs a default explanation");
    }

    /// <summary>
    /// **父尝试与耗时都真的写回** / Both the parent attempt and the elapsed time are really written back.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `ParentAttemptId` 就是传入的那一个（**值相等**——`SolveAttemptId` 是 `record struct`，不是"某个非空值"）。
    /// 2. `Elapsed` **不为 null** 且**非负**。
    /// 3. 传入**很久以前**的时间戳时，`Elapsed` **大于**先传当前时间戳的那一次——
    /// 这条把"现算"与"读报告"分开：报告里没有耗时，只有现算才有这个差别。
    ///
    /// Three paired points: `ParentAttemptId` **equals** the one passed in (`SolveAttemptId` is a `record struct`, so value equality, not merely non-null); `Elapsed` **is not null** and **non-negative**; and with a timestamp from
    /// **long ago** the elapsed time **exceeds** that of a run started now — which is what separates "computed on the spot" from "read off the report", since the report carries no elapsed time.
    /// </remarks>
    [Fact]
    public void WithAttemptMetadata_ShouldWriteParentAndElapsed() {
        long now = Stopwatch.GetTimestamp();
        SolveAttemptTrace<Flt64> fresh = Attempt(Report(TerminationReason.Completed))
            .WithAttemptMetadata(now, null, Parent, null, Completed);

        fresh.ParentAttemptId.Should().Be(Parent, "the parent is carried through unchanged");
        fresh.Elapsed.Should().NotBeNull();
        fresh.Elapsed!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

        long anHourAgo = now - (long)(Stopwatch.Frequency * 3600.0);
        SolveAttemptTrace<Flt64> stale = Attempt(Report(TerminationReason.Completed))
            .WithAttemptMetadata(anHourAgo, null, Parent, null, Completed);

        stale.Elapsed!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(59.0),
            "the elapsed time is measured from the timestamp, not read off the report");
        stale.Elapsed.Value.Should().BeGreaterThan(fresh.Elapsed.Value);
    }
}
