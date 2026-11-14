#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
/// <summary>
/// 切割方案生成终止原因 / Cutting plan generation stop reason.
/// </summary>
public enum CuttingPlanGenerationStopReason {
    /// <summary>已穷举 / Exhausted.</summary>
    Exhausted,
    /// <summary>达到最大方案数 / Max plans reached.</summary>
    MaxPlans,
    /// <summary>超时 / Timeout.</summary>
    Timeout
}

/// <summary>
/// 切割方案生成统计 / Cutting plan generation statistics.
/// </summary>
/// <param name="VisitedNodes">搜索访问节点数 / Visited search nodes.</param>
/// <param name="GeneratedCandidates">产出的候选方案数 / Generated candidate plan count.</param>
/// <param name="AcceptedPlans">接受的方案数 / Accepted plan count.</param>
/// <param name="InfeasibleCandidates">被基础可行性拒绝的候选数 / Candidates rejected by basic feasibility.</param>
/// <param name="DuplicateCandidates">被结构化去重过滤的候选数 / Candidates filtered by structural deduplication.</param>
/// <param name="DominatedCandidates">被 dominance 剪枝过滤的候选数 / Candidates filtered by dominance pruning.</param>
/// <param name="WidthBoundPrunedNodes">被剩余宽度上界剪枝的搜索节点数 / Nodes pruned by remaining-width upper bound.</param>
/// <param name="KnifeBoundPrunedNodes">被刀数下界可达性剪枝的搜索节点数 / Nodes pruned by knife-count reachability.</param>
/// <param name="LengthBoundPrunedEntries">被长度上界剪枝的搜索入口数 / Entries pruned by length upper bound.</param>
/// <param name="MaterialWidthIndexCacheHits">物料等价宽度入口缓存命中数 / Material-equivalent width-entry cache hits.</param>
/// <param name="MaterialSliceTemplateCacheHits">物料等价切片模板缓存命中数 / Material-equivalent slice-template cache hits.</param>
/// <param name="QuantityCacheHits">数量缓存命中数 / Quantity cache hits.</param>
/// <param name="QuantityCacheMisses">数量缓存未命中数 / Quantity cache misses.</param>
/// <param name="MaterialSliceTemplateCacheMisses">物料等价切片模板缓存未命中数 / Material-equivalent slice-template cache misses.</param>
/// <param name="CrossWorkerDuplicateCandidates">并行合并时跨 worker 重复候选数 / Cross-worker duplicate candidates during parallel merge.</param>
/// <param name="CrossContributionDominated">跨贡献 dominance 剪枝过滤的候选数 / Candidates filtered by cross-contribution dominance.</param>
/// <param name="ElapsedMilliseconds">生成耗时毫秒数 / Generation elapsed time in milliseconds.</param>
/// <param name="StopReason">终止原因 / Stop reason.</param>
public sealed record CuttingPlanGenerationStatistics(
    long VisitedNodes = 0L,
    long GeneratedCandidates = 0L,
    long AcceptedPlans = 0L,
    long InfeasibleCandidates = 0L,
    long DuplicateCandidates = 0L,
    long DominatedCandidates = 0L,
    long WidthBoundPrunedNodes = 0L,
    long KnifeBoundPrunedNodes = 0L,
    long LengthBoundPrunedEntries = 0L,
    long MaterialWidthIndexCacheHits = 0L,
    long MaterialSliceTemplateCacheHits = 0L,
    long QuantityCacheHits = 0L,
    long QuantityCacheMisses = 0L,
    long MaterialSliceTemplateCacheMisses = 0L,
    long CrossWorkerDuplicateCandidates = 0L,
    long CrossContributionDominated = 0L,
    long ElapsedMilliseconds = 0L,
    CuttingPlanGenerationStopReason StopReason = CuttingPlanGenerationStopReason.Exhausted
);

