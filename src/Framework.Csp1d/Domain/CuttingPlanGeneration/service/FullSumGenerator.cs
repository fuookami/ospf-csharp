#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// FullSum 方案生成器，枚举所有宽度求和组合 / FullSum generator: enumerate all width-sum combinations.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class FullSumGenerator<V> : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>
    where V : struct, IComparable<V> {
    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _constraints;
    private readonly long _maxPlans;
    private readonly long? _timeoutMs;
    private readonly long _parallelism;
    private readonly bool _enableDominancePruning;
    private readonly DominanceStrategy _dominanceStrategy;

    /// <summary>
    /// 创建 FullSum 生成器 / Create FullSum generator.
    /// </summary>
    public FullSumGenerator(
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        long maxPlans = 1000L,
        long? timeoutMs = null,
        long parallelism = 1L,
        bool enableDominancePruning = false,
        DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution) {
        _constraints = constraints;
        _maxPlans = maxPlans;
        _timeoutMs = timeoutMs;
        _parallelism = global::System.Math.Max(1L, parallelism);
        _enableDominancePruning = enableDominancePruning;
        _dominanceStrategy = dominanceStrategy;
    }

    /// <summary>
    /// 从 GenerationConstraints 创建 / Create from GenerationConstraints.
    /// </summary>
    public FullSumGenerator(
        GenerationConstraints<V> constraints,
        long maxPlans = 1000L,
        long? timeoutMs = null)
        : this(
            constraints: constraints.ToConstraints(),
            maxPlans: maxPlans,
            timeoutMs: timeoutMs,
            parallelism: constraints.Parallelism,
            enableDominancePruning: constraints.EnableDominancePruning,
            dominanceStrategy: constraints.DominanceStrategy) {
    }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> Generate(GenerationInput<V> input) => GenerateWithReport(input).Plans;

    /// <inheritdoc/>
    public CuttingPlanGenerationReport<CuttingPlan<V>> GenerateWithReport(GenerationInput<V> input) {
        long startTime = Environment.TickCount64;
        var collector = new GenerationCollector<CuttingPlan<V>>(
            maxPlans: _maxPlans,
            deadlineMs: _timeoutMs.HasValue ? startTime + _timeoutMs.Value : null,
            enableDominancePruning: _enableDominancePruning,
            dominanceStrategy: _dominanceStrategy,
            // **把配置里的 dominance 覆盖喂给收集器**：钩子早就实现了，只是没人传。
            // **Feed the configured dominance overrides to the collector**: the hook existed all along, nothing passed it.
            dominanceAcceptOverride: GenerationOverrideResolution.Fold(input.DominanceAcceptOverrides),
            candidateFilter: GenerationOverrideResolution.FoldCandidateFilters(input.CandidateFilters),
            existingPlansForFilter: input.ExistingPlans,
            crossContributionProjector: CrossContributionProjector.ForGenerator<V>()
        );

        var widthIndex = GenerationWidthIndex<V>.FromDemands(
            input.Demands,
            demand => demand.Product.Id,
            demand => demand.Product.Width,
            demand => demand.Quantity.Unit
        );

        if (!widthIndex.IsEmpty) {
            foreach (Material<V> material in input.Materials) {
                if (collector.ShouldStop()) {
                    break;
                }
                Search(material, input, widthIndex, collector);
            }
        }

        return new CuttingPlanGenerationReport<CuttingPlan<V>>(
            Plans: collector.Plans,
            Statistics: collector.Report() with {
                ElapsedMilliseconds = Environment.TickCount64 - startTime
            }
        );
    }

    /// <summary>
    /// 对每个物料执行共享的求和搜索 / Run the shared sum search for each material.
    /// </summary>
    /// <param name="material">物料 / Material.</param>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <param name="widthIndex">宽度索引 / Width index.</param>
    /// <param name="collector">收集器 / Collector.</param>
    /// <remarks>
    /// **本类不再自己实现搜索**：`FullSumGenerator` 与 `DFSGenerator` 搜索循环逐行相同，
    /// 差别只在"扩展时是否做剪枝约束检查"。逻辑抽到 `GenerationSumSearch`，这里传 `applyPruningConstraints: false`
    /// （全和生成器只在叶节点检查约束）。
    ///
    /// **This class no longer implements the search**: its `FullSumGenerator` and `DFSGenerator` search loops are line-for-line identical, differing
    /// only in whether pruning constraints are checked while expanding. The logic lives in `GenerationSumSearch`; this passes `applyPruningConstraints: false`
    /// (the full-sum generator checks constraints only at leaves).
    /// </remarks>
    private void Search(
        Material<V> material,
        GenerationInput<V> input,
        GenerationWidthIndex<V> widthIndex,
        GenerationCollector<CuttingPlan<V>> collector) =>
        GenerationSumSearch.Run(
            material: material,
            input: input,
            widthIndex: widthIndex,
            collector: collector,
            constraints: _constraints,
            planIdPrefix: "fullsum",
            applyPruningConstraints: false
        );
}
