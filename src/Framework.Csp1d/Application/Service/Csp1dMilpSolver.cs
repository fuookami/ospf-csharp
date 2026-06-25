#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// CSP1D MILP 求解器 / CSP1D MILP Solver.
///
/// 封装列生成求解器，提供 MILP 和 LP 松弛两种求解入口。
/// Wraps the column generation solver, providing both MILP and LP relaxation solve entry points.
/// </summary>
public sealed class Csp1dMilpSolver {
    private readonly IColumnGenerationSolver _solver;

    /// <summary>
    /// MILP 求解结果 / MILP solve result.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="Produce">主问题产出 / Master problem output.</param>
    /// <param name="YieldResult">产出建模结果 / Yield modeling result.</param>
    /// <param name="WasteResult">浪费最小化结果 / Waste minimization result.</param>
    /// <param name="LengthResult">长度分配结果 / Length assignment result.</param>
    /// <param name="Model">线性元模型 / Linear meta model.</param>
    /// <param name="Output">可行求解输出 / Feasible solver output.</param>
    public sealed record MilpResult<V>(
        Produce<V> Produce,
        LinearMetaModel<Flt64> Model,
        FeasibleSolverOutput<Flt64> Output,
        YieldModelingResult<Flt64>? YieldResult = null,
        WasteMinimizationResult<V>? WasteResult = null,
        LengthAssignmentModelingResult<Flt64>? LengthResult = null
    ) where V : struct;

    /// <summary>
    /// LP 求解结果 / LP solve result.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="ShadowPrices">影子价格映射 / Shadow price map.</param>
    /// <param name="Model">线性元模型 / Linear meta model.</param>
    /// <param name="LpOutput">LP 求解输出 / LP solver output.</param>
    /// <param name="FrameworkShadowPriceMap">框架影子价格映射 / Framework shadow price map.</param>
    public sealed record LpResult<V>(
        ShadowPriceMap<V> ShadowPrices,
        LinearMetaModel<Flt64> Model,
        IColumnGenerationSolver.LpResult LpOutput,
        AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>? FrameworkShadowPriceMap = null
    ) where V : struct;

