#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using SetRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.CorrectionSet, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>限制修正建议的策略 / Policy limiting correction recommendations.</summary>
/// <param name="MaxCandidates">最多保留的候选数 / Maximum number of candidates to retain.</param>
/// <param name="MaxPlans">最多保留的方案数 / Maximum number of plans to retain.</param>
/// <param name="RequirePositiveWeight">是否要求权重为正 / Whether weights must be positive.</param>
public sealed record RelaxabilityPolicy(
    int MaxCandidates = 32,
    int MaxPlans = 8,
    bool RequirePositiveWeight = true)
{
    /// <summary>在 Result 边界校验策略值 / Validate policy values at a Result boundary.</summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate() => MaxCandidates > 0 && MaxPlans > 0
        ? Results.OkInstance
        : Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "松弛策略上限必须为正 / Relaxability limits must be positive"));
}

/// <summary>可放宽的原始证据及其业务成本 / Original evidence that may be relaxed and its business cost.</summary>
/// <param name="Source">原始证据来源 / Original evidence source.</param>
/// <param name="Weight">业务权重 / Business weight.</param>
/// <param name="Relaxation">建议的放宽量 / Suggested relaxation amount.</param>
public sealed record CorrectionCandidate(DiagnosticSource Source, double Weight, double Relaxation)
{
    /// <summary>按推荐策略校验候选项 / Validate this candidate under a recommendation policy.</summary>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    /// <remarks>
    /// **`weight * relaxation` 必须是有限数**：两者各自有限但乘积溢出时，成本会变成 `Infinity`，
    /// 而按成本排序的方案会**把这条排到最后**——看起来像"最便宜的方案"。
    ///
    /// **`weight * relaxation` has to be finite**: when each is finite but the product overflows, the cost becomes `Infinity` and a cost-ranked plan puts it **last** — which
    /// reads as "the cheapest option".
    /// </remarks>
    public Try Validate(RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(Source);
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        Try policyValidation = settings.Validate();
        if (!policyValidation.IsOk) {
            return policyValidation;
        }
        if (!IsFinite(Weight) || !IsFinite(Relaxation) || Relaxation <= 0.0 || !IsFinite(Weight * Relaxation)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction weight/relaxation 必须有限且松弛量为正 / correction weight/relaxation must be finite with positive relaxation"));
        }
        if (settings.RequirePositiveWeight && Weight <= 0.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction weight 必须为正 / correction weight must be positive"));
        }
        return Results.OkInstance;
    }

    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>一个证据来源的业务成本与建议放宽量 / Business cost and suggested relaxation amount for one evidence source.</summary>
/// <param name="Weight">业务权重 / Business weight.</param>
/// <param name="Relaxation">建议的放宽量 / Suggested relaxation amount.</param>
public sealed record RelaxationCost(double Weight = 1.0, double Relaxation = 1.0)
{
    /// <summary>按推荐策略校验成本 / Validate this cost under a recommendation policy.</summary>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate(RelaxabilityPolicy? policy = null) {
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        Try policyValidation = settings.Validate();
        if (!policyValidation.IsOk) {
            return policyValidation;
        }
        if (!CorrectionCandidate.IsFinite(Weight) || !CorrectionCandidate.IsFinite(Relaxation)
            || Relaxation <= 0.0 || !CorrectionCandidate.IsFinite(Weight * Relaxation)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction weight/relaxation 必须有限且松弛量为正 / correction weight/relaxation must be finite with positive relaxation"));
        }
        if (settings.RequirePositiveWeight && Weight <= 0.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction weight 必须为正 / correction weight must be positive"));
        }
        return Results.OkInstance;
    }
}

