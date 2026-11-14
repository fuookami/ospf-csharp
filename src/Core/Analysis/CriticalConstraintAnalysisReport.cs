#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using FunnelRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.CandidateFunnelRanking, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>候选分析报告的 schema 版本 / The candidate analysis report schema version.</summary>
public static class CriticalAnalysisReport {
    /// <summary>报告 schema 版本 / Report schema version.</summary>
    public const string SchemaVersion = "1.0";
}

/// <summary>候选分层 / The candidate tier.</summary>
public enum CandidateTier
{
    /// <summary>active 且 |dual| 超过阈值 / Active with a dual above the threshold.</summary>
    TierA,

    /// <summary>active 但 dual 约为 0 / Active with an approximately zero dual.</summary>
    TierB,

    /// <summary>非 active / Not active.</summary>
    TierC,

    /// <summary>证据不足，无法分层 / Insufficient evidence to assign a tier.</summary>
    Unclassified,
}

/// <summary>候选层级的谓词与稳定名称 / Tier predicates and stable names.</summary>
public static class CandidateTiers
{
    /// <summary>计划中的层级名称 / Tier name used by the plan.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>层级名称 / The tier name.</returns>
    public static string TierName(this CandidateTier tier) => tier switch
    {
        CandidateTier.TierA => "A",
        CandidateTier.TierB => "B",
        CandidateTier.TierC => "C",
        _ => "UNCLASSIFIED",
    };

    /// <summary>该层是否默认进入昂贵分析短名单 / Whether this tier enters the expensive shortlist.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>是否默认进入短名单 / Whether it enters the shortlist by default.</returns>
    public static bool IsShortlistDefault(this CandidateTier tier) =>
        tier is CandidateTier.TierA or CandidateTier.TierB;
}

/// <summary>漏斗配置 / Funnel configuration.</summary>
/// <param name="DualThreshold">|dual| 超过该值才算强候选 / A dual above this value marks a strong candidate.</param>
/// <param name="DefaultCandidateLimit">昂贵分析的默认候选上限 / Default candidate limit for expensive analysis.</param>
public sealed record CandidateFunnelConfig(
    double DualThreshold = CandidateFunnelConfig.DefaultDualThreshold,
    int DefaultCandidateLimit = CandidateFunnelConfig.DefaultCandidateLimitValue)
{
    /// <summary>默认对偶阈值 / Default dual threshold.</summary>
    public const double DefaultDualThreshold = 1e-9;

    /// <summary>默认候选上限 / Default candidate limit.</summary>
    public const int DefaultCandidateLimitValue = 32;

    /// <summary>
    /// 校验配置 / Validate the configuration.
    /// </summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate() {
        if (double.IsNaN(DualThreshold) || double.IsInfinity(DualThreshold) || DualThreshold < 0.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "候选漏斗对偶阈值必须有限且非负 / Candidate funnel dual threshold must be finite and non-negative"));
        }
        if (DefaultCandidateLimit <= 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "候选漏斗候选上限必须为正 / Candidate funnel candidate limit must be positive"));
        }
        return Results.OkInstance;
    }
}

/// <summary>一个候选约束 / One candidate constraint.</summary>
/// <param name="ConstraintId">原始约束稳定身份 / Stable original-constraint identity.</param>
/// <param name="Group">约束组 / Constraint group.</param>
/// <param name="Tier">分层 / Tier.</param>
/// <param name="Activity">活动性状态 / Activity status.</param>
/// <param name="Slack">有符号松弛 / Signed slack.</param>
/// <param name="DualValue">固定整数 LP 对偶值 / Fixed-integer LP dual value.</param>
/// <param name="LocalEffective">局部有效性 / Local effectiveness.</param>
/// <param name="Priority">排序位置，0 表示优先级最高 / Rank position; 0 is the highest priority.</param>
public sealed record ConstraintCandidate(
    ConstraintId ConstraintId,
    string? Group,
    CandidateTier Tier,
    ActivityStatus Activity,
    double? Slack,
    Flt64? DualValue,
    bool? LocalEffective,
    int Priority)
{
    /// <summary>原始证据来源 / Original evidence source.</summary>
    public DiagnosticSource Source => new DiagnosticSource.Constraint(ConstraintId);
}

