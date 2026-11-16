#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 组合求解的报告支撑 / Report support for combinatorial solving.
/// </summary>
/// <remarks>
/// 四处沿用：
///
/// 1. **"有没有 incumbent"要两个条件**：有解对象**且** `SolutionPresence` 不是 `None`——只有解对象不算，可能是个占位。
/// 2. **终止错误的映射只覆盖三种状态**：不可行 / 无界 / 两者皆是；其余状态返回 `null` 而不是"随便给一个错误"。
/// 3. **已知终止尝试的判据是"状态非 Unknown 或已取消"**：`Unknown` 意味着后端没形成报告，不能当终局。
/// 4. **没有终局报告时兜底成"后端失败 + 无解 + Unknown"**，并把"所有组合 backend 均未形成报告"记成一条问题——
///    **空报告不许伪装成成功**。
///
/// Aligned with its (`withAttemptMetadata` excluded, see below). Four things copied: **"has an incumbent" needs both conditions**, a solution object
/// **and** a `SolutionPresence` other than `None`, since the object alone may be a placeholder; **the stop-error mapping covers only three statuses** (infeasible, unbounded, either) with `null`
/// for the rest rather than a made-up error; **a known terminal attempt is one whose status is not `Unknown` or that was cancelled**, `Unknown` meaning the backend produced no report and cannot
/// count as terminal; and **a missing terminal report falls back to backend failure with no solution and `Unknown`**, recording "no combinatorial backend produced a report" as an issue —
/// **an empty report must never masquerade as success**.
/// </remarks>
public static class CombinatorialSolveSupport {
    /// <summary>
    /// 补齐尝试的元数据 / Fill in an attempt's metadata.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="trace">尝试 / The attempt.</param>
    /// <param name="startedTimestamp">起始时间戳（`Stopwatch.GetTimestamp`）/ The start timestamp.</param>
    /// <param name="cancellationRecord">尝试开始时已知的取消记录 / The cancellation record known when the attempt started.</param>
    /// <param name="parentAttemptId">父尝试 / The parent attempt.</param>
    /// <param name="cancellationRecordAtCompletion">完成时点的取消记录 / The cancellation record at completion.</param>
    /// <param name="completedAt">完成时刻 / The completion instant.</param>
    /// <returns>补齐后的尝试 / The attempt with its metadata filled in.</returns>
    /// <remarks>
    /// 四处沿用：
    ///
    /// 1. **只有报告明确写了 `Cancelled` 才算取消**：`report == null` 不等于取消，它可能是后端根本没形成报告。
    /// 2. **完成时点的取消记录只在"它不晚于完成时刻"时才算数**：晚于完成时刻的取消是**下一次**尝试的事，
    ///    拿它来解释这一次会让"取消"看起来比实际更早。
    /// 3. **报告为 null 但有取消记录时也记原因**：没有报告又要解释"为什么没有"，只能靠这条。
    /// 4. **耗时由起始时间戳现算**，不从报告里读——报告的耗时可能只覆盖后端内部，不含排队。
    ///
    /// 差异：`TimeSource.Monotonic.ValueTimeMark`，这里用 `Stopwatch.GetTimestamp` 加
    /// `Stopwatch.GetElapsedTime`。**单调时钟的语义相同**（不受系统时间调整影响），只是 C# 没有把"起点"包成类型。
    /// 取消记录用 `PortableCancellationRecord`（epoch 毫秒），因此比较是**毫秒精度**而非 `Instant` 精度——
    /// 同一毫秒内请求并完成的取消会被当作"不晚于完成时刻"，与同一瞬间的取舍一致。
    ///
    /// Four things copied: **only a report that says `Cancelled` counts as a cancellation**, a null report meaning the backend produced no report at all
    /// rather than a cancellation; **the completion-time cancellation record counts only when it is not later than the completion instant**, since a later one belongs to the next attempt and would make
    /// the cancellation look earlier than it was; **a null report with a cancellation record still records a reason**, the only way to explain why there is no report; and **the elapsed time is computed
    /// from the start timestamp** rather than read from the report, which may cover only the backend's internals and not the queueing.
    ///
    /// The difference is `Stopwatch.GetTimestamp` plus `Stopwatch.GetElapsedTime` in place of `TimeSource.Monotonic.ValueTimeMark`. **The monotonic semantics are the same** (immune to
    /// system clock adjustments); C# simply does not wrap the origin in a type. Cancellation records use `PortableCancellationRecord` (epoch milliseconds), so the comparison is **millisecond-precise**
    /// rather than `Instant`-precise: a cancellation requested and completed within the same millisecond counts as "not later than completion", matching choice at that same instant.
    /// </remarks>
    public static SolveAttemptTrace<V> WithAttemptMetadata<V>(
        this SolveAttemptTrace<V> trace,
        long startedTimestamp,
        PortableCancellationRecord? cancellationRecord,
        SolveAttemptId parentAttemptId,
        PortableCancellationRecord? cancellationRecordAtCompletion,
        DateTimeOffset completedAt)
        where V : struct {
        // `SolveAttemptTrace` 是 `record`（引用类型），`SolveAttemptId` 是 `record struct`（值类型）——
        // 后者传 null 在类型上就不可能，对它做 `ThrowIfNull` 只会被分析器判为无效检查。
        // `SolveAttemptTrace` is a reference-type record while `SolveAttemptId` is a value-type record struct, so passing null for the latter is impossible at the type level and a `ThrowIfNull`
        // on it is a no-op the analyser flags.
        ArgumentNullException.ThrowIfNull(trace);

        bool cancelled = trace.Report?.TerminationReason == TerminationReason.Cancelled;
        PortableCancellationRecord? effective = cancelled
            ? (cancellationRecordAtCompletion is { } atCompletion
                && atCompletion.RequestedAtEpochMs <= completedAt.ToUnixTimeMilliseconds()
                    ? atCompletion
                    : cancellationRecord)
            : cancellationRecord;

        string? cancellationReason = cancelled || (trace.Report is null && cancellationRecord is not null)
            ? effective?.Reason ?? "cancellation requested"
            : null;

        return trace with {
            ParentAttemptId = parentAttemptId,
            Elapsed = Stopwatch.GetElapsedTime(startedTimestamp),
            CancellationReason = cancellationReason,
        };
    }

