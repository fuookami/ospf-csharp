#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Csp1d.Infrastructure.Dto;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 解状态 / CSP1D solution status.
/// </summary>
public enum Csp1dSolutionStatus {
    /// <summary>已得到最终 MILP 解 / Final MILP solution is available.</summary>
    Feasible,

    /// <summary>只有方案池或中间结果可用 / Only plan pool or intermediate result is available.</summary>
    Partial,

    /// <summary>无初始方案 / No initial plans.</summary>
    NoInitialPlans,

    /// <summary>求解失败且无可用部分结果 / Solve failed without usable partial result.</summary>
    Failed
}

/// <summary>
/// CSP1D KPI 稳定字段名 / Stable CSP1D KPI keys.
/// </summary>
public static class Csp1dKpiKeys {
    public const string SelectedPlanCount = "selectedPlanCount";
    public const string SelectedBatchCount = "selectedBatchCount";
    public const string SatisfiedDemandCount = "satisfiedDemandCount";
    public const string UnmetDemandCount = "unmetDemandCount";
    public const string MaterialUsageCount = "materialUsageCount";
    public const string MachineUsageCount = "machineUsageCount";
    public const string GeneratedPlanCount = "generatedPlanCount";
    public const string TopPlanCount = "topPlanCount";
    public const string YieldMetricCount = "yieldMetricCount";
    public const string WasteMetricCount = "wasteMetricCount";
    public const string LengthMetricCount = "lengthMetricCount";
    public const string SolutionStatus = "solutionStatus";
    public const string TerminationReason = "terminationReason";
    public const string FinalMilpStatus = "finalMilpStatus";
    public const string PartialSolutionAvailable = "partialSolutionAvailable";
    public const string FailureMessage = "failureMessage";
    public const string ColumnGenerationTerminationReason = "columnGeneration.terminationReason";
    public const string ColumnGenerationIterationCount = "columnGeneration.iterationCount";
    public const string ColumnGenerationPricedPlanCount = "columnGeneration.pricedPlanCount";
    public const string ColumnGenerationLastLpObjective = "columnGeneration.lastLpObjective";
    public const string ColumnGenerationLastPlanCount = "columnGeneration.lastPlanCount";
    public const string InitialGenerationVisitedNodes = "initialGeneration.visitedNodes";
    public const string InitialGenerationGeneratedCandidates = "initialGeneration.generatedCandidates";
    public const string InitialGenerationAcceptedPlans = "initialGeneration.acceptedPlans";
    public const string InitialGenerationInfeasibleCandidates = "initialGeneration.infeasibleCandidates";
    public const string InitialGenerationDuplicateCandidates = "initialGeneration.duplicateCandidates";
    public const string InitialGenerationDominatedCandidates = "initialGeneration.dominatedCandidates";
    public const string InitialGenerationWidthBoundPrunedNodes = "initialGeneration.widthBoundPrunedNodes";
    public const string InitialGenerationKnifeBoundPrunedNodes = "initialGeneration.knifeBoundPrunedNodes";
    public const string InitialGenerationLengthBoundPrunedEntries = "initialGeneration.lengthBoundPrunedEntries";
    public const string InitialGenerationMaterialWidthIndexCacheHits = "initialGeneration.materialWidthIndexCacheHits";
    public const string InitialGenerationMaterialSliceTemplateCacheHits = "initialGeneration.materialSliceTemplateCacheHits";
    public const string InitialGenerationQuantityCacheHits = "initialGeneration.quantityCacheHits";
    public const string InitialGenerationQuantityCacheMisses = "initialGeneration.quantityCacheMisses";
    public const string InitialGenerationMaterialSliceTemplateCacheMisses = "initialGeneration.materialSliceTemplateCacheMisses";
    public const string InitialGenerationCrossWorkerDuplicateCandidates = "initialGeneration.crossWorkerDuplicateCandidates";
    public const string InitialGenerationCrossContributionDominated = "initialGeneration.crossContributionDominated";
    public const string InitialGenerationElapsedMilliseconds = "initialGeneration.elapsedMilliseconds";
    public const string InitialGenerationStopReason = "initialGeneration.stopReason";
    public const string InitialVisitedNodes = "initialVisitedNodes";
    public const string InitialGeneratedCandidates = "initialGeneratedCandidates";
    public const string InitialAcceptedPlans = "initialAcceptedPlans";
    public const string InitialInfeasibleCandidates = "initialInfeasibleCandidates";
    public const string InitialDuplicateCandidates = "initialDuplicateCandidates";
    public const string InitialDominatedCandidates = "initialDominatedCandidates";
    public const string InitialWidthBoundPrunedNodes = "initialWidthBoundPrunedNodes";
    public const string InitialKnifeBoundPrunedNodes = "initialKnifeBoundPrunedNodes";
    public const string InitialLengthBoundPrunedEntries = "initialLengthBoundPrunedEntries";
    public const string InitialMaterialWidthIndexCacheHits = "initialMaterialWidthIndexCacheHits";
    public const string InitialMaterialSliceTemplateCacheHits = "initialMaterialSliceTemplateCacheHits";
    public const string InitialGenerationElapsedMillisecondsRender = "initialGenerationElapsedMilliseconds";
    public const string InitialGenerationStopReasonRender = "initialGenerationStopReason";
    public const string PricingVisitedNodes = "pricingGeneration.visitedNodes";
    public const string PricingGeneratedCandidates = "pricingGeneration.generatedCandidates";
    public const string PricingAcceptedPlans = "pricingGeneration.acceptedPlans";
    public const string PricingInfeasibleCandidates = "pricingGeneration.infeasibleCandidates";
    public const string PricingDuplicateCandidates = "pricingGeneration.duplicateCandidates";
    public const string PricingDominatedCandidates = "pricingGeneration.dominatedCandidates";
    public const string PricingElapsedMilliseconds = "pricingGeneration.elapsedMilliseconds";
    public const string PricingStopReason = "pricingGeneration.stopReason";
    public const string LpFailureMessage = "lpFailureMessage";
    public const string TotalTrimWidth = "totalTrimWidth";
    public const string TotalRestMaterial = "totalRestMaterial";
    public const string OverProductionArea = "overProductionArea";
    public const string OverProductionAreaMeasure = "overProductionAreaMeasure";
    public const string RestMaterialMeasure = "restMaterialMeasure";

