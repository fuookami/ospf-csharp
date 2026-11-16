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
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 目标可行性分析 / Target feasibility analysis.
/// </summary>
/// <remarks>
/// `TargetFeasibilityAnalyzer.analyze` 的求解与分类部分：派生重建 → 求解 → 按输出分类。
/// 候选漏斗与缓存那一层没实现。
///
/// Ported from the solve-and-classify part of `TargetFeasibilityAnalyzer.analyze`: derived rebuild, solve, then classify by output. The candidate funnel and the cache layer
/// are not implemented.
/// </remarks>
public static class TargetFeasibilityAnalyzer
{
    /// <summary>
    /// 判断目标是否可达 / Decide whether the target is reachable.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="solver">CP 求解器 / The CP solver.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>可行性报告 / A feasibility report.</returns>
    /// <remarks>
    /// **三条分类规则，每条都有一个反直觉的地方：**
    ///
    /// | 后端说 | 无保留地采信会怎样 | 这里怎么做 |
    /// | --- | --- | --- |
    /// | 可行 | 见证可能违反模型 | **复核**：不过就报 `Unknown`，不报可达 |
    /// | 不可行 | **把"没证出来"报成"不可达"** | 只有 `Verified` 才给 `Unreachable` |
    /// | 无结论 | 丢掉一个可用的见证 | 有合格见证就报 `Reachable` |
    ///
    /// **Three classification rules, each with a counter-intuitive point:** an unconditional "feasible" could carry a witness that violates the model, so it is **re-checked** and
    /// a failure reports `Unknown`, not reachability; an unconditional "infeasible" would **report "not proven" as "unreachable"**, so only `Verified` yields `Unreachable`;
    /// and an unconditional "no conclusion" would discard a perfectly usable witness, so a valid witness still reports `Reachable`.
    /// </remarks>
    public static async Task<Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        IConstraintProgrammingSolver solver,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(solver);

        if (!SnapshotModelRebuilder.TryIntegerBound(target, out Int64 integerBound)) {
            return Results.Ok(new TargetFeasibilityReport(
                Target: target,
                Status: AnalysisStatus.Unsupported,
                TargetFixed: false,
                Message: "ObjectiveTarget 无法精确映射为 CP Int64 边界 / ObjectiveTarget cannot be mapped exactly to a CP Int64 bound"));
        }

        Result<DerivedModel, ErrorCode, Error<ErrorCode>> derived =
            SnapshotModelRebuilder.RebuildWithTarget(snapshot, target);
        if (derived is not Ok<DerivedModel, ErrorCode, Error<ErrorCode>> okDerived) {
            return derived is Failed<DerivedModel, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<TargetFeasibilityReport>(failed.Error)
                : Results.Failed<TargetFeasibilityReport>(new Err<ErrorCode>(ErrorCode.ApplicationError, "派生模型构造失败 / Derived model construction failed"));
        }

        DerivedModel derivedModel = okDerived.Value!;
        try {
            // **复核必须针对实际提交给后端的那份快照**，不是基线快照（见 `TargetWitnessValidator`）。
 // **The re-check has to use the snapshot actually sent to the backend**, not the implementation.
            ConstraintProgrammingModelSnapshot submitted = derivedModel.Model.Snapshot().Value!;

            Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> solved;
            try {
                solved = await solver.SolveAsync(derivedModel.Model, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                return Results.Ok(new TargetFeasibilityReport(
                    Target: target,
                    EffectiveIntegerBound: integerBound,
                    Status: AnalysisStatus.Unknown,
                    Message: $"CP target 求解失败：{error.Message} / CP target solve failed: {error.Message}"));
            }

            if (solved is not Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> okSolved) {
                // **错误码是 `Other` 时按"不支持"处理**：它表示降阶不支持，不是求解失败。
                // **An `Other` error means unsupported lowering**, not a failed solve.
                return solved is Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> failedSolve
                       && failedSolve.Error.Code == ErrorCode.Other
                    ? Results.Ok(new TargetFeasibilityReport(
                        Target: target,
                        EffectiveIntegerBound: integerBound,
                        Status: AnalysisStatus.Unsupported,
                        TargetFixed: false,
                        Message: $"CP target 降阶不支持：{failedSolve.Error.Message} / CP target lowering is unsupported: {failedSolve.Error.Message}"))
                    : Results.Failed<TargetFeasibilityReport>(
                        (solved as Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)!.Error);
            }

            return Results.Ok(Classify(okSolved.Value!, target, integerBound, submitted));
        }
        finally {
            // **派生模型一定会被释放**：它持有原生句柄，**漏一次不是测试变红，而是内存慢慢涨。**
            // **The derived model is always disposed**: it holds native handles, and **a leak shows up as slowly growing memory, not as a red test.**
            derivedModel.Model.Dispose();
        }
    }