/// <summary>
/// 切割方案生成 benchmark 快照 / Cutting plan generation benchmark snapshot.
///
/// 只包含确定性的数量类统计，适合测试和文档中做稳定比较。
/// Only contains deterministic count statistics for stable comparison in tests and docs.
/// </summary>
/// <param name="GeneratorName">生成器名称 / Generator name.</param>
/// <param name="VisitedNodes">搜索访问节点数 / Visited search nodes.</param>
/// <param name="GeneratedCandidates">产出的候选方案数 / Generated candidate plan count.</param>
/// <param name="AcceptedPlans">接受的方案数 / Accepted plan count.</param>
/// <param name="InfeasibleCandidates">被基础可行性拒绝的候选数 / Candidates rejected by basic feasibility.</param>
/// <param name="DuplicateCandidates">被结构化去重过滤的候选数 / Candidates filtered by structural deduplication.</param>
/// <param name="DominatedCandidates">被 dominance 剪枝过滤的候选数 / Candidates filtered by dominance pruning.</param>
/// <param name="WidthBoundPrunedNodes">被剩余宽度上界剪枝的搜索节点数 / Nodes pruned by remaining-width upper bound.</param>
/// <param name="KnifeBoundPrunedNodes">被刀数下界可达性剪枝的搜索节点数 / Nodes pruned by knife-count reachability.</param>
/// <param name="LengthBoundPrunedEntries">被长度上界剪枝的搜索入口数 / Entries pruned by length upper bound.</param>
/// <param name="MaterialWidthIndexCacheHits">物料等价宽度入口缓存命中数 / Material-equivalent width-entry cache hits.</param>
/// <param name="MaterialSliceTemplateCacheHits">物料等价切片模板缓存命中数 / Material-equivalent slice-template cache hits.</param>
/// <param name="QuantityCacheHits">数量缓存命中数 / Quantity cache hits.</param>
/// <param name="QuantityCacheMisses">数量缓存未命中数 / Quantity cache misses.</param>
/// <param name="MaterialSliceTemplateCacheMisses">物料等价切片模板缓存未命中数 / Material-equivalent slice-template cache misses.</param>
/// <param name="CrossWorkerDuplicateCandidates">并行合并时跨 worker 重复候选数 / Cross-worker duplicate candidates.</param>
/// <param name="CrossContributionDominated">跨贡献 dominance 剪枝过滤的候选数 / Candidates filtered by cross-contribution dominance.</param>
/// <param name="StopReason">终止原因 / Stop reason.</param>
public sealed record CuttingPlanGenerationBenchmarkSnapshot(
    string GeneratorName,
    long VisitedNodes,
    long GeneratedCandidates,
    long AcceptedPlans,
    long InfeasibleCandidates,
    long DuplicateCandidates,
    long DominatedCandidates,
    long WidthBoundPrunedNodes,
    long KnifeBoundPrunedNodes,
    long LengthBoundPrunedEntries,
    long MaterialWidthIndexCacheHits,
    long MaterialSliceTemplateCacheHits,
    long QuantityCacheHits,
    long QuantityCacheMisses,
    long MaterialSliceTemplateCacheMisses,
    long CrossWorkerDuplicateCandidates,
    long CrossContributionDominated,
    CuttingPlanGenerationStopReason StopReason
) {
    /// <summary>
    /// 输出稳定文本行 / Render stable text line.
    /// </summary>
    /// <returns>可比较的稳定文本行 / Comparable stable text line.</returns>
    public string ToStableLine() {
        return string.Join(";", new[]
        {
            $"generator={GeneratorName}",
            $"visitedNodes={VisitedNodes}",
            $"generatedCandidates={GeneratedCandidates}",
            $"acceptedPlans={AcceptedPlans}",
            $"infeasibleCandidates={InfeasibleCandidates}",
            $"duplicateCandidates={DuplicateCandidates}",
            $"dominatedCandidates={DominatedCandidates}",
            $"widthBoundPrunedNodes={WidthBoundPrunedNodes}",
            $"knifeBoundPrunedNodes={KnifeBoundPrunedNodes}",
            $"lengthBoundPrunedEntries={LengthBoundPrunedEntries}",
            $"materialWidthIndexCacheHits={MaterialWidthIndexCacheHits}",
            $"materialSliceTemplateCacheHits={MaterialSliceTemplateCacheHits}",
            $"quantityCacheHits={QuantityCacheHits}",
            $"quantityCacheMisses={QuantityCacheMisses}",
            $"materialSliceTemplateCacheMisses={MaterialSliceTemplateCacheMisses}",
            $"crossWorkerDuplicateCandidates={CrossWorkerDuplicateCandidates}",
            $"crossContributionDominated={CrossContributionDominated}",
            $"stopReason={StopReason}"
        });
    }

    /// <summary>
    /// 从生成统计构造快照 / Build snapshot from generation statistics.
    /// </summary>
    /// <param name="generatorName">生成器名称 / Generator name.</param>
    /// <param name="statistics">生成统计 / Generation statistics.</param>
    /// <returns>benchmark 快照 / Benchmark snapshot.</returns>
    public static CuttingPlanGenerationBenchmarkSnapshot From(
        string generatorName,
        CuttingPlanGenerationStatistics statistics) {
        return new CuttingPlanGenerationBenchmarkSnapshot(
            GeneratorName: generatorName,
            VisitedNodes: statistics.VisitedNodes,
            GeneratedCandidates: statistics.GeneratedCandidates,
            AcceptedPlans: statistics.AcceptedPlans,
            InfeasibleCandidates: statistics.InfeasibleCandidates,
            DuplicateCandidates: statistics.DuplicateCandidates,
            DominatedCandidates: statistics.DominatedCandidates,
            WidthBoundPrunedNodes: statistics.WidthBoundPrunedNodes,
            KnifeBoundPrunedNodes: statistics.KnifeBoundPrunedNodes,
            LengthBoundPrunedEntries: statistics.LengthBoundPrunedEntries,
            MaterialWidthIndexCacheHits: statistics.MaterialWidthIndexCacheHits,
            MaterialSliceTemplateCacheHits: statistics.MaterialSliceTemplateCacheHits,
            QuantityCacheHits: statistics.QuantityCacheHits,
            QuantityCacheMisses: statistics.QuantityCacheMisses,
            MaterialSliceTemplateCacheMisses: statistics.MaterialSliceTemplateCacheMisses,
            CrossWorkerDuplicateCandidates: statistics.CrossWorkerDuplicateCandidates,
            CrossContributionDominated: statistics.CrossContributionDominated,
            StopReason: statistics.StopReason
        );
    }
}

