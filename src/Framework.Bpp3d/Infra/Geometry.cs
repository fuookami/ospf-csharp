#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra
{
    /// <summary>装箱形状类型枚举 / Packing shape type enumeration.</summary>
    public enum PackingShapeType { Cuboid, Cylinder }

    /// <summary>装箱算法形状类型枚举 / Packing algorithm shape type enumeration.</summary>
    public enum PackingAlgorithmShapeType { Cuboid, VerticalCylinder, HorizontalCylinderX, HorizontalCylinderZ }

    /// <summary>装箱坐标轴枚举 / Packing axis enumeration.</summary>
    public enum PackingAxis3 { X, Y, Z }

    /// <summary>形状包围盒 / Shape bounding box.</summary>
    public sealed record ShapeBoundingBox3<V>(Quantity<V> Width, Quantity<V> Height, Quantity<V> Depth)
        where V : struct, IFloatingNumber<V>;

    /// <summary>形状底面轮廓 / Shape footprint (2D projection).</summary>
    public abstract record ShapeFootprint2<V> where V : struct, IFloatingNumber<V>
    {
        public sealed record Rectangle(Quantity<V> Width, Quantity<V> Depth) : ShapeFootprint2<V>;
        public sealed record Circle(Quantity<V> Radius) : ShapeFootprint2<V>;
    }

    /// <summary>三维装箱形状接口 / 3D packing shape interface.</summary>
    public interface IPackingShape3<V> where V : struct, IFloatingNumber<V>
    {
        PackingShapeType ShapeType { get; }
        PackingAlgorithmShapeType AlgorithmShapeType { get; }
        Quantity<V> BoundingWidth { get; }
        Quantity<V> BoundingHeight { get; }
        Quantity<V> BoundingDepth { get; }
        Quantity<V> ActualVolume { get; }
        Axis3? Axis { get; }
        ShapeBoundingBox3<V> BoundingBox => new(BoundingWidth, BoundingHeight, BoundingDepth);
        ShapeFootprint2<V> Footprint();
    }

    /// <summary>长方体装箱形状 / Cuboid packing shape.</summary>
    public sealed record CuboidPackingShape3<V>(QuantityCuboid3<V> Cuboid) : IPackingShape3<V>
        where V : struct, IFloatingNumber<V>
    {
        public PackingShapeType ShapeType => PackingShapeType.Cuboid;
        public PackingAlgorithmShapeType AlgorithmShapeType => PackingAlgorithmShapeType.Cuboid;
        public Quantity<V> BoundingWidth => Cuboid.Width;
        public Quantity<V> BoundingHeight => Cuboid.Height;
        public Quantity<V> BoundingDepth => Cuboid.Depth;
        public Quantity<V> ActualVolume => Cuboid.Volume;
        public Axis3? Axis => null;
        public ShapeFootprint2<V> Footprint() => new ShapeFootprint2<V>.Rectangle(Cuboid.Width, Cuboid.Depth);
    }

    /// <summary>圆柱体装箱形状 / Cylinder packing shape.</summary>
    public sealed record CylinderPackingShape3(QuantityCylinder3<FltX> Cylinder) : IPackingShape3<FltX>
    {
        public PackingShapeType ShapeType => PackingShapeType.Cylinder;
        public PackingAlgorithmShapeType AlgorithmShapeType => Cylinder.Axis switch
        {
            Axis3.X => PackingAlgorithmShapeType.HorizontalCylinderX,
            Axis3.Y => PackingAlgorithmShapeType.VerticalCylinder,
            Axis3.Z => PackingAlgorithmShapeType.HorizontalCylinderZ,
            _ => PackingAlgorithmShapeType.VerticalCylinder
        };
        public Axis3? Axis => Cylinder.Axis;

        public Quantity<FltX> BoundingWidth => Cylinder.Axis switch
        {
            Axis3.X => Cylinder.Height,
            _ => Cylinder.Radius.MultiplyScalar(new FltX(2))
        };

        public Quantity<FltX> BoundingHeight => Cylinder.Axis switch
        {
            Axis3.Y => Cylinder.Height,
            _ => Cylinder.Radius.MultiplyScalar(new FltX(2))
        };

        public Quantity<FltX> BoundingDepth => Cylinder.Axis switch
        {
            Axis3.Z => Cylinder.Height,
            _ => Cylinder.Radius.MultiplyScalar(new FltX(2))
        };

        public Quantity<FltX> ActualVolume
        {
            get
            {
                var r = (double)Cylinder.Radius.Value.ToFlt64().ToDouble();
                var h = (double)Cylinder.Height.Value.ToFlt64().ToDouble();
                return new Quantity<FltX>(new FltX(global::System.Math.PI * r * r * h), Cylinder.Height.Unit);
            }
        }

        public ShapeFootprint2<FltX> Footprint() => Cylinder.Axis switch
        {
            Axis3.Y => new ShapeFootprint2<FltX>.Circle(Cylinder.Radius),
            _ => new ShapeFootprint2<FltX>.Rectangle(BoundingWidth, BoundingDepth)
        };
    }

    /// <summary>朝向枚举 / Orientation enumeration.</summary>
    public enum Orientation { Upright, Side, Lie }

    /// <summary>朝向类别 / Orientation category.</summary>
    public enum OrientationCategory { Upright, Side, Lie }
}
