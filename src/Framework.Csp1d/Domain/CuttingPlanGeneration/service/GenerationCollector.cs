#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 把覆盖函数列表折叠成单个 dominance 判据 / Fold override lists into one dominance predicate.
/// </summary>
/// <remarks>
/// **语义必须与基线一致**：`input.dominanceAcceptOverrides.all { it(plan, existing) }`——
/// **全部通过才接受**，不是"任一通过"。列表为空时返回 null（不额外过滤）。
///
/// **这段逻辑只写一次**，四个生成器共用——否则四处复制的 `All(...)` 迟早会有一处写成 `Any(...)`。
///
/// **The semantics must match the baseline**: `input.dominanceAcceptOverrides.all { it(plan, existing) }` — **all must pass, not any**. An empty list returns null (no
/// extra filtering).
///
/// **This logic is written once** and shared by the four generators — otherwise one of four copies of `All(...)` would eventually be written as `Any(...)`.
/// </remarks>
internal static class GenerationOverrideResolution {
    /// <summary>
    /// 折叠候选过滤 / Fold candidate filters.
    /// </summary>
    /// <remarks>
    /// **与 dominance 折叠同语义（全部通过）但用途不同**：候选过滤在**去重之前**生效，
    /// dominance 覆盖在**去重之后**。两者不能合并成一个助手——合并了就说不清哪个先跑。
    ///
    /// **Same semantics as the dominance fold (all must pass) but a different purpose**: the candidate filter runs **before** deduplication and the dominance override
    /// **after**. They must not be merged into one helper — merging would make the ordering unstateable.
    /// </remarks>
    /// <param name="filters">过滤函数列表 / Filter function list.</param>
    /// <returns>折叠后的判据，或 null / The folded predicate, or null.</returns>
    public static global::System.Func<TPlan, IReadOnlyList<TPlan>, bool>? FoldCandidateFilters<TPlan>(
        IReadOnlyList<global::System.Func<TPlan, IReadOnlyList<TPlan>, bool>>? filters) {
        if (filters is null || filters.Count == 0) {
            return null;
        }
        return (plan, existing) => {
            foreach (global::System.Func<TPlan, IReadOnlyList<TPlan>, bool> filter in filters) {
                if (!filter(plan, existing)) {
                    return false;
                }
            }
            return true;
        };
    }

    /// <summary>
    /// 折叠 dominance 覆盖 / Fold dominance overrides.
    /// </summary>
    /// <param name="overrides">覆盖函数列表 / Override function list.</param>
    /// <returns>折叠后的判据，或 null / The folded predicate, or null.</returns>
    public static global::System.Func<TPlan, IReadOnlyList<TPlan>, bool>? Fold<TPlan>(
        IReadOnlyList<global::System.Func<TPlan, IReadOnlyList<TPlan>, bool>>? overrides) {
        if (overrides is null || overrides.Count == 0) {
            return null;
        }
        return (plan, existing) => {
            foreach (global::System.Func<TPlan, IReadOnlyList<TPlan>, bool> override_ in overrides) {
                if (!override_(plan, existing)) {
                    return false;
                }
            }
            return true;
        };
    }
}

/// <summary>
/// 生成收集器，负责收集、去重和统计生成的切割方案 / Generation collector for collecting, deduplicating, and tracking generated cutting plans.
/// </summary>
/// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
internal sealed class GenerationCollector<TPlan> {
    private readonly long _maxPlans;
    private readonly long? _deadlineMs;
    private readonly bool _enableDominancePruning;
    private readonly DominanceStrategy _dominanceStrategy;
    private readonly Func<TPlan, CuttingPlanCanonicalKey>? _canonicalKeyResolver;
    private readonly Func<TPlan, IReadOnlyList<TPlan>, bool>? _dominanceAcceptOverride;
    private readonly Func<TPlan, IReadOnlyList<TPlan>, bool>? _candidateFilter;
    private readonly IReadOnlyList<TPlan> _existingPlansForFilter;
    private readonly Func<TPlan, CrossContributionProjection?>? _crossContributionProjector;

