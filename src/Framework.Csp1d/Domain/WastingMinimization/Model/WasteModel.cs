#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Model;
/// <summary>
/// 余宽浪费记录 / Rest width waste record for a cutting plan.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Plan">切割方案 / Cutting plan.</param>
/// <param name="RestWidth">剩余幅宽 / Remaining width.</param>
public sealed record RestWidthWaste<V>(
    CuttingPlan<V> Plan,
    Quantity<V> RestWidth
) where V : struct;

/// <summary>
/// 余料浪费记录 / Rest material waste record.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Plan">切割方案 / Cutting plan.</param>
/// <param name="RestMaterial">余料面积代理（按业务单位）/ Rest material area proxy in business unit.</param>
public sealed record RestMaterialWaste<V>(
    CuttingPlan<V> Plan,
    Quantity<V> RestMaterial
) where V : struct;

/// <summary>
/// 超产面积浪费 / Over-production area waste.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Product">产品 / Product.</param>
/// <param name="WasteArea">浪费面积代理 / Waste area proxy.</param>
public sealed record OverProductionAreaWaste<V>(
    Product<V> Product,
    Quantity<V> WasteArea
) where V : struct;

/// <summary>
/// 浪费最小化目标 / Waste minimization objective.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public abstract record WasteMinimizationObjective<V> where V : struct {
    private WasteMinimizationObjective() { }

    /// <summary>
    /// 最小化余宽 / Minimize rest width.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeRestWidth<V>(V Weight) : WasteMinimizationObjective<V> where V : struct;

    /// <summary>
    /// 最小化余料 / Minimize rest material.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeRestMaterial<V>(V Weight) : WasteMinimizationObjective<V> where V : struct;

    /// <summary>
    /// 最小化成本 / Minimize cost.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeCost<V>(V Weight) : WasteMinimizationObjective<V> where V : struct;

    /// <summary>
    /// 最小化超产面积浪费 / Minimize over-production area waste.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeOverProductionArea<V>(V Weight) : WasteMinimizationObjective<V> where V : struct;
}

/// <summary>
/// 超产面积度量口径 / Over-production area measure policy.
/// </summary>
public enum OverProductionAreaMeasure {
    /// <summary>使用产品最大宽度代理 / Use product max width proxy.</summary>
    ProductMaxWidthProxy
}

/// <summary>
/// 余料度量口径 / Rest material measure policy.
/// </summary>
public enum RestMaterialMeasure {
    /// <summary>使用余宽乘物料长度代理 / Use rest width by material length proxy.</summary>
    RestWidthByMaterialLengthProxy
}
