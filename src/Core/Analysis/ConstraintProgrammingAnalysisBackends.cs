#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 证明判定与分析侧纯投影 / Proof gating and the analysis-side pure projections.
/// </summary>
/// <remarks>
/// 补（270 行）里不需要求解器的部分**：
/// 两个证明判定、assumption 映射、固定值投影、后端失败传播。
///
/// **没实现的是 `ConstraintProgrammingPerturbationBackend` 本身**：它要 `solver.CreateSession(model, options)` 与
/// `session.Solve(hints)`，**那是真实后端**。
///
/// **The parts of (270 lines) that need no solver is implemented**: the two proof gates, the assumption mapper, the fixed-value projection and
/// the backend-failure propagation. **What did not come over is `ConstraintProgrammingPerturbationBackend` itself**, which needs `solver.CreateSession(model, options)` and
/// `session.Solve(hints)` — **that is a real backend.**
/// </remarks>
public static class ConstraintProgrammingAnalysisBackends
{
    /// <summary>
    /// 是否已证明最优 / Whether optimality was proven.
    /// </summary>
    /// <param name="output">可行输出 / The feasible output.</param>
    /// <returns>是否已证明最优 / Whether optimality was proven.</returns>
    /// <remarks>
    /// 判定依据是 `SolutionPresence.Optimal`——它本身就是"具备可靠最优性证明"的语义标记。
    /// **刻意不要求 `ProofStatus.Verified`**：真实后端适配器对最优解上报的是 `Claimed`，
    /// 要求 `Verified` 会让所有真实后端的有效性阶段**静默退化为"无结论"**。
    ///
    /// **这是"把可用能力误判为不可用"，与"Timeout 当成 UNSAT"是同一类错误，只是方向相反。**
    ///
    /// The signal is `SolutionPresence.Optimal`, which by definition means "a reliable optimality proof exists". **`ProofStatus.Verified` is deliberately not required**: real
    /// backend adapters report `Claimed` for optimal solutions, so requiring `Verified` would **silently degrade the effectiveness stage to "no conclusion" on every real
    /// backend.** **That is "treating an available capability as unavailable" — the same class of error as "Timeout → UNSAT", only in the opposite direction.**
    /// </remarks>
    public static bool IsProvenOptimal(this ConstraintProgrammingFeasibleOutput output) {
        ArgumentNullException.ThrowIfNull(output);

        if (output.Report is { } report) {
            return report.ProblemStatus == ProblemStatus.Feasible
                && report.SolutionPresence == SolutionPresence.Optimal;
        }
        // 没有统一报告时只能依赖输出自身的证明声明；`None` 表示没有证明。
        // Without a unified report the output's own proof claim is the only signal; `None` means no proof.
        return output.ProofStatus is ProofStatus.Verified or ProofStatus.Claimed;
    }

    /// <summary>
    /// 是否已证明不可行 / Whether infeasibility was proven.
    /// </summary>
    /// <param name="output">不可行输出 / The infeasible output.</param>
    /// <returns>是否已证明不可行 / Whether infeasibility was proven.</returns>
    /// <remarks>
    /// 不可行性必须有证书，所以这里**要求 `Verified`**：适配器对不可行上报的正是 `Verified`，
    /// 而 `Claimed` 不足以支撑"目标不可达"这类强结论。
    ///
    /// **两条判定的门槛刻意不同**：最优解有 incumbent 可以复核，不可行结论没有任何东西可以复核，
    /// **只能靠证书。**
    ///
    /// Infeasibility requires a certificate, so `Verified` is **required** here: adapters report `Verified` for infeasible, and a mere `Claimed` cannot support a strong conclusion
    /// like "target unreachable". **The two thresholds differ on purpose**: an optimal solution has an incumbent that can be re-checked, while an infeasibility conclusion has
    /// nothing to re-check — **only the certificate.**
    /// </remarks>
    public static bool IsProvenInfeasible(this ConstraintProgrammingInfeasibleOutput output) {
        ArgumentNullException.ThrowIfNull(output);

        if (output.Report is { } report) {
            return report.ProblemStatus == ProblemStatus.Infeasible
                && report.Proof.Status == ProofStatus.Verified;
        }
        return output.ProofStatus == ProofStatus.Verified;
    }

