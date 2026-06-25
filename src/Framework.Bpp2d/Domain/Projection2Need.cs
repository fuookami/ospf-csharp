#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维投影需求
/// 2D projection need.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Projection2Need<V>(
    /// <summary>宽度 / Width</summary>
    Quantity<V> Width,
    /// <summary>高度 / Height</summary>
    Quantity<V> Height
) where V : struct, IFloatingNumber<V> {
    /// <summary>面积 / Area</summary>
    public Quantity<V> Area => QuantityArithmetic.Product(Width, Height);
}
