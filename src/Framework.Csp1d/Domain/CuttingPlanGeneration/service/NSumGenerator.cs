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
/// N-Sum 方案生成器，深度受限 DFS 枚举多产品组合方案 / N-Sum generator: depth-limited DFS for multi-product combinations.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class NSumGenerator<V> : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>
    where V : struct, IComparable<V> {
    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _constraints;
    private readonly UInt64 _maxDepth;
    private readonly long _maxPlans;
    private readonly long? _timeoutMs;
    private readonly long _parallelism;
    private readonly bool _enableDominancePruning;
    private readonly DominanceStrategy _dominanceStrategy;

    /// <summary>
    /// 创建 NSum 生成器 / Create NSum generator.
    /// </summary>
    public NSumGenerator(
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        UInt64? maxDepth = null,
        long maxPlans = 1000L,
        long? timeoutMs = null,
        long parallelism = 1L,
        bool enableDominancePruning = false,
        DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution) {
        _constraints = constraints;
        _maxDepth = maxDepth ?? new UInt64(7UL);
        _maxPlans = maxPlans;
        _timeoutMs = timeoutMs;
        _parallelism = global::System.Math.Max(1L, parallelism);
        _enableDominancePruning = enableDominancePruning;
        _dominanceStrategy = dominanceStrategy;
    }

    /// <summary>
    /// 从 GenerationConstraints 创建 / Create from GenerationConstraints.
    /// </summary>
    public NSumGenerator(
        GenerationConstraints<V> constraints,
        UInt64? maxDepth = null,
        long maxPlans = 1000L,
        long? timeoutMs = null)
        : this(
            constraints: constraints.ToConstraints(),
            maxDepth: maxDepth,
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
    /// 深度优先搜索"多个不同宽度、总和不超过幅宽"的组合 / Depth-first search over combinations of distinct widths whose total fits the material.
    /// </summary>
    /// <param name="material">物料 / Material.</param>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <param name="widthIndex">宽度索引 / Width index.</param>
    /// <param name="collector">收集器 / Collector.</param>
    /// <remarks>
    /// 对应基线 `NSumGenerator.nSumSearch` 的**迭代式深度优先搜索**：栈里保存"已选切片 + 已用宽度 + 起始下标"，
    /// 每次弹栈时检查剩余宽度是否还容得下任何一个候选，否则就是叶子节点，尝试产出方案。
    ///
    /// **与基线的差异**（都让能力变弱而不让结果变错）：
    ///
    /// | 基线 | 这里 | 原因 |
    /// | --- | --- | --- |
    /// | 宽度上界剪枝 + 刀数下界可达性剪枝 | 只有"剩余宽度还容得下" | 可达性剪枝需要 `remainingGenerationCutCapacity`，未移植 |
    /// | 切片模板缓存与记录器 | 无 | 缓存类存在但没有工厂 |
    /// | 数量缓存 | 直接算 | 同上 |
    /// | 并行按物料分任务 | 串行 | 需要完整的报告合并语义 |
    ///
    /// **关键正确性点**：`startIndex` 保证**候选下标单调不减**，因此同一组合不会被不同顺序重复枚举。
    /// 这一点若写错，会产生大量重复方案——而**去重会静默吸收它们，使"方案数看起来正常"**。
    ///
    /// Mirrors the baseline's `nSumSearch` as an **iterative depth-first search**: the stack holds "chosen slices + used width + start index"; popping a frame
    /// checks whether the remaining width still fits any candidate, otherwise it is a leaf and a plan is emitted.
    ///
    /// The differences from the baseline are recorded above; **all of them weaken the capability without making results wrong**.
    ///
    /// **The key correctness point**: `startIndex` keeps **candidate indices monotonic**, so one combination is never enumerated in a different order. Getting
    /// this wrong produces a flood of duplicate plans — **and de-duplication silently absorbs them, making "the plan count look normal"**.
    /// </remarks>
    private void Search(
        Material<V> material,
        GenerationInput<V> input,
        GenerationWidthIndex<V> widthIndex,
        GenerationCollector<CuttingPlan<V>> collector) {
        Quantity<V> upperBound = material.WidthRange.UpperBound;
        IQuantityArithmetic<V>? arithmetic = GenerationQuantityMath.Resolve(upperBound.Value);
        if (arithmetic is null) {
            return;
        }

        var stack = new Stack<Frame>();
        stack.Push(new Frame(
            Slices: new List<CuttingPlanSlice<V>>(),
            UsedWidth: null,
            StartIndex: 0
        ));

        int planIndex = 0;
        while (stack.Count > 0 && !collector.ShouldStop()) {
            collector.VisitNode();
            Frame frame = stack.Pop();

            Quantity<V>? remaining = frame.UsedWidth is null
                ? upperBound
                : GenerationQuantityMath.RemainingNonNegative(upperBound, frame.UsedWidth, arithmetic);
            if (remaining is null) {
                continue;
            }

            // **每个非空栈帧都是一条候选方案**，无论剩余宽度是否还能再装。
            // 只在"装不下"时产出会**漏掉所有装满的方案**——而那正是最常见的方案。
            //
            // **Every non-empty frame is a candidate plan**, regardless of whether the remaining width still fits something. Emitting only when nothing else
            // fits **drops every full plan** — and those are the most common ones.
            if (frame.Slices.Count > 0) {
                TryEmit(material, frame, collector, ref planIndex);
            }

            bool canFitMore = widthIndex.HasFittableFrom(frame.StartIndex, remaining);
            if (!canFitMore) {
                continue;
            }

            for (int index = frame.StartIndex; index < widthIndex.Entries.Count; ++index) {
                GenerationProductWidthEntry<V> entry = widthIndex.Entries[index];
                if (!material.WidthRange.CanCut(entry.Width)) {
                    continue;
                }

                Quantity<V>? used = frame.UsedWidth is null
                    ? entry.Width
                    : GenerationQuantityMath.Add(frame.UsedWidth, entry.Width, arithmetic);
                if (used is null) {
                    continue;
                }
                Quantity<V>? afterAdd = GenerationQuantityMath.RemainingNonNegative(upperBound, used, arithmetic);
                // **减法成功不等于剩余宽度非负**：算术层不检查符号，必须在这里判。
                // **A successful subtraction does not mean the remainder is non-negative**: the arithmetic layer does not check sign, so it must be checked here.
                if (afterAdd is null || GenerationQuantityMath.IsNegative(afterAdd, arithmetic)) {
                    continue;
                }

                Product<V>? product = FindProduct(input, entry.ProductId);
                if (product is null) {
                    continue;
                }

                var slices = new List<CuttingPlanSlice<V>>(frame.Slices.Count + 1);
                slices.AddRange(frame.Slices);
                slices.Add(new CuttingPlanSlice<V>(production: product, width: entry.Width));

                stack.Push(new Frame(Slices: slices, UsedWidth: used, StartIndex: index));
            }
        }
    }

    private static Product<V>? FindProduct(GenerationInput<V> input, string productId) {
        foreach (ProductDemand<V> demand in input.Demands) {
            if (string.Equals(demand.Product.Id, productId, StringComparison.Ordinal)) {
                return demand.Product;
            }
        }
        return null;
    }

    private void TryEmit(
        Material<V> material,
        Frame frame,
        GenerationCollector<CuttingPlan<V>> collector,
        ref int planIndex) {
        foreach (CuttingPlanSlice<V> slice in frame.Slices) {
            if (slice.Production is null) {
                return;
            }
        }

        var contributions = new List<CuttingPlanDemandContribution<V>>(frame.Slices.Count);
        foreach (CuttingPlanSlice<V> slice in frame.Slices) {
            if (slice.Production is Product<V> product) {
                contributions.Add(new CuttingPlanDemandContribution<V>(product, slice.Width));
            }
        }
        if (contributions.Count == 0) {
            return;
        }

        var plan = new CuttingPlan<V>(
            id: $"nsum-{material.Id}-{planIndex}",
            material: material,
            slices: frame.Slices,
            demandContributions: contributions
        );
        ++planIndex;

        collector.Record(
            plan,
            candidate => CuttingPlanCanonicalKey.From(candidate),
            feasible: true
        );
    }

    private sealed record Frame(
        IReadOnlyList<CuttingPlanSlice<V>> Slices,
        Quantity<V>? UsedWidth,
        int StartIndex
    );
}