    private static TargetFeasibilityReport Classify(
        ConstraintProgrammingSolverOutput output,
        ObjectiveTarget target,
        Int64 integerBound,
        ConstraintProgrammingModelSnapshot submitted) {
        switch (output) {
            case ConstraintProgrammingFeasibleOutput feasible: {
                string? witnessError = TargetWitnessValidator.Validate(
                    submitted, target, integerBound, feasible.Solution);
                if (witnessError is null) {
                    return new TargetFeasibilityReport(
                        Target: target,
                        EffectiveIntegerBound: integerBound,
                        Status: AnalysisStatus.Reachable,
                        Solution: feasible.Solution,
                        ExactObjective: feasible.ExactObjective,
                        ObjectiveValue: feasible.Objective,
                        ProofStatus: feasible.ProofStatus);
                }
                return new TargetFeasibilityReport(
                    Target: target,
                    EffectiveIntegerBound: integerBound,
                    // **可行但见证不合格：报 `Unknown`，不报可达。**
                    // **Feasible with an invalid witness reports `Unknown`, not reachability.**
                    Status: AnalysisStatus.Unknown,
                    ProofStatus: feasible.ProofStatus,
                    Message: $"CP 后端返回的 target 见证无效：{witnessError} / CP backend returned an invalid target witness: {witnessError}");
            }

            case ConstraintProgrammingInfeasibleOutput infeasible: {
                // **"后端说不可行"不等于"已证明不可达"。** 只有 `Verified` 才是后者。
                // **"The backend says infeasible" is not "proven unreachable".** Only `Verified` is.
                bool verified = infeasible.ProofStatus == ProofStatus.Verified;
                return new TargetFeasibilityReport(
                    Target: target,
                    EffectiveIntegerBound: integerBound,
                    Status: verified ? AnalysisStatus.Unreachable : AnalysisStatus.Unknown,
                    ProofStatus: verified ? ProofStatus.Verified : infeasible.ProofStatus,
                    Message: verified ? null : "CP 后端未提供不可达证明 / CP backend did not provide a proof of target infeasibility");
            }

            case ConstraintProgrammingUnknownOutput unknown: {
                // **无结论的输出里也可能带着一个能用的见证。** 丢掉它等于白跑一次求解；
                // **但收下它之前同样要复核**——无结论恰恰是"后端自己也不确定"的时候。
                //
                // **An unknown output can still carry a usable witness.** Dropping it wastes the solve; **but it still goes through the same re-check** — an unknown result is
                // exactly when the backend is least sure of itself.
                ConstraintProgrammingSolution? witness = unknown.Report?.Solution is { } raw
                    && raw.Length == submitted.Variables.Count
                    ? new ConstraintProgrammingSolution(submitted.Variables
                        .Select((definition, index) => new KeyValuePair<VariableId, Int64>(definition.Id, raw.Values[index]))
                        .ToDictionary(pair => pair.Key, pair => pair.Value))
                    : null;
                string? witnessError = witness is null
                    ? "无见证解 / no witness"
                    : TargetWitnessValidator.Validate(submitted, target, integerBound, witness);
                bool reachable = witness is not null && witnessError is null;
                return new TargetFeasibilityReport(
                    Target: target,
                    EffectiveIntegerBound: integerBound,
                    Status: reachable ? AnalysisStatus.Reachable : AnalysisStatus.Unknown,
                    Solution: reachable ? witness : null,
                    TerminationReason: unknown.TerminationReason);
            }

            default:
                return new TargetFeasibilityReport(
                    Target: target,
                    EffectiveIntegerBound: integerBound,
                    Status: AnalysisStatus.Unknown,
                    Message: "CP 后端返回了无法识别的输出 / CP backend returned an unrecognised output");
        }
    }
}