/// <summary>
/// 切割方案生成报告 / Cutting plan generation report.
/// </summary>
/// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
/// <param name="Plans">生成并接受的切割方案 / Generated and accepted cutting plans.</param>
/// <param name="Statistics">生成统计 / Generation statistics.</param>
public sealed record CuttingPlanGenerationReport<TPlan>(
    IReadOnlyList<TPlan> Plans,
    CuttingPlanGenerationStatistics Statistics
);

/// <summary>
/// 初始切割方案生成器接口 / Initial cutting plan generator interface.
/// </summary>
/// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
/// <typeparam name="TInput">生成输入类型 / Generation input type.</typeparam>
public interface ICsp1dInitialCuttingPlanGenerator<TPlan, in TInput> {
    /// <summary>
    /// 生成初始切割方案 / Generate initial cutting plans.
    /// </summary>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <returns>初始切割方案列表 / Initial cutting plans.</returns>
    IReadOnlyList<TPlan> Generate(TInput input);

    /// <summary>
    /// 生成初始切割方案并返回统计 / Generate initial cutting plans with statistics.
    /// </summary>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <returns>切割方案生成报告 / Cutting plan generation report.</returns>
    CuttingPlanGenerationReport<TPlan> GenerateWithReport(TInput input) {
        long startTime = Environment.TickCount64;
        IReadOnlyList<TPlan> plans = Generate(input);
        return new CuttingPlanGenerationReport<TPlan>(
            Plans: plans,
            Statistics: new CuttingPlanGenerationStatistics(
                GeneratedCandidates: plans.Count,
                AcceptedPlans: plans.Count,
                ElapsedMilliseconds: Environment.TickCount64 - startTime
            )
        );
    }
}

/// <summary>
/// 定价子问题生成器接口 / Pricing sub-problem generator interface.
/// </summary>
/// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
/// <typeparam name="TPricingInput">定价输入类型 / Pricing input type.</typeparam>
public interface ICsp1dPricingGenerator<TPlan, in TPricingInput> {
    /// <summary>
    /// 生成新列 / Generate new columns.
    /// </summary>
    /// <param name="input">定价输入 / Pricing input.</param>
    /// <returns>新切割方案列表 / New cutting plans.</returns>
    IReadOnlyList<TPlan> Generate(TPricingInput input);

    /// <summary>
    /// 生成新列并返回统计 / Generate new columns with statistics.
    /// </summary>
    /// <param name="input">定价输入 / Pricing input.</param>
    /// <returns>切割方案生成报告 / Cutting plan generation report.</returns>
    CuttingPlanGenerationReport<TPlan> GenerateWithReport(TPricingInput input) {
        long startTime = Environment.TickCount64;
        IReadOnlyList<TPlan> plans = Generate(input);
        return new CuttingPlanGenerationReport<TPlan>(
            Plans: plans,
            Statistics: new CuttingPlanGenerationStatistics(
                GeneratedCandidates: plans.Count,
                AcceptedPlans: plans.Count,
                ElapsedMilliseconds: Environment.TickCount64 - startTime
            )
        );
    }
}

