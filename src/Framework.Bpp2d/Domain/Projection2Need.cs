#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维投影需求
/// 2D projection need.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Width">宽度 / Width</param>
/// <param name="Height">高度 / Height</param>
public sealed record Projection2Need<V>(
    Quantity<V> Width,
    Quantity<V> Height
) where V : struct, IFloatingNumber<V> {
    /// <summary>面积 / Area</summary>
    public Quantity<V> Area => QuantityArithmetic.Product(Width, Height);
}
