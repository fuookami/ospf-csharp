#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 六方向枚举（含旋转）/ Six-orientation enumeration (with rotated variants).
/// 扩展基础三方向，增加 UprightRotated / SideRotated / LieRotated。
/// Extends base three orientations with rotated counterparts.
/// </summary>
public enum Orientation6 {
    /// <summary>正立（无旋转）/ Upright (no rotation). Rank 0.</summary>
    Upright,
    /// <summary>正立旋转（宽深互换）/ Upright rotated (swap width/depth). Rank 1.</summary>
    UprightRotated,
    /// <summary>侧放（宽高互换）/ Side (swap width/height). Rank 2.</summary>
    Side,
    /// <summary>侧放旋转（三路轮换）/ Side rotated (three-way swap). Rank 3.</summary>
    SideRotated,
    /// <summary>平放（深高互换）/ Lie (swap depth/height). Rank 4.</summary>
    Lie,
    /// <summary>平放旋转（三路轮换）/ Lie rotated (three-way swap). Rank 5.</summary>
    LieRotated
}

/// <summary>
/// 六方向到轴置换映射 / Orientation6 to axis permutation mapping.
/// </summary>
public static class OrientationAxisPermutationMapping {
    /// <summary>
    /// 将六方向映射为轴置换 / Maps an orientation to its axis permutation.
    /// </summary>
    public static AxisPermutation3 ToAxisPermutation3(this Orientation6 orientation) => orientation switch {
        Orientation6.Upright => new AxisPermutation3(Axis3.X, Axis3.Y, Axis3.Z),
        Orientation6.UprightRotated => new AxisPermutation3(Axis3.Z, Axis3.Y, Axis3.X),
        Orientation6.Side => new AxisPermutation3(Axis3.Y, Axis3.X, Axis3.Z),
        Orientation6.SideRotated => new AxisPermutation3(Axis3.Z, Axis3.X, Axis3.Y),
        Orientation6.Lie => new AxisPermutation3(Axis3.X, Axis3.Z, Axis3.Y),
        Orientation6.LieRotated => new AxisPermutation3(Axis3.Y, Axis3.Z, Axis3.X),
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>方向类别 / Orientation category for Orientation6.</summary>
    public static OrientationCategory Category(this Orientation6 orientation) => orientation switch {
        Orientation6.Upright or Orientation6.UprightRotated => OrientationCategory.Upright,
        Orientation6.Side or Orientation6.SideRotated => OrientationCategory.Side,
        Orientation6.Lie or Orientation6.LieRotated => OrientationCategory.Lie,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>旋转对应方向 / Rotated counterpart.</summary>
    public static Orientation6 Rotation(this Orientation6 orientation) => orientation switch {
        Orientation6.Upright => Orientation6.UprightRotated,
        Orientation6.UprightRotated => Orientation6.Upright,
        Orientation6.Side => Orientation6.SideRotated,
        Orientation6.SideRotated => Orientation6.Side,
        Orientation6.Lie => Orientation6.LieRotated,
        Orientation6.LieRotated => Orientation6.Lie,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>是否为旋转方向 / Whether this is a rotated orientation.</summary>
    public static bool IsRotated(this Orientation6 orientation) =>
        orientation is Orientation6.UprightRotated or Orientation6.SideRotated or Orientation6.LieRotated;

    /// <summary>转换为基础三方向 / Convert to base three-direction.</summary>
    public static Orientation ToBaseOrientation(this Orientation6 orientation) => orientation switch {
        Orientation6.Upright or Orientation6.UprightRotated => Orientation.Upright,
        Orientation6.Side or Orientation6.SideRotated => Orientation.Side,
        Orientation6.Lie or Orientation6.LieRotated => Orientation.Lie,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>获取指定方向下的宽度 / Get width under this orientation.</summary>
    public static Quantity<V> Width<V>(this Orientation6 orientation, Quantity<V> width, Quantity<V> height, Quantity<V> depth)
        where V : struct, IFloatingNumber<V> => orientation switch {
        Orientation6.Upright => width,
        Orientation6.UprightRotated => depth,
        Orientation6.Side => height,
        Orientation6.SideRotated => depth,
        Orientation6.Lie => width,
        Orientation6.LieRotated => height,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>获取指定方向下的高度 / Get height under this orientation.</summary>
    public static Quantity<V> Height<V>(this Orientation6 orientation, Quantity<V> width, Quantity<V> height, Quantity<V> depth)
        where V : struct, IFloatingNumber<V> => orientation switch {
        Orientation6.Upright => height,
        Orientation6.UprightRotated => height,
        Orientation6.Side => width,
        Orientation6.SideRotated => width,
        Orientation6.Lie => depth,
        Orientation6.LieRotated => depth,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>获取指定方向下的深度 / Get depth under this orientation.</summary>
    public static Quantity<V> Depth<V>(this Orientation6 orientation, Quantity<V> width, Quantity<V> height, Quantity<V> depth)
        where V : struct, IFloatingNumber<V> => orientation switch {
        Orientation6.Upright => depth,
        Orientation6.UprightRotated => width,
        Orientation6.Side => depth,
        Orientation6.SideRotated => height,
        Orientation6.Lie => height,
        Orientation6.LieRotated => width,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };
}
