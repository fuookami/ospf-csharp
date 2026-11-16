#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 关键约束分析报告的累积器 / The accumulator for a critical-constraint analysis report.
/// </summary>
/// <remarks>
/// **七个阶段各自可选，`Build` 只负责汇总。**
///
/// **Each of the seven stages is optional; `Build` only aggregates.**
/// </remarks>
public sealed class CriticalConstraintAnalysisReportBuilder
{
    private AnalysisBaselineSummary? _baseline;
    private ConstraintActivityReport? _activity;
    private FixedIntegerLpSensitivityReport? _localSensitivity;
    private IReadOnlyList<ConstraintPerturbationReport> _perturbation = [];
    private TargetFeasibilityReport? _target;
    private ConflictExplanation? _conflict;
    private readonly List<string> _unavailableReasons = [];
    private bool _hasUnsupportedStage;

    /// <summary>记下基线摘要 / Record the baseline summary.</summary>
    /// <param name="value">基线摘要 / The baseline summary.</param>
    /// <returns>自身，便于串联 / This builder, for chaining.</returns>
    public CriticalConstraintAnalysisReportBuilder Baseline(AnalysisBaselineSummary value) {
        ArgumentNullException.ThrowIfNull(value);
        _baseline = value;
        return this;
    }

    /// <summary>记下活动性报告 / Record the activity report.</summary>
    /// <param name="value">活动性报告 / The activity report.</param>
    /// <returns>自身 / This builder.</returns>
    public CriticalConstraintAnalysisReportBuilder Activity(ConstraintActivityReport value) {
        ArgumentNullException.ThrowIfNull(value);
        _activity = value;
        return this;
    }

    /// <summary>记下局部敏感度 / Record local sensitivity.</summary>
    /// <param name="value">敏感度报告 / The sensitivity report.</param>
    /// <returns>自身 / This builder.</returns>
    public CriticalConstraintAnalysisReportBuilder LocalSensitivity(FixedIntegerLpSensitivityReport value) {
        ArgumentNullException.ThrowIfNull(value);
        _localSensitivity = value;
        return this;
    }

    /// <summary>记下扰动报告 / Record the perturbation reports.</summary>
    /// <param name="value">扰动报告 / The perturbation reports.</param>
    /// <returns>自身 / This builder.</returns>
    public CriticalConstraintAnalysisReportBuilder Perturbation(IReadOnlyList<ConstraintPerturbationReport> value) {
        ArgumentNullException.ThrowIfNull(value);
        _perturbation = value;
        return this;
    }

    /// <summary>记下目标可行性 / Record target feasibility.</summary>
    /// <param name="value">可行性报告 / The feasibility report.</param>
    /// <returns>自身 / This builder.</returns>
    public CriticalConstraintAnalysisReportBuilder Target(TargetFeasibilityReport value) {
        ArgumentNullException.ThrowIfNull(value);
        _target = value;
        return this;
    }

    /// <summary>记下冲突解释 / Record the conflict explanation.</summary>
    /// <param name="value">冲突解释 / The conflict explanation.</param>
    /// <returns>自身 / This builder.</returns>
    public CriticalConstraintAnalysisReportBuilder Conflict(ConflictExplanation value) {
        ArgumentNullException.ThrowIfNull(value);
        _conflict = value;
        return this;
    }

    /// <summary>
    /// 记下一个未执行的阶段 / Record one stage that did not run.
    /// </summary>
    /// <param name="reason">原因 / The reason.</param>
    /// <returns>自身 / This builder.</returns>
    /// <remarks>
    /// **"没跑"要同时做两件事**：记下原因，并把整体状态压到 `Unsupported`。
    ///
    /// 只记原因不改状态，一份"六个阶段没跑"的报告会带着 `Reachable` 的状态出去——
    /// **而 `Reachable` 的意思是"目标可达，一切正常"。**
    ///
    /// **"Did not run" has to do two things**: record the reason and push the overall status to `Unsupported`. Recording only the reason would let a report with six skipped stages
    /// go out carrying `Reachable` — **and `Reachable` means "the target is reachable, everything is fine".**
    /// </remarks>
    public CriticalConstraintAnalysisReportBuilder NoteUnavailable(string reason) {
        ArgumentNullException.ThrowIfNull(reason);
        _unavailableReasons.Add(reason);
        _hasUnsupportedStage = true;
        return this;
    }

