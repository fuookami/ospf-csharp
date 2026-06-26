#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 长方体抽象接口 / Abstract cuboid interface.
/// 所有长方体形状的基础属性。
/// Base properties for all cuboid shapes.
/// </summary>
public interface IAbstractCuboid<V> where V : struct, IFloatingNumber<V> {
    /// <summary>宽度 / Width.</summary>
    Quantity<V> Width { get; }
    /// <summary>高度 / Height.</summary>
    Quantity<V> Height { get; }
    /// <summary>深度 / Depth.</summary>
    Quantity<V> Depth { get; }
    /// <summary>重量 / Weight.</summary>
    Quantity<V> Weight { get; }
    /// <summary>体积 / Volume.</summary>
    Quantity<V> Volume { get; }
}

/// <summary>
/// 底部支撑信息 / Bottom support information.
/// 描述底部货物提供的支撑面积和重量。
/// </summary>
public sealed record BottomSupport(Quantity<FltX> Area, Quantity<FltX> Weight) {
    /// <summary>合并两个支撑 / Merge two supports.</summary>
    public static BottomSupport operator +(BottomSupport left, BottomSupport right) =>
        new(left.Area.Add(right.Area), left.Weight.Add(right.Weight));
}

/// <summary>
/// 长方体视图 / Cuboid view.
/// 将长方体以特定方向观察时的几何表示。
/// Geometric representation of a cuboid viewed in a specific orientation.
/// </summary>
public class CuboidView {
    /// <summary>关联的货物 / Associated item.</summary>
    public Infra.Item Unit { get; }
    /// <summary>观察方向 / View orientation.</summary>
    public Orientation Orientation { get; }
    /// <summary>观察后的宽度 / Width in this orientation.</summary>
    public Quantity<FltX> Width { get; }
    /// <summary>观察后的高度 / Height in this orientation.</summary>
    public Quantity<FltX> Height { get; }
    /// <summary>观察后的深度 / Depth in this orientation.</summary>
    public Quantity<FltX> Depth { get; }

    public CuboidView(Infra.Item unit, Orientation orientation,
        Quantity<FltX> width, Quantity<FltX> height, Quantity<FltX> depth) {
        Unit = unit;
        Orientation = orientation;
        Width = width;
        Height = height;
        Depth = depth;
    }

    /// <summary>旋转到对应方向 / Rotate to the counterpart orientation.</summary>
    public CuboidView Rotation(Quantity<FltX> width, Quantity<FltX> height, Quantity<FltX> depth) {
        Orientation newOrientation = Orientation switch {
            Orientation.Upright => Orientation.Lie,
            Orientation.Side => Orientation.Lie,
            Orientation.Lie => Orientation.Upright,
            _ => Orientation.Upright
        };
        return new CuboidView(Unit, newOrientation, width, height, depth);
    }
}
