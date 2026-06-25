#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// 列生成终止原因 / Column generation termination reason.
/// </summary>
public enum Csp1dTerminationReason {
    /// <summary>定价收敛（无新列）/ Pricing converged (no new columns).</summary>
    PricingConverged,

    /// <summary>达到迭代上限 / Iteration limit reached.</summary>
    IterationLimitReached,

    /// <summary>LP 求解失败（异常、超时等，有前序有效 LP 解）/ LP solve failed (exception, timeout, etc., with a prior valid LP result).</summary>
    LpSolveFailed,

    /// <summary>首次 LP 求解即失败，疑似 LP 松弛不可行 / First LP solve failed, likely LP relaxation infeasible.</summary>
    LpInfeasible,

    /// <summary>所有定价列均为重复 / All priced columns are duplicates.</summary>
    AllDuplicates,

    /// <summary>无初始方案 / No initial plans.</summary>
    NoInitialPlans
}

/// <summary>
/// 列生成迭代记录 / Column generation iteration record.
/// </summary>
/// <param name="Iteration">迭代序号 / Iteration number.</param>
/// <param name="LpObjective">LP 目标值 / LP objective value.</param>
/// <param name="PlanCountBefore">本轮开始时方案池大小 / Plan pool size before this iteration.</param>
/// <param name="PricedPlanCount">本轮定价新增方案数 / Number of plans added by pricing this iteration.</param>
/// <param name="PlanCountAfter">本轮结束时方案池大小 / Plan pool size after this iteration.</param>
public sealed record Csp1dIterationRecord(
    Int64 Iteration,
    Flt64 LpObjective,
    Int64 PlanCountBefore,
    UInt64 PricedPlanCount,
    Int64 PlanCountAfter
);

/// <summary>
/// CSP1D 解丰富辅助器 / CSP1D solution enrichment helper.
///
/// 提供 topCuttingPlans 与 enrichSolution 两个内部静态方法，
/// 用于在列生成完成后丰富解的 KPI、渲染输出和 Top-K 方案。
///
/// Provides two internal static methods, topCuttingPlans and enrichSolution,
/// used to enrich solution KPI, render output and Top-K plans after column generation.
/// </summary>
internal static class Csp1dSolutionEnrichmentHelper {
    /// <summary>
    /// 选取 Top-K 切割方案 / Select top-K cutting plans.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="plans">候选方案集合 / Candidate plan collection.</param>
    /// <param name="limit">Top-K 上限 / Top-K limit.</param>
    /// <returns>按评分降序排列的 Top-K 方案 / Top-K plans sorted by score in descending order.</returns>
    internal static List<CuttingPlan<V>> TopCuttingPlans<V>(
        IReadOnlyList<CuttingPlan<V>> plans,
        Int64? limit)
        where V : struct {
        if (limit == null || limit.Value <= Int64.Zero) {
            return [];
        }
        var topK = new TopKCuttingPlans<V>(limit.Value);
        topK.OfferAll(plans);
        return topK.ToSortedList();
    }

