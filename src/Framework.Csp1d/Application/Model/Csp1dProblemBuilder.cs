#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 问题定义 builder / Builder for CSP1D problem definition.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dProblemBuilder<V> where V : struct {
    private readonly List<Product<V>> _productBuffer = [];
    private readonly List<Material<V>> _materialBuffer = [];
    private readonly List<Machine<V>> _machineBuffer = [];
    private readonly List<Costar<V>> _costarBuffer = [];
    private readonly List<ProductDemand<V>> _demandBuffer = [];
    private Csp1dConfiguration<V> _configuration = new();
    private Csp1dSolveConfig<V>? _solveConfig;

    /// <summary>
    /// 增加产品 / Add a product.
    /// </summary>
    /// <param name="product">产品 / Product.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddProduct(Product<V> product) {
        _productBuffer.Add(product);
        return this;
    }

    /// <summary>
    /// 增加产品列表 / Add products.
    /// </summary>
    /// <param name="products">产品列表 / Products.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddProducts(IEnumerable<Product<V>> products) {
        _productBuffer.AddRange(products);
        return this;
    }

    /// <summary>
    /// 增加物料 / Add a material.
    /// </summary>
    /// <param name="material">物料 / Material.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddMaterial(Material<V> material) {
        _materialBuffer.Add(material);
        return this;
    }

    /// <summary>
    /// 增加物料列表 / Add materials.
    /// </summary>
    /// <param name="materials">物料列表 / Materials.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddMaterials(IEnumerable<Material<V>> materials) {
        _materialBuffer.AddRange(materials);
        return this;
    }

    /// <summary>
    /// 增加设备 / Add a machine.
    /// </summary>
    /// <param name="machine">设备 / Machine.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddMachine(Machine<V> machine) {
        _machineBuffer.Add(machine);
        return this;
    }

    /// <summary>
    /// 增加设备列表 / Add machines.
    /// </summary>
    /// <param name="machines">设备列表 / Machines.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddMachines(IEnumerable<Machine<V>> machines) {
        _machineBuffer.AddRange(machines);
        return this;
    }

    /// <summary>
    /// 增加配规 / Add a costar.
    /// </summary>
    /// <param name="costar">配规 / Costar.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddCostar(Costar<V> costar) {
        _costarBuffer.Add(costar);
        return this;
    }

    /// <summary>
    /// 增加配规列表 / Add costars.
    /// </summary>
    /// <param name="costars">配规列表 / Costars.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddCostars(IEnumerable<Costar<V>> costars) {
        _costarBuffer.AddRange(costars);
        return this;
    }

    /// <summary>
    /// 增加需求 / Add a demand.
    /// </summary>
    /// <param name="demand">需求 / Demand.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddDemand(ProductDemand<V> demand) {
        _demandBuffer.Add(demand);
        return this;
    }

    /// <summary>
    /// 增加需求列表 / Add demands.
    /// </summary>
    /// <param name="demands">需求列表 / Demands.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> AddDemands(IEnumerable<ProductDemand<V>> demands) {
        _demandBuffer.AddRange(demands);
        return this;
    }

    /// <summary>
    /// 设置列生成配置 / Set column generation configuration.
    /// </summary>
    /// <param name="configuration">配置 / Configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> SetConfiguration(Csp1dConfiguration<V> configuration) {
        _configuration = configuration;
        return this;
    }

    /// <summary>
    /// 设置一站式求解配置 / Set one-stop solve configuration.
    /// </summary>
    /// <param name="solveConfig">求解配置 / Solve configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> SetSolveConfig(Csp1dSolveConfig<V> solveConfig) {
        _solveConfig = solveConfig;
        return this;
    }

    /// <summary>
    /// 设置一站式求解配置（使用 builder）/ Set one-stop solve configuration (using builder).
    /// </summary>
    /// <param name="configure">配置 action / Configure action.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dProblemBuilder<V> SetSolveConfig(System.Action<Csp1dSolveConfigBuilder<V>> configure) {
        var builder = new Csp1dSolveConfigBuilder<V>();
        configure(builder);
        _solveConfig = builder.Build();
        return this;
    }

    /// <summary>
    /// 构建问题定义 / Build problem definition.
    /// </summary>
    /// <returns>CSP1D 问题定义 / CSP1D problem definition.</returns>
    public Csp1dProblem<V> Build() {
        return new Csp1dProblem<V>(
            Products: _productBuffer.ToList(),
            Materials: _materialBuffer.ToList(),
            Machines: _machineBuffer.ToList(),
            Costars: _costarBuffer.ToList(),
            Demands: _demandBuffer.ToList(),
            Configuration: _configuration,
            SolveConfig: _solveConfig
        );
    }
}

