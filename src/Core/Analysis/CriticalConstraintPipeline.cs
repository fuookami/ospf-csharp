#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 关键约束分析的五个阶段 / The five stages of the critical-constraint analysis.
/// </summary>
/// <remarks>
/// **每个阶段都以委托注入**，于是编排顺序与"什么时候停"可以脱离后端钉死。
///
/// **Every stage is injected as a delegate**, so the orchestration order and the stopping rules can be pinned down without a backend.
/// </remarks>
public sealed class CriticalPipelineBackends
{
    /// <summary>阶段 1：活动性分析 / Stage 1: activity analysis.</summary>
    public required Func<CancellationToken, Task<Result<ConstraintActivityReport, ErrorCode, Error<ErrorCode>>>> Activity { get; init; }

    /// <summary>阶段 2：固定整数 LP 局部敏感度 / Stage 2: fixed-integer LP local sensitivity.</summary>
    public Func<CancellationToken, Task<Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>>>? LocalSensitivity { get; init; }

    /// <summary>阶段 4：对一条候选做扰动分析 / Stage 4: perturb one candidate.</summary>
    public Func<ConstraintCandidate, CancellationToken, Task<Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>>>>? Perturbation { get; init; }

    /// <summary>阶段 5：目标可行性 / Stage 5: target feasibility.</summary>
    public Func<CancellationToken, Task<Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>>>>? Target { get; init; }

    /// <summary>阶段 5 之后：冲突分析 / After stage 5: conflict analysis.</summary>
    public Func<CancellationToken, Task<Result<ConflictExplanation, ErrorCode, Error<ErrorCode>>>>? Conflict { get; init; }
}

/// <summary>
/// 关键约束分析的编排 / The orchestration of the critical-constraint analysis.
/// </summary>
/// <remarks>
/// 移植自 `CriticalConstraintAnalysisPipeline.analyze` 的五阶段串联。**每个阶段的实际求解由注入的委托完成。**
///
/// Ported from the five-stage chain of `CriticalConstraintAnalysisPipeline.analyze`. **The work of each stage is done by the injected delegates.**
///
/// **三条编排规则，每条都有个"顺手写会错"的地方：**
///
/// | 规则 | 顺手写会怎样 |
/// | --- | --- |
/// | 阶段 1 失败就整批失败 | **吞掉错误继续跑，报告带着一份空的活动性结果出去** |
/// | **扰动只跑短名单，短名单为空但有候选要记原因** | 静默跳过，读者以为"没有值得扰动的约束" |
/// | **冲突只在目标不可达时才跑** | 目标可达还跑冲突，**报告会带一份"冲突"解释，而目标明明没问题** |
///
/// **Three orchestration rules, each with a way to get it wrong by accident**: swallowing a stage-1 error and carrying on with an empty activity result; silently skipping an empty
/// shortlist that had candidates, which reads as "nothing worth perturbing"; and running conflict analysis on a reachable target, **which hands back a "conflict" explanation for a
/// target that is fine.**
/// </remarks>
public static class CriticalConstraintPipeline
{
    /// <summary>
    /// 跑五个阶段并汇总一份报告 / Run the five stages and aggregate a report.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="objectiveId">目标标识 / The objective identity.</param>
    /// <param name="baselineObjective">基线目标值 / The baseline objective value.</param>
    /// <param name="baselineProvenOptimal">基线是否已证明最优 / Whether the baseline was proven optimal.</param>
    /// <param name="target">目标条件，可为 <c>null</c> / The target, or <c>null</c>.</param>
    /// <param name="options">分析选项 / The analysis options.</param>
    /// <param name="backends">注入的阶段实现 / The injected stage implementations.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>报告或结构化错误 / The report, or a structured error.</returns>
    /// <remarks>
    /// **转发不是"忽略"**：任何阶段的错误都原样返回，**因为没有哪个阶段的结果可以被默默换成空值。**
    ///
    /// **Forwarding is not ignoring**: any stage error is returned as it came, **because no stage's result may be silently replaced by an empty value.**
    /// </remarks>
    public static async Task<Result<CriticalConstraintAnalysisReport, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot,
        string objectiveId,
        Math.Algebra.Number.Flt64? baselineObjective,
        bool baselineProvenOptimal,
        ObjectiveTarget? target,
        CriticalConstraintAnalysisOptions? options,
        CriticalPipelineBackends backends,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(backends);

        CriticalConstraintAnalysisOptions settings = options ?? new CriticalConstraintAnalysisOptions();

        CriticalConstraintAnalysisReportBuilder builder = new CriticalConstraintAnalysisReportBuilder()
            .Baseline(new AnalysisBaselineSummary(
                ObjectiveId: objectiveId,
                Sense: snapshot.ObjectCategory.ToString(),
                ObjectiveValue: baselineObjective,
                Status: baselineProvenOptimal ? AnalysisStatus.Reachable : AnalysisStatus.Unknown,
                ProvenOptimal: baselineProvenOptimal));

        // 阶段 1：活动性。失败就整批失败——**没有活动性报告就没有漏斗，没有漏斗就没有短名单。**
        // Stage 1: activity. A failure fails the batch: **no activity means no funnel, and no funnel means no shortlist.**
        Result<ConstraintActivityReport, ErrorCode, Error<ErrorCode>> activity =
            await backends.Activity(cancellationToken).ConfigureAwait(false);
        if (activity is not Ok<ConstraintActivityReport, ErrorCode, Error<ErrorCode>> okActivity) {
            return Forward<CriticalConstraintAnalysisReport, ConstraintActivityReport>(activity);
        }
        builder.Activity(okActivity.Value!);

