#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Service.Pipeline;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
using Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization;
using Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Service.Pipeline;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using DomainOverProductionAreaMeasure = Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Service.Pipeline.OverProductionAreaMeasure;
using DomainRestMaterialMeasure = Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Service.Pipeline.RestMaterialMeasure;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// CSP1D 产出模型上下文 / CSP1D produce model context.
///
/// 组合所有 Aggregation 和 Pipeline，实现 ICsp1dIterativeContext 接口。
/// 这是 CSP1D 建模注册模式的默认实现，同时支持 MILP 和 LP 两种模式。
///
/// Composes all Aggregations and Pipelines, implementing the ICsp1dIterativeContext interface.
/// This is the default implementation of the CSP1D modeling registration pattern,
/// supporting both MILP and LP modes.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dProduceContext<V> : ICsp1dIterativeContext<V>, ICsp1dModelingContext<V>
    where V : struct {
    private readonly IReadOnlyList<IPipeline<LinearMetaModel<Flt64>>> _constraintPipelines;
    private readonly IReadOnlyList<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> _cgPipelines;
    private readonly YieldObjectivePipeline<V>? _yieldObjective;
    private readonly WasteObjectivePipeline<V>? _wasteObjective;
    private readonly LengthObjectivePipeline<V>? _lengthObjective;
    private readonly IReadOnlyList<IPipeline<LinearMetaModel<Flt64>>> _extraPipelines;
    private readonly IReadOnlyList<CuttingPlanUsage<V>> _warmStartPlanUsages;
    private readonly OverProductionAreaMeasure _wasteOverProductionAreaMeasure;
    private readonly RestMaterialMeasure _wasteRestMaterialMeasure;
    private readonly IReadOnlyList<ICsp1dObjectivePolicy<V>> _objectivePolicies;

    /// <summary>
    /// 构造 CSP1D 产出模型上下文 / Construct CSP1D produce model context.
    /// </summary>
    /// <param name="produce">产出聚合根 / Produce aggregation root.</param>
    /// <param name="yield">产出偏差聚合根，可为 null / Yield deviation aggregation root, may be null.</param>
    /// <param name="waste">浪费最小化聚合根，可为 null / Waste minimization aggregation root, may be null.</param>
    /// <param name="length">长度分配聚合根，可为 null / Length assignment aggregation root, may be null.</param>
    /// <param name="constraintPipelines">约束管线列表 / Constraint pipeline list.</param>
    /// <param name="cgPipelines">列生成管线列表 / Column generation pipeline list.</param>
    /// <param name="yieldObjective">产出偏差目标管线，可为 null / Yield objective pipeline, may be null.</param>
    /// <param name="wasteObjective">浪费最小化目标管线，可为 null / Waste objective pipeline, may be null.</param>
    /// <param name="lengthObjective">长度分配目标管线，可为 null / Length objective pipeline, may be null.</param>
    /// <param name="extraPipelines">额外约束管线 / Extra constraint pipelines.</param>
    /// <param name="mode">建模模式 / Modeling mode.</param>
    /// <param name="warmStartPlanUsages">热启动方案使用量 / Warm start plan usages.</param>
    /// <param name="wasteOverProductionAreaMeasure">超产面积度量口径 / Over-production area measure.</param>
    /// <param name="wasteRestMaterialMeasure">余料度量口径 / Rest material measure.</param>
    /// <param name="objectivePolicies">目标策略列表 / Objective policy list.</param>
    /// <param name="domainValueSample">领域数值样本 / Domain value sample.</param>
    /// <param name="isFinalMilp">是否为最终 MILP 求解 / Whether this is a final MILP solve.</param>
    /// <param name="feasibilityPhase">是否处于可行性阶段 / Whether the feasibility phase is active.</param>
    public Csp1dProduceContext(
        ProduceAggregation<V> produce,
        YieldAggregation? yield,
        WasteAggregation? waste,
        LengthAggregation? length,
        IReadOnlyList<IPipeline<LinearMetaModel<Flt64>>> constraintPipelines,
        IReadOnlyList<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> cgPipelines,
        YieldObjectivePipeline<V>? yieldObjective,
        WasteObjectivePipeline<V>? wasteObjective,
        LengthObjectivePipeline<V>? lengthObjective,
        IReadOnlyList<IPipeline<LinearMetaModel<Flt64>>> extraPipelines,
        Csp1dModelingMode mode,
        IReadOnlyList<CuttingPlanUsage<V>> warmStartPlanUsages,
        OverProductionAreaMeasure wasteOverProductionAreaMeasure,
        RestMaterialMeasure wasteRestMaterialMeasure,
        IReadOnlyList<ICsp1dObjectivePolicy<V>> objectivePolicies,
        V domainValueSample,
        bool isFinalMilp,
        bool feasibilityPhase = false) {
        Produce = produce;
        Yield = yield;
        Waste = waste;
        Length = length;
        _constraintPipelines = constraintPipelines;
        _cgPipelines = cgPipelines;
        _yieldObjective = yieldObjective;
        _wasteObjective = wasteObjective;
        _lengthObjective = lengthObjective;
        _extraPipelines = extraPipelines;
        Mode = mode;
        _warmStartPlanUsages = warmStartPlanUsages;
        _wasteOverProductionAreaMeasure = wasteOverProductionAreaMeasure;
        _wasteRestMaterialMeasure = wasteRestMaterialMeasure;
        _objectivePolicies = objectivePolicies;
        DomainValueSample = domainValueSample;
        IsFinalMilp = isFinalMilp;
        FeasibilityPhase = feasibilityPhase;
    }

    // ===== ICsp1dModelingContext<V> properties =====

    /// <inheritdoc/>
    public Csp1dModelingMode Mode { get; }

    /// <inheritdoc/>
    public bool IsFinalMilp { get; }

    /// <summary>
    /// 是否处于可行性阶段 / Whether the feasibility phase is active.
    /// </summary>
    /// <remarks>
    /// 基线把这个标志直接传给 `setObjective` 与 yield 聚合；C# 原本只传给了 yield 聚合，
    /// **目标函数看不到它**——于是第一阶段解得的是"批次最省"而非"短缺最小"。
    ///
    /// The baseline passes this flag to both `setObjective` and the yield aggregation; C# originally forwarded it only to the yield aggregation, so **the objective never
    /// saw it** — phase one solved "cheapest batches" instead of "smallest deficit" .
    /// </remarks>
    public bool FeasibilityPhase { get; }

    /// <summary>列生成管线列表 / Column generation pipeline list.</summary>
    public IReadOnlyList<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> CgPipelines => _cgPipelines;

    /// <inheritdoc/>
    public ProduceAggregation<V> Produce { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ProductDemand<V>> Demands => Produce.Demands;

    /// <inheritdoc/>
    public IReadOnlyList<Material<V>> Materials => Produce.Materials;

    /// <inheritdoc/>
    public IReadOnlyList<Machine<V>> Machines => Produce.Machines;

    /// <inheritdoc/>
    public V DomainValueSample { get; }

    /// <summary>产出偏差聚合根 / Yield deviation aggregation root.</summary>
    public YieldAggregation? Yield { get; }

    /// <summary>浪费最小化聚合根 / Waste minimization aggregation root.</summary>
    public WasteAggregation? Waste { get; }

    /// <summary>长度分配聚合根 / Length assignment aggregation root.</summary>
    public LengthAggregation? Length { get; }

    /// <inheritdoc/>
    public V ToDomainValue(Flt64 value) {
        Result<V, ErrorCode, Error<ErrorCode>> result = DomainValueConversion.ConvertSolverValue(DomainValueSample, value);
        if (result is Ok<V, ErrorCode, Error<ErrorCode>> ok) {
            return ok.Value;
        }
        throw new InvalidOperationException($"Failed to convert solver value {value} to domain type {typeof(V).Name}.");
    }

    // ===== ICsp1dModelContext<V> implementation =====

    /// <inheritdoc/>
    public Try Register(LinearMetaModel<Flt64> model) {
        // 1. 注册变量 / Register variables
        Try produceResult = Produce.Register(model);
        if (produceResult.IsFailed) {
            return produceResult;
        }

        // LP 模式不加 yield/length slack 变量 / LP mode does not add yield/length slack variables
        if (Mode == Csp1dModelingMode.MILP) {
            if (Yield is not null) {
                Try yieldResult = Yield.Register(model);
                if (yieldResult.IsFailed) {
                    return yieldResult;
                }
            }
            if (Length is not null) {
                Try lengthResult = Length.Register(model);
                if (lengthResult.IsFailed) {
                    return lengthResult;
                }
            }
        }

        // 2. 注册 CG 约束管线（含 group 注册）/ Register CG constraint pipelines (with group registration)
        foreach (ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> pipeline in _cgPipelines) {
            pipeline.Register(model);
            Try result = pipeline.Invoke(model);
            if (result.IsFailed) {
                return result;
            }
        }

        // 3. 注册普通约束管线（length-assignment 例外）/ Register plain constraint pipelines (length-assignment exception)
        foreach (IPipeline<LinearMetaModel<Flt64>> pipeline in _constraintPipelines) {
            pipeline.Register(model);
            Try result = pipeline.Invoke(model);
            if (result.IsFailed) {
                return result;
            }
        }

        // 4. 注册扩展约束管线 / Register extension constraint pipelines
        foreach (IPipeline<LinearMetaModel<Flt64>> pipeline in _extraPipelines) {
            pipeline.Register(model);
            Try result = pipeline.Invoke(model);
            if (result.IsFailed) {
                return result;
            }
        }

        // 5. 组装目标函数 / Assemble objective function
        SetObjective(model);

        // 6. 应用 warm start 初始解（仅 MILP 模式）/ Apply warm start initial solution (MILP mode only)
        if (Mode == Csp1dModelingMode.MILP && _warmStartPlanUsages.Count > 0) {
            ApplyWarmStart(model);
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public Result<Produce<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(IAbstractLinearMetaModel<Flt64> model) {
        var selectedPlans = new List<CuttingPlanUsage<V>>();
        var materialUsageMap = new Dictionary<string, UInt64>();

        for (int index = 0; index < Produce.PlanCount; index++) {
            CuttingPlan<V> plan = Produce.CuttingPlans[index];
            Token<Flt64>? token = model.Tokens.Find(Produce[index]!);
            double? doubleValue = token?.DoubleResult;
            if (doubleValue is null || doubleValue <= 0.0) {
                continue;
            }

            // **必须四舍五入，不是截断。**
            //
            // 基线是 `doubleValue.roundToLong.coerceAtLeast(0)`；C# 原本是 `(long)doubleValue`，
            // **那是截断**：`2.7` 得 2、`0.6` 得 0。后者更糟——**一条用了 0.6 次的方案会被
            // 当成"没用"而整条丢弃**，物料用量、设备用量、欠产判定全部跟着少算。
            //
            // **It must round, not truncate.** The baseline is `doubleValue.roundToLong.coerceAtLeast(0)`; C# was `(long)doubleValue`, **which truncates**: `2.7`
            // becomes 2 and `0.6` becomes 0. The latter is worse — **a plan used 0.6 times is discarded as "unused"**, and the material, machine and deficit figures all
            // under-count with it.
            var amount = new UInt64((ulong)System.Math.Max((long)System.Math.Round(doubleValue.Value, MidpointRounding.AwayFromZero), 0));
            selectedPlans.Add(new CuttingPlanUsage<V>(plan, amount));

            UInt64 currentMaterialUsage = materialUsageMap.TryGetValue(plan.Material.Id, out UInt64 existing)
                ? existing : UInt64.Zero;
            materialUsageMap[plan.Material.Id] = currentMaterialUsage + amount;
        }

        var materialUsages = Produce.Materials
            .Select(material => {
                if (!materialUsageMap.TryGetValue(material.Id, out UInt64 amount)) {
                    return null;
                }

                return new MaterialUsage<V>(material, amount);
            })
            .Where(m => m is not null)
            .Cast<MaterialUsage<V>>()
            .ToList();

        IReadOnlyList<MachineCapacityUsage<V>> machineUsages = ExtractMachineUsages(selectedPlans);

        var unmetDemands = Produce.Demands.Where(demand => {
            Flt64 supplied = selectedPlans.Aggregate(Flt64.Zero, (acc, usage) => {
                CuttingPlanDemandContribution<V>? contribution = usage.Plan.DemandContributions
                    .FirstOrDefault(c =>
                        c.Product.Id == demand.Product.Id
                        && c.Quantity.Unit == demand.Quantity.Unit);
                if (contribution is null) {
                    return acc;
                }

                return acc + ((V)(object)contribution.Quantity.Value).ToFlt64() * usage.Amount.ToFlt64();
            });
            return supplied < ((V)(object)demand.Quantity.Value).ToFlt64();
        }).ToList();

        return new Ok<Produce<V>, ErrorCode, Error<ErrorCode>>(
            new Produce<V>(selectedPlans, materialUsages, machineUsages, unmetDemands));
    }

    // ===== ICsp1dIterativeContext<V> implementation =====

    /// <inheritdoc/>
    public Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        UInt64 iteration,
        IReadOnlyList<CuttingPlan<V>> newPlans,
        IAbstractLinearMetaModel<Flt64> model) {
        int planCountBefore = Produce.PlanCount;

        // 1. 委托 ProduceAggregation.AddColumns 完成变量、batch 和约束中间符号的原地增量
        // Delegate to ProduceAggregation.AddColumns for in-place variable, batch and
        //    constraint intermediate symbol increment
        Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> addResult = Produce.AddColumns(iteration, newPlans, model);
        if (addResult is Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> addFailed) {
            return new Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(addFailed.Error);
        }
        if (addResult is Fatal<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> addFatal) {
            return new Fatal<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(addFatal.Errors);
        }
        IReadOnlyList<CuttingPlan<V>> addedPlans = addResult.Value;
        if (addedPlans.Count == 0) {
            return Results.Ok<IReadOnlyList<CuttingPlan<V>>>(Array.Empty<CuttingPlan<V>>());
        }

        // 2. 追加新列目标项到模型 / Append new column objective terms to model
        //    为每个新增方案追加 minimize(batchCoefficient * x_j) 到目标函数
        // Append minimize(batchCoefficient * x_j) for each new plan to the objective
        Flt64 baseBatchCoefficient = _lengthObjective?.BatchCoefficient() ?? Flt64.One;
        IReadOnlyList<IVariableItem>? latestBatch = Produce.LatestBatch;
        if (latestBatch is not null) {
            for (int planIndex = 0; planIndex < addedPlans.Count && planIndex < latestBatch.Count; planIndex++) {
                CuttingPlan<V> plan = addedPlans[planIndex];
                Flt64 batchCoefficient = _objectivePolicies.Count > 0
                    ? _objectivePolicies.Aggregate(baseBatchCoefficient, (coeff, policy) => {
                        var ctx = new SimpleDomainCalculationContext<V>(
                            plan: plan,
                            planIndex: planCountBefore + planIndex,
                            domainValueSample: DomainValueSample);
                        return policy.ModifyBatchCoefficient(ctx, coeff);
                    })
                    : baseBatchCoefficient;
                var monomial = new LinearMonomial<Flt64>(batchCoefficient, latestBatch[planIndex]);
                if (model is LinearMetaModel<Flt64> linearModel) {
                    Try objResult = linearModel.AddObject(
                        ObjectCategory.Minimum,
                        new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>> { monomial }, Flt64.Zero),
                        name: $"csp1d_objective_{iteration}_{planIndex}",
                        displayName: null);
                    if (objResult is Failed<Success, ErrorCode, Error<ErrorCode>> objFailed) {
                        return new Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(objFailed.Error);
                    }
                }
            }
        }

        // 3. 刷新支持增量加列的扩展管线 / Refresh extension pipelines that support incremental addColumns
        var incrementalContext = (ICsp1dModelingContext<V>)this;
        IReadOnlyList<CuttingPlan<V>> currentAddedPlans = addedPlans;
        foreach (IPipeline<LinearMetaModel<Flt64>> pipeline in _extraPipelines) {
            if (pipeline is not ICsp1dIncrementalPipeline<V> incrementalPipeline) {
                continue;
            }

            Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> incResult = incrementalPipeline.AddColumns(
                context: incrementalContext,
                iteration: iteration,
                newPlans: currentAddedPlans,
                model: model);
            if (incResult is Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> incFailed) {
                return new Failed<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(incFailed.Error);
            }
            if (incResult is Fatal<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> incFatal) {
                return new Fatal<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>>(incFatal.Errors);
            }
            currentAddedPlans = incResult.Value;
            if (currentAddedPlans.Count == 0) {
                return Results.Ok<IReadOnlyList<CuttingPlan<V>>>(Array.Empty<CuttingPlan<V>>());
            }
        }

        return Results.Ok<IReadOnlyList<CuttingPlan<V>>>(currentAddedPlans);
    }

    /// <inheritdoc/>
    public Try ExtractShadowPrice(
        IAbstractLinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices) {
        // 通过 CGPipeline refresh 机制自动提取影子价格到 AbstractCsp1dShadowPriceMap
        // Extract shadow prices automatically via CGPipeline refresh mechanism
        var frameworkMap = new Csp1dDefaultShadowPriceMap();
        var linearModel = (LinearMetaModel<Flt64>)model;
        foreach (ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> pipeline in _cgPipelines) {
            Try result = pipeline.Refresh(frameworkMap, linearModel, shadowPrices);
            if (result.IsFailed) {
                return result;
            }

            ShadowPriceExtractor<AbstractCsp1dShadowPriceArguments, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? extractor = pipeline.Extractor();
            if (extractor is not null) {
                frameworkMap.Put(extractor);
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    // ===== Public extraction methods =====

    /// <summary>
    /// 提取 yield 建模结果 / Extract yield modeling result.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>产出建模结果 / Yield modeling result.</returns>
    public YieldModelingResult<Flt64>? ExtractYieldResult(IAbstractLinearMetaModel<Flt64> model) => Yield?.ExtractResult(model);

    /// <summary>
    /// 提取 length 建模结果 / Extract length modeling result.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>长度分配建模结果 / Length assignment modeling result.</returns>
    public LengthAssignmentModelingResult<Flt64>? ExtractLengthResult(IAbstractLinearMetaModel<Flt64> model) => Length?.ExtractResult(model);

    /// <summary>
    /// 提取 waste 建模结果 / Extract waste modeling result.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>浪费最小化建模结果 / Waste minimization modeling result.</returns>
    public WasteMinimizationResult<V>? ExtractWasteResult(IAbstractLinearMetaModel<Flt64> model) {
        WasteAggregation? wasteAgg = Waste;
        if (wasteAgg is null || !wasteAgg.HasAnyPenalty) {
            return null;
        }

        // 计算总余宽 / Calculate total trim width
        V? totalTrimWidth = null;
        if (wasteAgg.TrimWidthPenalty is not null) {
            V? sum = null;
            for (int index = 0; index < Produce.PlanCount; index++) {
                CuttingPlan<V> plan = Produce.CuttingPlans[index];
                UInt64 batchCount = SolutionAmount(model, index);
                if (batchCount <= UInt64.Zero) {
                    continue;
                }

                V? restWidthValue = plan.RestWidth?.Value;
                if (restWidthValue is null) {
                    continue;
                }

                Result<V, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(restWidthValue.Value, batchCount.ToFlt64());
                if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                    continue;
                }

                dynamic contribution = (V)(dynamic)restWidthValue.Value * (dynamic)convOk.Value;
                sum = sum is not null ? (V)(dynamic)sum + (dynamic)contribution : contribution;
            }
            totalTrimWidth = sum;
        }

        // 计算总余料面积代理 / Calculate total rest material area proxy
        V? totalRestMaterial = null;
        if (wasteAgg.RestMaterialPenalty is not null) {
            V? sum = null;
            for (int index = 0; index < Produce.PlanCount; index++) {
                CuttingPlan<V> plan = Produce.CuttingPlans[index];
                UInt64 batchCount = SolutionAmount(model, index);
                if (batchCount <= UInt64.Zero) {
                    continue;
                }

                V? restMaterialValue = RestMaterialValue(plan);
                if (restMaterialValue is null) {
                    continue;
                }

                Result<V, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(restMaterialValue.Value, batchCount.ToFlt64());
                if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                    continue;
                }

                dynamic contribution = (V)(dynamic)restMaterialValue.Value * (dynamic)convOk.Value;
                sum = sum is not null ? (V)(dynamic)sum + (dynamic)contribution : contribution;
            }
            totalRestMaterial = sum;
        }

        // 计算物料成本 / Calculate material costs
        var materialCosts = new List<ModeledMaterialCost<V>>();
        if (wasteAgg.MaterialCostPenalty is { Count: > 0 }) {
            var costByMaterial = new Dictionary<string, V>();
            for (int index = 0; index < Produce.PlanCount; index++) {
                CuttingPlan<V> plan = Produce.CuttingPlans[index];
                UInt64 batchCount = SolutionAmount(model, index);
                if (batchCount <= UInt64.Zero) {
                    continue;
                }

                if (!wasteAgg.MaterialCostPenalty.TryGetValue(plan.Material.Id, out Flt64 costPenalty)) {
                    continue;
                }

                Result<Flt64, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(costPenalty, batchCount.ToFlt64());
                if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                    continue;
                }

                dynamic cost = (V)(dynamic)costPenalty * (dynamic)convOk.Value;
                if (costByMaterial.TryGetValue(plan.Material.Id, out V existing)) {
                    costByMaterial[plan.Material.Id] = (V)(dynamic)existing + (dynamic)cost;
                }
                else {
                    costByMaterial[plan.Material.Id] = cost;
                }
            }
            foreach ((string? materialId, V cost) in costByMaterial) {
                materialCosts.Add(new ModeledMaterialCost<V> { MaterialId = materialId, Cost = cost });
            }
        }

        // 计算超产面积代理 / Calculate over-production area proxy
        V? overProductionArea = null;
        Flt64? overAreaPenalty = wasteAgg.OverProductionAreaPenalty;
        if (overAreaPenalty is not null && Yield is not null) {
            V? areaSum = null;
            for (int demandIndex = 0; demandIndex < Produce.Demands.Count; demandIndex++) {
                ProductDemand<V> demand = Produce.Demands[demandIndex];
                URealVar? overVar = Yield.OverProduction.ElementAtOrDefault(demandIndex);
                if (overVar is null) {
                    continue;
                }

                Token<Flt64>? token = model.Tokens.Find(overVar);
                double? overDouble = token?.DoubleResult;
                if (overDouble is null || overDouble <= 0.0) {
                    continue;
                }

                V? productWidthValue = OverProductionAreaWidthValue(demand);
                if (productWidthValue is null) {
                    continue;
                }

                Result<V, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(productWidthValue.Value, new Flt64(overDouble.Value));
                if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                    continue;
                }

                dynamic area = (V)(dynamic)productWidthValue.Value * (dynamic)convOk.Value;
                areaSum = areaSum is not null ? (V)(dynamic)areaSum + (dynamic)area : area;
            }
            overProductionArea = areaSum;
        }

        return new WasteMinimizationResult<V> {
            TotalTrimWidth = totalTrimWidth,
            TotalRestMaterial = totalRestMaterial,
            MaterialCosts = materialCosts,
            OverProductionArea = overProductionArea,
            OverProductionAreaMeasure = _wasteOverProductionAreaMeasure,
            RestMaterialMeasure = _wasteRestMaterialMeasure
        };
    }

    /// <summary>
    /// 从 LP 对偶解提取影子价格映射 / Extract shadow price map from LP dual solution.
    ///
    /// 使用 CGPipeline extractor 机制从 AbstractCsp1dShadowPriceMap 转换为
    /// pricing 可消费的轻量级 ShadowPriceMap&lt;V&gt;。
    ///
    /// 注意：此方法不传 model，因此无法执行 CGPipeline refresh（需要 model.ConstraintsOfGroup）。
    /// 仅适用于已经把 shadow price key 写入 constraint.args 的对偶解。
    /// 推荐使用 Csp1dShadowPriceLifecycle.ExtractFromDualSolution(model, dualSolution) 走 CGPipeline 主路径。
    ///
    /// Use CGPipeline extractor mechanism to convert from AbstractCsp1dShadowPriceMap
    /// to lightweight ShadowPriceMap&lt;V&gt; for pricing consumption.
    ///
    /// Note: This method does not receive model, so CGPipeline refresh (which needs model.ConstraintsOfGroup)
    /// cannot be executed. It only works for dual solutions whose constraints already carry shadow price keys
    /// in constraint.args. Recommend using
    /// Csp1dShadowPriceLifecycle.ExtractFromDualSolution(model, dualSolution) for the CGPipeline primary path.
    /// </summary>
    /// <param name="dualSolution">对偶解 / Dual solution.</param>
    /// <returns>影子价格映射 / Shadow price map.</returns>
    public ShadowPriceMap<V> ExtractShadowPriceMap(
        IReadOnlyDictionary<IConstraint<Flt64, LinearCategory>, Flt64> dualSolution) {
        // 无 model 时无法执行 CGPipeline refresh，直接从 constraint.Origin.Args 提取
        // Without model, cannot execute CGPipeline refresh; extract from constraint.Origin.Args
        //
        // **这里原本还建了一个 `Csp1dDefaultShadowPriceMap` 并往里 `Put`——但那个变量
        // 从不被读取，也从不被返回。** 它只是一个**每轮迭代都白写一次**的垃圾容器，
        // 而且会让读者以为"框架映射也更新了"。
        //
        // **This used to build a `Csp1dDefaultShadowPriceMap` and `Put` into it — but that variable was never read and never returned.** It was a junk container written
        // once per iteration, and it misled readers into thinking "the framework map is updated too".
        var prices = new Dictionary<Csp1dShadowPriceKey, V>();
        foreach ((IConstraint<Flt64, LinearCategory>? constraint, Flt64 dualValue) in dualSolution) {
            if (constraint.Origin?.Args is not Csp1dShadowPriceKey args) {
                continue;
            }

            Result<V, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(DomainValueSample, dualValue);
            if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                continue;
            }

            if (prices.TryGetValue(args, out V existingValue)) {
                prices[args] = (V)(dynamic)existingValue + (dynamic)convOk.Value;
            }
            else {
                prices[args] = convOk.Value;
            }
        }
        return new ShadowPriceMap<V>(prices);
    }

    // ===== Private helper methods =====

    /// <summary>
    /// 组装目标函数 / Assemble objective function.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    private void SetObjective(LinearMetaModel<Flt64> model) {
        var monomials = new List<LinearMonomial<Flt64>>();

        // **可行性阶段的目标只有人工短缺项。**
        //
        // 基线 `setObjective` 第一件事就是这个分支，并且**提前返回**：
        //
        // ```
        // if (feasibilityPhase) {
        //     monomials.addAll(yieldObjective?.objectiveMonomials ?: emptyList)
        //     model.minimize(polynomial = LinearPolynomial(monomials, Flt64.zero), name = "csp1d_phase_one")
        //     return
        // }
        // ```
        //
        // C# 原本**没有这个分支**：`_feasibilityPhase` 只被传给了 yield 聚合，
        // `SetObjective` 从未读过它。于是可行性阶段最小化的仍是**批次成本**，
        // 人工短缺项的系数虽然被 `BuildFeasibilityYieldConfig` 设成 1，**却进不了目标**——
        // 第一阶段解得的是"批次最省"，不是"短缺最小"。
        //
        // **The feasibility phase's objective contains only the artificial deficit terms.** The baseline's `setObjective` opens with this branch and **returns early**
        // (code above). C# **had no such branch**: `_feasibilityPhase` was only forwarded to the yield aggregation and `SetObjective` never read it, so phase one still
        // minimised **batch cost** — the artificial deficits' coefficient of 1 (set by `BuildFeasibilityYieldConfig`) **never reached the objective**, and phase one solved
        // "cheapest batches" instead of "smallest deficit".
        if (FeasibilityPhase) {
            monomials.AddRange(_yieldObjective?.ObjectiveMonomials() ?? Enumerable.Empty<LinearMonomial<Flt64>>());
            model.AddObject(
                ObjectCategory.Minimum,
                new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
                name: "csp1d_phase_one",
                displayName: null);
            return;
        }

        // 基础目标: 最小化批次 / Base objective: minimize batches
        // 允许 objective policy 修正每个方案的 batch coefficient
        // Allow objective policy to modify batch coefficient for each plan
        Flt64 baseBatchCoefficient = _lengthObjective?.BatchCoefficient() ?? Flt64.One;
        for (int index = 0; index < Produce.PlanCount; index++) {
            CuttingPlan<V> plan = Produce.CuttingPlans[index];
            Flt64 batchCoefficient = _objectivePolicies.Count > 0
                ? _objectivePolicies.Aggregate(baseBatchCoefficient, (coeff, policy) => {
                    var ctx = new SimpleDomainCalculationContext<V>(
                        plan: plan,
                        planIndex: index,
                        domainValueSample: DomainValueSample);
                    return policy.ModifyBatchCoefficient(ctx, coeff);
                })
                : baseBatchCoefficient;
            monomials.Add(new LinearMonomial<Flt64>(batchCoefficient, Produce[index]!));
        }

        // LP 模式不加 yield/waste/length 目标项 / LP mode does not add yield/waste/length objective terms
        if (Mode == Csp1dModelingMode.MILP) {
            monomials.AddRange(_yieldObjective?.ObjectiveMonomials() ?? Enumerable.Empty<LinearMonomial<Flt64>>());
            monomials.AddRange(_wasteObjective?.ObjectiveMonomials() ?? Enumerable.Empty<LinearMonomial<Flt64>>());
            monomials.AddRange(_lengthObjective?.ObjectiveMonomials() ?? Enumerable.Empty<LinearMonomial<Flt64>>());
        }

        var objective = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
        model.AddObject(
            ObjectCategory.Minimum,
            objective,
            name: "csp1d_objective",
            displayName: null);
    }

    /// <summary>
    /// 应用 warm start 初始解 / Apply warm start initial solution.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    private void ApplyWarmStart(LinearMetaModel<Flt64> model) {
        Dictionary<CuttingPlanCanonicalKey, UInt64> usageByKey = WarmStartUsageByKey();
        if (usageByKey.Count == 0) {
            return;
        }

        var initialSolution = new Dictionary<IVariableItem, Flt64>();
        for (int index = 0; index < Produce.CuttingPlans.Count; index++) {
            CuttingPlan<V> plan = Produce.CuttingPlans[index];
            var key = CuttingPlanCanonicalKey.From(plan);
            if (!usageByKey.TryGetValue(key, out UInt64 usage)) {
                continue;
            }

            if (usage > UInt64.Zero) {
                initialSolution[Produce[index]!] = usage.ToFlt64();
            }
        }
        if (initialSolution.Count > 0) {
            model.SetSolution(initialSolution);
        }
    }

    /// <summary>
    /// 构建 warm start 使用量映射 / Build warm start usage map.
    /// </summary>
    /// <returns>按 canonical key 聚合的使用量映射 / Usage map aggregated by canonical key.</returns>
    private Dictionary<CuttingPlanCanonicalKey, UInt64> WarmStartUsageByKey() {
        var result = new Dictionary<CuttingPlanCanonicalKey, UInt64>();
        foreach (CuttingPlanUsage<V> usage in _warmStartPlanUsages) {
            if (usage.Amount <= UInt64.Zero) {
                continue;
            }

            var key = CuttingPlanCanonicalKey.From(usage.Plan);
            result[key] = (result.TryGetValue(key, out UInt64 existing) ? existing : UInt64.Zero) + usage.Amount;
        }
        return result;
    }

    /// <summary>
    /// 提取设备使用量 / Extract machine usages.
    /// </summary>
    /// <param name="selectedPlans">选中的方案列表 / Selected plan list.</param>
    /// <returns>设备产能使用列表 / Machine capacity usage list.</returns>
    private IReadOnlyList<MachineCapacityUsage<V>> ExtractMachineUsages(
        IReadOnlyList<CuttingPlanUsage<V>> selectedPlans) {
        return Produce.Machines.Select(machine => {
            Quantity<V>? machineCapacity = machine.Capacity;
            Quantity<V>? used = null;
            foreach (CuttingPlanUsage<V>? usage in selectedPlans.Where(u => u.Plan.MachineId == machine.Id)) {
                Quantity<V>? consumption = usage.Plan.CapacityConsumption;
                if (consumption is null) {
                    continue;
                }

                if (machineCapacity is not null && consumption.Unit != machineCapacity.Unit) {
                    continue;
                }

                if (used is not null && used.Unit != consumption.Unit) {
                    continue;
                }

                Result<V, ErrorCode, Error<ErrorCode>> convResult = DomainValueConversion.ConvertSolverValue(consumption.Value, usage.Amount.ToFlt64());
                if (convResult is not Ok<V, ErrorCode, Error<ErrorCode>> convOk) {
                    continue;
                }

                var contribution = new Quantity<V>(
                    (V)(dynamic)consumption.Value * (dynamic)convOk.Value,
                    consumption.Unit);
                used = used is null
                    ? contribution
                    : new Quantity<V>((V)(dynamic)used.Value + (dynamic)contribution.Value, used.Unit);
            }
            return used is not null ? new MachineCapacityUsage<V>(machine, used) : null;
        }).Where(x => x is not null).Cast<MachineCapacityUsage<V>>().ToList();
    }

    /// <summary>
    /// 提取方案求解数量 / Extract solution amount for a plan.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="index">方案索引 / Plan index.</param>
    /// <returns>方案使用数量 / Plan usage amount.</returns>
    private UInt64 SolutionAmount(IAbstractLinearMetaModel<Flt64> model, int index) {
        Token<Flt64>? token = model.Tokens.Find(Produce[index]!);
        double? value = token?.DoubleResult;
        if (value is null || value <= 0.0) {
            return UInt64.Zero;
        }

        // **与 `ExtractSolution` 同一处取整规则：四舍五入，不是截断。**
        //
        // 只修了 `ExtractSolution`，**漏了这里**。基线两处都是 `roundToLong`，
        // C# 两处都是 `(long)`——所以修一处等于没修：`SolutionAmount` 是
        // **waste 结果（余宽、余料、物料成本）的唯一入口**，截断会让这些指标系统性偏低。
        //
        // **Same rounding rule as `ExtractSolution`: round, not truncate.** fixed only `ExtractSolution` and **missed this one**. Both baseline sites are
        // `roundToLong` and both C# sites were `(long)`, so fixing one changed nothing: `SolutionAmount` is **the only entry point for the waste results** (trim width,
        // rest material, material cost), and truncation makes all of them systematically low.
        return new UInt64((ulong)System.Math.Max((long)System.Math.Round(value.Value, MidpointRounding.AwayFromZero), 0));
    }

    /// <summary>
    /// 计算余料面积代理值 / Calculate rest material area proxy value.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <returns>余料面积代理值 / Rest material area proxy value.</returns>
    private V? RestMaterialValue(CuttingPlan<V> plan) {
        V? restWidth = plan.RestWidth?.Value;
        if (restWidth is null) {
            return null;
        }

        return _wasteRestMaterialMeasure switch {
            RestMaterialMeasure.RestWidthByMaterialLengthProxy => RestWidthByMaterialLengthProxy(plan, restWidth.Value),
            _ => null
        };
    }

    /// <summary>
    /// 计算余宽乘物料长度代理 / Calculate rest width by material length proxy.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="restWidth">余宽值 / Rest width value.</param>
    /// <returns>余宽乘物料长度 / Rest width times material length.</returns>
    private static V? RestWidthByMaterialLengthProxy(CuttingPlan<V> plan, V restWidth) {
        V? materialLength = plan.Material.Length?.Value;
        if (materialLength is null) {
            return null;
        }

        var zero = default(V);
        if ((dynamic)restWidth <= zero || (dynamic)materialLength <= zero) {
            return null;
        }

        return (V)(dynamic)restWidth * (dynamic)materialLength;
    }

    /// <summary>
    /// 计算超产面积宽度代理值 / Calculate over-production area width proxy value.
    /// </summary>
    /// <param name="demand">产品需求 / Product demand.</param>
    /// <returns>超产面积宽度代理值 / Over-production area width proxy value.</returns>
    private V? OverProductionAreaWidthValue(ProductDemand<V> demand) {
        return _wasteOverProductionAreaMeasure switch {
            OverProductionAreaMeasure.ProductMaxWidthProxy => demand.Product.MaxWidth()?.Value,
            _ => null
        };
    }
}

