#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
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
/// 假设式冲突种子 / The assumption-based conflict seed.
/// </summary>
/// <remarks>
/// `ConflictAnalyzer.assumptionSeed`：**给每条约束加一个 guard 字面量，把它们全部作为假设求解，**
/// 再按后端报告的冲突反查原始证据。
///
/// **这条路不要求后端暴露原生 IIS 接口**，所以它是两个种子策略里更通用的那个。
///
/// Ported from `assumptionSeed`: **guard every constraint with a literal, solve with all of them assumed**, then map the backend's conflict back onto the original evidence.
/// **This path does not require a native IIS interface**, which makes it the more portable of the two seed strategies.
/// </remarks>
public static class AssumptionConflictSeed
{
    /// <summary>
    /// 用假设求解求一个冲突种子 / Obtain a conflict seed by solving under assumptions.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="evidence">调用方请求的证据 / The evidence the caller asked about.</param>
    /// <param name="solver">CP 求解器 / The CP solver.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>种子，或 <c>null</c> 表示这条路拿不到种子 / The seed, or <c>null</c> when this path cannot produce one.</returns>
    /// <remarks>
    /// **六处"拿不到就返回 `null`"都是刻意的**：这条路是一条**尽力而为的**取证路径，
    /// 拿不到种子意味着"改用另一个策略"，**而不是"没有冲突"。**
    ///
    /// **把任何一处失败当成空种子返回，调用方会读到"模型没有冲突"——那是一个完全相反的结论。**
    ///
    /// **The six "give up and return `null`" points are deliberate**: this is a **best-effort** evidence path, and failing to produce a seed means "try the other strategy",
    /// **not "there is no conflict".** Turning any of those failures into an empty seed would let the caller read "the model has no conflict" — the opposite conclusion.
    /// </remarks>
    public static async Task<IReadOnlyList<DiagnosticSource>?> TrySeedAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyList<DiagnosticSource> evidence,
        IConstraintProgrammingSolver solver,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(solver);

        // 边界与值域不能当假设用：它们没有对应的 guard 字面量。
        // Bounds and domains cannot be assumed: they have no guard literal.
        DiagnosticSource[] fixedSources = evidence
            .Where(source => source is not DiagnosticSource.Constraint)
            .ToArray();
        if (snapshot.Constraints.Count == 0) {
            return null;
        }

        // **派生模型里不能再有原始约束。** assumption 派生模型的做法是"每条约束换成一个 guard 版本"，
        // 而不是"在原始约束旁边再加一条"——加在旁边的话，原始约束仍然无条件生效，
        // **假设求解永远不可能通过取反 guard 来发现冲突，种子必然是空的。**
        //
        // **The derived model must not still contain the original constraints.** The assumption model replaces each constraint with a guarded version rather than adding one
        // beside it; beside it, the original stays unconditionally active and **the assumption solve can never find a conflict by negating a guard, so the seed is always empty.**
        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
            SnapshotModelRebuilder.Rebuild(snapshot.WithoutConstraints());
        if (rebuilt is not Ok<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> okRebuilt) {
            return null;
        }

        ConstraintProgrammingModel model = okRebuilt.Value!;
        var literals = new Dictionary<ConstraintId, BooleanLiteral>();
        try {
            // **guard 变量用序号命名，不用约束 id**：约束 id 里可能有 `:`，而变量 id 的解析规则不同；
            // 序号名保证唯一且可预测。
            //
            // **The guard variables are numbered rather than named after the constraint**: a constraint id may contain `:`, while variable ids parse differently; a numbered name is
            // unique and predictable.
            for (int index = 0; index < snapshot.Constraints.Count; index++) {
                ConstraintProgrammingConstraintSnapshot entry = snapshot.Constraints[index];
                var variable = new BinVar($"analysis-assumption-{index}");
                var variableId = new VariableId($"analysis-assumption:{entry.Id.Value}");

                if (model.RegisterVariable(id: variableId, variable: variable, domain: IntegerDomain.Boolean)
                        is not Ok<VariableId, ErrorCode, Error<ErrorCode>>) {
                    return null;
                }

                var literal = new BooleanLiteral.VariableLiteral(variable, negated: false, id: variableId);
                var guarded = new ConstraintProgrammingConstraint.Implication(literal, entry.Constraint);
                if (model.AddConstraint(guarded, id: entry.Id, name: entry.Name)
                        is not Ok<ConstraintId, ErrorCode, Error<ErrorCode>>) {
                    return null;
                }
                literals[entry.Id] = literal;
            }

            var activations = DiagnosticActivationSet.FromSnapshot(snapshot);
            IReadOnlyList<BooleanLiteral> assumptions =
                ConstraintProgrammingAnalysisBackends.Assumptions(activations, literals);
            if (assumptions.Count == 0) {
                return null;
            }

            Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> sessionResult =
                solver.CreateSession(model, options);
            if (sessionResult is not Ok<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> okSession) {
                return null;
            }

            ConstraintProgrammingSolverOutput output;
            try {
                Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> solved =
                    await okSession.Value!.SolveAsync(assumptions: assumptions, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                if (solved is not Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> okSolved) {
                    return null;
                }
                output = okSolved.Value!;
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                return null;
            }
            finally {
                okSession.Value!.Dispose();
            }

            // **必须是"已证明不可行"。** 一个 `Unknown` 的不可行输出说明后端没证出来，
            // **把它当成种子会让报告说"这是冲突"——而实际上什么都没证。**
            //
            // **It has to be *proven* infeasible.** An `Unknown` infeasible output means the backend proved nothing, and **treating it as a seed would let the report say "this is the
            // conflict" when nothing was established.**
            if (output is not ConstraintProgrammingInfeasibleOutput infeasible
                || infeasible.ProofStatus != ProofStatus.Verified) {
                return null;
            }

            IReadOnlyList<DiagnosticSource> projected = ConflictEvidenceSupport.RemapBackendConflict(
                snapshot, infeasible.Conflict, evidence, literals);

            // **边界/值域是背景证据，必须带上**：assumption 派生模型保持它们激活，
            // 所以即使后端只报 guard 约束，种子也要包含这些可独立复验的来源。
            //
            // **Bounds and domains are background evidence and have to come along**: the assumption model keeps them active, so even when the backend reports only guarded
            // constraints, the seed carries these independently verifiable sources.
            var withBackground = new List<DiagnosticSource>(projected);
            foreach (DiagnosticSource source in fixedSources) {
                if (!withBackground.Contains(source)) {
                    withBackground.Add(source);
                }
            }
            return withBackground.Count == 0 ? null : withBackground;
        }
        finally {
            // **模型一定释放**：它持有原生句柄。
            // **The model is always disposed**: it holds native handles.
            model.Dispose();
        }
    }
}