    /// <summary>
    /// 汇总出一份报告 / Aggregate into a report.
    /// </summary>
    /// <param name="config">候选漏斗配置 / The candidate-funnel configuration.</param>
    /// <returns>报告或结构化错误 / The report, or a structured error.</returns>
    /// <remarks>
    /// **三种失败都返回错误，不返回"尽力而为"的报告：**
    ///
    /// | 缺什么 | 为什么不能凑合 |
    /// | --- | --- |
    /// | 基线摘要 | 一份不知道什么的分析报告 |
    /// | 活动性报告 | 候选漏斗无从算起 |
    /// | **有扰动但基线目标值缺失** | **有效性排序拿不到比较基准** |
    ///
    /// **第三条最容易写漏**：扰动报告为空时不需要基线值，**非空时才需要**——
    /// 一个无条件要求基线值的实现，会让"没跑扰动"的正常报告也失败。
    ///
    /// **Three failures return an error rather than a best-effort report**: no baseline, no activity report, and **perturbation without a baseline objective value**. **The third is the
    /// easy omission**: an empty perturbation list needs no baseline value and a non-empty one does, so demanding it unconditionally would fail the perfectly normal report that
    /// skipped perturbation.**
    /// </remarks>
    public Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>> Build(
        CandidateFunnelConfig? config = null) {
        if (_baseline is null) {
            return Results.Failed<CriticalConstraintAnalysisReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "临界约束分析需要基线摘要 / Critical constraint analysis requires a baseline summary"));
        }
        if (_activity is null) {
            return Results.Failed<CriticalConstraintAnalysisReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "临界约束分析需要活动性报告 / Critical constraint analysis requires an activity report"));
        }

        AnalysisBaselineSummary baseline = _baseline;
        ConstraintActivityReport activity = _activity;

        Result<CandidateFunnelRanking, ErrorCode, Error<ErrorCode>> funnelResult =
            CriticalCandidateFunnel.BuildCandidateFunnel(activity, _localSensitivity, config);
        if (funnelResult is not Ok<CandidateFunnelRanking, ErrorCode, Error<ErrorCode>> okFunnel) {
            return Results.Failed<CriticalConstraintAnalysisReport>(
                funnelResult is Failed<CandidateFunnelRanking, ErrorCode, Error<ErrorCode>> failed
                    ? failed.Error
                    : new Err<ErrorCode>(ErrorCode.Other, "候选漏斗构建失败 / Candidate funnel construction failed"));
        }
        CandidateFunnelRanking funnel = okFunnel.Value!;

        EffectivenessRanking? effectiveness = null;
        if (_perturbation.Count > 0) {
            if (baseline.ObjectiveValue is null) {
                return Results.Failed<CriticalConstraintAnalysisReport>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "有效性排序需要已证明的基线目标值 / Effectiveness ranking requires a proven baseline objective value"));
            }
            Result<EffectivenessRanking, ErrorCode, Error<ErrorCode>> ranking =
                CriticalAnalysisReports.BuildEffectivenessRanking(baseline.ObjectiveValue.Value, funnel, _perturbation);
            if (ranking is not Ok<EffectivenessRanking, ErrorCode, Error<ErrorCode>> okRanking) {
                return Results.Failed<CriticalConstraintAnalysisReport>(
                    ranking is Failed<EffectivenessRanking, ErrorCode, Error<ErrorCode>> failedRanking
                        ? failedRanking.Error
                        : new Err<ErrorCode>(ErrorCode.Other, "有效性排序构建失败 / Effectiveness ranking construction failed"));
            }
            effectiveness = okRanking.Value;
        }

        IReadOnlyList<AnalysisGroupSummary> groupSummaries =
            CriticalAnalysisReports.SummarizeGroups(activity, effectiveness, _conflict);

        // **总体结论取最弱环节。** 一个阶段没跑出结论，整份报告就不能说"目标可达"。
        // **The overall status is the weakest link.** One inconclusive stage stops the report from saying "reachable".
        AnalysisStatus status = baseline.ProvenOptimal ? AnalysisStatus.Reachable : AnalysisStatus.Unknown;
        if (_hasUnsupportedStage) {
            status = CriticalConstraintAnalysisReport.CombineStatus(status, AnalysisStatus.Unsupported);
        }
        status = CriticalConstraintAnalysisReport.CombineStatus(status, _target?.Status);
        status = CriticalConstraintAnalysisReport.CombineStatus(status, _conflict?.Status);
        foreach (ConstraintPerturbationReport entry in _perturbation) {
            status = CriticalConstraintAnalysisReport.CombineStatus(status, entry.Status);
        }
        if (_localSensitivity is not null) {
            status = CriticalConstraintAnalysisReport.CombineStatus(status, _localSensitivity.Status);
        }

        var reasons = new List<string>(_unavailableReasons);
        if (funnel.Unclassified > 0) {
            reasons.Add($"有 {funnel.Unclassified} 条候选因缺少对偶证据而无法分层 / "
                + $"{funnel.Unclassified} candidate(s) could not be tiered because dual evidence was unavailable");
        }

        var report = new CriticalConstraintAnalysisReport(
            Baseline: baseline,
            Activity: activity,
            LocalSensitivity: _localSensitivity,
            Funnel: funnel,
            Effectiveness: effectiveness,
            Target: _target,
            Conflict: _conflict,
            GroupSummaries: groupSummaries,
            Status: status,
            UnavailableReasons: reasons);

        // **最后一道自洽校验**：报告自己说不过去就不交出去。
        // **The final self-consistency check**: a report that does not add up is not handed out.
        IReadOnlyList<string> problems = report.Validate();
        return problems.Count == 0
            ? Results.Ok(report)
            : Results.Failed<CriticalConstraintAnalysisReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, string.Join("; ", problems)));
    }
}