    private readonly HashSet<CuttingPlanCanonicalKey> _canonicalKeys = new();
    private readonly List<TPlan> _acceptedPlans = new();

    private long _visitedNodes;
    private long _generatedCandidates;
    private long _infeasibleCandidates;
    private long _duplicateCandidates;
    private long _dominatedCandidates;
    private long _filteredCandidates;
    private long _widthBoundPrunedNodes;
    private long _knifeBoundPrunedNodes;
    private long _lengthBoundPrunedEntries;
    private long _materialWidthIndexCacheHits;
    private long _materialSliceTemplateCacheHits;
    private long _materialSliceTemplateCacheMisses;
    private long _quantityCacheHits;
    private long _quantityCacheMisses;
    private long _crossContributionDominated;
    private bool _timedOut;

    /// <summary>
    /// 创建生成收集器 / Create generation collector.
    /// </summary>
    /// <param name="maxPlans">最大方案数 / Max plans.</param>
    /// <param name="deadlineMs">截止时间（毫秒，可空）/ Deadline in milliseconds (nullable).</param>
    /// <param name="enableDominancePruning">是否启用 dominance 剪枝 / Enable dominance pruning.</param>
    /// <param name="dominanceStrategy">dominance 策略 / Dominance strategy.</param>
    /// <param name="canonicalKeyResolver">自定义 canonical key 解析器 / Custom canonical key resolver.</param>
    /// <param name="dominanceAcceptOverride">自定义 dominance 接受函数 / Custom dominance acceptance function.</param>
    /// <param name="candidateFilter">候选过滤函数；为 null 时不过滤 / Candidate filter; null means no filtering.</param>
    /// <param name="existingPlansForFilter">
    /// 传给候选过滤的"已有方案"列表，对应基线 `input.existingPlans` / The "existing plans" list passed to the candidate filter, matching the baseline's
    /// `input.existingPlans`.
    /// </param>
    /// <param name="crossContributionProjector">跨贡献投影器；为 null 时跨贡献剪枝不发生 / Cross-contribution projector; when null, cross-contribution pruning does not happen.</param>
    public GenerationCollector(
        long maxPlans,
        long? deadlineMs = null,
        bool enableDominancePruning = false,
        DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution,
        Func<TPlan, CuttingPlanCanonicalKey>? canonicalKeyResolver = null,
        Func<TPlan, IReadOnlyList<TPlan>, bool>? dominanceAcceptOverride = null,
        Func<TPlan, IReadOnlyList<TPlan>, bool>? candidateFilter = null,
        IReadOnlyList<TPlan>? existingPlansForFilter = null,
        Func<TPlan, CrossContributionProjection?>? crossContributionProjector = null) {
        _maxPlans = maxPlans;
        _deadlineMs = deadlineMs;
        _enableDominancePruning = enableDominancePruning;
        _dominanceStrategy = dominanceStrategy;
        _canonicalKeyResolver = canonicalKeyResolver;
        _dominanceAcceptOverride = dominanceAcceptOverride;
        _candidateFilter = candidateFilter;
        _existingPlansForFilter = existingPlansForFilter ?? System.Array.Empty<TPlan>();
        _crossContributionProjector = crossContributionProjector;
    }

    /// <summary>已接受的方案列表 / Accepted plan list.</summary>
    public IReadOnlyList<TPlan> Plans => _acceptedPlans;

    /// <summary>记录访问节点 / Record visited node.</summary>
    public void VisitNode() => _visitedNodes++;

    /// <summary>记录宽度剪枝节点 / Record width-bound pruned node.</summary>
    public void RecordWidthBoundPrunedNode() => _widthBoundPrunedNodes++;

    /// <summary>记录刀数剪枝节点 / Record knife-bound pruned node.</summary>
    public void RecordKnifeBoundPrunedNode() => _knifeBoundPrunedNodes++;

    /// <summary>记录长度剪枝条目 / Record length-bound pruned entries.</summary>
    public void RecordLengthBoundPrunedEntries(long count = 1L) => _lengthBoundPrunedEntries += count;

