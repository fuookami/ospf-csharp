#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
/// <summary>
/// 需求残量成本 / Demand reduced cost.
/// </summary>
public sealed record DemandReducedCost(
    string MaterialNo,
    double ReducedCost);

/// <summary>
/// 需求统计 / Demand statistics.
/// </summary>
public sealed record DemandStatistics(
    string MaterialNo,
    ulong Amount);

/// <summary>
/// 量化需求残量成本 / Quantity demand reduced cost.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record QuantityDemandReducedCost<V>(
    string MaterialNo,
    Quantity<V> ReducedCost)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 量化需求统计 / Quantity demand statistics.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record QuantityDemandStatistics<V>(
    string MaterialNo,
    Quantity<V> Amount)
    where V : struct, IFloatingNumber<V>;
