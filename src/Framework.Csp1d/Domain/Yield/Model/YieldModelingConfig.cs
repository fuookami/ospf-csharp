#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
/// <summary>
/// yield 建模配置，供 application 层消费 / Yield modeling configuration for application layer.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="UnderProductionPenalty">欠产惩罚系数，按 product + unit 口径 / Under-production penalty coefficient per product+unit.</param>
/// <param name="OverProductionPenalty">超产惩罚系数，按 product + unit 口径 / Over-production penalty coefficient per product+unit.</param>
/// <param name="OverProductionUpperBound">超产上限，按 product + unit 口径，null 表示无上限 / Over-production upper bound per product+unit, null for no limit.</param>
public sealed record YieldModelingConfig<V>(
    IReadOnlyDictionary<ProductDemandShadowPriceKey, V>? UnderProductionPenalty = null,
    IReadOnlyDictionary<ProductDemandShadowPriceKey, V>? OverProductionPenalty = null,
    IReadOnlyDictionary<ProductDemandShadowPriceKey, V>? OverProductionUpperBound = null
) where V : struct;

/// <summary>
/// yield 建模结果，从 solver solution 回填 / Yield modeling result back-filled from solver solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="UnderProductions">欠产变量值 / Under-production variable values.</param>
/// <param name="OverProductions">超产变量值 / Over-production variable values.</param>
public sealed record YieldModelingResult<V>(
    IReadOnlyList<ModeledUnderProduction<V>>? UnderProductions = null,
    IReadOnlyList<ModeledOverProduction<V>>? OverProductions = null
) where V : struct;

/// <summary>
/// 欠产变量结果（建模层扁平类型，从 solver solution 回填）/ Under-production variable result (flat modeling type, back-filled from solver solution).
///
/// 与 YieldModel 中的 UnderProduction（分析层富类型）不同，此类型仅记录 solver 变量值，
/// 不持有 ProductDemand 或 Quantity 等领域对象。
/// Unlike UnderProduction in YieldModel (rich analysis type), this type only records solver
/// variable values without holding domain objects like ProductDemand or Quantity.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductId">产品 ID / Product id.</param>
/// <param name="UnitSymbol">需求单位符号 / Demand unit symbol.</param>
/// <param name="Amount">欠产量 / Under-production amount.</param>
public sealed record ModeledUnderProduction<V>(
    string ProductId,
    string UnitSymbol,
    V Amount
) where V : struct;

/// <summary>
/// 超产变量结果（建模层扁平类型，从 solver solution 回填）/ Over-production variable result (flat modeling type, back-filled from solver solution).
///
/// 与 YieldModel 中的 OverProduction（分析层富类型）不同，此类型仅记录 solver 变量值，
/// 不持有 ProductDemand 或 Quantity 等领域对象。
/// Unlike OverProduction in YieldModel (rich analysis type), this type only records solver
/// variable values without holding domain objects like ProductDemand or Quantity.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductId">产品 ID / Product id.</param>
/// <param name="UnitSymbol">需求单位符号 / Demand unit symbol.</param>
/// <param name="Amount">超产量 / Over-production amount.</param>
public sealed record ModeledOverProduction<V>(
    string ProductId,
    string UnitSymbol,
    V Amount
) where V : struct;
