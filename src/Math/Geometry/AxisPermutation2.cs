#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 二维轴置换 / 2D axis permutation.
/// </summary>
public sealed record AxisPermutation2(Axis2 WidthAxis, Axis2 HeightAxis) {
    /// <summary>XY 置换 / XY permutation.</summary>
    public static readonly AxisPermutation2 XY = new(Axis2.X, Axis2.Y);
    /// <summary>YX 置换 / YX permutation.</summary>
    public static readonly AxisPermutation2 YX = new(Axis2.Y, Axis2.X);

    /// <summary>按轴置换矩形 / Permute a rectangle by axes.</summary>
    public Rectangle2<V> Apply<V>(Rectangle2<V> rectangle) where V : struct, IFloatingNumber<V>
        => new(rectangle.Along(WidthAxis), rectangle.Along(HeightAxis));

    /// <summary>圆形无方向性，原样返回 / Circle is direction-agnostic.</summary>
    public Circle2<V> Apply<V>(Circle2<V> circle) where V : struct, IFloatingNumber<V> => circle;
}
