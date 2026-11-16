#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 行对偶与 Farkas 乘子的后处理：强对偶校验、对偶模型回退和机制约束映射。
/// Post-processing for SCIP row duals and Farkas multipliers: strong-duality check, dual-model fallback and
/// mechanism-constraint mapping.
/// </summary>
internal static class ScipLpDualSupport {
    /// <summary>强对偶校验容差 / Strong-duality tolerance.</summary>
    private const double Tolerance = 1e-6;

    /// <summary>返回与约束行同序的行对偶 / Return row duals aligned with the constraint rows.</summary>
    /// <remarks>
    /// 后端逐行对偶只有在强对偶等式 |Σ rhs·y - objective| ≤ 1e-6 成立时才可信；否则回退到显式对偶模型复核。
    /// 回退时重新求解对偶模型，因此带非零目标常数或界生效的模型都会走到回退分支。
    /// Backend row duals are only trusted when the strong-duality equality holds; otherwise the explicit dual model is
    /// solved instead. So models with a non-zero objective constant or active bounds
    /// take the fallback branch.
    /// </remarks>
    /// <param name="model">已松弛的原始线性模型 / Relaxed primal linear model.</param>
    /// <param name="outcome">LP 模式求解结果 / LP-mode solving outcome.</param>
    /// <param name="dualSolver">用于求解对偶模型的线性求解器 / Linear solver used for the dual model.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>行对偶向量或错误 / Row dual vector or an error.</returns>
    internal static async Task<Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>> RowDualsAsync(
        LinearTriadModel model,
        ScipLpOutcome outcome,
        ILinearSolver dualSolver,
        CancellationToken cancellationToken) {
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> extracted = model.TidyDualSolution(outcome.RowDuals);
        if (extracted is Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> values
            && outcome.Output is { Obj: { } objective }
            && MatchesStrongDuality(model, values.Value, objective)) {
            return extracted;
        }
        return await TriadDualSolverSupport.SolveDualAsync(
            model: model,
            solver: dualSolver,
            cancellationToken: cancellationToken);
    }

    /// <summary>校验强对偶等式 / Check the strong-duality equality.</summary>
    private static bool MatchesStrongDuality(LinearTriadModel model, IReadOnlyList<Flt64> rowDuals, Flt64 objective) {
        IReadOnlyList<Flt64> rhs = model.Constraints.Rhs;
        double sum = 0;
        for (int row = 0; row < rowDuals.Count && row < rhs.Count; row++) {
            double term = rhs[row].ToDouble() * rowDuals[row].ToDouble();
            sum += term;
            if (!double.IsFinite(sum)) {
                return false;
            }
        }
        return double.IsFinite(sum) && System.Math.Abs(sum - objective.ToDouble()) <= Tolerance;
    }





    /// <summary>把非成功结果转换成目标结果类型 / Convert a non-success result into the target result type.</summary>
    internal static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result,
        string message) {
        if (result is Failed<S, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<T>(failed.Error);
        }
        if (result is Fatal<S, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, message));
    }
}

