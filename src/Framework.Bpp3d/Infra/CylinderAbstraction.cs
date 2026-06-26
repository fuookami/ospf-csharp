#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 圆柱体抽象接口 / Abstract cylinder interface.
/// </summary>
public interface IAbstractCylinder<V> where V : struct, IFloatingNumber<V> {
    /// <summary>半径 / Radius.</summary>
    Quantity<V> Radius { get; }
    /// <summary>高度 / Height.</summary>
    Quantity<V> Height { get; }
    /// <summary>轴方向 / Axis direction.</summary>
    Axis3 Axis { get; }
    /// <summary>重量 / Weight.</summary>
    Quantity<V> Weight { get; }
}

/// <summary>
/// 圆柱体几何视图 / Cylinder geometry view.
/// </summary>
public sealed record CylinderGeometryView(
    Quantity<FltX> Radius,
    Quantity<FltX> Height,
    Axis3 Axis) {
    /// <summary>直径 / Diameter.</summary>
    public Quantity<FltX> Diameter => new(Radius.Value.Plus(Radius.Value), Radius.Unit);

    /// <summary>包围长方体 / Bounding cuboid.</summary>
    public QuantityCuboid3<FltX> BoundingCuboid => Axis switch {
        Axis3.X => new QuantityCuboid3<FltX>(Height, Diameter, Diameter),
        Axis3.Y => new QuantityCuboid3<FltX>(Diameter, Height, Diameter),
        Axis3.Z => new QuantityCuboid3<FltX>(Diameter, Diameter, Height),
        _ => new QuantityCuboid3<FltX>(Diameter, Height, Diameter)
    };

    /// <summary>体积（使用 pi 近似）/ Volume (using pi approximation).</summary>
    public Quantity<FltX> Volume {
        get {
            double r = (double)Radius.Value.ToFlt64().ToDouble();
            double h = (double)Height.Value.ToFlt64().ToDouble();
            return new Quantity<FltX>(new FltX(global::System.Math.PI * r * r * h), Height.Unit);
        }
    }

    /// <summary>创建支持的轴列表 / Create list of supported axes.</summary>
    public static IReadOnlyList<Axis3> SupportedAxes { get; } = [Axis3.X, Axis3.Y, Axis3.Z];
}