    /// <summary>记录宽度索引缓存命中 / Record width index cache hit.</summary>
    public void RecordMaterialWidthIndexCacheHit() => _materialWidthIndexCacheHits++;

    /// <summary>记录切片模板缓存命中 / Record slice template cache hit.</summary>
    public void RecordMaterialSliceTemplateCacheHit() => _materialSliceTemplateCacheHits++;

    /// <summary>记录切片模板缓存未命中 / Record slice template cache miss.</summary>
    public void RecordMaterialSliceTemplateCacheMiss() => _materialSliceTemplateCacheMisses++;

    /// <summary>记录数量缓存命中 / Record quantity cache hits.</summary>
    /// <param name="hits">本次新增命中数 / Hits to add.</param>
    public void RecordQuantityCacheHits(long hits) => _quantityCacheHits += hits;

    /// <summary>记录数量缓存未命中 / Record quantity cache misses.</summary>
    /// <param name="misses">本次新增未命中数 / Misses to add.</param>
    public void RecordQuantityCacheMisses(long misses) => _quantityCacheMisses += misses;

    /// <summary>记录跨贡献 dominance 剪枝的候选数 / Record candidates pruned by cross-contribution dominance.</summary>
    /// <param name="count">本次新增候选数 / Candidates to add.</param>
    /// <remarks>
    /// 基线在 `GenerationCollector.kt` 的跨贡献分支里递增这个计数。C# 侧此前**只有字段与统计参数**，
    /// `_dominanceStrategy` 被存下来但**从不读取**，跨贡献分支根本不存在。
    ///
    /// **本批只补计数通道，不补跨贡献判定本身**——判定需要 `relaxedDominanceKey` 与
    /// `canCrossContributionDominate`，那是一个独立的能力缺口（见计划小节"仍然存在的缺口"）。
    ///
    /// The baseline increments this counter in `GenerationCollector.kt`'s cross-contribution branch. On the C# side there was previously **only the field and
    /// the statistics parameter**; `_dominanceStrategy` was stored but **never read**, and the cross-contribution branch did not exist at all.
    ///
    /// **This batch adds only the counting channel, not the cross-contribution decision itself** — that decision needs `relaxedDominanceKey` and
    /// `canCrossContributionDominate`, which is a separate capability gap (see the plan section's "remaining gaps").
    /// </remarks>
    public void RecordCrossContributionDominated(long count = 1L) => _crossContributionDominated += count;

    /// <summary>当前 dominance 策略 / The current dominance strategy.</summary>
    /// <remarks>
    /// 暴露出来是为了让"策略被存下却从不读取"这个缺陷**不可能再悄悄回来**：
    /// 有测试断言这个值确实反映了构造参数。
    ///
    /// Exposed so that "the strategy is stored but never read" **cannot silently return**: a test asserts this value really reflects the constructor argument.
    /// </remarks>
    public DominanceStrategy DominanceStrategy => _dominanceStrategy;

    /// <summary>
    /// 检查是否应停止 / Check whether should stop.
    /// </summary>
    public bool ShouldStop() => IsFull() || IsTimedOut();

    /// <summary>
    /// 检查是否超时 / Check whether timed out.
    /// </summary>
    public bool IsTimedOut() {
        if (_timedOut) {
            return true;
        }

        if (_deadlineMs.HasValue && Environment.TickCount64 > _deadlineMs.Value) {
            _timedOut = true;
        }
        return _timedOut;
    }

