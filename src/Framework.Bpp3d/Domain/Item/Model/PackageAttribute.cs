#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
/// <summary>
/// 包装类型 / Package type.
/// </summary>
public enum PackageType {
    /// <summary>箱 / Box</summary>
    Box,
    /// <summary>袋 / Bag</summary>
    Bag,
    /// <summary>桶 / Barrel</summary>
    Barrel,
    /// <summary>捆 / Bundle</summary>
    Bundle,
    /// <summary>托盘 / Pallet</summary>
    Pallet,
    /// <summary>卷 / Roll</summary>
    Roll
}

/// <summary>
/// 包装类别 / Package category.
/// </summary>
public enum PackageCategory {
    Standard,
    Irregular,
    Cylindrical
}

/// <summary>
/// 包装属性 / Package attribute.
/// </summary>
public sealed record PackageAttribute(
    PackageType PackageType,
    UInt64 MaxLayer,
    UInt64 MaxHeight,
    UInt64 MinDepth,
    UInt64 MaxDepth,
    bool BottomOnly = false,
    bool TopFlat = true,
    bool EnabledSideOnTop = false,
    bool EnabledLieOnTop = false) {
    /// <summary>包装类别 / Package category.</summary>
    public PackageCategory Category => PackageType switch {
        PackageType.Box or PackageType.Pallet => PackageCategory.Standard,
        PackageType.Barrel or PackageType.Roll => PackageCategory.Cylindrical,
        _ => PackageCategory.Irregular
    };
}
