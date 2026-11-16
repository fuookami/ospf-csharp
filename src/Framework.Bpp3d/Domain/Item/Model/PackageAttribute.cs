#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
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
    UInt64 PackageMaxLayer,
    UInt64 MaxHeight,
    UInt64 MinDepth,
    UInt64 MaxDepth,
    bool BottomOnly = false,
    bool TopFlat = true,
    bool EnabledSideOnTop = false,
    bool EnabledLieOnTop = false) {
    /// <summary>货物属性（可为空）/ The cargo attribute, possibly absent.</summary>
    public AbstractCargoAttribute? CargoAttribute { get; init; }

    /// <summary>重量属性：与包材层数取小，得到有效层数 / The weight attribute: combined with the package layer count by minimum.</summary>
    public AbstractWeightAttribute? WeightAttribute { get; init; }

    /// <summary>变形属性 / The deformation attribute.</summary>
    public AbstractDeformationAttribute? DeformationAttribute { get; init; }

    /// <summary>悬挂策略 / The hanging policy.</summary>
    public AbstractHangingPolicy? HangingPolicy { get; init; }

    /// <summary>堆叠策略 / The stacking-on policy.</summary>
    public AbstractStackingOnPolicy? StackingOnPolicy { get; init; }

    /// <summary>
    /// 有效堆叠层数 = min(包材层数, 重量属性层数) / The effective layer count: the minimum of the package and the weight attribute.
    /// </summary>
    /// <remarks>
    /// 合成规则是 `maxLayer = min(packageMaxLayer, weightAttribute.maxLayer)`。
    /// 原来的位置参数就叫 `MaxLayer`，那会让"包材层数"与"有效层数"共用一个名字，于是这条合成规则没有地方落脚——
    /// 位置参数因此改名 `PackageMaxLayer`，有效层数沿用 `MaxLayer`。没配重量属性时两者相等，既有调用方行为不变。
    ///
    /// The composition is `maxLayer = min(packageMaxLayer, weightAttribute.maxLayer)`. The positional parameter used to be called
    /// `MaxLayer`, which left the package count and the effective count sharing one name so the composition had nowhere to live — hence the rename.
    /// With no weight attribute configured the two are equal, so existing callers behave exactly as before.
    /// </remarks>
    public UInt64 MaxLayer => WeightAttribute is null || WeightAttribute.MaxLayer.CompareTo(PackageMaxLayer) > 0
        ? PackageMaxLayer
        : WeightAttribute.MaxLayer;

    /// <summary>
    /// 可以堆在上方的包装类型 / The package types allowed to sit on top.
    /// </summary>
    /// <remarks>
    /// 的 `overPackageTypes`，缺省是全部类型。
    /// 堆叠策略靠它做第一道判断，所以补齐属性模型时必须一起补上。
    ///
    /// its `overPackageTypes`, defaulting to every type. The stacking policies use it as their first gate, so completing the
    /// attribute model means adding it here too.
    /// </remarks>
    public IReadOnlySet<PackageType> OverPackageTypes { get; init; }
        = new HashSet<PackageType>((PackageType[])System.Enum.GetValues(typeof(PackageType)));

    /// <summary>包装类别 / Package category.</summary>
    public PackageCategory Category => PackageType switch {
        PackageType.Box or PackageType.Pallet => PackageCategory.Standard,
        PackageType.Barrel or PackageType.Roll => PackageCategory.Cylindrical,
        _ => PackageCategory.Irregular
    };
}
