#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 二维投影形状接口 / 2D projection shape interface.
/// 所有二维投影形状（圆形、矩形）的公共接口。
/// Common interface for all 2D projection shapes (circle, rectangle).
/// </summary>
/// <typeparam name="V">数值类型 / Number type.</typeparam>
public interface IQuantityProjection2<V>
    where V : struct, IFloatingNumber<V>
{
}

/// <summary>
/// 二维圆形投影 / 2D circle projection.
/// </summary>
public sealed record QuantityCircle2<V>(
    Quantity<V> Radius
) : IQuantityProjection2<V>
    where V : struct, IFloatingNumber<V>
{
    /// <summary>直径 / Diameter.</summary>
    public Quantity<V> Diameter => new(Radius.Value.Plus(Radius.Value), Radius.Unit);

    /// <summary>计算面积 / Compute the area (pi * r^2).</summary>
    public Quantity<V> Area(V pi) =>
        QuantityOps.QuantityProduct(
            QuantityOps.QuantityProduct(Radius, Radius),
            pi);

    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public QuantityBox2<V> BoundingBoxAtOrigin() => QuantityBox2<V>.AtOrigin(this);
}

/// <summary>
/// 二维矩形投影 / 2D rectangle projection.
/// </summary>
public sealed record QuantityRectangle2<V>(
    Quantity<V> Width,
    Quantity<V> Height
) : IQuantityProjection2<V>
    where V : struct, IFloatingNumber<V>
{
    /// <summary>面积 / Area.</summary>
    public Quantity<V> Area => QuantityOps.QuantityProduct(Width, Height);

    /// <summary>获取沿指定轴的尺寸 / Get the dimension along a specified axis.</summary>
    public Quantity<V> Along(Axis2 axis) => axis switch
    {
        Axis2.X => Width,
        Axis2.Y => Height,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    /// <summary>对矩形应用轴置换 / Apply axis permutation to the rectangle.</summary>
    public QuantityRectangle2<V> Permute(AxisPermutation2 permutation) =>
        new(Along(permutation.WidthAxis), Along(permutation.HeightAxis));

    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public QuantityBox2<V> AtOrigin() => QuantityBox2<V>.AtOrigin(this);
}
