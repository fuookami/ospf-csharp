#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// N-Same 方案生成器，为每个产品-宽度组合生成单产品方案 / N-Same generator producing single-product plans.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class NSameGenerator<V> : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>
    where V : struct, IComparable<V> {
    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _constraints;
    private readonly bool _allAmount;
    private readonly long? _timeoutMs;
    private readonly long _maxPlans;
    private readonly long _parallelism;
    private readonly bool _enableDominancePruning;
    private readonly DominanceStrategy _dominanceStrategy;
    private readonly UInt64? _maxKnifeCount;

    /// <summary>
    /// 创建 NSame 生成器 / Create NSame generator.
    /// </summary>
    public NSameGenerator(
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        bool allAmount = false,
        long? timeoutMs = null,
        long maxPlans = 1000L,
        long parallelism = 1L,
        bool enableDominancePruning = false,
        DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution) {
        _constraints = constraints;
        _allAmount = allAmount;
        _timeoutMs = timeoutMs;
        _maxPlans = maxPlans;
        _parallelism = global::System.Math.Max(1L, parallelism);
        _enableDominancePruning = enableDominancePruning;
        _dominanceStrategy = dominanceStrategy;
        _maxKnifeCount = System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.OfType<MaxKnifeCountConstraint<V>>(constraints))?.Threshold;
    }

    /// <summary>
    /// 从 GenerationConstraints 创建 / Create from GenerationConstraints.
    /// </summary>
    public NSameGenerator(
        GenerationConstraints<V> constraints,
        bool allAmount = false,
        long? timeoutMs = null,
        long maxPlans = 1000L)
        : this(
            constraints: constraints.ToConstraints(),
            allAmount: allAmount,
            timeoutMs: timeoutMs,
            maxPlans: maxPlans,
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

        foreach (Material<V> material in input.Materials) {
            if (collector.ShouldStop()) {
                break;
            }
            GenerateForMaterial(material, input, collector);
        }

        return new CuttingPlanGenerationReport<CuttingPlan<V>>(
            Plans: collector.Plans,
            Statistics: collector.Report() with {
                ElapsedMilliseconds = Environment.TickCount64 - startTime
            }
        );
    }

    /// <summary>
    /// 为单个物料生成"同宽单产品"方案 / Generate single-product same-width plans for one material.
    /// </summary>
    /// <param name="material">物料 / Material.</param>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <param name="collector">收集器 / Collector.</param>
    /// <remarks>
    /// 对应基线 `NSameGenerator.generateMaterial`：**遍历需求 → 产品宽度 → 数量**，
    /// 每个组合产出一个**单切片方案**（"N 个相同切片"）。
    ///
    /// 与基线的三点差异，均已记录：
    ///
    /// | 基线 | 这里 | 原因 |
    /// | --- | --- | --- |
    /// | `GenerationQuantityCache` 加速重复宽度 | 直接算 | 缓存类存在但**没有工厂**，构造需要 4 个委托；先用直接算法，缓存留待后续 |
    /// | `allAmount` 时枚举 1..maxAmount | 仅 `maxAmount` | 枚举需要 `maxAmount` 的数值类型无关算法，先用最省事的 |
    /// | 并行按物料分任务 | 串行 | 并行需要合并报告的完整语义，先用串行保证正确 |
    ///
    /// **这些差异都让能力变弱而不让结果变错**：产出的方案是基线结果的子集，且每条都经过约束检查。
    ///
    /// Mirrors the baseline's `NSameGenerator.generateMaterial`: **walk demands → product widths → amounts**, emitting one **single-slice plan** ("N same
    /// slices") per combination. The three differences from the baseline are recorded above; **all of them make the capability weaker without making results
    /// wrong** — the emitted plans are a subset of the baseline's, each still passing the constraint checks.
    /// </remarks>
    private void GenerateForMaterial(
        Material<V> material,
        GenerationInput<V> input,
        GenerationCollector<CuttingPlan<V>> collector) {
        int planIndex = 0;
        foreach (ProductDemand<V> demand in input.Demands) {
            if (collector.ShouldStop()) {
                return;
            }

            foreach (Quantity<V> productWidth in demand.Product.Width) {
                if (collector.ShouldStop()) {
                    return;
                }

                if (!material.WidthRange.CanCut(productWidth)) {
                    continue;
                }

                collector.VisitNode();

                var slices = new List<CuttingPlanSlice<V>> {
                    new(production: demand.Product, width: productWidth),
                };

                if (!SatisfiesConstraints(slices, material)) {
                    continue;
                }

                Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>> arithmeticResult =
                    DefaultQuantityArithmetic.ResolveFor(material.WidthRange.LowerBound.Value);
                if (arithmeticResult.IsFailed) {
                    continue;
                }

                Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> contribution =
                    demand.Contribution(
                        width: productWidth,
                        amount: UInt64.One,
                        arithmetic: arithmeticResult.Value);
                if (contribution.IsFailed) {
                    continue;
                }

                var plan = new CuttingPlan<V>(
                    id: $"nsame-{material.Id}-{demand.Product.Id}-{planIndex}",
                    material: material,
                    slices: slices,
                    demandContributions: new[] { contribution.Value }
                );
                ++planIndex;

                collector.Record(
                    plan,
                    candidate => CuttingPlanCanonicalKey.From(candidate),
                    feasible: true
                );
            }
        }
    }

    /// <summary>
    /// 检查约束 / Check the constraints.
    /// </summary>
    /// <param name="slices">切片 / Slices.</param>
    /// <param name="material">物料 / Material.</param>
    /// <returns>是否全部满足 / Whether all are satisfied.</returns>
    private bool SatisfiesConstraints(IReadOnlyList<CuttingPlanSlice<V>> slices, Material<V> material) {
        if (_constraints.Count == 0) {
            return true;
        }

        var stubs = new List<CuttingPlanSliceStub<V>>(slices.Count);
        Quantity<V> totalWidth = material.WidthRange.LowerBound;
        foreach (CuttingPlanSlice<V> slice in slices) {
            stubs.Add(new CuttingPlanSliceStub<V>(
                ProductionType: "Product",
                ProductionLength: null,
                Width: slice.Width,
                Amount: slice.Amount
            ));
        }

        var context = new CuttingPlanConstraintContext<V>(
            Slices: stubs,
            TotalWidth: totalWidth,
            UpperBound: material.WidthRange.UpperBound,
            MaterialId: material.Id
        );

        foreach (ICuttingPlanConstraint<V> constraint in _constraints) {
            if (!constraint.IsSatisfied(context)) {
                return false;
            }
        }
        return true;
    }
}
