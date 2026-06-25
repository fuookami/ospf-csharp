#nullable enable
using System;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维长方体 / 3D cuboid (width x height x depth).
/// </summary>
public sealed record Cuboid3<V>(V Width, V Height, V Depth) : IShape3<V>
    where V : struct, IFloatingNumber<V>
{
    /// <inheritdoc/>
    public Cuboid3<V> BoundingCuboid => this;

    /// <summary>体积 / Volume.</summary>
    public V Volume => Width.Times(Height).Times(Depth);

    /// <summary>放在原点 / Place at origin.</summary>
    public Box3<V> AtOrigin() => Box3<V>.AtOrigin(this);

    /// <summary>放在指定位置 / Place at position.</summary>
    public Box3<V> At(V x, V y, V z) => new(x, y, z, this);

    /// <summary>沿指定轴的尺寸 / Dimension along an axis.</summary>
    public V Along(Axis3 axis) => axis switch
    {
        Axis3.X => Width,
        Axis3.Y => Height,
        Axis3.Z => Depth,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}