    /// <summary>
    /// 把分析器的原始证据激活状态表达为后端 assumption / Express the analyzer's original-evidence activation state as backend assumptions.
    /// </summary>
    /// <param name="activations">原始证据激活集合 / Original-evidence activation set.</param>
    /// <param name="literals">按约束 ID 编索引的布尔文字 / Boolean literals keyed by constraint ID.</param>
    /// <returns>后端 assumption 列表 / Backend assumption list.</returns>
    /// <remarks>
    /// **只有布尔文字类证据可以表达为 assumption**；数值约束必须通过移除约束的派生模型表达。
    /// 这里刻意只处理 `BooleanLiteral`，**从而不会为不可表达的约束伪造 assumption**。
    ///
    /// **Only Boolean-literal evidence can be expressed as an assumption**; numeric constraints must be expressed by deriving a model without them. This deliberately handles only
    /// `BooleanLiteral`, so **it never fabricates an assumption for a constraint that cannot be expressed as one.**
    /// </remarks>
    public static IReadOnlyList<BooleanLiteral> Assumptions(
        DiagnosticActivationSet activations,
        IReadOnlyDictionary<ConstraintId, BooleanLiteral> literals) {
        ArgumentNullException.ThrowIfNull(activations);
        ArgumentNullException.ThrowIfNull(literals);

        return activations.ActiveSources()
            .Select(source => source as DiagnosticSource.Constraint)
            .Where(source => source is not null)
            .Select(source => literals.TryGetValue(source!.Id, out BooleanLiteral? literal) ? literal : null)
            .Where(literal => literal is not null)
            .Select(literal => literal!)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// 从 CP 解提取固定整数值 / Extract fixed integer values from a CP solution.
    /// </summary>
    /// <param name="solution">CP 解，可为 <c>null</c> / The CP solution, or <c>null</c>.</param>
    /// <returns>按变量 ID 编索引的固定值 / Fixed values keyed by variable ID.</returns>
    /// <remarks>
    /// **只做投影，不做数值近似**：`Int64` 原样传递。把整数值转成 `double` 再传下去，
    /// 会让 `9007199254740993` 变成 `9007199254740992`——**而那是另一个模型。**
    ///
    /// **This only projects and never approximates**: `Int64` values pass through unchanged. Converting to `double` on the way would turn `9007199254740993` into
    /// `9007199254740992` — **a different model.**
    /// </remarks>
    public static IReadOnlyDictionary<VariableId, Int64> FixedValues(ConstraintProgrammingSolution? solution) =>
        solution?.Values ?? new Dictionary<VariableId, Int64>();

    /// <summary>
    /// 派生 LP 目标与基线目标的一致性校验 / Validate that a derived LP objective matches the baseline objective.
    /// </summary>
    /// <param name="baseline">基线目标值 / Baseline objective value.</param>
    /// <param name="derived">派生目标值 / Derived objective value.</param>
    /// <param name="tolerance">相对容差 / Relative tolerance.</param>
    /// <returns>两个目标值是否一致 / Whether the objective values agree.</returns>
    /// <remarks>
    /// **任一侧为 `null` 都返回 `false`**：没有基线目标时"一致"无从谈起，返回 `true` 会让一份
    /// 没有可比对象的报告声称"派生目标与基线一致"。
    ///
    /// **容差按量级缩放**：目标值在 `1e6` 量级时，绝对容差 `1e-9` 严苛到永远不成立。
    ///
    /// **`null` on either side returns `false`**: with no baseline objective there is nothing to agree with, and returning `true` would let a report with nothing to compare claim
    /// "the derived objective matches the baseline". **The tolerance scales with magnitude**: at an objective around `1e6` an absolute `1e-9` is strict enough to never hold.
    /// </remarks>
    public static bool ObjectivesAgree(Flt64? baseline, Flt64? derived, double tolerance) {
        if (baseline is not { } leftValue || derived is not { } rightValue) {
            return false;
        }
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0.0) {
            return false;
        }

        double left = leftValue.ToDouble();
        double right = rightValue.ToDouble();
        double difference = global::System.Math.Abs(left - right);
        double scale = global::System.Math.Max(1.0, global::System.Math.Abs(left));
        return difference <= tolerance * scale;
    }

    /// <summary>
    /// 将后端失败映射为调用方结果类型 / Map a backend failure to the caller's result type.
    /// </summary>
    /// <typeparam name="T">结果值类型 / The result value type.</typeparam>
    /// <param name="result">后端结果 / The backend result.</param>
    /// <returns>映射后的结果 / The mapped result.</returns>
    /// <remarks>
    /// **成功被映射成失败，而不是原样透传。** 一个"成功"的后端结果出现在一个本该失败的路径上，
    /// **说明后端的状态和调用方的预期已经不一致了——而那不是成功，那是一个错误的结果状态。**
    ///
    /// **A success is mapped to a failure, not passed through.** A "successful" backend result arriving on a path that should have failed **means the backend state and the caller expectation have already
    /// diverged — and that is not a success, it is an invalid result state.**
    /// </remarks>
    public static Result<T, ErrorCode, Error<ErrorCode>> PropagateBackendFailure<T>(
        Result<object, ErrorCode, Error<ErrorCode>> result) {
        ArgumentNullException.ThrowIfNull(result);

        return result switch {
            Failed<object, ErrorCode, Error<ErrorCode>> failed =>
                new Failed<T, ErrorCode, Error<ErrorCode>>(failed.Error),
            Fatal<object, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => new Failed<T, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "CP 后端返回了无效的结果状态 / The CP backend returned an invalid result state")),
        };
    }
}
