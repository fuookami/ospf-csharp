#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 一个阶段该不该跑，以及不跑时怎么说 / Whether a stage runs, and how to say it did not.
/// </summary>
/// <param name="Runs">是否运行 / Whether the stage runs.</param>
/// <param name="Reason">不跑的原因，跑则为 <c>null</c> / Why it does not run; <c>null</c> when it does.</param>
public sealed record StageDecision(bool Runs, string? Reason)
{
    /// <summary>运行 / It runs.</summary>
    public static StageDecision Run { get; } = new(true, null);

    /// <summary>不运行，并给出原因 / It does not run, for the given reason.</summary>
    /// <param name="reason">原因 / The reason.</param>
    /// <returns>决策 / The decision.</returns>
    public static StageDecision Skip(string reason) => new(false, reason);
}

/// <summary>
/// 关键约束分析流水线的阶段决策 / The stage decisions of the critical-constraint analysis pipeline.
/// </summary>
/// <remarks>
/// `CriticalConstraintAnalysisPipeline.analyze` 里每个阶段的"跑 / 不跑"三元判断。
/// **真正求解的部分由调用方完成**，这一段只决定"这一步做不做、不做怎么说"。
///
/// Ported from the run/skip triads at the head of each stage in `CriticalConstraintAnalysisPipeline.analyze`. **The solving belongs to the caller**; this part only decides whether a
/// step happens and how to explain it if it does not.
///
/// **每个阶段有两个不同的"不跑"理由，而它们的措辞必须分开：**
///
/// | 阶段 | 理由一 | 理由二 |
/// | --- | --- | --- |
/// | 扰动 | 调用方关掉了 | **没提供扰动后端** |
/// | 目标 | 调用方关掉了 | **没提供目标条件** |
/// | 局部敏感度 | — | 没提供固定整数 LP 后端 |
///
/// **两条理由混成一条，读者就无法区分"我关了它"和"环境里没有它"** ——
/// 前者是配置选择，后者是能力缺失，**而报告的可信度依赖这个区别。**
///
/// **Each stage has two distinct skip reasons whose wording has to stay apart**: the caller turned it off, or the environment did not supply it. **Merged into one, the reader cannot
/// tell "I switched it off" from "this build cannot do it"** — the first is a configuration choice, the second a capability gap, **and how much the report can be trusted depends on
/// that difference.**
/// </remarks>
public static class CriticalPipelineStages
{
    /// <summary>调用方关掉扰动阶段时的话 / What to say when the caller switched perturbation off.</summary>
    public const string PerturbationDisabled = "扰动阶段被调用方关闭 / The perturbation stage was disabled by the caller";

    /// <summary>没提供扰动后端时的话 / What to say when no perturbation backend was supplied.</summary>
    public const string PerturbationBackendMissing =
        "扰动后端未提供；有效性阶段返回 Unsupported / No perturbation backend was supplied; the effectiveness stage reports Unsupported";

    /// <summary>短名单为空时的话 / What to say when the shortlist came out empty.</summary>
    public const string ShortlistEmpty =
        "短名单为空，未执行任何扰动分析 / The shortlist was empty, so no perturbation was analyzed";

    /// <summary>调用方关掉目标阶段时的话 / What to say when the caller switched target analysis off.</summary>
    public const string TargetDisabled = "目标可行性阶段被调用方关闭 / The target-feasibility stage was disabled by the caller";

    /// <summary>没提供目标条件时的话 / What to say when no target was supplied.</summary>
    public const string TargetMissing =
        "未提供目标条件，阻塞性分析未执行 / No objective target was supplied, so blocking analysis did not run";

    /// <summary>调用方关掉局部敏感度时的话 / What to say when the caller switched local sensitivity off.</summary>
    public const string SensitivityDisabled = "局部有效性阶段被调用方关闭 / The local-effectiveness stage was disabled by the caller";

    /// <summary>没提供 LP 后端时的话 / What to say when no fixed-integer LP backend was supplied.</summary>
    public const string SensitivityBackendMissing =
        "固定整数 LP 后端未提供；局部有效性阶段返回 Unsupported / No fixed-integer LP backend was supplied; the local-sensitivity stage reports Unsupported";

    /// <summary>没提供冲突分析后端时的话 / What to say when no conflict backend was supplied.</summary>
    public const string ConflictBackendMissing =
        "冲突分析后端未提供；目标不可达但未给出阻塞原因 / No conflict backend was supplied; the target is unreachable but no blocking cause was produced";

    /// <summary>
    /// 决定扰动阶段跑不跑 / Decide whether the perturbation stage runs.
    /// </summary>
    /// <param name="enabled">调用方是否开启 / Whether the caller enabled it.</param>
    /// <param name="backendAvailable">是否提供了后端 / Whether a backend was supplied.</param>
    /// <param name="candidateCount">候选数量 / The number of candidates.</param>
    /// <param name="candidateLimit">候选上限 / The candidate limit.</param>
    /// <returns>决策 / The decision.</returns>
    /// <remarks>
    /// **判断顺序是"关掉 → 没后端 → 没候选"，而顺序会影响措辞。**
    ///
    /// 先判候选数的话，一个"关掉扰动且没有候选"的分析会报"短名单为空"——
    /// **而真正的原因是调用方根本没要它。** 按顺序判，报的是最上游的那个原因。
    ///
    /// **The order is disabled, then no backend, then no candidates, and the order decides the wording.** Checking the candidate count first would make a "perturbation off, no
    /// candidates" run report "the shortlist was empty" — **while the real reason is that the caller never asked for it.** Checking in order reports the most upstream reason.
    /// </remarks>
    public static StageDecision Perturbation(bool enabled, bool backendAvailable, int candidateCount, int candidateLimit) {
        if (!enabled) {
            return StageDecision.Skip(PerturbationDisabled);
        }
        if (!backendAvailable) {
            return StageDecision.Skip(PerturbationBackendMissing);
        }
        // **上限为 0 或候选为空，都是"没有可分析的短名单"。**
        // **A limit of zero and an empty candidate list both mean "no shortlist to analyse".**
        int shortlist = global::System.Math.Min(candidateCount, candidateLimit);
        return shortlist <= 0 ? StageDecision.Skip(ShortlistEmpty) : StageDecision.Run;
    }