    /// <summary>
    /// 丰富 CSP1D 解 / Enrich CSP1D solution.
    ///
    /// 将 KPI 明细、渲染输出、Top-K 方案、终止原因、生成统计等信息
    /// 回填到解对象中。提取策略失败不会中断丰富流程。
    ///
    /// Backfill KPI details, render output, Top-K plans, termination reason,
    /// generation statistics, etc. into the solution object.
    /// Extraction policy failure does not interrupt the enrichment pipeline.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="solution">原始解 / Original solution.</param>
    /// <param name="topPlans">Top-K 方案 / Top-K plans.</param>
    /// <param name="status">解状态 / Solution status.</param>
    /// <param name="failureMessage">失败信息 / Failure message.</param>
    /// <param name="terminationReason">列生成终止原因 / Column generation termination reason.</param>
    /// <param name="finalMilpStatus">最终 MILP 状态 / Final MILP status.</param>
    /// <param name="partialSolutionAvailable">是否有部分解可用 / Whether partial solution is available.</param>
    /// <param name="initialGenerationStatistics">初始生成统计 / Initial generation statistics.</param>
    /// <param name="pricingGenerationStatistics">定价生成统计 / Pricing generation statistics.</param>
    /// <param name="lpFailureMessage">LP 失败信息 / LP failure message.</param>
    /// <param name="iterationRecords">迭代记录 / Iteration records.</param>
    /// <param name="extractionPolicies">提取策略列表 / Extraction policy list.</param>
    /// <param name="demands">需求列表 / Demand list.</param>
    /// <param name="materials">物料列表 / Material list.</param>
    /// <param name="machines">设备列表 / Machine list.</param>
    /// <returns>丰富后的解 / Enriched solution.</returns>
    internal static Csp1dSolution<V> EnrichSolution<V>(
        Csp1dSolution<V> solution,
        IReadOnlyList<CuttingPlan<V>> topPlans,
        Csp1dSolutionStatus status,
        string? failureMessage,
        Csp1dTerminationReason? terminationReason = null,
        Csp1dFinalMilpStatus? finalMilpStatus = null,
        bool partialSolutionAvailable = false,
        CuttingPlanGenerationStatistics? initialGenerationStatistics = null,
        CuttingPlanGenerationStatistics? pricingGenerationStatistics = null,
        string? lpFailureMessage = null,
        IReadOnlyList<Csp1dIterationRecord>? iterationRecords = null,
        IReadOnlyList<ICsp1dExtractionPolicy<V>>? extractionPolicies = null,
        IReadOnlyList<ProductDemand<V>>? demands = null,
        IReadOnlyList<Material<V>>? materials = null,
        IReadOnlyList<Machine<V>>? machines = null)
        where V : struct {
        iterationRecords ??= [];
        extractionPolicies ??= [];
        demands ??= [];
        materials ??= [];
        machines ??= [];

        Dictionary<string, string> details = KpiDetails(
            solution: solution,
            topPlans: topPlans,
            terminationReason: terminationReason,
            finalMilpStatus: finalMilpStatus,
            partialSolutionAvailable: partialSolutionAvailable,
            initialGenerationStatistics: initialGenerationStatistics,
            iterationRecords: iterationRecords
        );

        Csp1dKpi kpi = solution.Kpi with {
            TopPlanCount = new UInt64((ulong)topPlans.Count),
            YieldMetricCount = YieldMetricCount(solution),
            WasteMetricCount = WasteMetricCount(solution),
            LengthMetricCount = LengthMetricCount(solution),
            Details = details
        };

        var renderKpi = solution.Render.Kpi.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (KeyValuePair<string, string> kv in details) {
            renderKpi[kv.Key] = kv.Value;
        }
        renderKpi[Csp1dKpiKeys.TopPlanCount] = kpi.TopPlanCount.ToString();
        renderKpi[Csp1dKpiKeys.YieldMetricCount] = kpi.YieldMetricCount.ToString();
        renderKpi[Csp1dKpiKeys.WasteMetricCount] = kpi.WasteMetricCount.ToString();
        renderKpi[Csp1dKpiKeys.LengthMetricCount] = kpi.LengthMetricCount.ToString();
        renderKpi[Csp1dKpiKeys.SolutionStatus] = status.ToString();
        if (terminationReason != null) {
            renderKpi[Csp1dKpiKeys.TerminationReason] = terminationReason.ToString()!;
        }
        if (finalMilpStatus != null) {
            renderKpi[Csp1dKpiKeys.FinalMilpStatus] = finalMilpStatus.ToString()!;
        }
        renderKpi[Csp1dKpiKeys.PartialSolutionAvailable] = partialSolutionAvailable.ToString();
        if (initialGenerationStatistics != null) {
            renderKpi[Csp1dKpiKeys.InitialVisitedNodes] = initialGenerationStatistics.VisitedNodes.ToString();
            renderKpi[Csp1dKpiKeys.InitialGeneratedCandidates] = initialGenerationStatistics.GeneratedCandidates.ToString();
            renderKpi[Csp1dKpiKeys.InitialAcceptedPlans] = initialGenerationStatistics.AcceptedPlans.ToString();
            renderKpi[Csp1dKpiKeys.InitialInfeasibleCandidates] = initialGenerationStatistics.InfeasibleCandidates.ToString();
            renderKpi[Csp1dKpiKeys.InitialDuplicateCandidates] = initialGenerationStatistics.DuplicateCandidates.ToString();
            renderKpi[Csp1dKpiKeys.InitialDominatedCandidates] = initialGenerationStatistics.DominatedCandidates.ToString();
            renderKpi[Csp1dKpiKeys.InitialWidthBoundPrunedNodes] = initialGenerationStatistics.WidthBoundPrunedNodes.ToString();
            renderKpi[Csp1dKpiKeys.InitialKnifeBoundPrunedNodes] = initialGenerationStatistics.KnifeBoundPrunedNodes.ToString();
            renderKpi[Csp1dKpiKeys.InitialLengthBoundPrunedEntries] = initialGenerationStatistics.LengthBoundPrunedEntries.ToString();
            renderKpi[Csp1dKpiKeys.InitialMaterialWidthIndexCacheHits] = initialGenerationStatistics.MaterialWidthIndexCacheHits.ToString();
            renderKpi[Csp1dKpiKeys.InitialMaterialSliceTemplateCacheHits] = initialGenerationStatistics.MaterialSliceTemplateCacheHits.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationQuantityCacheHits] = initialGenerationStatistics.QuantityCacheHits.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationQuantityCacheMisses] = initialGenerationStatistics.QuantityCacheMisses.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationMaterialSliceTemplateCacheMisses] = initialGenerationStatistics.MaterialSliceTemplateCacheMisses.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationCrossWorkerDuplicateCandidates] = initialGenerationStatistics.CrossWorkerDuplicateCandidates.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationCrossContributionDominated] = initialGenerationStatistics.CrossContributionDominated.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationElapsedMillisecondsRender] = initialGenerationStatistics.ElapsedMilliseconds.ToString();
            renderKpi[Csp1dKpiKeys.InitialGenerationStopReasonRender] = initialGenerationStatistics.StopReason.ToString();
        }
        if (pricingGenerationStatistics != null) {
            renderKpi[Csp1dKpiKeys.PricingVisitedNodes] = pricingGenerationStatistics.VisitedNodes.ToString();
            renderKpi[Csp1dKpiKeys.PricingGeneratedCandidates] = pricingGenerationStatistics.GeneratedCandidates.ToString();
            renderKpi[Csp1dKpiKeys.PricingAcceptedPlans] = pricingGenerationStatistics.AcceptedPlans.ToString();
            renderKpi[Csp1dKpiKeys.PricingInfeasibleCandidates] = pricingGenerationStatistics.InfeasibleCandidates.ToString();
            renderKpi[Csp1dKpiKeys.PricingDuplicateCandidates] = pricingGenerationStatistics.DuplicateCandidates.ToString();
            renderKpi[Csp1dKpiKeys.PricingDominatedCandidates] = pricingGenerationStatistics.DominatedCandidates.ToString();
            renderKpi[Csp1dKpiKeys.PricingElapsedMilliseconds] = pricingGenerationStatistics.ElapsedMilliseconds.ToString();
            renderKpi[Csp1dKpiKeys.PricingStopReason] = pricingGenerationStatistics.StopReason.ToString();
        }
        if (lpFailureMessage != null) {
            renderKpi[Csp1dKpiKeys.LpFailureMessage] = lpFailureMessage;
        }
        else {
            renderKpi.Remove(Csp1dKpiKeys.LpFailureMessage);
        }
        if (failureMessage != null) {
            renderKpi[Csp1dKpiKeys.FailureMessage] = failureMessage;
        }
        else {
            renderKpi.Remove(Csp1dKpiKeys.FailureMessage);
        }

        // Apply extraction policies to enrich output
        if (extractionPolicies.Count > 0) {
            var extractionDetails = new Dictionary<string, string>(details);
            foreach (ICsp1dExtractionPolicy<V> policy in extractionPolicies) {
                try {
                    policy.EnrichOutput(
                        details: extractionDetails,
                        renderKpi: renderKpi,
                        produce: solution.Produce,
                        demands: demands,
                        materials: materials,
                        machines: machines,
                        generatedPlans: solution.GeneratedPlans,
                        iterationCount: (long)iterationRecords.Count,
                        terminationReason: terminationReason?.ToString(),
                        finalMilpStatus: finalMilpStatus?.ToString(),
                        pricingStatistics: pricingGenerationStatistics
                    );
                }
                catch {
                    // Extraction policy failure must not escape or break the enrichment pipeline
                }
            }
            // Merge extraction details into kpi.details and renderKpi
            foreach (string? key in extractionDetails.Keys.Except(details.Keys)) {
                if (extractionDetails.TryGetValue(key, out string? value)) {
                    renderKpi[key] = value;
                }
            }
            return solution with {
                Kpi = kpi with { Details = extractionDetails },
                Render = solution.Render with { Kpi = renderKpi },
                Status = status,
                FailureMessage = failureMessage,
                TopPlans = topPlans
            };
        }

        return solution with {
            Kpi = kpi,
            Render = solution.Render with { Kpi = renderKpi },
            Status = status,
            FailureMessage = failureMessage,
            TopPlans = topPlans
        };
    }

    /// <summary>
    /// 构建 KPI 明细 / Build KPI details.
    /// </summary>
    private static Dictionary<string, string> KpiDetails<V>(
        Csp1dSolution<V> solution,
        IReadOnlyList<CuttingPlan<V>> topPlans,
        Csp1dTerminationReason? terminationReason,
        Csp1dFinalMilpStatus? finalMilpStatus,
        bool partialSolutionAvailable,
        CuttingPlanGenerationStatistics? initialGenerationStatistics,
        IReadOnlyList<Csp1dIterationRecord> iterationRecords)
        where V : struct {
        var details = new Dictionary<string, string>();

        details[Csp1dKpiKeys.GeneratedPlanCount] = solution.GeneratedPlans.Count.ToString();
        details[Csp1dKpiKeys.SelectedPlanCount] = solution.Produce.CuttingPlans.Count.ToString();
        details[Csp1dKpiKeys.SelectedBatchCount] = solution.Produce.CuttingPlans.Aggregate(
            UInt64.Zero,
            (acc, usage) => acc + usage.Amount
        ).ToString();
        details[Csp1dKpiKeys.TopPlanCount] = topPlans.Count.ToString();
        details[Csp1dKpiKeys.PartialSolutionAvailable] = partialSolutionAvailable.ToString();

        if (terminationReason != null) {
            details[Csp1dKpiKeys.ColumnGenerationTerminationReason] = terminationReason.ToString()!;
        }
        if (finalMilpStatus != null) {
            details[Csp1dKpiKeys.FinalMilpStatus] = finalMilpStatus.ToString()!;
        }
        details[Csp1dKpiKeys.ColumnGenerationIterationCount] = iterationRecords.Count.ToString();
        details[Csp1dKpiKeys.ColumnGenerationPricedPlanCount] = iterationRecords.Aggregate(
            UInt64.Zero,
            (acc, record) => acc + record.PricedPlanCount
        ).ToString();
        Csp1dIterationRecord? lastRecord = iterationRecords.LastOrDefault();
        if (lastRecord != null) {
            details[Csp1dKpiKeys.ColumnGenerationLastLpObjective] = lastRecord.LpObjective.ToString();
            details[Csp1dKpiKeys.ColumnGenerationLastPlanCount] = lastRecord.PlanCountAfter.ToString();
        }

        if (initialGenerationStatistics != null) {
            details[Csp1dKpiKeys.InitialGenerationVisitedNodes] = initialGenerationStatistics.VisitedNodes.ToString();
            details[Csp1dKpiKeys.InitialGenerationGeneratedCandidates] = initialGenerationStatistics.GeneratedCandidates.ToString();
            details[Csp1dKpiKeys.InitialGenerationAcceptedPlans] = initialGenerationStatistics.AcceptedPlans.ToString();
            details[Csp1dKpiKeys.InitialGenerationInfeasibleCandidates] = initialGenerationStatistics.InfeasibleCandidates.ToString();
            details[Csp1dKpiKeys.InitialGenerationDuplicateCandidates] = initialGenerationStatistics.DuplicateCandidates.ToString();
            details[Csp1dKpiKeys.InitialGenerationDominatedCandidates] = initialGenerationStatistics.DominatedCandidates.ToString();
            details[Csp1dKpiKeys.InitialGenerationWidthBoundPrunedNodes] = initialGenerationStatistics.WidthBoundPrunedNodes.ToString();
            details[Csp1dKpiKeys.InitialGenerationKnifeBoundPrunedNodes] = initialGenerationStatistics.KnifeBoundPrunedNodes.ToString();
            details[Csp1dKpiKeys.InitialGenerationLengthBoundPrunedEntries] = initialGenerationStatistics.LengthBoundPrunedEntries.ToString();
            details[Csp1dKpiKeys.InitialGenerationMaterialWidthIndexCacheHits] = initialGenerationStatistics.MaterialWidthIndexCacheHits.ToString();
            details[Csp1dKpiKeys.InitialGenerationMaterialSliceTemplateCacheHits] = initialGenerationStatistics.MaterialSliceTemplateCacheHits.ToString();
            details[Csp1dKpiKeys.InitialGenerationQuantityCacheHits] = initialGenerationStatistics.QuantityCacheHits.ToString();
            details[Csp1dKpiKeys.InitialGenerationQuantityCacheMisses] = initialGenerationStatistics.QuantityCacheMisses.ToString();
            details[Csp1dKpiKeys.InitialGenerationMaterialSliceTemplateCacheMisses] = initialGenerationStatistics.MaterialSliceTemplateCacheMisses.ToString();
            details[Csp1dKpiKeys.InitialGenerationCrossWorkerDuplicateCandidates] = initialGenerationStatistics.CrossWorkerDuplicateCandidates.ToString();
            details[Csp1dKpiKeys.InitialGenerationCrossContributionDominated] = initialGenerationStatistics.CrossContributionDominated.ToString();
            details[Csp1dKpiKeys.InitialGenerationElapsedMilliseconds] = initialGenerationStatistics.ElapsedMilliseconds.ToString();
            details[Csp1dKpiKeys.InitialGenerationStopReason] = initialGenerationStatistics.StopReason.ToString();
        }

        foreach (MaterialUsage<V> materialUsage in solution.Produce.MaterialUsages) {
            details[Csp1dKpiKeys.MaterialUsageBatchCount(materialUsage.Material.Id)] = materialUsage.Amount.ToString();
        }
        foreach (MachineCapacityUsage<V> machineUsage in solution.Produce.MachineUsages) {
            Quantity<V>? used = machineUsage.Used;
            if (used == null) {
                continue;
            }

            details[Csp1dKpiKeys.MachineCapacityUsed(machineUsage.Machine.Id)] = used.ToString();
        }
        foreach (ModeledUnderProduction<V> underProduction in solution.YieldResult?.UnderProductions ?? []) {
            details[Csp1dKpiKeys.UnderProduction(
                productId: underProduction.ProductId,
                unitSymbol: underProduction.UnitSymbol
            )] = underProduction.Amount.ToString()!;
        }
        foreach (ModeledOverProduction<V> overProduction in solution.YieldResult?.OverProductions ?? []) {
            details[Csp1dKpiKeys.OverProduction(
                productId: overProduction.ProductId,
                unitSymbol: overProduction.UnitSymbol
            )] = overProduction.Amount.ToString()!;
        }

        WasteMinimizationResult<V>? wasteResult = solution.WasteResult;
        if (wasteResult != null) {
            if (wasteResult.TotalTrimWidth != null) { details[Csp1dKpiKeys.TotalTrimWidth] = wasteResult.TotalTrimWidth.ToString()!; }
            if (wasteResult.TotalRestMaterial != null) { details[Csp1dKpiKeys.TotalRestMaterial] = wasteResult.TotalRestMaterial.ToString()!; }
            if (wasteResult.OverProductionArea != null) { details[Csp1dKpiKeys.OverProductionArea] = wasteResult.OverProductionArea.ToString()!; }
            details[Csp1dKpiKeys.OverProductionAreaMeasure] = wasteResult.OverProductionAreaMeasure.ToString();
            details[Csp1dKpiKeys.RestMaterialMeasure] = wasteResult.RestMaterialMeasure.ToString();
            foreach (ModeledMaterialCost<V> materialCost in wasteResult.MaterialCosts) {
                details[Csp1dKpiKeys.MaterialCost(materialCost.MaterialId)] = materialCost.Cost.ToString()!;
            }
        }

        foreach (ModeledAssignedLength<V> assignedLength in solution.LengthResult?.AssignedLengths ?? []) {
            details[Csp1dKpiKeys.AssignedLength(assignedLength.ProductId)] = assignedLength.AssignedLength.ToString()!;
        }
        foreach (ModeledOverLength<V> overLength in solution.LengthResult?.OverLengths ?? []) {
            details[Csp1dKpiKeys.OverLength(overLength.ProductId)] = overLength.OverLength.ToString()!;
        }

        return details;
    }

    /// <summary>
    /// 计算 yield 指标数量 / Compute yield metric count.
    /// </summary>
    private static UInt64 YieldMetricCount<V>(Csp1dSolution<V> solution) where V : struct {
        YieldModelingResult<V>? result = solution.YieldResult;
        if (result == null) {
            return UInt64.Zero;
        }

        return new UInt64((ulong)(result.UnderProductions?.Count ?? 0))
             + new UInt64((ulong)(result.OverProductions?.Count ?? 0));
    }

    /// <summary>
    /// 计算 waste 指标数量 / Compute waste metric count.
    /// </summary>
    private static UInt64 WasteMetricCount<V>(Csp1dSolution<V> solution) where V : struct {
        WasteMinimizationResult<V>? result = solution.WasteResult;
        if (result == null) {
            return UInt64.Zero;
        }

        var count = new UInt64((ulong)result.MaterialCosts.Count);
        if (result.TotalTrimWidth != null) { count += UInt64.One; }
        if (result.TotalRestMaterial != null) { count += UInt64.One; }
        if (result.OverProductionArea != null) { count += UInt64.One; }
        return count;
    }

    /// <summary>
    /// 计算 length 指标数量 / Compute length metric count.
    /// </summary>
    private static UInt64 LengthMetricCount<V>(Csp1dSolution<V> solution) where V : struct {
        LengthAssignmentModelingResult<V>? result = solution.LengthResult;
        if (result == null) {
            return UInt64.Zero;
        }

        return new UInt64((ulong)(result.AssignedLengths?.Count ?? 0))
             + new UInt64((ulong)(result.OverLengths?.Count ?? 0));
    }
}