/// <summary>
/// Csp1dProduceContext 构建器 / Csp1dProduceContext builder.
///
/// 提供流式 API 构建 Csp1dProduceContext 实例。
/// Provides fluent API to build Csp1dProduceContext instances.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dProduceContextBuilder<V>
    where V : struct {
    private readonly ProduceInput<V> _input;
    private YieldModelingConfig<V>? _yieldConfig;
    private WasteMinimizationConfig<V>? _wasteConfig;
    private bool _feasibilityPhase;
    private LengthAssignmentModelingConfig<V>? _lengthConfig;
    private Csp1dModelingMode _mode = Csp1dModelingMode.MILP;
    private bool _isFinalMilp;
    private readonly List<IPipeline<LinearMetaModel<Flt64>>> _extraPipelines = new();
    private readonly List<ICsp1dObjectivePolicy<V>> _objectivePolicies = new();
    private readonly List<Csp1dModelingExtension<V>> _extensions = new();

    /// <summary>
    /// 构造构建器 / Construct builder.
    /// </summary>
    /// <param name="input">产出输入 / Produce input.</param>
    public Csp1dProduceContextBuilder(ProduceInput<V> input) {
        _input = input;
    }

    /// <summary>
    /// 设置 yield 建模配置 / Set yield modeling config.
    /// </summary>
    /// <param name="config">yield 建模配置 / Yield modeling config.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> YieldConfig(YieldModelingConfig<V>? config) {
        _yieldConfig = config;
        return this;
    }

    /// <summary>
    /// 设置 waste 建模配置 / Set waste modeling config.
    /// </summary>
    /// <param name="config">waste 建模配置 / Waste modeling config.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> WasteConfig(WasteMinimizationConfig<V>? config) {
        _wasteConfig = config;
        return this;
    }

    /// <summary>
    /// 设置 length 建模配置 / Set length modeling config.
    /// </summary>
    /// <param name="config">length 建模配置 / Length modeling config.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> LengthConfig(LengthAssignmentModelingConfig<V>? config) {
        _lengthConfig = config;
        return this;
    }

    /// <summary>
    /// 设置建模模式（LP 或 MILP）/ Set modeling mode (LP or MILP).
    /// </summary>
    /// <param name="mode">建模模式 / Modeling mode.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> Mode(Csp1dModelingMode mode) {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// 启用"只最小化人工需求短缺"的 LP 可行性阶段 / Enable the LP phase that minimises artificial demand deficits only.
    /// </summary>
    /// <param name="enabled">是否启用 / Whether to enable.</param>
    /// <returns>构建器自身 / The builder itself.</returns>
    /// <remarks>
    /// 基线的 `feasibilityPhase` 只做一件事：**让 LP 模式下也构建 yield 聚合**。
    /// 基线里 `val yieldAgg = if (_mode == MILP || _feasibilityPhase) ...`——
    /// C# 原本只有 `_mode == MILP`，所以**可行性阶段根本无法表达**。
    ///
    /// The baseline's `feasibilityPhase` does exactly one thing: **make an LP-mode build create the yield aggregation too**. The baseline has
    /// `val yieldAgg = if (_mode == MILP || _feasibilityPhase) ...`, while C# only had `_mode == MILP`, so **the feasibility phase could not be expressed at all**.
    /// </remarks>
    public Csp1dProduceContextBuilder<V> FeasibilityPhase(bool enabled = true) {
        _feasibilityPhase = enabled;
        return this;
    }

    /// <summary>
    /// 设置是否为最终 MILP 求解 / Set whether this is a final MILP solve.
    /// </summary>
    /// <param name="isFinalMilp">是否为最终 MILP / Whether this is final MILP.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> IsFinalMilp(bool isFinalMilp) {
        _isFinalMilp = isFinalMilp;
        return this;
    }

    /// <summary>
    /// 追加额外约束管线 / Append extra constraint pipeline.
    /// </summary>
    /// <param name="pipeline">约束管线 / Constraint pipeline.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> ExtraPipeline(IPipeline<LinearMetaModel<Flt64>> pipeline) {
        _extraPipelines.Add(pipeline);
        return this;
    }

    /// <summary>
    /// 追加目标函数策略 / Append objective policy.
    /// </summary>
    /// <param name="policy">目标函数策略 / Objective policy.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> ObjectivePolicy(ICsp1dObjectivePolicy<V> policy) {
        _objectivePolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 设置目标策略列表 / Set objective policy list.
    /// </summary>
    /// <param name="policies">目标策略列表 / Objective policy list.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> ObjectivePolicies(IReadOnlyList<ICsp1dObjectivePolicy<V>> policies) {
        _objectivePolicies.AddRange(policies);
        return this;
    }

    /// <summary>
    /// 追加建模扩展，build 时自动解析 context-aware pipeline / Add a modeling extension.
    /// </summary>
    /// <param name="extension">建模扩展 / Modeling extension.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> Extension(Csp1dModelingExtension<V> extension) {
        _extensions.Add(extension);
        return this;
    }

    /// <summary>
    /// 设置建模扩展列表 / Set modeling extension list.
    /// </summary>
    /// <param name="extensions">建模扩展列表 / Modeling extension list.</param>
    /// <returns>构建器自身 / Builder itself.</returns>
    public Csp1dProduceContextBuilder<V> Extensions(IReadOnlyList<Csp1dModelingExtension<V>> extensions) {
        _extensions.AddRange(extensions);
        return this;
    }

    /// <summary>
    /// 构建 Csp1dProduceContext 实例 / Build Csp1dProduceContext instance.
    ///
    /// 解析所有扩展管线，组装产出/yield/waste/length 聚合与约束管线，返回完整建模上下文。
    /// Resolves all extension pipelines, assembles produce/yield/waste/length aggregations with
    /// constraint pipelines, and returns the complete modeling context.
    /// </summary>
    /// <returns>CSP1D 产出模型上下文 / CSP1D produce model context.</returns>
    public Csp1dProduceContext<V> Build() {
        var produce = new ProduceAggregation<V>(
            cuttingPlans: _input.CuttingPlans,
            demands: _input.Demands,
            materials: _input.Materials,
            machines: _input.Machines);

        // 为 context-aware 扩展构建只读建模上下文
        // Build read-only modeling context for context-aware extension resolution
        V domainValueSample = ResolveDomainValueSample(_input);
        var modelingContext = new BuildTimeModelingContext(
            mode: _mode,
            isFinalMilp: _isFinalMilp,
            produce: produce,
            domainValueSample: domainValueSample);

        // 解析 context-aware 扩展管线 / Resolve context-aware extension pipelines
        foreach (Csp1dModelingExtension<V> ext in _extensions) {
            if (ext.Mode.Matches(_mode, _isFinalMilp)) {
                _extraPipelines.Add(ext.ResolvePipeline(modelingContext));
            }
        }

        // LP 模式不加 yield/length slack / LP mode does not add yield/length slack
        // Note: YieldAggregation, WasteAggregation, LengthAggregation are Flt64-specific in C#.
        // V is always Flt64 in practice (solver model is LinearMetaModel<Flt64>).
        IReadOnlyList<ProductDemand<Flt64>> flt64Demands = CastToFlt64List<ProductDemand<V>, ProductDemand<Flt64>>(_input.Demands);
        IReadOnlyList<CuttingPlan<Flt64>> flt64Plans = CastToFlt64List<CuttingPlan<V>, CuttingPlan<Flt64>>(_input.CuttingPlans);

        YieldAggregation? yieldAgg = _mode == Csp1dModelingMode.MILP || _feasibilityPhase
            ? _yieldConfig is not null
                ? new YieldAggregation(
                    Config: CastConfigToFlt64(_yieldConfig),
                    Demands: flt64Demands,
                    NeedsOverSlackForOverArea: _wasteConfig?.OverProductionAreaPenalty is not null)
                : null
            : null;

        WasteAggregation? wasteAgg = _mode == Csp1dModelingMode.MILP
            ? _wasteConfig is not null
                ? new WasteAggregation(
                    CuttingPlans: flt64Plans,
                    TrimWidthPenalty: _wasteConfig.TrimWidthPenalty is V twp ? ToFlt64(twp) : null,
                    MaterialCostPenalty: _wasteConfig.MaterialCostPenalty.Count > 0
                        ? _wasteConfig.MaterialCostPenalty.ToDictionary(kv => kv.Key, kv => ToFlt64(kv.Value))
                        : null,
                    OverProductionAreaPenalty: _wasteConfig.OverProductionAreaPenalty is V opap ? ToFlt64(opap) : null,
                    RestMaterialPenalty: _wasteConfig.RestMaterialPenalty is V rmp ? ToFlt64(rmp) : null)
                : null
            : null;

        LengthAggregation? lengthAgg = _mode == Csp1dModelingMode.MILP
            ? _lengthConfig is not null
                ? new LengthAggregation(Config: CastLengthConfigToFlt64(_lengthConfig), Demands: flt64Demands)
                : null
            : null;

        // 构建 CG 约束管线（demand/material/machine，MILP 下包含 yield）
        // Build CG constraint pipelines (demand/material/machine, plus yield in MILP)
        var cgPipelines = new List<ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>>();
        cgPipelines.Add(new DemandConstraintPipeline<V>(
            produce: produce,
            demands: _input.Demands,
            yieldUnderVars: yieldAgg?.UnderProduction,
            yieldOverVars: yieldAgg?.OverProduction));
        cgPipelines.Add(new MaterialConstraintPipeline<V>(
            produce: produce,
            materials: _input.Materials));
        cgPipelines.Add(new MachineConstraintPipeline<V>(
            produce: produce,
            machines: _input.Machines));
        if (yieldAgg is not null && _yieldConfig is not null) {
            cgPipelines.Add(new YieldConstraintPipeline<V>(
                yield: yieldAgg,
                config: _yieldConfig,
                demands: _input.Demands));
        }

        // 普通约束管线只保留 length-assignment 例外
        // Plain constraint pipelines keep only the length-assignment exception
        var constraintPipelines = new List<IPipeline<LinearMetaModel<Flt64>>>();
        if (lengthAgg is not null && _lengthConfig is not null) {
            constraintPipelines.Add(new LengthConstraintPipeline<V>(
                length: lengthAgg,
                config: _lengthConfig,
                demands: _input.Demands));
        }

        YieldObjectivePipeline<V>? yieldObjective = yieldAgg is not null && _yieldConfig is not null
            ? new YieldObjectivePipeline<V>(yield: yieldAgg, config: _yieldConfig, demands: _input.Demands)
            : null;

        WasteObjectivePipeline<V>? wasteObjective = wasteAgg is not null
            ? new WasteObjectivePipeline<V>(
                produce: produce,
                waste: wasteAgg,
                demands: _input.Demands,
                overProductionVars: yieldAgg?.OverProduction,
                overProductionAreaMeasure: (DomainOverProductionAreaMeasure)(_wasteConfig?.OverProductionAreaMeasure ?? OverProductionAreaMeasure.ProductMaxWidthProxy),
                restMaterialMeasure: (DomainRestMaterialMeasure)(_wasteConfig?.RestMaterialMeasure ?? RestMaterialMeasure.RestWidthByMaterialLengthProxy))
            : null;

        LengthObjectivePipeline<V>? lengthObjective = lengthAgg is not null && _lengthConfig is not null
            ? new LengthObjectivePipeline<V>(produce: produce, length: lengthAgg, config: _lengthConfig)
            : null;

        return new Csp1dProduceContext<V>(
            produce: produce,
            yield: yieldAgg,
            waste: wasteAgg,
            length: lengthAgg,
            constraintPipelines: constraintPipelines,
            cgPipelines: cgPipelines,
            yieldObjective: yieldObjective,
            wasteObjective: wasteObjective,
            lengthObjective: lengthObjective,
            extraPipelines: _extraPipelines,
            mode: _mode,
            warmStartPlanUsages: _input.WarmStartPlanUsages,
            wasteOverProductionAreaMeasure: _wasteConfig?.OverProductionAreaMeasure ?? OverProductionAreaMeasure.ProductMaxWidthProxy,
            wasteRestMaterialMeasure: _wasteConfig?.RestMaterialMeasure ?? RestMaterialMeasure.RestWidthByMaterialLengthProxy,
            objectivePolicies: _objectivePolicies,
            domainValueSample: domainValueSample,
            isFinalMilp: _isFinalMilp,
            feasibilityPhase: _feasibilityPhase);
    }

    /// <summary>
    /// 从输入数据推导领域数值样本 / Derive a domain value sample from input data.
    /// </summary>
    /// <param name="input">产出输入 / Produce input.</param>
    /// <returns>领域数值样本 / Domain value sample.</returns>
    private static V ResolveDomainValueSample(ProduceInput<V> input) {
        // 优先从 demand 的 quantity 获取 / Prefer demand quantity
        if (input.Demands.Count > 0) {
            return input.Demands[0].Quantity.Value;
        }
        // 其次从 material 的 widthRange 获取 / Fallback to material width range
        if (input.Materials.Count > 0) {
            return input.Materials[0].WidthRange.LowerBound.Value;
        }
        // 再次从 cutting plan 的 restWidth 获取 / Fallback to plan restWidth
        foreach (CuttingPlan<V> plan in input.CuttingPlans) {
            if (plan.RestWidth is not null) {
                return plan.RestWidth.Value;
            }
        }
        throw new InvalidOperationException(
            "Cannot derive domain value sample from ProduceInput; " +
            "at least one demand, material, or config with domain value is required.");
    }

    /// <summary>
    /// 将 V 类型值转换为 Flt64 / Convert V type value to Flt64.
    /// </summary>
    /// <param name="value">领域数值 / Domain value.</param>
    /// <returns>Flt64 值 / Flt64 value.</returns>
    private static Flt64 ToFlt64(V value) {
        if (value is Flt64 f) {
            return f;
        }

        if (value is FltX fx) {
            return fx.ToFlt64();
        }

        if (value is Fuookami.Ospf.Math.Algebra.Number.Int64 i) {
            return i.ToFlt64();
        }

        return new Flt64(((dynamic)value).ToDouble());
    }

    /// <summary>
    /// 将 V-generic 列表转换为 Flt64-generic 列表（V 在实践中始终为 Flt64）/
    /// Cast V-generic list to Flt64-generic list (V is always Flt64 in practice).
    /// </summary>
    private static IReadOnlyList<TFlt64> CastToFlt64List<TV, TFlt64>(IReadOnlyList<TV> list) => (IReadOnlyList<TFlt64>)(object)list;

    /// <summary>
    /// 将 YieldModelingConfig&lt;V&gt; 转换为 YieldModelingConfig&lt;Flt64&gt; /
    /// Convert YieldModelingConfig&lt;V&gt; to YieldModelingConfig&lt;Flt64&gt;.
    /// </summary>
    private static YieldModelingConfig<Flt64> CastConfigToFlt64(YieldModelingConfig<V> config) => (YieldModelingConfig<Flt64>)(object)config;

    /// <summary>
    /// 将 LengthAssignmentModelingConfig&lt;V&gt; 转换为 LengthAssignmentModelingConfig&lt;Flt64&gt; /
    /// Convert LengthAssignmentModelingConfig&lt;V&gt; to LengthAssignmentModelingConfig&lt;Flt64&gt;.
    /// </summary>
    private static LengthAssignmentModelingConfig<Flt64> CastLengthConfigToFlt64(LengthAssignmentModelingConfig<V> config) => (LengthAssignmentModelingConfig<Flt64>)(object)config;

    /// <summary>
    /// 构建时只读建模上下文 / Build-time read-only modeling context.
    /// </summary>
    private sealed class BuildTimeModelingContext : ICsp1dModelingContext<V> {
        public BuildTimeModelingContext(
            Csp1dModelingMode mode,
            bool isFinalMilp,
            ProduceAggregation<V> produce,
            V domainValueSample) {
            Mode = mode;
            IsFinalMilp = isFinalMilp;
            Produce = produce;
            DomainValueSample = domainValueSample;
        }

        public Csp1dModelingMode Mode { get; }
        public bool IsFinalMilp { get; }
        public ProduceAggregation<V> Produce { get; }
        public IReadOnlyList<ProductDemand<V>> Demands => Produce.Demands;
        public IReadOnlyList<Material<V>> Materials => Produce.Materials;
        public IReadOnlyList<Machine<V>> Machines => Produce.Machines;
        public V DomainValueSample { get; }

        public V ToDomainValue(Flt64 value) {
            Result<V, ErrorCode, Error<ErrorCode>> result = DomainValueConversion.ConvertSolverValue(DomainValueSample, value);
            if (result is Ok<V, ErrorCode, Error<ErrorCode>> ok) {
                return ok.Value;
            }

            return default;
        }
    }
}

/// <summary>
/// 集合扩展方法 / Collection extension methods.
/// </summary>
internal static class CollectionExtensions {
    /// <summary>
    /// 选择非 null 结果 / Select non-null results.
    /// </summary>
    public static IEnumerable<T> SelectNotNull<T>(this IEnumerable<T> source, Func<T, T?> selector)
        where T : class {
        foreach (T item in source) {
            T? result = selector(item);
            if (result is not null) {
                yield return result;
            }
        }
    }
}