    /// <summary>物料使用批次数 key / Material usage batch-count key.</summary>
    public static string MaterialUsageBatchCount(string materialId) => $"materialUsage.{materialId}.batchCount";

    /// <summary>设备产能使用 key / Machine capacity usage key.</summary>
    public static string MachineCapacityUsed(string machineId) => $"machineCapacityUsed.{machineId}";

    /// <summary>欠产 key / Under-production key.</summary>
    public static string UnderProduction(string productId, string unitSymbol) => $"underProduction.{productId}.{unitSymbol}";

    /// <summary>超产 key / Over-production key.</summary>
    public static string OverProduction(string productId, string unitSymbol) => $"overProduction.{productId}.{unitSymbol}";

    /// <summary>物料成本 key / Material cost key.</summary>
    public static string MaterialCost(string materialId) => $"materialCost.{materialId}";

    /// <summary>分配长度 key / Assigned length key.</summary>
    public static string AssignedLength(string productId) => $"assignedLength.{productId}";

    /// <summary>超长 key / Over-length key.</summary>
    public static string OverLength(string productId) => $"overLength.{productId}";
}

/// <summary>
/// CSP1D KPI / CSP1D KPI.
/// </summary>
public sealed record Csp1dKpi {
    /// <summary>选中方案数量 / Selected plan count.</summary>
    public UInt64 SelectedPlanCount { get; init; }

    /// <summary>选中车次数量 / Selected batch count.</summary>
    public UInt64 SelectedBatchCount { get; init; }

    /// <summary>已满足需求数量 / Satisfied demand count.</summary>
    public UInt64 SatisfiedDemandCount { get; init; }

    /// <summary>未满足需求数量 / Unmet demand count.</summary>
    public UInt64 UnmetDemandCount { get; init; }

    /// <summary>物料使用条目数 / Material usage entry count.</summary>
    public UInt64 MaterialUsageCount { get; init; }

    /// <summary>设备使用条目数 / Machine usage entry count.</summary>
    public UInt64 MachineUsageCount { get; init; }

