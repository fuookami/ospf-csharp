#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing;
/// <summary>
/// 装箱聚合接口 / Packing aggregation interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IPackingAggregation<V> where V : struct, IFloatingNumber<V> {
}

/// <summary>
/// 装箱聚合 / Packing aggregation.
/// 聚合装箱方案与求解结果 / Aggregates packing plans and solve results.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class PackingAggregation<V> : IPackingAggregation<V>
    where V : struct, IFloatingNumber<V> {
}
