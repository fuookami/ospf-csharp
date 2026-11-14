#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 求解器输出到目标状态的两处判定 / The two verdicts that turn solver output into a target status.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.mapOutput` 里两处最容易搞错的状态判定。**
///
/// ### 一、`Infeasible` ≠ `Unreachable`
///
/// 求解器说"不可行"，可能有两种意思：
///
/// | 求解器给的 | 真意 | 状态 |
/// | --- | --- | --- |
/// | 不可行 **+ 证明** | 这个目标确实达不到 | **`Unreachable`** |
/// | 不可行 **，无证明** | **只是没找到解** | **`Unknown`** |
///
/// **把第二行报成 `Unreachable` 是这个模块最贵的错误**：`Unreachable` 正是冲突分析
/// 去声称"这是一个 MUS"的前提。**一个没证明的"不可行"会让整条 MUS 结论建在空上。**
///
/// **Reporting the second row as `Unreachable` is the most expensive mistake in this module**: `Unreachable` is exactly what conflict analysis requires before claiming "this is a MUS". **An unproven
/// "infeasible" builds the whole MUS conclusion on nothing.**
///
/// ### 二、无效见证是 `Unknown`，不是失败
///
/// 求解器说"可行"，而见证通不过校验。**这时求解器的说法被推翻了，但我们的结论也还没出来**——
/// 所以是 `Unknown` 加一条带原因的消息，**不是失败。** 失败意味着"这次分析出错了"，
/// 而这次分析完全正常：它只是验出后端给的东西不对。
///
/// **The solver says "feasible" and the witness fails validation. Its claim is refuted, but ours is not established either** — so it is `Unknown` with a message carrying the reason, **not a failure.** A
/// failure means "this analysis went wrong", while this analysis went exactly right: it caught the backend.
/// </remarks>
public static class TargetOutputStatus
{
    /// <summary>
    /// 不可行输出对应的状态 / The status for an infeasible output.
    /// </summary>
    /// <param name="outputProofVerified">输出自带的证明是否已验证 / Whether the output's own proof is verified.</param>
    /// <param name="reportProofVerified">报告附加的证明是否已验证 / Whether the report's attached proof is verified.</param>
    /// <returns>`Unreachable` 或 `Unknown` / Either Unreachable or Unknown.</returns>
    /// <remarks>
    /// **两个来源都要看。** 只查 `output.ProofStatus` 会漏掉"证明挂在 report 上"的那一类后端，
    /// **而漏查的后果是把一个已证明的不可行报成 `Unknown`——代价是一个本来能拿到的 MUS 拿不到了。**
    ///
    /// **Both sources are consulted.** Checking only `output.ProofStatus` misses backends that attach the proof to the report, **and the cost is reporting a proven infeasibility as `Unknown` — losing a MUS that
    /// was there to be had.**
    /// </remarks>
    public static AnalysisStatus FromInfeasible(bool outputProofVerified, bool reportProofVerified) =>
        outputProofVerified || reportProofVerified
            ? AnalysisStatus.Unreachable
            : AnalysisStatus.Unknown;

    /// <summary>
    /// 见证无效时的报告 / The report for an invalid witness.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <param name="integerBound">目标的整数边界 / The target's integer bound.</param>
    /// <param name="witnessError">见证校验给出的原因 / The reason the witness failed.</param>
    /// <param name="proofStatus">求解器报的证明状态 / The proof status the solver reported.</param>
    /// <returns>一份 `Unknown` 报告 / An `Unknown` report.</returns>
    public static TargetFeasibilityReport InvalidWitness(
        ObjectiveTarget target,
        Int64 integerBound,
        string witnessError,
        ProofStatus proofStatus) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(witnessError);

        return new TargetFeasibilityReport(
            Target: target,
            EffectiveIntegerBound: integerBound,
            Status: AnalysisStatus.Unknown,
            ProofStatus: proofStatus,
            TargetFixed: false,
            Message: $"CP 后端返回的 target 见证无效：{witnessError} / CP backend returned an invalid target witness: {witnessError}");
    }

    /// <summary>
    /// 不可行时的报告 / The report for an infeasible output.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <param name="integerBound">目标的整数边界 / The target's integer bound.</param>
    /// <param name="outputProofVerified">输出自带的证明是否已验证 / Whether the output's own proof is verified.</param>
    /// <param name="reportProofVerified">报告附加的证明是否已验证 / Whether the report's attached proof is verified.</param>
    /// <param name="proofStatus">求解器报的证明状态 / The proof status the solver reported.</param>
    /// <param name="message">消息 / The message.</param>
    /// <returns>一份 `Unreachable` 或 `Unknown` 报告 / An Unreachable or Unknown report.</returns>
    public static TargetFeasibilityReport Infeasible(
        ObjectiveTarget target,
        Int64 integerBound,
        bool outputProofVerified,
        bool reportProofVerified,
        ProofStatus proofStatus,
        string? message = null) {
        ArgumentNullException.ThrowIfNull(target);

        AnalysisStatus status = FromInfeasible(outputProofVerified, reportProofVerified);

        return new TargetFeasibilityReport(
            Target: target,
            EffectiveIntegerBound: integerBound,
            Status: status,
            ProofStatus: proofStatus,
            TargetFixed: status == AnalysisStatus.Unreachable,
            Message: message);
    }

    /// <summary>
    /// 可行输出的附加说明 / The caveat attached to a feasible output.
    /// </summary>
    /// <param name="presence">求解器报的解存在性 / The solution presence the solver reported.</param>
    /// <returns>说明，或 <c>null</c> 表示不必附加 / The caveat, or <c>null</c> when none is needed.</returns>
    /// <remarks>
    /// **`Reachable` 是可行性结论，不是最优性结论。**
    ///
    /// | 求解器报的 | 状态 | 说明 |
    /// | --- | --- | --- |
    /// | `Optimal` | `Reachable` | **无** |
    /// | `Incumbent` | `Reachable` | **"找到可行见证，但未证明最优"** |
    ///
    /// **两个方向都不能省：**
    ///
    /// - 少了 `Incumbent` 那一行，**读者会把"可达"读成"这就是最优"**；
    /// - 给 `Optimal` 也加上那一行，**每一条已经证明最优的结果都会被无谓地打个折**，
    ///   而"有保留"说多了就等于没有保留。
    ///
    /// **Neither direction may be dropped:** omit the `Incumbent` caveat and **the reader takes "reachable" for "optimal"**; add it to `Optimal` too and **every genuinely proven optimum gets needlessly
    /// hedged**, and a hedge that always fires is not a hedge.
    ///
    /// **`None` 与 `null` 不给说明。** 一份"可行但没有解存在性信息"的输出不该被
    /// **凭空安上一句"未证明最优"**——那是把缺失当结论。
    ///
    /// **`None` and `null` get no caveat.** A feasible output with no presence information must not be **handed a "not proven optimal" out of nothing** — that turns an absence into a conclusion.
    /// </remarks>
    public static string? FeasibleCaveat(SolutionPresence? presence) =>
        presence == SolutionPresence.Incumbent
            ? "目标找到可行见证，但未证明最优 / A feasible target witness was found without an optimality proof"
            : null;
}