    /// <summary>本轮生成方案总数 / Generated plan count.</summary>
    public UInt64 GeneratedPlanCount { get; init; }

    /// <summary>Top-K 方案数量 / Top-K plan count.</summary>
    public UInt64 TopPlanCount { get; init; }

    /// <summary>yield 回填指标数 / Yield metric count.</summary>
    public UInt64 YieldMetricCount { get; init; }

    /// <summary>waste 回填指标数 / Waste metric count.</summary>
    public UInt64 WasteMetricCount { get; init; }

    /// <summary>length 回填指标数 / Length metric count.</summary>
    public UInt64 LengthMetricCount { get; init; }

    /// <summary>可序列化 KPI 明细 / Serializable KPI details.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// CSP1D 求解结果 / CSP1D solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record Csp1dSolution<V> where V : struct {
    /// <summary>主问题结果 / Master problem output.</summary>
    public required Produce<V> Produce { get; init; }

    /// <summary>yield 建模结果 / Yield modeling result.</summary>
    public YieldModelingResult<V>? YieldResult { get; init; }

    /// <summary>waste 建模结果 / Waste modeling result.</summary>
    public WasteMinimizationResult<V>? WasteResult { get; init; }

    /// <summary>length 建模结果 / Length modeling result.</summary>
    public LengthAssignmentModelingResult<V>? LengthResult { get; init; }

    /// <summary>切割方案池 / Generated cutting plans.</summary>
    public required IReadOnlyList<CuttingPlan<V>> GeneratedPlans { get; init; }

    /// <summary>KPI / KPI.</summary>
    public required Csp1dKpi Kpi { get; init; }

    /// <summary>渲染输出 / Render output.</summary>
    public required RenderSchemaDTO Render { get; init; }

    /// <summary>解状态 / Solution status.</summary>
    public Csp1dSolutionStatus Status { get; init; } = Csp1dSolutionStatus.Feasible;

    /// <summary>失败信息 / Failure message.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>Top-K 方案 / Top-K plans.</summary>
    public IReadOnlyList<CuttingPlan<V>> TopPlans { get; init; } = [];
}

/// <summary>
/// CSP1D 解分析器 / CSP1D solution analyzer.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dSolutionAnalyzer<V> where V : struct {
    /// <summary>
    /// 分析并组装解 / Analyze and build solution.
    /// </summary>
    /// <param name="problem">原问题 / Original problem.</param>
    /// <param name="produce">主问题结果 / Master problem output.</param>
    /// <param name="generatedPlans">切割方案池 / Generated cutting plans.</param>
    /// <returns>CSP1D 解 / CSP1D solution.</returns>
    Csp1dSolution<V> Analyze(Csp1dProblem<V> problem, Produce<V> produce, IReadOnlyList<CuttingPlan<V>> generatedPlans);
}