    /// <summary>
    /// 决定目标可行性阶段跑不跑 / Decide whether the target-feasibility stage runs.
    /// </summary>
    /// <param name="enabled">调用方是否开启 / Whether the caller enabled it.</param>
    /// <param name="targetSupplied">是否提供了目标 / Whether a target was supplied.</param>
    /// <returns>决策 / The decision.</returns>
    /// <remarks>
    /// **"关掉"优先于"没提供"**：调用方明确关掉时，说"没提供目标"会让人以为漏传了参数。
    ///
    /// **"Disabled" outranks "not supplied"**: when the caller switched it off, saying "no target was supplied" reads like a forgotten argument.
    /// </remarks>
    public static StageDecision Target(bool enabled, bool targetSupplied) {
        if (!enabled) {
            return StageDecision.Skip(TargetDisabled);
        }
        return targetSupplied ? StageDecision.Run : StageDecision.Skip(TargetMissing);
    }

    /// <summary>
    /// 决定局部敏感度阶段跑不跑 / Decide whether the local-sensitivity stage runs.
    /// </summary>
    /// <param name="enabled">调用方是否开启 / Whether the caller enabled it.</param>
    /// <param name="backendAvailable">是否提供了 LP 后端 / Whether an LP backend was supplied.</param>
    /// <returns>决策 / The decision.</returns>
    public static StageDecision LocalSensitivity(bool enabled, bool backendAvailable) {
        if (!enabled) {
            return StageDecision.Skip(SensitivityDisabled);
        }
        return backendAvailable ? StageDecision.Run : StageDecision.Skip(SensitivityBackendMissing);
    }

    /// <summary>
    /// 每个"不跑"的理由都必须是不同的一句话 / Every skip reason has to be a distinct sentence.
    /// </summary>
    /// <returns>是否可区分 / Whether it is distinguishable.</returns>
    /// <remarks>
    /// 留一个可执行的自检：**八条理由两两不同**。合并其中任意两条，判据就红。
    ///
    /// A runnable self-check: **the eight reasons are pairwise distinct.** Merging any two turns the criterion red.
    /// </remarks>
    public static bool AreReasonsDistinct() {
        string[] reasons = [
            PerturbationDisabled, PerturbationBackendMissing, ShortlistEmpty,
            TargetDisabled, TargetMissing,
            SensitivityDisabled, SensitivityBackendMissing, ConflictBackendMissing,
        ];
        return new System.Collections.Generic.HashSet<string>(reasons, StringComparer.Ordinal).Count == reasons.Length;
    }

    /// <summary>
    /// 按有效性排序与漏斗排序给出候选顺序 / Rank candidates from the effectiveness ranking and the funnel.
    /// </summary>
    /// <param name="ranking">有效性排序，可为 <c>null</c> / The effectiveness ranking, or <c>null</c>.</param>
    /// <param name="funnel">漏斗排序，可为 <c>null</c> / The funnel ranking, or <c>null</c>.</param>
    /// <param name="limit">上限 / The limit.</param>
    /// <returns>候选约束的顺序 / The candidate constraint order.</returns>
    /// <remarks>
    /// **这是 `rankConstraintCandidates` 的实现。** 顺序只有一条规则：
    /// **有效性排序存在时它优先**，否则退到漏斗短名单，**两者都没有就是空。**
    ///
    /// **有效性优先是有意的**：漏斗只按对偶阈值分档，**而有效性排序是实测出来的**——
    /// **有实测就不该再退回启发式分档。**
    ///
    /// **This is the implementation of `rankConstraintCandidates`.** One rule orders everything: **the effectiveness ranking wins when present**, otherwise the funnel shortlist, **and neither means empty.**
    /// **Effectiveness first is deliberate**: the funnel only buckets by dual threshold, **while the effectiveness ranking is measured** — **once something is measured, falling back to a heuristic bucket is a regression.**
    /// </remarks>
    public static IReadOnlyList<ConstraintId> RankConstraintCandidates(
        EffectivenessRanking? ranking,
        CandidateFunnelRanking? funnel,
        int limit = CandidateFunnelConfig.DefaultCandidateLimitValue) {
        // **上限为 0 或负数返回空，而不是"不设上限"。**
        // **A limit of zero or less returns empty, not "no limit".**
        if (limit <= 0) {
            return Array.Empty<ConstraintId>();
        }
        if (ranking is not null) {
            return ranking.EntryList.Take(limit).Select(entry => entry.ConstraintId).ToArray();
        }
        return funnel is null
            ? Array.Empty<ConstraintId>()
            : funnel.Shortlist(limit).Select(candidate => candidate.ConstraintId).ToArray();
    }
}