    /// <summary>
    /// 是否有可用的 incumbent / Whether there is a usable incumbent.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="report">求解报告 / The solve report.</param>
    /// <returns>有则 true / True when there is one.</returns>
    public static bool HasCombinatorialIncumbent<V>(this SolveReport<V> report)
        where V : struct =>
        report is not null
        && report.Solution is not null
        && report.SolutionPresence != SolutionPresence.None;

    /// <summary>
    /// 组合求解的终止错误码 / The combinatorial solve's stop error code.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="report">求解报告 / The solve report.</param>
    /// <returns>错误码，非终止状态为 null / The error code, null when the status is not terminal.</returns>
    public static ErrorCode? CombinatorialStopErrorCode<V>(this SolveReport<V> report)
        where V : struct => report?.ProblemStatus switch {
            ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
            ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
            ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
            _ => null,
        };

    /// <summary>
    /// 找出已知终局的那次尝试 / Find the attempt already known to be terminal.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="attempts">尝试列表 / The attempt list.</param>
    /// <returns>终局尝试，没有则 null / The terminal attempt, or null.</returns>
    public static SolveAttemptTrace<V>? KnownTerminalAttempt<V>(this IReadOnlyList<SolveAttemptTrace<V>> attempts)
        where V : struct {
        ArgumentNullException.ThrowIfNull(attempts);
        foreach (SolveAttemptTrace<V> attempt in attempts) {
            if (attempt.Report is null) {
                continue;
            }
            if (attempt.Report.ProblemStatus != ProblemStatus.Unknown
                || attempt.Report.TerminationReason == TerminationReason.Cancelled) {
                return attempt;
            }
        }
        return null;
    }

    /// <summary>
    /// 把组合报告压成单一终局报告 / Collapse a combinatorial report into one terminal report.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="report">组合报告 / The combinatorial report.</param>
    /// <returns>终局报告 / The terminal report.</returns>
    /// <remarks>
    /// 错误汇总的顺序：**先取终局报告的诊断错误，再补各次尝试的错误**——终局那份是权威，尝试的是补充材料。
    /// 两者都没有且**根本没有终局报告**时，才补那条"没有任何 backend 形成报告"的问题。
    ///
    /// The error aggregation order: **the terminal report's diagnostic errors first, then each attempt's errors**, since the terminal one is authoritative and the attempts are supplementary.
    /// Only when neither exists and **there is no terminal report at all** is the "no backend produced a report" issue added.
    /// </remarks>
    public static SolveReport<V> ToTerminalReport<V>(this CombinatorialSolveReport<V> report)
        where V : struct {
        ArgumentNullException.ThrowIfNull(report);

        IReadOnlyList<SolveIssue> attemptErrors = report.Attempts
            .SelectMany(attempt => attempt.Errors ?? Array.Empty<SolveIssue>())
            .ToArray();
        SolveReport<V> fallback = report.FinalReport ?? new SolveReport<V> {
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.BackendFailure,
            SolutionPresence = SolutionPresence.None,
            Diagnostics = new SolveDiagnostics<V>(),
        };
        IReadOnlyList<SolveIssue> errors = (fallback.Diagnostics.Errors ?? Array.Empty<SolveIssue>())
            .Concat(attemptErrors)
            .ToArray();
        IReadOnlyList<SolveIssue> terminalErrors;
        if (errors.Count > 0) {
            terminalErrors = errors;
        } else if (report.FinalReport is null) {
            terminalErrors = new[] {
                new SolveIssue(
                    Code: "combinatorial-no-successful-attempt",
                    Category: SolveIssueCategory.Backend,
                    Message: "所有组合 backend 均未形成报告 / No combinatorial backend produced a report"),
            };
        } else {
            terminalErrors = Array.Empty<SolveIssue>();
        }

        return fallback with {
            Diagnostics = fallback.Diagnostics with { Errors = terminalErrors },
            Attempts = report.Attempts,
            SelectedAttemptId = report.SelectedAttemptId,
            SelectionReason = report.SelectionReason,
        };
    }
}