/// <summary>已校验的加权修正集合 / A validated weighted correction set.</summary>
/// <param name="Members">修正候选成员 / Correction candidates.</param>
/// <param name="TotalCost">成员总成本 / Total member cost.</param>
/// <param name="Minimal">是否已验证删除最小性 / Whether deletion minimality was verified.</param>
public sealed record CorrectionSet(
    IReadOnlyList<CorrectionCandidate> Members,
    double TotalCost,
    bool Minimal = false)
{
    /// <summary>是否验证了删除最小性 / Whether deletion minimality was verified for this candidate set.</summary>
    /// <returns>是否已验证 / Whether it was verified.</returns>
    public bool DeletionMinimalityVerified() => Minimal;

    /// <summary>本协议不会证明完整模型范围 MCS / This protocol never proves a complete model-wide MCS.</summary>
    /// <returns>恒为 <c>false</c> / Always <c>false</c>.</returns>
    public bool IsCompleteMcs() => false;

    /// <summary>数值放宽只是建议，始终需要调用方重新校验 / Numeric relaxation is a recommendation and always needs caller-side revalidation.</summary>
    public bool RequiresRevalidation => true;

    /// <summary>是否需要重新校验（方法形式）/ Whether revalidation is required (method form).</summary>
    /// <returns>恒为 <c>true</c> / Always <c>true</c>.</returns>
    public bool RequiresRevalidationMethod() => RequiresRevalidation;

    /// <summary>按推荐策略校验 correction set / Validate this set under a recommendation policy.</summary>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate(RelaxabilityPolicy? policy = null) {
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        Try policyValidation = settings.Validate();
        if (!policyValidation.IsOk) {
            return policyValidation;
        }
        if (Members.Count == 0 || Members.Count > settings.MaxCandidates) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction set 大小超出策略范围 / correction set size is outside policy"));
        }

        var seen = new HashSet<string>();
        double expected = 0.0;
        foreach (CorrectionCandidate member in Members) {
            Try memberValidation = member.Validate(settings);
            if (!memberValidation.IsOk) {
                return memberValidation;
            }
            if (!seen.Add(member.Source.StableId)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "correction set 不得包含重复证据 / correction set contains duplicate evidence"));
            }
            expected += member.Weight * member.Relaxation;
        }

        // **总成本必须与成员一致**：不一致的集合会让"按成本排序"给出一个成员成本解释不了的顺序。
        // **The total has to agree with the members**: an inconsistent set makes a cost ranking produce an order its members cannot explain.
        return CorrectionCandidate.IsFinite(expected) && global::System.Math.Abs(TotalCost - expected) <= 1e-9
            ? Results.OkInstance
            : Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "correction set 总成本不一致 / correction set total cost is inconsistent"));
    }
}

/// <summary>一个达到目标条件的备选方案 / One alternative plan for reaching an objective target.</summary>
/// <param name="Target">目标条件 / Objective target.</param>
/// <param name="CorrectionSet">修正集合 / Correction set.</param>
/// <param name="Rank">方案排序名次 / Plan rank.</param>
public sealed record AlternativeImprovementPlan(ObjectiveTarget Target, CorrectionSet CorrectionSet, int Rank)
{
    /// <summary>是否为完整模型范围 MCS / Whether this plan is a complete model-wide MCS.</summary>
    /// <returns>恒为 <c>false</c> / Always <c>false</c>.</returns>
    public bool IsCompleteMcs() => CorrectionSet.IsCompleteMcs();

    /// <summary>应用数值候选项不能证明目标已经可达 / Applying a numeric candidate never proves that this target is now reachable.</summary>
    public bool RequiresRevalidation => CorrectionSet.RequiresRevalidation;

    /// <summary>按推荐策略校验改进方案 / Validate this plan under a recommendation policy.</summary>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate(RelaxabilityPolicy? policy = null) {
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        Try policyValidation = settings.Validate();
        if (!policyValidation.IsOk) {
            return policyValidation;
        }
        Try targetValidation = Target.Validate();
        if (!targetValidation.IsOk) {
            return targetValidation;
        }
        Try setValidation = CorrectionSet.Validate(settings);
        if (!setValidation.IsOk) {
            return setValidation;
        }
        return Rank < settings.MaxPlans
            ? Results.OkInstance
            : Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "改进方案名次超出策略范围 / improvement plan rank exceeds policy"));
    }
}

