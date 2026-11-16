#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 已装箱物品的几何快照 / Geometry snapshot of a packed item.
/// </summary>
/// <remarks>
/// （23023 B）在每次校验时从 `QuantityPlacement3` 现算轴对齐包围盒。
/// C# 侧的 `PackingItem` **原本一点放置信息都不带**，于是守卫的重叠判断只能恒返回 `false`——
/// 一个永远说"没有重叠"的守卫。本类型把包围盒**在装箱时就冻结下来**，守卫和渲染器共用同一份。
///
/// The code computes the axis-aligned bounding box from `QuantityPlacement3` on every check. The C# `PackingItem` **used to carry no placement at all**,
/// so the guard's overlap check could only ever return `false` — a guard that always says "no overlap". This type freezes the bounding box **at packing
/// time**, and both the guard and the renderer read the same one.
/// </remarks>
/// <param name="ShapeType">装箱形状类型 / Packing shape type.</param>
/// <param name="Axis">圆柱轴向（长方体为 null）/ Cylinder axis (null for cuboids).</param>
/// <param name="Radius">圆柱半径（长方体为 0）/ Cylinder radius (0 for cuboids).</param>
/// <param name="MinX">X 下界 / X lower bound.</param>
/// <param name="MinY">Y 下界 / Y lower bound.</param>
/// <param name="MinZ">Z 下界 / Z lower bound.</param>
/// <param name="MaxX">X 上界 / X upper bound.</param>
/// <param name="MaxY">Y 上界 / Y upper bound.</param>
/// <param name="MaxZ">Z 上界 / Z upper bound.</param>
public sealed record PackingItemGeometry(
    PackingShapeType ShapeType,
    Axis3? Axis,
    double Radius,
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ) {

    /// <summary>是否为圆柱 / Whether this is a cylinder.</summary>
    public bool IsCylinder => ShapeType == PackingShapeType.Cylinder;

    /// <summary>包围盒宽度 / Bounding width.</summary>
    public double Width => MaxX - MinX;

    /// <summary>包围盒高度 / Bounding height.</summary>
    public double Height => MaxY - MinY;

    /// <summary>包围盒深度 / Bounding depth.</summary>
    public double Depth => MaxZ - MinZ;

    /// <summary>
    /// 从装箱形状与位置构造几何快照 / Build the geometry snapshot from a packing shape and a position.
    /// </summary>
    /// <param name="shape">装箱形状 / Packing shape.</param>
    /// <param name="position">放置点 / Placement point.</param>
    /// <returns>几何快照 / The geometry snapshot.</returns>
    public static PackingItemGeometry From(Infra.IPackingShape3<FltX> shape, Infra.QuantityPoint3<FltX> position) {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(position);
        double minX = position.X.ToDouble();
        double minY = position.Y.ToDouble();
        double minZ = position.Z.ToDouble();
        return new PackingItemGeometry(
            ShapeType: shape.ShapeType,
            Axis: shape.Axis,
            Radius: shape is Infra.CylinderPackingShape3 cylinder
                ? cylinder.Cylinder.Radius.ToDouble()
                : 0.0,
            MinX: minX,
            MinY: minY,
            MinZ: minZ,
            MaxX: minX + shape.BoundingWidth.ToDouble(),
            MaxY: minY + shape.BoundingHeight.ToDouble(),
            MaxZ: minZ + shape.BoundingDepth.ToDouble());
    }

    /// <summary>沿指定轴的下界 / Lower bound along the given axis.</summary>
    /// <param name="axis">坐标轴 / The axis.</param>
    /// <returns>下界 / The lower bound.</returns>
    public double Min(Axis3 axis) => axis switch {
        Axis3.X => MinX,
        Axis3.Y => MinY,
        _ => MinZ,
    };

    /// <summary>沿指定轴的上界 / Upper bound along the given axis.</summary>
    /// <param name="axis">坐标轴 / The axis.</param>
    /// <returns>上界 / The upper bound.</returns>
    public double Max(Axis3 axis) => axis switch {
        Axis3.X => MaxX,
        Axis3.Y => MaxY,
        _ => MaxZ,
    };

    /// <summary>沿指定轴的中心 / Center along the given axis.</summary>
    /// <param name="axis">坐标轴 / The axis.</param>
    /// <returns>中心坐标 / The center coordinate.</returns>
    public double Center(Axis3 axis) => (Min(axis) + Max(axis)) / 2.0;
}
