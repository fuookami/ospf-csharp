#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 问题定义 / CSP1D problem definition.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Products">产品列表 / Product list.</param>
/// <param name="Materials">物料列表 / Material list.</param>
/// <param name="Machines">设备列表 / Machine list.</param>
/// <param name="Costars">配规列表 / Costar list.</param>
/// <param name="Demands">需求列表 / Demand list.</param>
/// <param name="Configuration">求解配置 / Solving configuration.</param>
/// <param name="SolveConfig">一站式求解配置，缺省时使用 Configuration / One-stop solve config, falls back to Configuration.</param>
public sealed record Csp1dProblem<V>(
    IReadOnlyList<Product<V>> Products,
    IReadOnlyList<Material<V>> Materials,
    IReadOnlyList<Machine<V>> Machines,
    IReadOnlyList<Costar<V>> Costars,
    IReadOnlyList<ProductDemand<V>> Demands,
    Csp1dConfiguration<V> Configuration,
    Csp1dSolveConfig<V>? SolveConfig
) where V : struct {
    /// <summary>配规列表默认为空 / Costar list defaults to empty.</summary>
    public Csp1dProblem(
        IReadOnlyList<Product<V>> Products,
        IReadOnlyList<Material<V>> Materials,
        IReadOnlyList<Machine<V>> Machines,
        IReadOnlyList<ProductDemand<V>> Demands,
        Csp1dConfiguration<V>? Configuration = null,
        Csp1dSolveConfig<V>? SolveConfig = null
    ) : this(
        Products,
        Materials,
        Machines,
        [],
        Demands,
        Configuration ?? new Csp1dConfiguration<V>(),
        SolveConfig
    ) { }
}

/// <summary>
/// CSP1D 求解配置 / CSP1D solving configuration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record Csp1dConfiguration<V> where V : struct {
    /// <summary>初始方案上限 / Initial plan limit.</summary>
    public Int64 MaxInitialPlans { get; init; } = new(1024);

    /// <summary>每轮定价新增方案上限 / Max pricing plans per iteration.</summary>
    public Int64 MaxPricingPlans { get; init; } = new(64);

    /// <summary>列生成迭代上限 / Column generation iteration limit.</summary>
    public Int64 IterationLimit { get; init; } = new(8);
}

/// <summary>
/// CSP1D 一站式求解配置 / CSP1D one-stop solve configuration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record Csp1dSolveConfig<V> where V : struct {
    /// <summary>列生成配置 / Column generation configuration.</summary>
    public Csp1dConfiguration<V> ColumnGeneration { get; init; } = new();

    /// <summary>产出约束与目标配置 / Yield constraint and objective configuration.</summary>
    public YieldModelingConfig<V>? YieldConfig { get; init; }

    /// <summary>浪费最小化配置 / Waste minimization configuration.</summary>
    public WasteMinimizationConfig<V>? WasteConfig { get; init; }

    /// <summary>长度分配配置 / Length assignment configuration.</summary>
    public LengthAssignmentModelingConfig<V>? LengthConfig { get; init; }

    /// <summary>Top-K 方案保留上限，null 表示不输出 / Top-K plan limit, null for disabled.</summary>
    public Int64? TopKPlanLimit { get; init; }

    /// <summary>最终 MILP 失败时是否返回部分结果 / Whether to return a partial result when final MILP fails.</summary>
    public bool AllowPartialSolution { get; init; } = true;

    /// <summary>建模扩展列表，用于注入额外管线 / Modeling extensions for injecting additional pipelines.</summary>
    public IReadOnlyList<Csp1dModelingExtension<V>> Extensions { get; init; } = [];

    /// <summary>扩展集合 / Extension set.</summary>
    public Csp1dExtensionSet<V> ExtensionSet { get; init; } = Csp1dExtensionSet<V>.Empty;

    /// <summary>
    /// 合并 Extensions 与 ExtensionSet.ModelingExtensions /
    /// Merge Extensions and ExtensionSet.ModelingExtensions.
    ///
    /// 保证直接构造 Csp1dSolveConfig(ExtensionSet = ...) 时 ModelingExtensions 也能进入求解路径。
    /// Ensures that ModelingExtensions from ExtensionSet also enters the solve path
    /// when Csp1dSolveConfig is constructed directly with ExtensionSet.
    /// </summary>
    public IReadOnlyList<Csp1dModelingExtension<V>> AllExtensions {
        get {
            var result = new List<Csp1dModelingExtension<V>>(Extensions);
            var existingSet = new HashSet<Csp1dModelingExtension<V>>(Extensions);
            foreach (Csp1dModelingExtension<V> ext in ExtensionSet.ModelingExtensions) {
                if (!existingSet.Contains(ext)) {
                    result.Add(ext);
                }
            }
            return result;
        }
    }
}