/// <summary>修正集代数 / The correction-set algebra.</summary>
/// <remarks>
/// 只实现这一半**：434 行里，尾部约 110 行要 `ConflictExplanation`，而后者住在
/// **带后端求解**的 里。**没实现的那部分没有空跑版本。**
///
/// **Only this half is implemented**: of its 434 lines, the last ~110 need `ConflictExplanation`, which lives inside the **backend-solving**. **Nothing was
/// stubbed in its place.**
/// </remarks>
public static class CorrectionRecommendations
{
    /// <summary>
    /// 构造确定性的加权修正集合，只保留策略允许的候选项 / Build a deterministic weighted correction set, retaining only policy-approved candidates.
    /// </summary>
    /// <param name="candidates">候选项 / Candidate corrections.</param>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>加权修正集合 / Weighted correction set.</returns>
    /// <exception cref="ArgumentException">某个候选项的权重不为正 / A candidate's weight is not positive.</exception>
    public static CorrectionSet WeightedCorrectionSet(
        IReadOnlyList<CorrectionCandidate> candidates,
        RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(candidates);
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        if (candidates.Any(candidate => settings.RequirePositiveWeight && candidate.Weight <= 0.0)) {
            throw new ArgumentException(
                "correction weight 必须为正 / correction weight must be positive", nameof(candidates));
        }

        CorrectionCandidate[] selected = candidates
            .OrderBy(candidate => candidate.Weight * candidate.Relaxation)
            .ThenBy(candidate => candidate.Source.StableId, StringComparer.Ordinal)
            .Take(settings.MaxCandidates)
            .ToArray();

        // **`CorrectionSet` 的 `init` 里做这四条检查，所以构造出来就一定是合法的。**
        // **C# 这边检查写在 `Validate` 里，而它没有任何构造路径调用**——
        // **于是"空候选"能造出一个修复不了任何东西的修复计划，"重复来源"能把同一份证据算两遍价。**
        //
 // **This implementation does these four checks in `CorrectionSet`'s `init`, so a constructed set is always legal.**
        // **On the C# side the checks live in `Validate`, and formerly no construction path called it** —
        // **so empty candidates could produce a repair plan that repairs nothing, and a duplicate source could price the same evidence twice.**
        return Validated(new CorrectionSet(selected, selected.Sum(candidate => candidate.Weight * candidate.Relaxation)), settings);
    }

    /// <summary>
    /// 构造后立刻校验不变量 / Validate the invariants right after construction.
    /// </summary>
    /// <param name="set">刚构造的集合 / The freshly built set.</param>
    /// <param name="policy">策略 / The policy.</param>
    /// <returns>同一个集合 / The same set.</returns>
    /// <exception cref="ArgumentException">不变量被违反 / An invariant is violated.</exception>
    /// <remarks>
    /// **这是"把检查放在构造路径上"而不是"多提供一个 Validate 方法"。**
    /// **`Validate` 一直在，而它此前一个调用方都没有**——
    /// **一个没人调用的检查，和没写过的检查在出事那一刻完全一样。**
    ///
    /// **This puts the check on the construction path instead of merely offering a `Validate` method.**
    /// **`Validate` has always existed and had no caller at all** — **and a check nobody calls is indistinguishable from an unwritten one at the moment things go wrong.**
    /// </remarks>
    private static CorrectionSet Validated(CorrectionSet set, RelaxabilityPolicy policy) {
        Try validation = set.Validate(policy);
        return validation.IsOk
            ? set
            : throw new ArgumentException(
                "correction set 不满足不变量 / correction set violates its invariants", nameof(set));
    }

