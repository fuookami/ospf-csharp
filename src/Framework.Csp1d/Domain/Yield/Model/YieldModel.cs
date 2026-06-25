#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
/// <summary>
/// 需求聚合键，确保相同产品不同单位的产出不混算 / Demand aggregation key ensuring same-product different-unit outputs are not mixed.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductId">产品标识 / Product identifier.</param>
/// <param name="Unit">物理单位 / Physical unit.</param>
public sealed record DemandAggregationKey<V>(
    string ProductId,
    PhysicalUnit Unit
) where V : struct;

/// <summary>
/// 欠产 / Under-production.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Demand">产品需求 / Product demand.</param>
/// <param name="Shortfall">欠产量 / Shortfall quantity.</param>
public sealed record UnderProduction<V>(
    ProductDemand<V> Demand,
    Quantity<V> Shortfall
) where V : struct;

/// <summary>
/// 超产 / Over-production.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Demand">产品需求 / Product demand.</param>
/// <param name="Surplus">超产量 / Surplus quantity.</param>
public sealed record OverProduction<V>(
    ProductDemand<V> Demand,
    Quantity<V> Surplus
) where V : struct;

/// <summary>
/// 产品产出汇总 / Product output summary.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Product">产品 / Product.</param>
/// <param name="TotalQuantity">总产出量 / Total output quantity.</param>
/// <param name="Mode">需求口径 / Demand mode.</param>
public sealed record ProductOutput<V>(
    Product<V> Product,
    Quantity<V> TotalQuantity,
    DemandMode? Mode = null
) where V : struct;

/// <summary>
/// 产出偏差分析结果 / Yield deviation analysis result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="UnderProductions">欠产列表 / Under-production list.</param>
/// <param name="OverProductions">超产列表 / Over-production list.</param>
/// <param name="Outputs">产品产出列表 / Product output list.</param>
public sealed record YieldAnalysis<V>(
    IReadOnlyList<UnderProduction<V>> UnderProductions,
    IReadOnlyList<OverProduction<V>> OverProductions,
    IReadOnlyList<ProductOutput<V>> Outputs
) where V : struct;