/// <summary>
/// CSP1D 一站式求解配置 builder / Builder for CSP1D one-stop solve configuration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dSolveConfigBuilder<V> where V : struct {
    private Csp1dConfiguration<V> _columnGeneration = new();
    private YieldModelingConfig<V>? _yieldConfig;
    private WasteMinimizationConfig<V>? _wasteConfig;
    private LengthAssignmentModelingConfig<V>? _lengthConfig;
    private Int64? _topKPlanLimit;
    private bool _allowPartialSolution = true;
    private readonly List<Csp1dModelingExtension<V>> _extensions = [];
    private readonly List<ICsp1dDomainPolicy<V>> _domainPolicies = [];
    private readonly List<ICsp1dObjectivePolicy<V>> _objectivePolicies = [];
    private readonly List<ICsp1dGenerationStrategy<V>> _generationStrategies = [];
    private readonly List<ICsp1dPricingPolicy<V>> _pricingPolicies = [];
    private readonly List<ICsp1dFlowPolicy<V>> _flowPolicies = [];
    private readonly List<ICsp1dExtractionPolicy<V>> _extractionPolicies = [];

    /// <summary>
    /// 设置列生成配置 / Set column generation configuration.
    /// </summary>
    /// <param name="configuration">配置 / Configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetColumnGeneration(Csp1dConfiguration<V> configuration) {
        _columnGeneration = configuration;
        return this;
    }

    /// <summary>
    /// 设置列生成配置 / Set column generation configuration.
    /// </summary>
    /// <param name="maxInitialPlans">初始方案上限 / Initial plan limit.</param>
    /// <param name="maxPricingPlans">每轮定价方案上限 / Pricing plan limit per iteration.</param>
    /// <param name="iterationLimit">迭代上限 / Iteration limit.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetColumnGeneration(Int64 maxInitialPlans, Int64 maxPricingPlans, Int64 iterationLimit) {
        _columnGeneration = new Csp1dConfiguration<V> {
            MaxInitialPlans = maxInitialPlans,
            MaxPricingPlans = maxPricingPlans,
            IterationLimit = iterationLimit
        };
        return this;
    }

    /// <summary>
    /// 设置 yield 建模配置 / Set yield modeling configuration.
    /// </summary>
    /// <param name="config">yield 建模配置 / Yield modeling configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetYieldConfig(YieldModelingConfig<V>? config) {
        _yieldConfig = config;
        return this;
    }

    /// <summary>
    /// 设置 waste 建模配置 / Set waste modeling configuration.
    /// </summary>
    /// <param name="config">waste 建模配置 / Waste modeling configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetWasteConfig(WasteMinimizationConfig<V>? config) {
        _wasteConfig = config;
        return this;
    }

    /// <summary>
    /// 设置 length 建模配置 / Set length modeling configuration.
    /// </summary>
    /// <param name="config">length 建模配置 / Length modeling configuration.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetLengthConfig(LengthAssignmentModelingConfig<V>? config) {
        _lengthConfig = config;
        return this;
    }

    /// <summary>
    /// 设置 Top-K 方案上限 / Set Top-K plan limit.
    /// </summary>
    /// <param name="limit">Top-K 方案上限 / Top-K plan limit.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetTopKPlanLimit(Int64? limit) {
        _topKPlanLimit = limit;
        return this;
    }

    /// <summary>
    /// 设置是否允许部分结果 / Set whether partial results are allowed.
    /// </summary>
    /// <param name="enabled">是否允许 / Whether enabled.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> SetAllowPartialSolution(bool enabled) {
        _allowPartialSolution = enabled;
        return this;
    }

    /// <summary>
    /// 追加建模扩展 / Add a modeling extension.
    /// </summary>
    /// <param name="extension">建模扩展 / Modeling extension.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddExtension(Csp1dModelingExtension<V> extension) {
        _extensions.Add(extension);
        return this;
    }

    /// <summary>
    /// 追加建模扩展列表 / Add modeling extensions.
    /// </summary>
    /// <param name="extensions">建模扩展列表 / Modeling extensions.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddExtensions(IEnumerable<Csp1dModelingExtension<V>> extensions) {
        _extensions.AddRange(extensions);
        return this;
    }

    /// <summary>
    /// 便捷方法：追加扩展管线（默认所有模式）/ Convenience: add an extension pipeline (all modes).
    /// </summary>
    /// <param name="pipeline">扩展管线 / Extension pipeline.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddExtensionPipeline(IPipeline<LinearMetaModel<Flt64>> pipeline) {
        _extensions.Add(new Csp1dModelingExtension<V>(pipeline: pipeline));
        return this;
    }

    /// <summary>
    /// 便捷方法：追加扩展管线（指定模式）/ Convenience: add an extension pipeline (specific mode).
    /// </summary>
    /// <param name="pipeline">扩展管线 / Extension pipeline.</param>
    /// <param name="mode">扩展适用模式 / Extension applicable mode.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddExtensionPipeline(IPipeline<LinearMetaModel<Flt64>> pipeline, Csp1dExtensionMode mode) {
        _extensions.Add(new Csp1dModelingExtension<V>(pipeline: pipeline, mode: mode));
        return this;
    }

    /// <summary>
    /// 便捷方法：追加上下文感知扩展管线（默认所有模式）/ Convenience: add a context-aware extension pipeline (all modes).
    /// </summary>
    /// <param name="factory">上下文感知管线工厂 / Context-aware pipeline factory.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddContextAwareExtensionPipeline(System.Func<ICsp1dModelingContext<V>, IPipeline<LinearMetaModel<Flt64>>> factory) {
        _extensions.Add(new Csp1dModelingExtension<V>(contextAwarePipeline: factory));
        return this;
    }

    /// <summary>
    /// 便捷方法：追加上下文感知扩展管线（指定模式）/ Convenience: add a context-aware extension pipeline (specific mode).
    /// </summary>
    /// <param name="factory">上下文感知管线工厂 / Context-aware pipeline factory.</param>
    /// <param name="mode">扩展适用模式 / Extension applicable mode.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddContextAwareExtensionPipeline(System.Func<ICsp1dModelingContext<V>, IPipeline<LinearMetaModel<Flt64>>> factory, Csp1dExtensionMode mode) {
        _extensions.Add(new Csp1dModelingExtension<V>(mode: mode, contextAwarePipeline: factory));
        return this;
    }

    /// <summary>
    /// 追加领域策略 / Add a domain policy.
    /// </summary>
    /// <param name="policy">领域策略 / Domain policy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddDomainPolicy(ICsp1dDomainPolicy<V> policy) {
        _domainPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 追加目标策略 / Add an objective policy.
    /// </summary>
    /// <param name="policy">目标策略 / Objective policy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddObjectivePolicy(ICsp1dObjectivePolicy<V> policy) {
        _objectivePolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 追加生成策略 / Add a generation strategy.
    /// </summary>
    /// <param name="strategy">生成策略 / Generation strategy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddGenerationStrategy(ICsp1dGenerationStrategy<V> strategy) {
        _generationStrategies.Add(strategy);
        return this;
    }

    /// <summary>
    /// 追加定价策略 / Add a pricing policy.
    /// </summary>
    /// <param name="policy">定价策略 / Pricing policy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddPricingPolicy(ICsp1dPricingPolicy<V> policy) {
        _pricingPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 追加流程策略 / Add a flow policy.
    /// </summary>
    /// <param name="policy">流程策略 / Flow policy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddFlowPolicy(ICsp1dFlowPolicy<V> policy) {
        _flowPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 追加提取策略 / Add an extraction policy.
    /// </summary>
    /// <param name="policy">提取策略 / Extraction policy.</param>
    /// <returns>builder 自身 / Builder itself.</returns>
    public Csp1dSolveConfigBuilder<V> AddExtractionPolicy(ICsp1dExtractionPolicy<V> policy) {
        _extractionPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// 构建求解配置 / Build solve configuration.
    /// </summary>
    /// <returns>一站式求解配置 / One-stop solve configuration.</returns>
    public Csp1dSolveConfig<V> Build() {
        return new Csp1dSolveConfig<V> {
            ColumnGeneration = _columnGeneration,
            YieldConfig = _yieldConfig,
            WasteConfig = _wasteConfig,
            LengthConfig = _lengthConfig,
            TopKPlanLimit = _topKPlanLimit,
            AllowPartialSolution = _allowPartialSolution,
            Extensions = _extensions.ToList(),
            ExtensionSet = new Csp1dExtensionSet<V>(
                ModelingExtensions: [],
                DomainPolicies: _domainPolicies.ToList(),
                ObjectivePolicies: _objectivePolicies.ToList(),
                GenerationStrategies: _generationStrategies.ToList(),
                PricingPolicies: _pricingPolicies.ToList(),
                FlowPolicies: _flowPolicies.ToList(),
                ExtractionPolicies: _extractionPolicies.ToList()
            )
        };
    }
}

/// <summary>
/// CSP1D 问题构建工厂方法 / CSP1D problem factory methods.
/// </summary>
public static class Csp1dProblemFactory {
    /// <summary>
    /// 构建 CSP1D 问题定义 / Build CSP1D problem definition.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="configure">配置 action / Configure action.</param>
    /// <returns>CSP1D 问题定义 / CSP1D problem definition.</returns>
    public static Csp1dProblem<V> CreateProblem<V>(System.Action<Csp1dProblemBuilder<V>> configure) where V : struct {
        var builder = new Csp1dProblemBuilder<V>();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// 构建 CSP1D 求解配置 / Build CSP1D solve configuration.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="configure">配置 action / Configure action.</param>
    /// <returns>CSP1D 求解配置 / CSP1D solve configuration.</returns>
    public static Csp1dSolveConfig<V> CreateSolveConfig<V>(System.Action<Csp1dSolveConfigBuilder<V>> configure) where V : struct {
        var builder = new Csp1dSolveConfigBuilder<V>();
        configure(builder);
        return builder.Build();
    }
}