    /// <summary>面向不接受异常调用方的 Result 形式加权构造器 / Result-form weighted correction builder.</summary>
    /// <param name="candidates">候选项 / Candidate corrections.</param>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>加权修正集合结果 / Result containing the weighted correction set.</returns>
    public static SetRet TryWeightedCorrectionSet(
        IReadOnlyList<CorrectionCandidate> candidates,
        RelaxabilityPolicy? policy = null) {
        try {
            return Results.Ok(WeightedCorrectionSet(candidates, policy));
        }
        catch (ArgumentException error) {
            return Results.Failed<CorrectionSet>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "构造加权 correction set 失败 / Failed to build weighted correction set: " + error.Message));
        }
    }

    /// <summary>
    /// 构造调用方收缩器提供的、已明确校验的最小修正集合 / Construct an explicitly validated minimal correction set.
    /// </summary>
    /// <param name="candidates">候选项 / Candidate corrections.</param>
    /// <param name="verified">是否已完成删除验证 / Whether deletion verification completed.</param>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>最小修正集合 / Minimal correction set.</returns>
    /// <exception cref="ArgumentException">调用方没有完成删除验证 / The caller did not complete deletion verification.</exception>
    /// <remarks>
    /// **策略一旦截断输入，就不能再声称最小性**：删除验证只覆盖被截断前的候选全集，
    /// 而返回的集合已经不是那个全集。**保留加权建议，但不声称它是已验证的最小集合。**
    ///
    /// **Once the policy truncates the input, minimality may no longer be claimed**: deletion verification covered the untruncated candidate universe, and the returned set is no
    /// longer that universe. **Keep the weighted recommendation, but do not claim it is a verified minimal set.**
    /// </remarks>
    public static CorrectionSet MinimalCorrectionSet(
        IReadOnlyList<CorrectionCandidate> candidates,
        bool verified,
        RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(candidates);
        RelaxabilityPolicy settings = policy ?? new RelaxabilityPolicy();

        if (!verified) {
            throw new ArgumentException(
                "最小修正集合需要删除验证 / minimal correction set requires deletion verification", nameof(verified));
        }

        CorrectionSet weighted = WeightedCorrectionSet(candidates, settings);
        return Validated(weighted with { Minimal = candidates.Count <= settings.MaxCandidates }, settings);
    }

    /// <summary>面向不接受异常调用方的 Result 形式最小修正构造器 / Result-form minimal correction builder.</summary>
    /// <param name="candidates">候选项 / Candidate corrections.</param>
    /// <param name="verified">是否已完成删除验证 / Whether deletion verification completed.</param>
    /// <param name="policy">修正建议策略 / Correction recommendation policy.</param>
    /// <returns>最小修正集合结果 / Result containing the minimal correction set.</returns>
    public static SetRet TryMinimalCorrectionSet(
        IReadOnlyList<CorrectionCandidate> candidates,
        bool verified,
        RelaxabilityPolicy? policy = null) {
        try {
            return Results.Ok(MinimalCorrectionSet(candidates, verified, policy));
        }
        catch (ArgumentException error) {
            return Results.Failed<CorrectionSet>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "构造最小 correction set 失败 / Failed to build minimal correction set: " + error.Message));
        }
    }

    /// <summary>
    /// 把冲突解释转成带名次的改进方案 / Turn a conflict explanation into ranked improvement plans.
    /// </summary>
    /// <param name="conflict">已校验的冲突解释 / The verified conflict explanation.</param>
    /// <param name="costs">按稳定来源 ID 给出的成本 / Costs keyed by stable source ID.</param>
    /// <param name="policy">修正建议策略 / The correction recommendation policy.</param>
    /// <returns>按名次排好的方案 / The plans, in rank order.</returns>
    /// <remarks>
    /// **`Rank` 是列表下标，不是评分。** 它只在"同一个冲突、同一套成本、同一份策略"下可比，
    /// **而 `AlternativeImprovementPlan.Validate` 会拿它和 `MaxPlans` 比**——所以名次越界是一个真实的配置错误，
    /// 不是显示问题。
    ///
    /// **`Rank` is the list index, not a score.** It is only comparable within one conflict, one cost table and one policy, **and `AlternativeImprovementPlan.Validate` compares it against `MaxPlans`** —
    /// so an out-of-range rank is a real configuration error, not a display quirk.
    /// </remarks>
    public static IReadOnlyList<AlternativeImprovementPlan> AlternativeImprovementPlansFromConflict(
        ConflictExplanation conflict,
        IReadOnlyDictionary<string, RelaxationCost>? costs = null,
        RelaxabilityPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(conflict);

        return CorrectionProjection.WeightedCorrectionSetsFromConflict(conflict, costs, policy)
            .Select((correctionSet, rank) => new AlternativeImprovementPlan(
                Target: conflict.Target,
                CorrectionSet: correctionSet,
                Rank: rank))
            .ToArray();
    }

    /// <summary>
    /// 放宽建议的别名 / An alias for the relaxation recommendations.
    /// </summary>
    /// <param name="conflict">已校验的冲突解释 / The verified conflict explanation.</param>
    /// <param name="costs">按稳定来源 ID 给出的成本 / Costs keyed by stable source ID.</param>
    /// <param name="policy">修正建议策略 / The correction recommendation policy.</param>
    /// <returns>放宽建议方案 / The relaxation recommendation plans.</returns>
    /// <remarks>
    /// **它只是转发。** 保留同一个别名，**因为调用方读的是"放宽建议"这个意图，而不是"改进方案"这个形状**。
    ///
 /// **In the implementation it only forwards.** The alias is kept because **the caller reads the intent "relaxation recommendation", not the shape "improvement plan".**
    /// </remarks>
    public static IReadOnlyList<AlternativeImprovementPlan> RelaxationRecommendationsFromConflict(
        ConflictExplanation conflict,
        IReadOnlyDictionary<string, RelaxationCost>? costs = null,
        RelaxabilityPolicy? policy = null) =>
        AlternativeImprovementPlansFromConflict(conflict, costs, policy);
}