/// <summary>
/// 生成输入，包含物料列表和需求列表 / Generation input containing material and demand lists.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class GenerationInput<V> where V : struct {
    /// <summary>物料列表 / Material list.</summary>
    public IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.Material<V>> Materials { get; init; }
        = Array.Empty<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.Material<V>>();

    /// <summary>需求列表 / Demand list.</summary>
    public IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.ProductDemand<V>> Demands { get; init; }
        = Array.Empty<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.ProductDemand<V>>();

    /// <summary>配规列表 / Costar list.</summary>
    public IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.Costar<V>> Costars { get; init; }
        = Array.Empty<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.Costar<V>>();

    /// <summary>
    /// 已存在（已在列池里）的方案 / Plans that already exist (already in the column pool).
    /// </summary>
    /// <remarks>
    /// **基线 `CuttingPlanGenerationInput.existingPlans` 的对应物。** 定价阶段靠它做去重——
    /// **不传的话，定价会把池里已有的方案再报一遍**，于是第一轮就"全部重复"。
    ///
    /// **这解释了 B338 之前观察到的 `reason=AllDuplicates iters=1 priced=0` 的另一半原因**：
    /// 那一半是"定价器不读对偶价格"（B338 已修），这一半是"**定价器不知道池里有什么**"。
    ///
    /// **The counterpart of the baseline's `CuttingPlanGenerationInput.existingPlans`.** Pricing uses it for deduplication — **without it, pricing re-reports plans
    /// already in the pool**, so the very first round is "all duplicates".
    ///
    /// **This is the other half of the `reason=AllDuplicates iters=1 priced=0` observation from before B338**: one half was "pricing ignored the dual prices" (fixed in
    /// B338), this half is "**pricing does not know what the pool already holds**".
    /// </remarks>
    public IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>> ExistingPlans { get; init; }
        = Array.Empty<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>>();

    /// <summary>
    /// dominance 接受覆盖函数列表 / Dominance acceptance override function list.
    /// </summary>
    /// <remarks>
    /// **基线 `dominanceAcceptOverrides` 的对应物**，由 `ICsp1dGenerationStrategy.AcceptDominance` 投影而来。
    ///
    /// **语义是「全部通过」**：基线写作 `input.dominanceAcceptOverrides.all { it(plan, existing) }`——
    /// **任何一个返回 false，候选就被拒**。空列表表示不额外过滤。
    ///
    /// **The counterpart of the baseline's `dominanceAcceptOverrides`**, projected from `ICsp1dGenerationStrategy.AcceptDominance`.
    ///
    /// **The semantics are "all must pass"**: the baseline writes `input.dominanceAcceptOverrides.all { it(plan, existing) }` — **any false rejects the candidate**.
    /// An empty list means no extra filtering.
    /// </remarks>
    /// <summary>
    /// 候选过滤函数列表 / Candidate filter function list.
    /// </summary>
    /// <remarks>
    /// **基线 `candidateFilters` 的对应物**，由 `ICsp1dGenerationStrategy.AcceptCandidate` 投影而来。
    ///
    /// **语义同样是「全部通过」**：基线写作 `candidateFilters.all { it(plan, input.existingPlans) }`。
    /// 注意第二个参数是 **`input.existingPlans`**——同一个钩子在**初始池**（空）与**定价**（当前列池）
    /// 两种上下文下行为不同，这正是它能表达"别跟已有方案撞"的原因。
    ///
    /// **The counterpart of the baseline's `candidateFilters`**, projected from `ICsp1dGenerationStrategy.AcceptCandidate`.
    ///
    /// **The semantics are also "all must pass"**: the baseline writes `candidateFilters.all { it(plan, input.existingPlans) }`. Note the second argument is
    /// **`input.existingPlans`** — the same hook behaves differently in the **initial pool** (empty) and **pricing** (current column pool) contexts, which is exactly what
    /// lets it express "do not collide with existing plans".
    /// </remarks>
    public IReadOnlyList<global::System.Func<
        Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>,
        IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>>,
        bool>> CandidateFilters { get; init; }
        = Array.Empty<global::System.Func<
            Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>,
            IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>>,
            bool>>();

    public IReadOnlyList<global::System.Func<
        Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>,
        IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>>,
        bool>> DominanceAcceptOverrides { get; init; }
        = Array.Empty<global::System.Func<
            Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>,
            IReadOnlyList<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>>,
            bool>>();
}
