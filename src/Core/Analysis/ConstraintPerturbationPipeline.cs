#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 一次扰动分析的编排 / The orchestration of one perturbation analysis.
/// </summary>
/// <remarks>
/// **这是 `ConstraintPerturbationAnalyzer.analyze` 的编排层。** 所有判定都已单独搬过
/// （派生、求解请求、分类、聚合、删除、二分、扫描、前置门），**这一段只把它们的顺序钉死。**
///
/// **This is the orchestration layer of `ConstraintPerturbationAnalyzer.analyze`.** Every verdict was ported on its own — derivation, solve request, classification, aggregation,
/// removal, bisection, sweep, pre-flight gate — **and this only fixes their order.**
///
/// **两条顺序规则，都容易写反：**
///
/// | 规则 | 写反会怎样 |
/// | --- | --- |
/// | **RHS 扫描之后才做二分，且用扫描的结论作为区间端点** | 二分从空区间起步，**退化成一个没有依据的猜测** |
/// | **先判前置门，再花任何求解预算** | 一个做不了的分析**先跑了求解才说做不了** |
///
/// **Two ordering rules, both easy to flip**: the bisection runs after the sweep and takes the sweep's endpoints as its interval (starting from an empty interval, **the bisection
/// degenerates into an unfounded guess**), and the pre-flight gate runs before any solve budget is spent (**otherwise an impossible analysis solves first and refuses afterwards**).
/// </remarks>
public static class ConstraintPerturbationPipeline
{
    /// <summary>
    /// 跑一次完整的扰动分析 / Run one complete perturbation analysis.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="constraintId">候选约束 / The candidate constraint.</param>
    /// <param name="baselineSolution">基线解，可为 <c>null</c> / The baseline solution, or <c>null</c>.</param>
    /// <param name="baselineObjective">基线目标值，可为 <c>null</c> / The baseline objective value, or <c>null</c>.</param>
    /// <param name="capabilityMatrix">能力矩阵 / The capability matrix.</param>
    /// <param name="policy">扰动策略 / The perturbation policy.</param>
    /// <param name="solver">CP 求解器，可为 <c>null</c> / The CP solver, or <c>null</c>.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>报告或结构化错误 / The report, or a structured error.</returns>
    public static async Task<Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintId constraintId,
        ConstraintProgrammingSolution? baselineSolution,
        Flt64? baselineObjective,
        CapabilityMatrix capabilityMatrix,
        ConstraintPerturbationPolicy policy,
        IConstraintProgrammingSolver? solver,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(capabilityMatrix);
        ArgumentNullException.ThrowIfNull(policy);

