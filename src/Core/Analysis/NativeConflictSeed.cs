#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 原生 IIS 冲突种子 / The native-IIS conflict seed.
/// </summary>
/// <remarks>
/// 移植自 `ConflictAnalyzer.nativeSeed`：**直接在派生模型上请求后端自己算冲突**，不再加 guard 字面量。
/// 比假设式快，**但要求后端真的会做 conflict 分析**——它不做就返回 `null`，让调用方退回假设式。
///
/// Ported from `nativeSeed`: **ask the backend to work out the conflict on the derived model**, with no guard literals. Faster than the assumption path, **but it requires a
/// backend that really performs conflict analysis**; one that does not gets `null` and the caller falls back.
/// </remarks>
public static class NativeConflictSeed
{
    /// <summary>
    /// 用后端原生冲突分析取种子 / Obtain a seed through the backend's own conflict analysis.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="evidence">调用方请求的证据 / The evidence the caller asked about.</param>
    /// <param name="solver">CP 求解器 / The CP solver.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>种子，或 <c>null</c> / The seed, or <c>null</c>.</returns>
    /// <remarks>
    /// **只传约束类的 activation id，不传边界/值域的。** 这是一段很容易被"顺手补全"的推理：
    /// 每个后端都保证为约束建 activation，**但当原始边界已经等于安全基础值域时，后端可以省略那条 activation**——
    /// 把这些 id 强行传进去，**一次合法的原生请求会变成"未知 id"的建模错误。**
    ///
    /// **Only constraint activation ids are passed, never bound or domain ones.** This is the part a "helpful completion" would ruin: every backend guarantees a constraint
    /// activation, **but when an original bound already equals the safe base domain the backend may omit that activation** — passing those ids turns **a legitimate native request
    /// into an unknown-id modelling error.**
    /// </remarks>
    public static async Task<IReadOnlyList<DiagnosticSource>?> TrySeedAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        IReadOnlyList<DiagnosticSource> evidence,
        IConstraintProgrammingSolver solver,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(solver);

        Result<DerivedModel, ErrorCode, Error<ErrorCode>> derived =
            SnapshotModelRebuilder.RebuildWithTarget(snapshot, target);
        if (derived is not Ok<DerivedModel, ErrorCode, Error<ErrorCode>> okDerived) {
            return null;
        }

        try {
            ConstraintProgrammingSolveOptions effective = (options ?? new ConstraintProgrammingSolveOptions()) with {
                CollectConflict = true,
                ShrinkConflict = false,
                ConflictActivationIds = ActivationIds(evidence),
            };

            ConstraintProgrammingSolverOutput output;
            try {
                Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> solved =
                    await solver.SolveAsync(okDerived.Value!.Model, effective, cancellationToken).ConfigureAwait(false);
                if (solved is not Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> okSolved) {
                    return null;
                }
                output = okSolved.Value!;
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                return null;
            }

            // **必须已证明不可行**：`Unknown` 的不可行输出说明后端没证出来。
            // **It has to be proven infeasible**: an `Unknown` infeasible output means the backend proved nothing.
            if (output is not ConstraintProgrammingInfeasibleOutput infeasible
                || infeasible.ProofStatus != ProofStatus.Verified) {
                return null;
            }

            IReadOnlyList<DiagnosticSource> projected = ConflictEvidenceSupport.RemapBackendConflict(
                snapshot, infeasible.Conflict, evidence);
            return projected.Count == 0 ? null : projected;
        }
        finally {
            // **派生模型一定释放**：它持有原生句柄。
            // **The derived model is always disposed**: it holds native handles.
            okDerived.Value!.Model.Dispose();
        }
    }

    /// <summary>
    /// 把证据里的约束翻译成 activation id / Translate the constraint evidence into activation ids.
    /// </summary>
    /// <param name="evidence">证据 / The evidence.</param>
    /// <returns>activation id 集合，可能为空 / The activation ids, possibly empty.</returns>
    /// <remarks>
    /// **边界与值域一律不传**，理由见类型说明。空集合返回 `null` 而不是空集，
    /// **因为"不指定"和"指定零个"在部分后端上是两种不同的请求。**
    ///
    /// **Bounds and domains are never passed**; see the type remarks. An empty set returns `null` rather than an empty collection, **because "unspecified" and "zero specified"
    /// are two different requests to some backends.**
    /// </remarks>
    public static IReadOnlySet<string>? ActivationIds(IReadOnlyList<DiagnosticSource> evidence) {
        ArgumentNullException.ThrowIfNull(evidence);

        string[] ids = evidence
            .Select(source => source as DiagnosticSource.Constraint)
            .Where(source => source is not null)
            .Select(source => $"constraint:{source!.Id.Value}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return ids.Length == 0 ? null : new HashSet<string>(ids, StringComparer.Ordinal);
    }
}