/// <summary>
/// 默认解分析器 / Default solution analyzer.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class DefaultCsp1dSolutionAnalyzer<V> : ICsp1dSolutionAnalyzer<V> where V : struct {
    /// <inheritdoc/>
    public Csp1dSolution<V> Analyze(
        Csp1dProblem<V> problem,
        Produce<V> produce,
        IReadOnlyList<CuttingPlan<V>> generatedPlans) {
        UInt64 selectedBatchCount = produce.CuttingPlans.Aggregate(
            UInt64.Zero,
            (acc, usage) => acc + usage.Amount);

        var satisfiedDemandCount = new UInt64((ulong)(problem.Demands.Count - produce.UnmetDemands.Count));

        var kpi = new Csp1dKpi {
            SelectedPlanCount = new UInt64((ulong)produce.CuttingPlans.Count),
            SelectedBatchCount = selectedBatchCount,
            SatisfiedDemandCount = satisfiedDemandCount,
            UnmetDemandCount = new UInt64((ulong)produce.UnmetDemands.Count),
            MaterialUsageCount = new UInt64((ulong)produce.MaterialUsages.Count),
            MachineUsageCount = new UInt64((ulong)produce.MachineUsages.Count),
            GeneratedPlanCount = new UInt64((ulong)generatedPlans.Count)
        };

        var renderKpi = new Dictionary<string, string> {
            [Csp1dKpiKeys.SelectedPlanCount] = kpi.SelectedPlanCount.ToString(),
            [Csp1dKpiKeys.SelectedBatchCount] = kpi.SelectedBatchCount.ToString(),
            [Csp1dKpiKeys.SatisfiedDemandCount] = kpi.SatisfiedDemandCount.ToString(),
            [Csp1dKpiKeys.UnmetDemandCount] = kpi.UnmetDemandCount.ToString(),
            [Csp1dKpiKeys.MaterialUsageCount] = kpi.MaterialUsageCount.ToString(),
            [Csp1dKpiKeys.MachineUsageCount] = kpi.MachineUsageCount.ToString(),
            [Csp1dKpiKeys.GeneratedPlanCount] = kpi.GeneratedPlanCount.ToString(),
            [Csp1dKpiKeys.TopPlanCount] = kpi.TopPlanCount.ToString(),
            [Csp1dKpiKeys.YieldMetricCount] = kpi.YieldMetricCount.ToString(),
            [Csp1dKpiKeys.WasteMetricCount] = kpi.WasteMetricCount.ToString(),
            [Csp1dKpiKeys.LengthMetricCount] = kpi.LengthMetricCount.ToString()
        };

        var render = new RenderSchemaDTO(
            Kpi: renderKpi,
            CuttingPlans: produce.CuttingPlans.Select(usage => RenderCuttingPlan(usage.Plan, usage.Amount)).ToList()
        );

        return new Csp1dSolution<V> {
            Produce = produce,
            GeneratedPlans = generatedPlans,
            Kpi = kpi,
            Render = render
        };
    }

    private RenderCuttingPlanDTO RenderCuttingPlan(CuttingPlan<V> plan, UInt64 amount) {
        List<RenderCuttingPlanProductionDTO> productions = new();
        FltX cursor = FltX.Zero;
        foreach (CuttingPlanSlice<V> slice in plan.Slices) {
            FltX width = QuantityToFltX(slice.Width);
            RenderCuttingPlanProductionDTO productionDto = ToRenderProductionDto(slice.Production, cursor, width, slice.Amount);
            productions.Add(productionDto);
            cursor += width;
        }

        return new RenderCuttingPlanDTO(
            Group: new List<string> { plan.Material.Name, plan.MachineId ?? "unassigned-machine" },
            Productions: productions,
            Width: plan.UsedWidth != null ? QuantityToFltX(plan.UsedWidth) : FltX.Zero,
            StandardWidth: QuantityToFltX(plan.Material.WidthRange.UpperBound),
            Amount: amount,
            Info: new Dictionary<string, string> { ["planId"] = plan.Id }
        );
    }

    private RenderCuttingPlanProductionDTO ToRenderProductionDto(object production, FltX x, FltX width, UInt64 amount) {
        if (production is Product<V> product) {
            return product.ToRenderDto(
                x: x,
                productionType: RenderProductionType.Product,
                info: new Dictionary<string, string> { ["amount"] = amount.ToString() });
        }

        if (production is Costar<V> costar) {
            return new RenderCuttingPlanProductionDTO(
                Name: costar.Name,
                X: x,
                Width: width,
                UnitLength: costar.Length != null ? QuantityToFltX(costar.Length) : (FltX?)null,
                ProductionType: RenderProductionType.Costar,
                Info: new Dictionary<string, string> { ["amount"] = amount.ToString() }
            );
        }

        return new RenderCuttingPlanProductionDTO(
            Name: "unknown-production",
            X: x,
            Width: width,
            UnitLength: null,
            ProductionType: RenderProductionType.Product,
            Info: new Dictionary<string, string> { ["amount"] = amount.ToString() }
        );
    }

    /// <summary>
    /// 运行时安全转换 Quantity 值到 FltX / Runtime-safe conversion from Quantity value to FltX
    /// </summary>
    private static FltX QuantityToFltX(Quantity<V> quantity) {
        if (quantity.Value is FltX fx) {
            return fx;
        }

        if (quantity.Value is Flt64 f64) {
            return f64.ToFltX();
        }

        if (quantity.Value is Int64 i64) {
            return i64.ToFlt64().ToFltX();
        }

        throw new System.NotSupportedException($"Unsupported quantity value type: {quantity.Value.GetType().Name}");
    }
}