    /// <summary>
    /// 记录候选方案 / Record a candidate plan.
    /// </summary>
    /// <param name="plan">候选方案 / Candidate plan.</param>
    /// <param name="getCanonicalKey">获取 canonical key 的函数 / Function to get canonical key.</param>
    /// <param name="feasible">是否可行 / Whether feasible.</param>
    /// <returns>true 如果方案被接受 / true if plan was accepted.</returns>
    public bool Record(
        TPlan plan,
        Func<TPlan, CuttingPlanCanonicalKey> getCanonicalKey,
        bool feasible) {
        _generatedCandidates++;
        if (!feasible) {
            _infeasibleCandidates++;
            return false;
        }

        // **候选过滤必须在去重之前。** 基线把 `candidateFilters` 放在 `plans.add(plan)` 之前，
        // 而 `plans.add` 之后才轮到收集器的去重。顺序反了会让"被过滤掉的候选"占掉规范键名额，
        // 把**本来该被接受的同键候选**挤掉。
        //
        // **The candidate filter must run before deduplication.** The baseline applies `candidateFilters` before `plans.add(plan)`, and only after that does the
        // collector dedupe. Reversing the order lets a **filtered-out** candidate occupy the canonical-key slot and squeeze out a same-key candidate that should have
        // been accepted.
        if (_candidateFilter is not null && !_candidateFilter(plan, _existingPlansForFilter)) {
            ++_filteredCandidates;
            return false;
        }

        CuttingPlanCanonicalKey key = getCanonicalKey(plan);
        if (!_canonicalKeys.Add(key)) {
            _duplicateCandidates++;
            return false;
        }

        // 跨贡献策略：真的做跨贡献判定（需要投影器）。
        // Cross-contribution strategy: perform the actual cross-contribution decision (requires a projector).
        if (_dominanceStrategy == DominanceStrategy.CrossContribution && _crossContributionProjector is not null) {
            CrossContributionProjection? candidate = _crossContributionProjector(plan);
            if (candidate is not null) {
                foreach (TPlan accepted in _acceptedPlans) {
                    CrossContributionProjection? existing = _crossContributionProjector(accepted);
                    if (existing is not null && CrossContributionDominance.Dominates(existing, candidate)) {
                        _canonicalKeys.Remove(key);
                        _crossContributionDominated++;
                        return false;
                    }
                }
            }
        }

        if (_dominanceAcceptOverride is not null && !_dominanceAcceptOverride(plan, _acceptedPlans)) {
            _canonicalKeys.Remove(key);
            // 跨贡献策略下，被拒绝的候选记进**跨贡献**计数，而不是同贡献计数。
            // Under the cross-contribution strategy, a rejected candidate counts as **cross-contribution**, not same-contribution.
            if (_dominanceStrategy == DominanceStrategy.CrossContribution) {
                _crossContributionDominated++;
            } else {
                _dominatedCandidates++;
            }
            return false;
        }

        if (IsFull()) {
            return false;
        }

        _acceptedPlans.Add(plan);
        return true;
    }

    /// <summary>
    /// 生成报告 / Generate report.
    /// </summary>
    /// <returns>生成统计 / Generation statistics.</returns>
    public CuttingPlanGenerationStatistics Report() {
        return new CuttingPlanGenerationStatistics(
            VisitedNodes: _visitedNodes,
            GeneratedCandidates: _generatedCandidates,
            AcceptedPlans: _acceptedPlans.Count,
            InfeasibleCandidates: _infeasibleCandidates,
            DuplicateCandidates: _duplicateCandidates,
            DominatedCandidates: _dominatedCandidates,
            WidthBoundPrunedNodes: _widthBoundPrunedNodes,
            KnifeBoundPrunedNodes: _knifeBoundPrunedNodes,
            LengthBoundPrunedEntries: _lengthBoundPrunedEntries,
            MaterialWidthIndexCacheHits: _materialWidthIndexCacheHits,
            MaterialSliceTemplateCacheHits: _materialSliceTemplateCacheHits,
            MaterialSliceTemplateCacheMisses: _materialSliceTemplateCacheMisses,
            QuantityCacheHits: _quantityCacheHits,
            QuantityCacheMisses: _quantityCacheMisses,
            CrossContributionDominated: _crossContributionDominated,
            StopReason: StopReason()
        );
    }

    private bool IsFull() => _acceptedPlans.Count >= _maxPlans;

    private CuttingPlanGenerationStopReason StopReason() {
        if (_timedOut) {
            return CuttingPlanGenerationStopReason.Timeout;
        }

        if (IsFull()) {
            return CuttingPlanGenerationStopReason.MaxPlans;
        }

        return CuttingPlanGenerationStopReason.Exhausted;
    }
}