/// <summary>候选漏斗排序结果 / Candidate funnel ranking.</summary>
/// <param name="DualThreshold">使用的对偶阈值 / Dual threshold used.</param>
/// <param name="Candidates">全部候选，按优先级排序 / Every candidate, ordered by priority.</param>
/// <param name="TierA">Tier A 数量 / Tier A count.</param>
/// <param name="TierB">Tier B 数量 / Tier B count.</param>
/// <param name="TierC">Tier C 数量 / Tier C count.</param>
/// <param name="Unclassified">无法分层的数量 / Unclassified count.</param>
/// <param name="SchemaVersion">报告 schema / Report schema.</param>
public sealed record CandidateFunnelRanking(
    double DualThreshold,
    IReadOnlyList<ConstraintCandidate> Candidates,
    int TierA,
    int TierB,
    int TierC,
    int Unclassified,
    string SchemaVersion = CriticalAnalysisReport.SchemaVersion)
{
    /// <summary>
    /// 昂贵分析短名单 / Shortlist for expensive analysis.
    /// </summary>
    /// <param name="limit">名额上限 / Slot limit.</param>
    /// <returns>短名单 / The shortlist.</returns>
    /// <remarks>
    /// 短名单优先包含强候选与退化候选；Tier C 及未分层候选**只在仍有名额时**才被纳入，
    /// 因此它们被降权而**不会被永久丢弃**。
    ///
    /// The shortlist prefers strong and degenerate candidates; Tier C and unclassified candidates are appended **only while slots remain**, so they are deprioritised
    /// rather than **dropped**.
    /// </remarks>
    public IReadOnlyList<ConstraintCandidate> Shortlist(int limit = CandidateFunnelConfig.DefaultCandidateLimitValue) {
        if (limit <= 0) {
            return Array.Empty<ConstraintCandidate>();
        }
        ConstraintCandidate[] preferred = Candidates.Where(candidate => candidate.Tier.IsShortlistDefault()).ToArray();
        if (preferred.Length >= limit) {
            return preferred.Take(limit).ToArray();
        }
        ConstraintCandidate[] fallback = Candidates.Where(candidate => !candidate.Tier.IsShortlistDefault()).ToArray();
        return preferred.Concat(fallback.Take(limit - preferred.Length)).ToArray();
    }

    /// <summary>分层查找 / Look up candidates by tier.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>该层级的候选 / The candidates in that tier.</returns>
    public IReadOnlyList<ConstraintCandidate> WithTier(CandidateTier tier) =>
        Candidates.Where(candidate => candidate.Tier == tier).ToArray();

    /// <summary>
    /// 校验报告不变量 / Validate report invariants.
    /// </summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    /// <remarks>
    /// **`TierA` 必须有超过阈值的对偶，`TierB` 必须有不超过阈值的对偶，`TierC` 必须不是 active**：
    /// 这三条合起来保证"分层"和"证据"不会互相矛盾。少了任何一条，一份自相矛盾的报告都能通过校验。
    ///
    /// **`TierA` needs a dual above the threshold, `TierB` one at or below it, and `TierC` must not be active**: together these keep the tier and the evidence from
    /// contradicting each other. Drop any one and a self-contradictory report validates clean.
    /// </remarks>
    public Try Validate() {
        if (string.IsNullOrWhiteSpace(SchemaVersion)
            || double.IsNaN(DualThreshold) || double.IsInfinity(DualThreshold) || DualThreshold < 0.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "候选漏斗报告无效 / Candidate funnel ranking is invalid"));
        }

        var seen = new HashSet<ConstraintId>();
        for (int position = 0; position < Candidates.Count; position++) {
            ConstraintCandidate candidate = Candidates[position];
            if (string.IsNullOrWhiteSpace(candidate.ConstraintId.Value) || candidate.Priority != position) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "候选优先级必须连续且与稳定身份一致 / Candidate funnel priorities must be dense and match stable identities"));
            }
            if (!seen.Add(candidate.ConstraintId)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "候选漏斗包含重复约束 / Candidate funnel contains duplicate constraints"));
            }
            switch (candidate.Tier) {
                case CandidateTier.TierA:
                    if (candidate.DualValue is not { } tierADual) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "Tier A 候选必须有对偶值 / Tier A candidate requires a dual value"));
                    }
                    if (tierADual.ToDouble() <= DualThreshold) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "Tier A 候选的对偶值必须超过阈值 / Tier A candidate requires a dual above the threshold"));
                    }
                    break;
                case CandidateTier.TierB:
                    if (candidate.DualValue is not { } tierBDual) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "Tier B 候选必须有对偶值 / Tier B candidate requires a dual value"));
                    }
                    if (tierBDual.ToDouble() > DualThreshold) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "Tier B 候选的对偶值不得超过阈值 / Tier B candidate requires a dual at or below the threshold"));
                    }
                    break;
                case CandidateTier.TierC:
                    if (candidate.Activity is ActivityStatus.Active or ActivityStatus.NearlyActive) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "Tier C 候选不得为 active / Tier C candidate must not be active"));
                    }
                    break;
                default:
                    break;
            }
        }

        foreach ((CandidateTier tier, int expected) in new[] {
            (CandidateTier.TierA, TierA),
            (CandidateTier.TierB, TierB),
            (CandidateTier.TierC, TierC),
            (CandidateTier.Unclassified, Unclassified),
        }) {
            if (WithTier(tier).Count != expected) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "候选漏斗层级计数与候选列表不一致 / Candidate funnel tier counts do not match the candidate list"));
            }
        }
        return Results.OkInstance;
    }
}