    /// <summary>
    /// 创建 CSP1D MILP 求解器 / Create CSP1D MILP solver.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    public Csp1dMilpSolver(IColumnGenerationSolver solver) {
        _solver = solver;
    }

    /// <summary>
    /// 求解 MILP / Solve MILP.
    ///
    /// 委托给 solveInternal 执行实际求解逻辑。
    /// Delegates to SolveInternalAsync for the actual solve logic.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="input">产出输入 / Produce input.</param>
    /// <param name="yieldConfig">产出建模配置 / Yield modeling config.</param>
    /// <param name="wasteConfig">浪费最小化配置 / Waste minimization config.</param>
    /// <param name="lengthConfig">长度分配配置 / Length assignment config.</param>
    /// <param name="extensions">建模扩展列表 / Modeling extension list.</param>
    /// <param name="objectivePolicies">目标策略列表 / Objective policy list.</param>
    /// <param name="isFinalMilp">是否为列生成最终 MILP / Whether this is column generation final MILP.</param>
    /// <returns>MILP 求解结果 / MILP solve result.</returns>
    public async Task<Result<MilpResult<V>?, ErrorCode, Error<ErrorCode>>> SolveAsync<V>(
        ProduceInput<V> input,
        YieldModelingConfig<V>? yieldConfig = null,
        WasteMinimizationConfig<V>? wasteConfig = null,
        LengthAssignmentModelingConfig<V>? lengthConfig = null,
        IReadOnlyList<Csp1dModelingExtension<V>>? extensions = null,
        IReadOnlyList<ICsp1dObjectivePolicy<V>>? objectivePolicies = null,
        bool isFinalMilp = false
    ) where V : struct {
        return await SolveInternalAsync(
            input,
            yieldConfig,
            wasteConfig,
            lengthConfig,
            extensions ?? Array.Empty<Csp1dModelingExtension<V>>(),
            objectivePolicies ?? Array.Empty<ICsp1dObjectivePolicy<V>>(),
            isFinalMilp);
    }

    /// <summary>
    /// 内部 MILP 求解实现 / Internal MILP solve implementation.
    ///
    /// 构建模型上下文，注册变量/约束/目标，求解 MILP，提取解。
    /// Builds model context, registers variables/constraints/objectives,
    /// solves MILP, and extracts solution.
    /// </summary>
    private async Task<Result<MilpResult<V>?, ErrorCode, Error<ErrorCode>>> SolveInternalAsync<V>(
        ProduceInput<V> input,
        YieldModelingConfig<V>? yieldConfig,
        WasteMinimizationConfig<V>? wasteConfig,
        LengthAssignmentModelingConfig<V>? lengthConfig,
        IReadOnlyList<Csp1dModelingExtension<V>> extensions,
        IReadOnlyList<ICsp1dObjectivePolicy<V>> objectivePolicies,
        bool isFinalMilp
    ) where V : struct {
        // 空方案直接返回 null / Return null for empty cutting plans
        if (input.CuttingPlans.Count == 0) {
            return Results.Ok<MilpResult<V>?>(null);
        }

        // 创建元模型 / Create meta model
        var model = new LinearMetaModel<Flt64>(name: "csp1d_produce");

        // 推导默认长度边界 / Derive default length bounds
        LengthAssignmentModelingConfig<V>? resolvedLengthConfig = ResolveDefaultLengthBounds(input, lengthConfig);

        // 构建上下文 / Build context
        Csp1dProduceContext<V> context = new Csp1dProduceContextBuilder<V>(input)
            .YieldConfig(yieldConfig)
            .WasteConfig(wasteConfig)
            .LengthConfig(resolvedLengthConfig)
            .Mode(Csp1dModelingMode.MILP)
            .IsFinalMilp(isFinalMilp)
            .Extensions(extensions)
            .ObjectivePolicies(objectivePolicies)
            .Build();

        // 注册变量、约束和目标 / Register variables, constraints and objectives
        Result<Success, ErrorCode, Error<ErrorCode>> registerResult = context.Register(model);
        if (registerResult.IsFailed) {
            return PropagateTryFailure<MilpResult<V>?>(registerResult);
        }

        // 求解 MILP / Solve MILP
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult = await _solver.SolveMILPAsync("csp1d-produce", model);
        if (solveResult.IsFailed) {
            return PropagateResultFailure<FeasibleSolverOutput<Flt64>, MilpResult<V>?>(solveResult);
        }
        FeasibleSolverOutput<Flt64> output = solveResult.Value;

        // 回填解到模型 / Set solution back to model
        model.SetSolution(output.Solution.Values);

        // 提取主问题产出 / Extract master problem output
        Result<Produce<V>, ErrorCode, Error<ErrorCode>> extractResult = context.ExtractSolution(model);
        if (extractResult.IsFailed) {
            return PropagateResultFailure<Produce<V>, MilpResult<V>?>(extractResult);
        }
        Produce<V> produce = extractResult.Value;

        // 提取可选结果 / Extract optional results
        YieldModelingResult<Flt64>? yieldResult = context.ExtractYieldResult(model);
        WasteMinimizationResult<V>? wasteResult = context.ExtractWasteResult(model);
        LengthAssignmentModelingResult<Flt64>? lengthResult = context.ExtractLengthResult(model);

        return Results.Ok<MilpResult<V>?>(new MilpResult<V>(
            Produce: produce,
            YieldResult: yieldResult,
            WasteResult: wasteResult,
            LengthResult: lengthResult,
            Model: model,
            Output: output));
    }

    /// <summary>
    /// LP 松弛求解 / LP relaxation solve.
    ///
    /// 以 LP 模式构建模型，求解 LP 松弛，提取影子价格。
    /// Builds model in LP mode, solves LP relaxation, and extracts shadow prices.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="input">产出输入 / Produce input.</param>
    /// <param name="extensions">建模扩展列表 / Modeling extension list.</param>
    /// <returns>LP 求解结果 / LP solve result.</returns>
    public async Task<Result<LpResult<V>?, ErrorCode, Error<ErrorCode>>> SolveLPAsync<V>(
        ProduceInput<V> input,
        IReadOnlyList<Csp1dModelingExtension<V>>? extensions = null
    ) where V : struct {
        // 空方案直接返回 null / Return null for empty cutting plans
        if (input.CuttingPlans.Count == 0) {
            return Results.Ok<LpResult<V>?>(null);
        }

        // 解析领域数值样本 / Resolve domain value sample
        Result<V, ErrorCode, Error<ErrorCode>> sampleResult = ResolveDomainValueSampleForLP(input);
        if (sampleResult.IsFailed) {
            return PropagateResultFailure<V, LpResult<V>?>(sampleResult);
        }
        V domainValueSample = sampleResult.Value;

        // 创建元模型 / Create meta model
        var model = new LinearMetaModel<Flt64>(name: "csp1d_produce_lp");

        // 以 LP 模式构建上下文 / Build context in LP mode
        Csp1dProduceContext<V> context = new Csp1dProduceContextBuilder<V>(input)
            .Mode(Csp1dModelingMode.LP)
            .Extensions(extensions ?? Array.Empty<Csp1dModelingExtension<V>>())
            .Build();

        // 注册变量、约束和目标 / Register variables, constraints and objectives
        Result<Success, ErrorCode, Error<ErrorCode>> registerResult = context.Register(model);
        if (registerResult.IsFailed) {
            return PropagateTryFailure<LpResult<V>?>(registerResult);
        }

        // 求解 LP / Solve LP
        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpSolveResult = await _solver.SolveLPAsync("csp1d-produce-lp", model);
        if (lpSolveResult.IsFailed) {
            return PropagateResultFailure<IColumnGenerationSolver.LpResult, LpResult<V>?>(lpSolveResult);
        }
        IColumnGenerationSolver.LpResult lpOutput = lpSolveResult.Value;

        // 提取影子价格 / Extract shadow prices
        var shadowPriceLifecycle = new Csp1dShadowPriceLifecycle<V>(domainValueSample);
        Result<ShadowPriceMap<V>, ErrorCode, Error<ErrorCode>> shadowPriceResult = shadowPriceLifecycle.ExtractFromDualSolution(
            model,
            lpOutput.DualSolution);
        if (shadowPriceResult.IsFailed) {
            return PropagateResultFailure<ShadowPriceMap<V>, LpResult<V>?>(shadowPriceResult);
        }

        return Results.Ok<LpResult<V>?>(new LpResult<V>(
            ShadowPrices: shadowPriceResult.Value,
            Model: model,
            LpOutput: lpOutput,
            FrameworkShadowPriceMap: shadowPriceLifecycle.FrameworkShadowPriceMap));
    }

    /// <summary>
    /// 解析 LP 领域数值样本 / Resolve LP domain value sample.
    ///
    /// 从输入的需求或物料中提取一个 V 类型样本值，用于 solver 值到领域值的转换。
    /// Extracts a V-type sample value from demands or materials in the input
    /// for solver-to-domain value conversion.
    /// </summary>
    private static Result<V, ErrorCode, Error<ErrorCode>> ResolveDomainValueSampleForLP<V>(
        ProduceInput<V> input
    ) where V : struct {
        // 优先从需求中获取样本 / Prefer sample from demands
        if (input.Demands.Count > 0) {
            return Results.Ok(input.Demands[0].Quantity.Value);
        }

        // 回退到物料宽度 / Fallback to material width
        if (input.Materials.Count > 0) {
            Material<V> material = input.Materials[0];
            if (material.WidthRange.LowerBound is Quantity<V> lowerBoundQ) {
                V lowerBound = lowerBoundQ.Value;
                return Results.Ok(lowerBound);
            }
        }

        // 最后回退到默认值 / Final fallback to default
        return Results.Ok(default(V));
    }

    /// <summary>
    /// 推导默认长度边界 / Derive default length bounds.
    ///
    /// 对于动态长度产品，若未显式指定上下界，则从需求和产品属性中推导。
    /// For dynamic-length products, derives default lower/upper bounds from
    /// demands and product properties when not explicitly specified.
    ///
    /// 算法 / Algorithm:
    /// 1. lengthConfig 为 null 则返回 null / Return null if lengthConfig is null.
    /// 2. 无 dynamicProductIds 则原样返回 / Return as-is if no dynamicProductIds.
    /// 3. 检查是否需要推导 / Check if derivation is needed.
    /// 4. 从需求和产品推导默认边界 / Derive defaults from demands and products.
    /// 5. 返回新配置 / Return new config.
    /// </summary>
    private static LengthAssignmentModelingConfig<V>? ResolveDefaultLengthBounds<V>(
        ProduceInput<V> input,
        LengthAssignmentModelingConfig<V>? lengthConfig
    ) where V : struct {
        // 1. null 则返回 null / Return null if null
        if (lengthConfig is null) {
            return null;
        }

        // 2. 无动态产品 ID 则原样返回 / Return as-is if no dynamic product IDs
        if (lengthConfig.DynamicProductIds is null || lengthConfig.DynamicProductIds.Count == 0) {
            return lengthConfig;
        }

        // 3. 检查是否需要推导 / Check if any product needs derivation
        bool needsDerivation = false;
        foreach (string productId in lengthConfig.DynamicProductIds) {
            bool hasLower = lengthConfig.AssignedLengthLowerBound?.ContainsKey(productId) == true;
            bool hasUpper = lengthConfig.AssignedLengthUpperBound?.ContainsKey(productId) == true;
            if (!hasLower || !hasUpper) {
                needsDerivation = true;
                break;
            }
        }

        if (!needsDerivation) {
            return lengthConfig;
        }

        // 4. 从需求和产品推导默认边界 / Derive defaults from demands and products
        Dictionary<string, V> newLower = lengthConfig.AssignedLengthLowerBound is not null
            ? new Dictionary<string, V>(lengthConfig.AssignedLengthLowerBound)
            : new Dictionary<string, V>();
        Dictionary<string, V> newUpper = lengthConfig.AssignedLengthUpperBound is not null
            ? new Dictionary<string, V>(lengthConfig.AssignedLengthUpperBound)
            : new Dictionary<string, V>();

        // 按产品 ID 索引需求 / Index demands by product ID
        var demandsByProduct = new Dictionary<string, List<ProductDemand<V>>>();
        foreach (ProductDemand<V> demand in input.Demands) {
            if (!demandsByProduct.TryGetValue(demand.Product.Id, out List<ProductDemand<V>>? list)) {
                list = new List<ProductDemand<V>>();
                demandsByProduct[demand.Product.Id] = list;
            }
            list.Add(demand);
        }

        foreach (string productId in lengthConfig.DynamicProductIds) {
            if (!demandsByProduct.TryGetValue(productId, out List<ProductDemand<V>>? productDemands)) {
                continue;
            }

            Product<V> product = productDemands[0].Product;

            // 推导下界：从需求量中取最小值 / Derive lower bound: minimum demand quantity
            if (!newLower.ContainsKey(productId)) {
                V minDemand = productDemands[0].Quantity.Value;
                foreach (ProductDemand<V>? d in productDemands.Skip(1)) {
                    if (System.Collections.Generic.Comparer<V>.Default.Compare(d.Quantity.Value, minDemand) < 0) {
                        minDemand = d.Quantity.Value;
                    }
                }
                newLower[productId] = minDemand;
            }

            // 推导上界：使用产品最大超产长度或需求最大值 / Derive upper bound: max overproduce length or max demand
            if (!newUpper.ContainsKey(productId)) {
                if (product.MaxOverProduceLength is Quantity<V> maxOverLen) {
                    newUpper[productId] = maxOverLen.Value;
                }
                else {
                    V maxDemand = productDemands[0].Quantity.Value;
                    foreach (ProductDemand<V>? d in productDemands.Skip(1)) {
                        if (System.Collections.Generic.Comparer<V>.Default.Compare(d.Quantity.Value, maxDemand) > 0) {
                            maxDemand = d.Quantity.Value;
                        }
                    }
                    newUpper[productId] = maxDemand;
                }
            }
        }

        // 5. 返回新配置 / Return new config
        return new LengthAssignmentModelingConfig<V>(
            DynamicProductIds: lengthConfig.DynamicProductIds,
            AssignedLengthLowerBound: newLower.Count > 0 ? newLower : null,
            AssignedLengthUpperBound: newUpper.Count > 0 ? newUpper : null,
            OverLengthPenalty: lengthConfig.OverLengthPenalty,
            OverLengthUpperBound: lengthConfig.OverLengthUpperBound,
            TotalLengthPenalty: lengthConfig.TotalLengthPenalty,
            BatchMinPenalty: lengthConfig.BatchMinPenalty);
    }

    /// <summary>
    /// 传播 Try 失败 / Propagate Try failure.
    ///
    /// 将 Try（Result&lt;Success, ...&gt;）的失败转换为目标类型的失败。
    /// Converts a Try (Result&lt;Success, ...&gt;) failure to a failure of the target type.
    /// </summary>
    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateTryFailure<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) {
        if (result is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        var failed = (Failed<Success, ErrorCode, Error<ErrorCode>>)result;
        return new Failed<T, ErrorCode, Error<ErrorCode>>(failed.Error);
    }

    /// <summary>
    /// 传播 Result 失败 / Propagate Result failure.
    ///
    /// 将 Result&lt;S, ...&gt; 的失败转换为 Result&lt;T, ...&gt; 的失败。
    /// Converts a Result&lt;S, ...&gt; failure to a Result&lt;T, ...&gt; failure.
    /// </summary>
    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateResultFailure<S, T>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        if (result is Fatal<S, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        var failed = (Failed<S, ErrorCode, Error<ErrorCode>>)result;
        return new Failed<T, ErrorCode, Error<ErrorCode>>(failed.Error);
    }
}
