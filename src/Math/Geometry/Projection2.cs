#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using System;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 二维投影形状 / 2D projection shape.
/// </summary>
public abstract record Projection2<V> where V : struct, IFloatingNumber<V>;

/// <summary>
/// 二维圆形投影 / 2D circle projection (radius).
/// </summary>
public sealed record Circle2<V>(V Radius) : Projection2<V> where V : struct, IFloatingNumber<V> {
    /// <summary>直径 / Diameter.</summary>
    public V Diameter => GeometryOps.Plus(Radius, Radius);

    /// <summary>面积 / Area.</summary>
    public V Area(V pi) => Radius.Times(Radius).Times(pi);

    /// <summary>原点处包围盒 / Bounding box at origin.</summary>
    public Box2<V> BoundingBoxAtOrigin() => Box2<V>.AtOrigin(this);
}

/// <summary>
/// 二维矩形投影 / 2D rectangle projection (width x height).
/// </summary>
public sealed record Rectangle2<V>(V Width, V Height) : Projection2<V> where V : struct, IFloatingNumber<V> {
    /// <summary>面积 / Area.</summary>
    public V Area => Width.Times(Height);

    /// <summary>沿指定轴的尺寸 / Dimension along an axis.</summary>
    public V Along(Axis2 axis) => axis switch {
        Axis2.X => Width,
        Axis2.Y => Height,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    /// <summary>按轴置置换 / Permute by axis permutation.</summary>
    public Rectangle2<V> Permute(AxisPermutation2 permutation) => permutation.Apply(this);

    /// <summary>原点处包围盒 / Bounding box at origin.</summary>
    public Box2<V> AtOrigin() => Box2<V>.AtOrigin(this);
}
