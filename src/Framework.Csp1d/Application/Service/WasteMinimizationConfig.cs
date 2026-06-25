#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// 超产面积度量口径 / Over-production area measure policy.
/// </summary>
public enum OverProductionAreaMeasure {
    /// <summary>使用产品最大宽度代理: over_i * product.maxWidth() / Use product max width proxy: over_i * product.maxWidth().</summary>
    ProductMaxWidthProxy
}

/// <summary>
/// 余料度量口径 / Rest material measure policy.
/// </summary>
public enum RestMaterialMeasure {
    /// <summary>使用余宽乘物料长度代理: restWidth * material.length / Use rest width by material length proxy: restWidth * material.length.</summary>
    RestWidthByMaterialLengthProxy
}

/// <summary>
/// 浪费最小化建模配置 / Waste minimization modeling configuration.
///
/// 当提供此配置时，Csp1dMilpSolver 在目标函数中加入余宽惩罚、余料惩罚、物料成本惩罚和超产面积惩罚。
/// 所有权重为无量纲归一化系数，由调用方负责单位换算。
///
/// When this configuration is provided, Csp1dMilpSolver adds trim width penalty,
/// rest material penalty, material cost penalty, and over-production area penalty to the objective.
/// All weights are dimensionless normalization coefficients; unit conversion is the caller's responsibility.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record WasteMinimizationConfig<V> where V : struct {
    /// <summary>余宽惩罚权重（每单位余宽）/ Trim width penalty weight (per unit of trim width).</summary>
    public V? TrimWidthPenalty { get; init; }

    /// <summary>按物料 ID 的单位成本惩罚 / Per-material unit cost penalty.</summary>
    public IReadOnlyDictionary<string, V> MaterialCostPenalty { get; init; } = new Dictionary<string, V>();

    /// <summary>超产面积惩罚权重 / Over-production area penalty weight.</summary>
    public V? OverProductionAreaPenalty { get; init; }

    /// <summary>余料惩罚权重（每单位余料面积代理）/ Rest material penalty weight (per unit of rest material area proxy).</summary>
    public V? RestMaterialPenalty { get; init; }

    /// <summary>超产面积度量口径 / Over-production area measure policy.</summary>
    public OverProductionAreaMeasure OverProductionAreaMeasure { get; init; } = OverProductionAreaMeasure.ProductMaxWidthProxy;

    /// <summary>余料度量口径 / Rest material measure policy.</summary>
    public RestMaterialMeasure RestMaterialMeasure { get; init; } = RestMaterialMeasure.RestWidthByMaterialLengthProxy;
}

/// <summary>
/// 浪费最小化建模结果 / Waste minimization modeling result.
///
/// 从 solver solution 回填的浪费相关 KPI。
/// Waste-related KPI backfilled from the solver solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record WasteMinimizationResult<V> where V : struct {
    /// <summary>总余宽 / Total trim width.</summary>
    public V? TotalTrimWidth { get; init; }

    /// <summary>按物料的成本 / Per-material costs.</summary>
    public IReadOnlyList<ModeledMaterialCost<V>> MaterialCosts { get; init; } = [];

    /// <summary>总超产面积代理 / Total over-production area proxy.</summary>
    public V? OverProductionArea { get; init; }

    /// <summary>总余料面积代理 / Total rest material area proxy.</summary>
    public V? TotalRestMaterial { get; init; }

    /// <summary>超产面积度量口径 / Over-production area measure policy.</summary>
    public OverProductionAreaMeasure OverProductionAreaMeasure { get; init; } = OverProductionAreaMeasure.ProductMaxWidthProxy;

    /// <summary>余料度量口径 / Rest material measure policy.</summary>
    public RestMaterialMeasure RestMaterialMeasure { get; init; } = RestMaterialMeasure.RestWidthByMaterialLengthProxy;
}

/// <summary>
/// 物料成本建模结果 / Material cost modeling result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record ModeledMaterialCost<V> where V : struct {
    /// <summary>物料 ID / Material id.</summary>
    public required string MaterialId { get; init; }

    /// <summary>成本值 / Cost value.</summary>
    public required V Cost { get; init; }
}
