#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维长方体形状 / 3D cuboid shape.
/// 由宽度、高度和深度定义的三维长方体，实现 IQuantityShape3 接口。
/// A 3D cuboid defined by width, height, and depth, implementing IQuantityShape3.
/// </summary>
public sealed record QuantityCuboid3<V>(
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth
) : IQuantityShape3<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>最小包围长方体（自身）/ Minimum bounding cuboid (self).</summary>
    public QuantityCuboid3<V> BoundingCuboid => this;

    /// <summary>体积 / Volume (width * height * depth; infallible product chain).</summary>
    public Quantity<V> Volume =>
        QuantityOps.QuantityProduct(
            QuantityOps.QuantityProduct(Width, Height),
            Depth);

    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public QuantityBox3<V> AtOrigin() => QuantityBox3<V>.AtOrigin(this);

    /// <summary>在指定位置创建包围盒 / Create a bounding box at the specified position.</summary>
    public QuantityBox3<V> At(Quantity<V> x, Quantity<V> y, Quantity<V> z) =>
        new(x, y, z, this);

    /// <summary>获取沿指定轴的尺寸 / Get the dimension along a specified axis.</summary>
    public Quantity<V> Along(Axis3 axis) => axis switch {
        Axis3.X => Width,
        Axis3.Y => Height,
        Axis3.Z => Depth,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    /// <summary>按轴置换 / Apply axis permutation.</summary>
    public QuantityCuboid3<V> Permute(AxisPermutation3 permutation) =>
        new(Along(permutation.WidthAxis), Along(permutation.HeightAxis), Along(permutation.DepthAxis));
}