        // **候选必须真的在模型里**：不在就不是"做不了"，而是调用方给错了 id。
        // **The candidate has to exist in the model**: an absent id is a caller mistake, not an inability.
        if (!snapshot.Constraints.Any(entry => Equals(entry.Id, constraintId))) {
            return Results.Failed<ConstraintPerturbationReport>(new Err<ErrorCode>(
                ErrorCode.DataNotFound, $"扰动候选约束不存在：{constraintId} / Perturbation candidate does not exist: {constraintId}"));
        }
        if (baselineObjective is { } objective
            && (double.IsNaN(objective.ToDouble()) || double.IsInfinity(objective.ToDouble()))) {
            return Results.Failed<ConstraintPerturbationReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "基线目标值必须有限 / Baseline objective must be finite"));
        }

        bool needsRhs = ConstraintPerturbationSupport.RequestedDeltas(policy).Count > 0 || policy.Adaptive is not null;
        bool rhsAllowed = !needsRhs
            || ConstraintPerturbationSupport.CapabilityAllowed(capabilityMatrix, AnalysisCapability.RhsPerturbation);
        bool removalAllowed = !policy.RemovalTest
            || ConstraintPerturbationSupport.CapabilityAllowed(capabilityMatrix, AnalysisCapability.RemovalTest);

        // **前置门在任何求解之前。** 一个做不了的分析不该先花预算再说做不了。
        // **The pre-flight gate comes before any solve.** An impossible analysis must not spend budget and refuse afterwards.
        ConstraintPerturbationSupport.PreflightVerdict verdict = ConstraintPerturbationSupport.Preflight(
            backendAvailable: solver is not null,
            needsRhsPerturbation: needsRhs,
            rhsAllowed: rhsAllowed,
            removalRequested: policy.RemovalTest,
            removalAllowed: removalAllowed);
        if (verdict == ConstraintPerturbationSupport.PreflightVerdict.WholeReportUnsupported) {
            return Results.Ok(new ConstraintPerturbationReport(
                ConstraintId: constraintId,
                Status: AnalysisStatus.Unsupported,
                BaselineObjective: baselineObjective,
                Message: "当前求解器未声明 RHS 扰动能力 / No RHS perturbation backend is declared"));
        }

        var observations = new List<ConstraintPerturbationObservation>();
        bool budgetStopped = false;
        int solveCount = 0;

        // **一个 delta 的完整处理：派生 → 求解 → 分类。三步任一走不通都要留下一条观察，**
        // **而不是让整次分析失败**——某一条 delta 做不了不代表这次分析做不了。
        //
        // **The full handling of one delta: derive, solve, classify.** A failure at any step leaves an observation rather than failing the whole analysis: one impossible delta
        // does not mean the analysis is impossible.
        async Task<PerturbationOutcome> RunDeltaAsync(Flt64 delta) {
            if (!rhsAllowed) {
                return Record(AnalysisStatus.Unsupported, delta, null, null, null, null,
                    "当前求解器未声明 RHS 扰动能力 / Solver does not declare RHS perturbation support");
            }
            if (solveCount >= policy.MaxSolves) {
                budgetStopped = true;
                return Record(AnalysisStatus.Unknown, delta, null, null, null, null,
                    "扰动分析预算已耗尽 / Perturbation analysis budget was exhausted");
            }

            RhsPerturbationResult derived = ConstraintPerturbationSupport.DeriveRhsSnapshot(snapshot, constraintId, delta);
            if (derived is not RhsPerturbationReady ready) {
                string message = derived is RhsPerturbationUnsupported unsupported
                    ? unsupported.Message
                    : "无法派生出扰动后的快照 / The perturbed snapshot could not be derived";
                return Record(AnalysisStatus.Unsupported, delta, null, null, null, null, message);
            }

            solveCount++;
            Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
                SnapshotModelRebuilder.Rebuild(ready.Snapshot);
            if (rebuilt is not Ok<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> okRebuilt) {
                return Record(AnalysisStatus.Unknown, delta, ready.NewRhs, null, null, null,
                    "扰动模型重建失败 / The perturbed model could not be rebuilt");
            }

            try {
                Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> solved =
                    await solver!.SolveAsync(okRebuilt.Value!, options, cancellationToken).ConfigureAwait(false);
                if (solved is not Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> okSolved) {
                    return Record(AnalysisStatus.Unknown, delta, ready.NewRhs, null, null, null,
                        "扰动求解失败 / The perturbation solve failed");
                }

                // **状态与解都从具体变体取**：基类不带这两个字段。
                // **Status and solution come from the concrete variant**: the base type carries neither.
                var feasible = okSolved.Value as ConstraintProgrammingFeasibleOutput;
                AnalysisStatus solveStatus = okSolved.Value switch {
                    ConstraintProgrammingFeasibleOutput => AnalysisStatus.Reachable,
                    ConstraintProgrammingInfeasibleOutput => AnalysisStatus.Unreachable,
                    _ => AnalysisStatus.Unknown,
                };
                Flt64? currentObjective = feasible?.Objective;
                Flt64? improvement = ConstraintPerturbationSupport.ObjectiveImprovement(
                    snapshot.ObjectCategory, baselineObjective, currentObjective);
                PerturbationOutcome outcome = ConstraintPerturbationSupport.ClassifyOutcome(
                    solveStatus, improvement, policy.ObjectiveTolerance);

                observations.Add(new ConstraintPerturbationObservation(
                    ConstraintId: constraintId,
                    Kind: ConstraintPerturbationKind.Rhs,
                    Delta: delta,
                    PerturbedRhs: ready.NewRhs,
                    Status: solveStatus,
                    BaselineObjective: baselineObjective,
                    NewObjective: currentObjective,
                    ObjectiveImprovement: improvement,
                    IntegerPatternChanged: ConstraintPerturbationSupport.IntegerPatternChanged(baselineSolution, feasible?.Solution),
                    SolveTime: null,
                    Outcome: outcome));
                return outcome;
            }
            finally {
                // **派生模型一定释放**：它持有原生句柄。
                // **The derived model is always disposed**: it holds native handles.
                okRebuilt.Value!.Dispose();
            }
        }

        // **一条"没走到求解"的观察**：状态与原因各就各位，数值一律为 `null`。
        // **An observation that never reached a solve**: status and reason set, every number `null`.
        PerturbationOutcome Record(
            AnalysisStatus status, Flt64 delta, Flt64? perturbedRhs, Flt64? newObjective,
            Flt64? improvement, bool? patternChanged, string message) {
            observations.Add(new ConstraintPerturbationObservation(
                ConstraintId: constraintId,
                Kind: ConstraintPerturbationKind.Rhs,
                Delta: delta,
                PerturbedRhs: perturbedRhs,
                Status: status,
                BaselineObjective: baselineObjective,
                NewObjective: newObjective,
                ObjectiveImprovement: improvement,
                IntegerPatternChanged: patternChanged,
                SolveTime: null,
                Outcome: PerturbationOutcome.Unknown,
                Message: message));
            return PerturbationOutcome.Unknown;
        }
        // ponytail: sync-over-async —— 无 SynchronizationContext 的宿主（测试、服务端）安全；
        // 若将来有 UI 宿主调用，改成 async 版扫描（规则一样，只是 probe 返回 Task）。
        //
        // ponytail: sync-over-async — safe on hosts without a SynchronizationContext (tests, servers);
        // if a UI host ever calls this, add an async sweep (same rules, the probe just returns a Task).
        //
 // **固定 delta 扫描**：首个有效就停，`Unknown` 既不记也不停。
 // **The fixed-delta sweep**: it stops at the first effective one, and `Unknown` neither records nor stops .
        ConstraintPerturbationSupport.DeltaSweepResult sweep = ConstraintPerturbationSupport.RunDeltas(
            ConstraintPerturbationSupport.RequestedDeltas(policy),
            delta => RunDeltaAsync(delta).GetAwaiter().GetResult());
        budgetStopped |= sweep.BudgetStopped;

        // **二分用扫描给出的端点**：下端是最后一个无效果，上端是首个有效。
        // **The bisection takes the sweep's endpoints**: the last no-effect as the lower side, the first effective as the upper.
        AdaptivePerturbationResult? adaptive = null;
        if (policy.Adaptive is { } adaptivePolicy && sweep.FirstEffective is { } upper) {
            Flt64 lower = sweep.PreviousNoEffect ?? new Flt64(0.0);
            adaptive = ConstraintPerturbationSupport.RefineThreshold(
                lower, upper, adaptivePolicy.RefinementIterations,
                delta => RunDeltaAsync(delta).GetAwaiter().GetResult());
        }

        ConstraintPerturbationObservation? removal = null;
        if (policy.RemovalTest && !budgetStopped && removalAllowed) {
            removal = await ConstraintPerturbationSupport.RunRemovalAsync(
                snapshot, constraintId, baselineObjective, baselineSolution, policy, solver!,
                budgetStopped, options, cancellationToken).ConfigureAwait(false);
            observations.Add(removal);
        }

        AnalysisStatus status = ConstraintPerturbationSupport.AggregateStatus(observations, budgetStopped);
        return Results.Ok(new ConstraintPerturbationReport(
            ConstraintId: constraintId,
            Status: status,
            BaselineObjective: baselineObjective,
            Observations: observations,
            Adaptive: adaptive,
            Removal: removal));
    }
}
