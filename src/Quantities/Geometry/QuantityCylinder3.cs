#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维圆柱体形状 / 3D cylinder shape.
/// 由半径、高度和轴方向定义的圆柱体，实现 IQuantityShape3 接口。
/// A cylinder defined by radius, height, and axis direction, implementing IQuantityShape3.
/// </summary>
public sealed record QuantityCylinder3<V>(
    Quantity<V> Radius,
    Quantity<V> Height,
    Axis3 Axis
) : IQuantityShape3<V>
    where V : struct, IFloatingNumber<V>
{
    /// <summary>直径 / Diameter.</summary>
    public Quantity<V> Diameter => new(Radius.Value.Plus(Radius.Value), Radius.Unit);

    /// <summary>最小包围长方体 / Minimum bounding cuboid.</summary>
    public QuantityCuboid3<V> BoundingCuboid => Axis switch
    {
        Axis3.X => new QuantityCuboid3<V>(Height, Diameter, Diameter),
        Axis3.Y => new QuantityCuboid3<V>(Diameter, Height, Diameter),
        Axis3.Z => new QuantityCuboid3<V>(Diameter, Diameter, Height),
        _ => throw new ArgumentOutOfRangeException(nameof(Axis)),
    };

    /// <summary>获取沿指定轴的尺寸 / Get the dimension along a specified axis.</summary>
    public Quantity<V> Along(Axis3 axis) => axis == Axis ? Height : Diameter;

    /// <summary>原点处的轴线段 / Axis line segment at the origin.</summary>
    public QuantityAxisLine3<V> AxisLineAtOrigin => new(Axis, QuantityOps.QuantityZeroOf(Height), Height);

    /// <summary>在指定平面上的投影 / Projection onto a specified plane.</summary>
    public IQuantityProjection2<V> ProjectionOn(AxisPlane3 plane) =>
        plane.Contains(Axis)
            ? new QuantityRectangle2<V>(Along(plane.FirstAxis), Along(plane.SecondAxis))
            : new QuantityCircle2<V>(Radius);

    /// <summary>计算底面积 / Compute the base area.</summary>
    public Quantity<V> BaseArea(V pi) =>
        QuantityOps.QuantityProduct(QuantityOps.QuantityProduct(Radius, Radius), pi);

    /// <summary>计算体积 / Compute the volume.</summary>
    public Quantity<V> Volume(V pi) => QuantityOps.QuantityProduct(BaseArea(pi), Height);

    /// <summary>按轴置换 / Apply axis permutation.</summary>
    public Result<QuantityCylinder3<V>, ErrorCode, Error<ErrorCode>> Permute(AxisPermutation3 permutation) =>
        permutation.MapAxis(Axis).Map(axis => this with { Axis = axis });

    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public QuantityBox3<V> BoundingBoxAtOrigin() => QuantityBox3<V>.AtOrigin(BoundingCuboid);

    /// <summary>在指定位置创建包围盒 / Create a bounding box at the specified position.</summary>
    public QuantityBox3<V> ToBoundingBox(Quantity<V> x, Quantity<V> y, Quantity<V> z) =>
        new(x, y, z, BoundingCuboid);
}