/// <summary>
/// 候选分层与漏斗构建 / Candidate classification and funnel construction.
/// </summary>
/// <remarks>
/// **基线的 `classifyCandidate` / `buildCandidateFunnel` 是顶层函数，C# 没有顶层函数**，所以它们收在这里。
/// 收进一个带名字的类不是装饰：**它让"这是一组报告侧纯函数"这件事有个地方可写。**
///
/// **The baseline's `classifyCandidate` and `buildCandidateFunnel` are top-level functions and C# has none**, so they live here. Putting them in a named class is not
/// decoration: **it gives the fact that these are report-side pure functions somewhere to be stated.**
/// </remarks>
public static class CriticalCandidateFunnel {

    /// <summary>
    /// 分层一条候选 / Classify one candidate.
    /// </summary>
    /// <param name="activity">活动性状态 / Activity status.</param>
    /// <param name="dualValue">对偶值 / Dual value.</param>
    /// <param name="dualThreshold">对偶阈值 / Dual threshold.</param>
    /// <returns>候选层级 / The candidate tier.</returns>
    /// <remarks>
    /// **只有同时具备活动性与对偶证据时才能给出分层**；否则返回 `Unclassified`，
    /// 以免把"未知"当成"不重要"。
    ///
    /// **A tier requires both activity and dual evidence**; otherwise the candidate is `Unclassified`, so "unknown" can never be read as "unimportant".
    /// </remarks>
        public static CandidateTier ClassifyCandidate(
        ActivityStatus activity,
        Flt64? dualValue,
        double dualThreshold = CandidateFunnelConfig.DefaultDualThreshold) {
        if (activity is ActivityStatus.Active or ActivityStatus.NearlyActive) {
            if (dualValue is not { } dual) {
                return CandidateTier.Unclassified;
            }
            return dual.Abs().ToDouble() > dualThreshold ? CandidateTier.TierA : CandidateTier.TierB;
        }
        if (activity is ActivityStatus.Inactive or ActivityStatus.SatisfiedWithoutSlackMetric) {
            return CandidateTier.TierC;
        }
        return CandidateTier.Unclassified;
    }

