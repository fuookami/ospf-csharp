#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 二维轴置换 / 2D axis permutation.
/// 纯几何概念；仅 Apply 与数量模型绑定。
/// Pure geometry; only Apply binds to quantity models.
/// </summary>
public sealed record QuantityAxisPermutation2(Axis2 WidthAxis, Axis2 HeightAxis) {
    /// <summary>X-Y 置换 / X-Y permutation.</summary>
    public static readonly QuantityAxisPermutation2 XY = new(Axis2.X, Axis2.Y);

    /// <summary>Y-X 置换 / Y-X permutation.</summary>
    public static readonly QuantityAxisPermutation2 YX = new(Axis2.Y, Axis2.X);

    /// <summary>对矩形应用轴置换 / Apply axis permutation to a rectangle.</summary>
    public QuantityRectangle2<V> Apply<V>(QuantityRectangle2<V> rectangle)
        where V : struct, IFloatingNumber<V>
        => new(rectangle.Along(WidthAxis), rectangle.Along(HeightAxis));

    /// <summary>对圆形应用轴置换（圆形无变化）/ Apply axis permutation to a circle (no change).</summary>
    public QuantityCircle2<V> Apply<V>(QuantityCircle2<V> circle)
        where V : struct, IFloatingNumber<V>
        => circle;
}
