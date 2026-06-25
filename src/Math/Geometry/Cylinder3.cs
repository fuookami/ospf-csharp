#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>三维轴对齐线段 / 3D axis-aligned line segment.</summary>
public sealed record AxisLine3<V>(Axis3 Axis, V From, V To) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 三维圆柱体 / 3D cylinder (radius x height x axis).
/// </summary>
public sealed record Cylinder3<V>(V Radius, V Height, Axis3 Axis) : IShape3<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>直径 / Diameter.</summary>
    public V Diameter => GeometryOps.Plus(Radius, Radius);

    /// <inheritdoc/>
    public Cuboid3<V> BoundingCuboid => Axis switch {
        Axis3.X => new(Height, Diameter, Diameter),
        Axis3.Y => new(Diameter, Height, Diameter),
        Axis3.Z => new(Diameter, Diameter, Height),
        _ => throw new ArgumentOutOfRangeException(nameof(Axis)),
    };

    /// <summary>沿指定轴的尺寸 / Dimension along an axis.</summary>
    public V Along(Axis3 axis) => axis == Axis ? Height : Diameter;

    /// <summary>原点处的轴线 / Axis line at origin.</summary>
    public AxisLine3<V> AxisLineAtOrigin => new(Axis, GeometryOps.ZeroOf(Height), Height);

    /// <summary>在指定平面上的投影形状 / Projection on a plane.</summary>
    public Projection2<V> ProjectionOn(AxisPlane3 plane) =>
        plane.Contains(Axis)
            ? new Rectangle2<V>(Along(plane.FirstAxis), Along(plane.SecondAxis))
            : (Projection2<V>)new Circle2<V>(Radius);

    /// <summary>底面积 / Base area.</summary>
    public V BaseArea(V pi) => Radius.Times(Radius).Times(pi);

    /// <summary>体积 / Volume.</summary>
    public V Volume(V pi) => BaseArea(pi).Times(Height);

    /// <summary>按轴置置换 / Permute by axis permutation.</summary>
    public Result<Cylinder3<V>, ErrorCode, Error<ErrorCode>> Permute(AxisPermutation3 permutation) =>
        permutation.Apply(this);

    /// <summary>原点处包围盒 / Bounding box at origin.</summary>
    public Box3<V> BoundingBoxAtOrigin() => Box3<V>.AtOrigin(BoundingCuboid);

    /// <summary>指定位置的包围盒 / Bounding box at position.</summary>
    public Box3<V> ToBoundingBox(V x, V y, V z) => new(x, y, z, BoundingCuboid);
}
