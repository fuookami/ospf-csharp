#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 临界约束分析流水线的选项 / Options for the critical-constraint analysis pipeline.
/// </summary>
/// <remarks>
/// 补的是（359 行）的选项层，没补编排器本身。**
/// 编排器要依次调用扰动、目标可行性与冲突三个分析器，**而这三个的分析器本体都还没搬**（只有各自的报告类型）。
/// 写一个调不到东西的编排器，正是本仓反复拒绝的那种"看起来有了"。
///
/// **The option layer of (359 lines) and not the orchestrator. is implemented** The orchestrator calls the perturbation, target-feasibility and
/// conflict analyzers in sequence, **and none of those three analyzer bodies is implemented** (only their report types). An orchestrator that calls nothing is exactly the
/// "looks like it is there" this repository keeps refusing.
/// </remarks>
/// <param name="Funnel">候选漏斗配置 / Candidate funnel configuration.</param>
/// <param name="Perturbation">扰动策略 / Perturbation policy.</param>
/// <param name="Conflict">冲突分析选项 / Conflict analysis options.</param>
/// <param name="Lp">固定整数 LP 选项 / Fixed-integer LP options.</param>
/// <param name="SolveOptions">CP 求解选项 / CP solve options.</param>
/// <param name="RunPerturbation">是否运行扰动阶段 / Whether to run the perturbation stage.</param>
/// <param name="RunTargetAnalysis">是否运行目标与冲突阶段 / Whether to run the target and conflict stages.</param>
/// <param name="CandidateLimit">昂贵分析候选上限；0 表示只做筛选不做扰动 / Candidate limit for expensive analysis; 0 means screening only.</param>
public sealed record CriticalConstraintAnalysisOptions(
    CandidateFunnelConfig? Funnel = null,
    ConstraintPerturbationPolicy? Perturbation = null,
    ConflictAnalysisOptions? Conflict = null,
    FixedIntegerLpSensitivityOptions? Lp = null,
    ConstraintProgrammingSolveOptions? SolveOptions = null,
    bool RunPerturbation = true,
    bool RunTargetAnalysis = true,
    int CandidateLimit = CandidateFunnelConfig.DefaultCandidateLimitValue)
{
    /// <summary>候选漏斗配置，未给出时取默认 / The funnel configuration, defaults when omitted.</summary>
    public CandidateFunnelConfig FunnelValue => Funnel ?? new CandidateFunnelConfig();

    /// <summary>
    /// 扰动策略，未给出时**只看删除** / The perturbation policy, which looks at removals only when omitted.
    /// </summary>
    /// <remarks>
    /// **这个默认值不是 `ConstraintPerturbationPolicy` 自己的默认值，别"顺手统一"掉。**
    ///
    /// 流水线默认 `deltas = emptyList, removalTest = true`——只做单约束删除测试，不做 RHS 扰动；
    /// 而策略类型自己的默认是 `deltas = [1.0]`，要跑一串 RHS 扰动。**两者代价差一个数量级**，
    /// 而流水线是自动跑的，**默认值取重的那个会让"跑一次分析"变成一件很贵的事。**
    ///
    /// **This default is not `ConstraintPerturbationPolicy`'s own default, and it must not be "unified" by accident.** The code pipeline defaults to
    /// `deltas = emptyList, removalTest = true` — removal tests only, no RHS perturbation — while the policy type's own default is `deltas = [1.0]`, which runs a series of RHS
    /// perturbations. **The two differ by an order of magnitude in cost**, and the pipeline runs automatically, so **picking the heavier default turns "one analysis run" into
    /// something expensive.**
    /// </remarks>
    public ConstraintPerturbationPolicy PerturbationValue =>
        Perturbation ?? new ConstraintPerturbationPolicy(Deltas: Array.Empty<Fuookami.Ospf.Math.Algebra.Number.Flt64>(), RemovalTest: true);

    /// <summary>冲突分析选项，未给出时取默认 / The conflict options, defaults when omitted.</summary>
    public ConflictAnalysisOptions ConflictValue => Conflict ?? new ConflictAnalysisOptions();

    /// <summary>CP 求解选项，未给出时取默认 / The CP solve options, defaults when omitted.</summary>
    public ConstraintProgrammingSolveOptions SolveOptionsValue => SolveOptions ?? new ConstraintProgrammingSolveOptions();

    /// <summary>
    /// 校验选项 / Validate the options.
    /// </summary>
    /// <returns>问题描述，合法时为 <c>null</c> / The problem, or <c>null</c> when well formed.</returns>
    /// <remarks>
    /// **`CandidateLimit = 0` 是合法的**：它的意思是"只做筛选，不做昂贵的扰动分析"。
    /// 把它当非法拒掉，等于让调用方**连一次便宜的分析都跑不了**。
    /// 只有负数才是错误——负数既不是"不做"，也不是"做多少"。
    ///
    /// **`CandidateLimit = 0` is legal**: it means "screen only, skip the expensive perturbation analysis". Rejecting it would leave the caller **unable to run even the cheap
    /// analysis**. Only a negative value is an error — a negative is neither "do none" nor "do some number".
    /// </remarks>
    public string? Validate() {
        if (CandidateLimit < 0) {
            return "昂贵分析候选上限不得为负 / The expensive-analysis candidate limit must not be negative";
        }
        if (PerturbationValue.Validate() is { } perturbationProblem) {
            return perturbationProblem;
        }
        if (ConflictValue.Validate() is { } conflictProblem) {
            return conflictProblem;
        }
        return null;
    }
}