    /// <summary>
    /// 构建候选漏斗 / Build the candidate funnel.
    /// </summary>
    /// <param name="activity">活动性报告 / The activity report.</param>
    /// <param name="sensitivity">敏感性报告，可为 <c>null</c> / The sensitivity report, or <c>null</c>.</param>
    /// <param name="config">漏斗配置 / Funnel configuration.</param>
    /// <returns>候选漏斗排序 / The candidate funnel ranking.</returns>
    /// <remarks>
    /// `sensitivity` 为 `null` 时，所有候选都只能停在 `Unclassified`：**没有对偶证据就不允许声称分层**。
    /// Tier C 依然按活动性给出，并保留在结果中。
    ///
    /// When `sensitivity` is `null` every candidate stays `Unclassified`: **without dual evidence no tier may be claimed**. Tier C is still derived from activity and retained.
    /// </remarks>
        public static FunnelRet BuildCandidateFunnel(
        ConstraintActivityReport activity,
        FixedIntegerLpSensitivityReport? sensitivity,
        CandidateFunnelConfig? config = null) {
        ArgumentNullException.ThrowIfNull(activity);
        CandidateFunnelConfig settings = config ?? new CandidateFunnelConfig();

        Try validation = settings.Validate();
        if (!validation.IsOk) {
            return Results.Failed<CandidateFunnelRanking>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "候选漏斗配置无效 / Candidate funnel configuration is invalid"));
        }

        IReadOnlyDictionary<ConstraintId, Flt64?> duals = sensitivity is null
            ? new Dictionary<ConstraintId, Flt64?>()
            : sensitivity.LocalSensitivities
                .GroupBy(sensitivityRecord => sensitivityRecord.ConstraintId)
                .ToDictionary(group => group.Key, group => group.Last().DualValue);

        // **每个候选只查一次对偶值**：三次 `TryGetValue` 的写法看起来一样，但它在"有 dual"和
        // "dual 用到阈值"之间留了一条会各自走开的口子——两个分支用的是同一份数据，就该只取一次。
        // **Each candidate looks up its dual once**: three `TryGetValue` calls look equivalent, but they let the dual and the threshold test drift apart — one datum, one read.
        ConstraintCandidate[] ordered = activity.ConstraintActivities
            .Select(record => {
                ConstraintId id = record.ConstraintId!.Value;
                Flt64? dual = duals.TryGetValue(id, out Flt64? found) ? found : null;
                return new ConstraintCandidate(
                    id,
                    record.Group,
                    CriticalCandidateFunnel.ClassifyCandidate(record.Status, dual, settings.DualThreshold),
                    record.Status,
                    record.Slack,
                    dual,
                    dual is { } available ? available.Abs().ToDouble() > settings.DualThreshold : null,
                    0);
            })
            // 优先级：先按层级，再按 |dual| 降序，最后按稳定身份保证确定性。
            // Priority: tier order, then descending |dual|, then stable identity for determinism.
            .OrderBy(candidate => candidate.Tier)
            .ThenByDescending(candidate => candidate.DualValue is { } value ? value.Abs().ToDouble() : double.NegativeInfinity)
            .ThenBy(candidate => candidate.ConstraintId.Value, StringComparer.Ordinal)
            .Select((candidate, position) => candidate with { Priority = position })
            .ToArray();

        var ranking = new CandidateFunnelRanking(
            settings.DualThreshold,
            ordered,
            ordered.Count(candidate => candidate.Tier == CandidateTier.TierA),
            ordered.Count(candidate => candidate.Tier == CandidateTier.TierB),
            ordered.Count(candidate => candidate.Tier == CandidateTier.TierC),
            ordered.Count(candidate => candidate.Tier == CandidateTier.Unclassified));

        Try rankingValidation = ranking.Validate();
        if (!rankingValidation.IsOk) {
            return Results.Failed<CandidateFunnelRanking>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "候选漏斗报告无效 / Candidate funnel ranking is invalid"));
        }
        return Results.Ok(ranking);
    }
}