        // 阶段 2：局部敏感度。没后端就记原因——**"没提供"是能力缺失，不是配置选择。**
        // Stage 2: local sensitivity. No backend means note the reason: **"not supplied" is a capability gap, not a configuration choice.**
        FixedIntegerLpSensitivityReport? sensitivity = null;
        StageDecision sensitivityDecision = CriticalPipelineStages.LocalSensitivity(
            enabled: true, backendAvailable: backends.LocalSensitivity is not null);
        if (!sensitivityDecision.Runs) {
            builder.NoteUnavailable(sensitivityDecision.Reason!);
        }
        else {
            Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result =
                await backends.LocalSensitivity!(cancellationToken).ConfigureAwait(false);
            if (result is not Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> okSensitivity) {
                return Forward<CriticalConstraintAnalysisReport, FixedIntegerLpSensitivityReport>(result);
            }
            sensitivity = okSensitivity.Value!;
            builder.LocalSensitivity(sensitivity);
        }

        // 阶段 3：漏斗。**它在这里算一次，扰动的短名单从它来**——再算一次就可能与报告里的那份不一致。
        // Stage 3: the funnel. **It is computed once here and the shortlist comes from it**; computing it again could disagree with the one in the report.
        Result<CandidateFunnelRanking, ErrorCode, Error<ErrorCode>> funnel =
            CriticalCandidateFunnel.BuildCandidateFunnel(okActivity.Value!, sensitivity, settings.Funnel);
        if (funnel is not Ok<CandidateFunnelRanking, ErrorCode, Error<ErrorCode>> okFunnel) {
            return Forward<CriticalConstraintAnalysisReport, CandidateFunnelRanking>(funnel);
        }
        CandidateFunnelRanking funnelRanking = okFunnel.Value!;

        // 阶段 4：扰动，只跑短名单。
        // Stage 4: perturbation, shortlist only.
        StageDecision perturbationDecision = CriticalPipelineStages.Perturbation(
            enabled: settings.RunPerturbation,
            backendAvailable: backends.Perturbation is not null,
            candidateCount: funnelRanking.Candidates.Count,
            candidateLimit: settings.CandidateLimit);
        var perturbationReports = new List<ConstraintPerturbationReport>();
        if (!perturbationDecision.Runs) {
            builder.NoteUnavailable(perturbationDecision.Reason!);
        }
        else {
            foreach (ConstraintCandidate candidate in funnelRanking.Candidates.Take(settings.CandidateLimit)) {
                Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>> result =
                    await backends.Perturbation!(candidate, cancellationToken).ConfigureAwait(false);
                if (result is not Ok<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>> okPerturbation) {
                    return Forward<CriticalConstraintAnalysisReport, ConstraintPerturbationReport>(result);
                }
                perturbationReports.Add(okPerturbation.Value!);
            }
            builder.Perturbation(perturbationReports);
        }

        // 阶段 5：目标可行性与冲突。
        // Stage 5: target feasibility and conflict.
        StageDecision targetDecision = CriticalPipelineStages.Target(
            enabled: settings.RunTargetAnalysis, targetSupplied: target is not null);
        TargetFeasibilityReport? targetReport = null;
        if (!targetDecision.Runs) {
            builder.NoteUnavailable(targetDecision.Reason!);
        }
        else {
            Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> result =
                await backends.Target!(cancellationToken).ConfigureAwait(false);
            if (result is not Ok<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> okTarget) {
                return Forward<CriticalConstraintAnalysisReport, TargetFeasibilityReport>(result);
            }
            targetReport = okTarget.Value!;
            builder.Target(targetReport);

            // **冲突只在目标不可达时才跑。** 目标可达还跑冲突，**报告会带一份"冲突"解释，而目标明明没问题。**
            // **Conflict analysis runs only when the target is unreachable.** Otherwise **the report carries a "conflict" explanation for a target that is fine.**
            if (targetReport.Status == AnalysisStatus.Unreachable) {
                if (backends.Conflict is null) {
                    builder.NoteUnavailable(CriticalPipelineStages.ConflictBackendMissing);
                }
                else {
                    Result<ConflictExplanation, ErrorCode, Error<ErrorCode>> conflict =
                        await backends.Conflict(cancellationToken).ConfigureAwait(false);
                    if (conflict is not Ok<ConflictExplanation, ErrorCode, Error<ErrorCode>> okConflict) {
                        return Forward<CriticalConstraintAnalysisReport, ConflictExplanation>(conflict);
                    }
                    builder.Conflict(okConflict.Value!);
                }
            }
        }

        return builder.Build(settings.Funnel);
    }

    /// <summary>
    /// 阶段错误原样包装成整体错误 / Repackage a stage error as a whole-result error.
    /// </summary>
    /// <typeparam name="TReport">报告类型 / The report type.</typeparam>
    /// <typeparam name="TStage">阶段结果类型 / The stage result type.</typeparam>
    /// <param name="result">阶段结果 / The stage result.</param>
    /// <returns>失败的整体结果 / The failed whole result.</returns>
    /// <remarks>
    /// **保留阶段错误，不换成一句泛泛的"某阶段失败"**——原因在阶段的错误里。
    ///
    /// **The stage error is kept rather than replaced with a generic "a stage failed"**: the reason lives in that error.
    /// </remarks>
    private static Result<TReport, ErrorCode, Error<ErrorCode>> Forward<TReport, TStage>(
        Result<TStage, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<TStage, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<TReport>(failed.Error)
            : throw new InvalidOperationException("只有失败结果会被转发 / only a failed result is forwarded");
}
