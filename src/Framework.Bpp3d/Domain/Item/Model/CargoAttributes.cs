#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 货物属性模型：堆叠、悬挂、变形、重量 / The cargo attribute model: stacking, hanging, deformation, weight.
/// </summary>
/// <remarks>
/// B1305 补齐基线 `PackageAttribute.kt` 里的 12 个类型。 复审（B1297）发现它们在 C# 侧一个都没有，
/// 而三份文档也从未提到 `PackageAttribute`——所以这不是"待办"，是"从未被记录"。
///
/// 一句话说明这次补齐的边界： 这些类型在基线里只被 `PackageAttribute.kt` 自身读取，没有任何生产判定依赖它们
/// （B1297 实测：`MaterialPacker.kt` 只在构造属性时填默认值）。 所以补进 C# 之后，算法行为不变——
/// 判据 `CargoAttributeModelTests` 钉住这一点：十二个类型都在，而 `src/` 里没有生产代码构造它们。
///
/// B1305 fills in the twelve types from the baseline's `PackageAttribute.kt`. The review (B1297) found none of them on the C# side, and none of
/// the three documents ever mentioned `PackageAttribute` — so this is not "pending", it was never recorded. One sentence on the boundary: in the
/// baseline these types are read only inside `PackageAttribute.kt` itself and no production decision depends on them (B1297 measured that
/// `MaterialPacker.kt` merely stamps default values while constructing an attribute). So porting them changes no algorithm, and a criterion pins
/// exactly that: all twelve types exist while no production code in `src/` constructs them.
/// </remarks>
public interface ICargoAttributeMarker {
}

/// <summary>货物属性接口 / The cargo attribute marker interface.</summary>
public interface AbstractCargoAttribute : ICargoAttributeMarker {
}

/// <summary>策略比较用的货物视图 / The cargo view the policies compare against.</summary>
/// <remarks>
/// 基线直接用 `ItemView`。 这里收窄成"策略真正读到的那些成员"，有两个好处： `ActualItem` 只要实现这几个属性，
/// 而判据可以造一个最小替身来验证策略逻辑，不必构造整个物品模型。
///
/// The baseline uses `ItemView` directly. Narrowing to the members the policies actually read has two benefits: `ActualItem` only has to
/// expose these, and a criterion can build a minimal double to exercise the policy logic without constructing the whole item model.
/// </remarks>
public interface ICargoView {
    /// <summary>包装类型 / Package type.</summary>
    PackageType PackageType { get; }
    /// <summary>包装类别 / Package category.</summary>
    PackageCategory PackageCategory { get; }
    /// <summary>宽 / Width.</summary>
    Quantity<FltX> Width { get; }
    /// <summary>深 / Depth.</summary>
    Quantity<FltX> Depth { get; }
    /// <summary>高 / Height.</summary>
    Quantity<FltX> Height { get; }
    /// <summary>重量 / Weight.</summary>
    Quantity<FltX> Weight { get; }
    /// <summary>允许堆叠层数 / Allowed layer count.</summary>
    UInt64 MaxLayer { get; }
    /// <summary>
    /// 允许的最大高度（长度）；未知时为 null / The allowed maximum height as a length, or null when unknown.
    /// </summary>
    /// <remarks>
    /// B1305：基线把 `maxHeight` 存成 `Quantity&lt;FltX&gt;`（长度），而 C# 现有物品模型里 `MaxHeight` 是 `UInt64` 的层数。
    /// 这是真实的语义差异，不能靠换算糊过去：把层数当长度用会让高度门限静默失效。
    /// 所以这里如实做成"未知即 null"，策略遇到未知高度一律判否。
    ///
    /// B1305: the baseline stores `maxHeight` as a length (`Quantity&lt;FltX&gt;`), while the current C# item model keeps `MaxHeight` as a `UInt64` layer
    /// count. That is a real semantic difference and cannot be papered over by conversion: treating a layer count as a length would let the height
    /// gate silently pass. So it is modelled honestly as "unknown means null", and the policies refuse whenever the height is unknown.
    /// </remarks>
    Quantity<FltX>? MaxHeightLength { get; }
    /// <summary>顶面是否平整 / Whether the top is flat.</summary>
    bool TopFlat { get; }
    /// <summary>可以堆在上方的包装类型 / Package types that may sit on top.</summary>
    IReadOnlySet<PackageType> OverPackageTypes { get; }
    /// <summary>底部投影面积；无投影时为 null / Bottom footprint area, or null when there is none.</summary>
    Quantity<FltX>? BottomFootprintArea { get; }
    /// <summary>底部投影的最小跨度；无投影时为 null / The minimum span of the bottom footprint, or null.</summary>
    Quantity<FltX>? BottomFootprintMinSpan { get; }
}

/// <summary>堆叠策略接口 / The stacking-on policy interface.</summary>
public interface AbstractStackingOnPolicy {
    /// <summary>
    /// 判断 `item` 能否堆在 `bottomItem` 上。 先查"允许上覆的包装类型"，再查额外规则，最后查超重、层数与高度。
    ///
    /// Decide whether `item` may be stacked on `bottomItem`: the allowed over-package types first, then the extra rule, then overweight, layer
    /// count and height.
    /// </summary>
    /// <param name="item">待堆叠的货物 / The item to stack.</param>
    /// <param name="bottomItem">底部货物 / The bottom item.</param>
    /// <param name="layer">当前层数 / The current layer.</param>
    /// <param name="height">当前高度 / The current height.</param>
    /// <returns>是否可以堆叠 / Whether stacking is allowed.</returns>
    bool EnabledStackingOn(ICargoView item, ICargoView bottomItem, UInt64 layer, Quantity<FltX> height);
}

/// <summary>
/// 过滤式堆叠策略：只做"允许上覆类型 + 额外规则 + 超重 + 层数/高度"四道判断 /
/// Filter stacking: allowed over-package types, an extra rule, overweight, then layer count and height.
/// </summary>
/// <param name="MaxOverWeight">最大允许超重 / Maximum allowed overweight.</param>
/// <param name="ExtraStackingOnRule">额外堆叠规则 / An extra stacking rule.</param>
public sealed record FilterStackingOnPolicy(
    FltX? MaxOverWeight = null,
    Func<ICargoView, ICargoView, bool>? ExtraStackingOnRule = null) : AbstractStackingOnPolicy {

    /// <summary>实际使用的最大允许超重（参数为 null 时取 10.0）/ The effective overweight; ten when the parameter is null.</summary>
    public FltX EffectiveMaxOverWeight => MaxOverWeight ?? new FltX(10.0);

    /// <inheritdoc/>
    public bool EnabledStackingOn(ICargoView item, ICargoView bottomItem, UInt64 layer, Quantity<FltX> height) {
        if (!bottomItem.OverPackageTypes.Contains(item.PackageType)) {
            return false;
        }
        if (ExtraStackingOnRule is not null && !ExtraStackingOnRule(item, bottomItem)) {
            return false;
        }
        if (!CargoComparisons.NotGreater(
                CargoComparisons.Between(item.Weight, bottomItem.Weight),
                CargoComparisons.Scaled(EffectiveMaxOverWeight, CargoComparisons.UnitOf(item.Weight)))) {
            return false;
        }
        if (item.MaxHeightLength is not { } maxHeight) {
            return false;
        }
        return CargoComparisons.Below(layer, item.MaxLayer) && CargoComparisons.NotGreater(
            CargoComparisons.HeightSum(height, item.Height),
            maxHeight);
    }
}

/// <summary>
/// 纸箱容器堆叠策略：额外要求两个软箱之间的底面积差不超过阈值 /
/// Carton-container stacking: additionally requires two soft boxes to differ in base size by no more than a threshold.
/// </summary>
/// <param name="MaxDifference">最大允许尺寸差 / Maximum allowed size difference.</param>
/// <param name="MaxOverWeight">最大允许超重 / Maximum allowed overweight.</param>
/// <param name="ExtraStackingOnRule">额外堆叠规则 / An extra stacking rule.</param>
public sealed record CartonContainerStackingOnPolicy(
    FltX MaxDifference,
    FltX? MaxOverWeight = null,
    Func<ICargoView, ICargoView, bool>? ExtraStackingOnRule = null) : AbstractStackingOnPolicy {

    /// <summary>实际使用的最大允许超重（参数为 null 时取 10.0）/ The effective overweight; ten when the parameter is null.</summary>
    public FltX EffectiveMaxOverWeight => MaxOverWeight ?? new FltX(10.0);

    /// <inheritdoc/>
    public bool EnabledStackingOn(ICargoView item, ICargoView bottomItem, UInt64 layer, Quantity<FltX> height) {
        if (!bottomItem.OverPackageTypes.Contains(item.PackageType)) {
            return false;
        }
        if (ExtraStackingOnRule is not null && !ExtraStackingOnRule(item, bottomItem)) {
            return false;
        }
        if (bottomItem.PackageCategory == PackageCategory.Irregular && item.PackageCategory == PackageCategory.Irregular) {
            Quantity<FltX> difference = CargoComparisons.BaseSizeDifference(item, bottomItem);
            if (!CargoComparisons.NotGreater(difference, CargoComparisons.Scaled(MaxDifference, CargoComparisons.UnitOf(difference)))) {
                return false;
            }
        }
        if (!CargoComparisons.NotGreater(
                CargoComparisons.Between(item.Weight, bottomItem.Weight),
                CargoComparisons.Scaled(EffectiveMaxOverWeight, CargoComparisons.UnitOf(item.Weight)))) {
            return false;
        }
        if (item.MaxHeightLength is not { } maxHeight) {
            return false;
        }
        return CargoComparisons.Below(layer, item.MaxLayer) && CargoComparisons.NotGreater(
            CargoComparisons.HeightSum(height, item.Height),
            maxHeight);
    }
}

/// <summary>
/// 箱式堆叠策略：托盘上方必须平顶，且两侧的宽深差之和不超过阈值 /
/// Box stacking: a pallet below must be flat-topped, and the summed width/depth difference must stay under a threshold.
/// </summary>
/// <param name="MaxDifference">最大允许尺寸差 / Maximum allowed size difference.</param>
/// <param name="MaxOverWeight">最大允许超重 / Maximum allowed overweight.</param>
/// <param name="ExtraStackingOnRule">额外堆叠规则 / An extra stacking rule.</param>
public sealed record BoxStackingOnPolicy(
    FltX MaxDifference,
    FltX? MaxOverWeight = null,
    Func<ICargoView, ICargoView, bool>? ExtraStackingOnRule = null) : AbstractStackingOnPolicy {

    /// <summary>实际使用的最大允许超重（参数为 null 时取 10.0）/ The effective overweight; ten when the parameter is null.</summary>
    public FltX EffectiveMaxOverWeight => MaxOverWeight ?? new FltX(10.0);

    /// <inheritdoc/>
    public bool EnabledStackingOn(ICargoView item, ICargoView bottomItem, UInt64 layer, Quantity<FltX> height) {
        if (!bottomItem.OverPackageTypes.Contains(item.PackageType)) {
            return false;
        }
        if (ExtraStackingOnRule is not null && !ExtraStackingOnRule(item, bottomItem)) {
            return false;
        }
        if (bottomItem.PackageCategory == PackageCategory.Standard && !bottomItem.TopFlat) {
            return false;
        }
        Quantity<FltX> difference = CargoComparisons.BaseSizeDifference(item, bottomItem);
        if (!CargoComparisons.NotGreater(difference, CargoComparisons.Scaled(MaxDifference, CargoComparisons.UnitOf(difference)))) {
            return false;
        }
        if (!CargoComparisons.NotGreater(
                CargoComparisons.Between(item.Weight, bottomItem.Weight),
                CargoComparisons.Scaled(EffectiveMaxOverWeight, CargoComparisons.UnitOf(item.Weight)))) {
            return false;
        }
        if (item.MaxHeightLength is not { } maxHeight) {
            return false;
        }
        return CargoComparisons.Below(layer, item.MaxLayer) && CargoComparisons.NotGreater(
            CargoComparisons.HeightSum(height, item.Height),
            maxHeight);
    }
}

/// <summary>悬挂策略接口 / The hanging policy interface.</summary>
public interface AbstractHangingPolicy {
    /// <summary>
    /// 判断 `item` 能否挂在 `bottomSupport` 上。 基线比的是"投影面积减去支撑面积"与一个允许的悬挂面积。
    ///
    /// Decide whether `item` may hang over `bottomSupport`: the baseline compares the footprint area minus the supported area against an allowed
    /// hanging area.
    /// </summary>
    /// <param name="item">待放置的货物 / The item being placed.</param>
    /// <param name="bottomSupport">底部支撑 / The bottom support.</param>
    /// <returns>是否允许 / Whether it is allowed.</returns>
    bool EnabledStackingOn(ICargoView item, BottomSupport bottomSupport);
}

/// <summary>
/// 相对悬挂策略：允许的悬挂面积是投影面积的一个比例 /
/// Relative hanging: the allowed hanging area is a fraction of the footprint area.
/// </summary>
/// <param name="HangingPercentage">允许的悬挂比例 / The allowed hanging fraction.</param>
/// <param name="WithWeight">是否考虑重量 / Whether weight is considered.</param>
public sealed record RelativeHangingPolicy(
    FltX HangingPercentage,
    bool WithWeight = true) : AbstractHangingPolicy {

    /// <inheritdoc/>
    public bool EnabledStackingOn(ICargoView item, BottomSupport bottomSupport) {
        if (WithWeight && CargoComparisons.Greater(item.Weight, bottomSupport.Weight)) {
            return false;
        }
        if (item.BottomFootprintArea is not { } footprintArea) {
            return false;
        }
        Quantity<FltX> maxHangingArea = CargoComparisons.Scaled(HangingPercentage, footprintArea);
        Quantity<FltX> hangingArea = CargoComparisons.Difference(footprintArea, bottomSupport.Area);
        return CargoComparisons.NotGreater(hangingArea, maxHangingArea);
    }
}

/// <summary>
/// 绝对悬挂策略：允许的悬挂面积是"最大差值 × 投影最小跨度" /
/// Absolute hanging: the allowed hanging area is "maximum difference times the minimum footprint span".
/// </summary>
/// <param name="MaxDifference">最大允许差值 / The maximum allowed difference.</param>
/// <param name="WithWeight">是否考虑重量 / Whether weight is considered.</param>
public sealed record AbsoluteHangingPolicy(
    FltX MaxDifference,
    bool WithWeight = true) : AbstractHangingPolicy {

    /// <inheritdoc/>
    public bool EnabledStackingOn(ICargoView item, BottomSupport bottomSupport) {
        if (WithWeight && CargoComparisons.Greater(item.Weight, bottomSupport.Weight)) {
            return false;
        }
        if (item.BottomFootprintArea is not { } footprintArea) {
            return false;
        }
        if (item.BottomFootprintMinSpan is not { } minSpan) {
            return false;
        }
        Quantity<FltX> maxDifferenceSpan = CargoComparisons.Scaled(MaxDifference, CargoComparisons.UnitOf(minSpan));
        Quantity<FltX> maxHangingArea = maxDifferenceSpan.Multiply(minSpan);
        Quantity<FltX> hangingArea = CargoComparisons.Difference(footprintArea, bottomSupport.Area);
        return CargoComparisons.NotGreater(hangingArea, maxHangingArea);
    }
}

/// <summary>变形属性接口 / The deformation attribute interface.</summary>
public interface AbstractDeformationAttribute {
    /// <summary>按体积计算三维变形量 / Compute the three-axis deformation from a volume.</summary>
    /// <param name="volume">体积 / The volume.</param>
    /// <returns>三维变形向量 / The three-axis deformation vector.</returns>
    Vector<Dim3, FltX> DeformationQuantity(FltX volume);

    /// <summary>按货物视图计算三维变形量 / Compute the deformation from a cargo view.</summary>
    /// <param name="item">货物视图 / The cargo view.</param>
    /// <returns>三维变形向量 / The three-axis deformation vector.</returns>
    Vector<Dim3, FltX> DeformationQuantity(ICargoView item);
}

/// <summary>线性变形属性：三个方向的变形量都等于"体积 × 变形系数" / Linear deformation: each axis deforms by volume times the coefficient.</summary>
/// <param name="DeformationCoefficient">变形系数 / The deformation coefficient.</param>
public sealed record LinearDeformationAttribute(FltX DeformationCoefficient) : AbstractDeformationAttribute {
    /// <inheritdoc/>
    public Vector<Dim3, FltX> DeformationQuantity(FltX volume) {
        FltX deformed = (volume * DeformationCoefficient)!;
        return Vector<Dim3, FltX>.Create(deformed, deformed, deformed);
    }

    /// <inheritdoc/>
    public Vector<Dim3, FltX> DeformationQuantity(ICargoView item) =>
        DeformationQuantity(CargoComparisons.Volume(item));
}

/// <summary>重量属性接口：只定义"最大层数" / The weight attribute interface: it defines only the maximum layer count.</summary>
public interface AbstractWeightAttribute {
    /// <summary>最大堆叠层数 / The maximum layer count.</summary>
    UInt64 MaxLayer { get; }
}

/// <summary>重量属性 / A weight attribute.</summary>
/// <param name="MaxLayer">最大堆叠层数，缺省为最大值 / The maximum layer count, unbounded by default.</param>
public sealed record WeightAttribute(UInt64 MaxLayer) : AbstractWeightAttribute {
    /// <summary>无穷层（不额外限制）/ Unbounded layers (no extra limit).</summary>
    public static WeightAttribute Unbounded => new(new UInt64(ulong.MaxValue));
}

/// <summary>策略里用到的数量比较 / The quantity comparisons the policies need.</summary>
/// <remarks>
/// C# 的 `Quantity` 算术返回 `Result`（单位不匹配会失败），而基线的 `gr`／`leq` 直接给出布尔。 这里把两者对齐成
/// "单位不匹配即判否"： 一个算不出来的比较不能当作"允许堆叠"。 这是本批唯一的行为选择，写在这里而不是散在四个策略里。
///
/// C# `Quantity` arithmetic returns `Result` (a unit mismatch fails) while the baseline's `gr`/`leq` yield a boolean. The bridge here is "a unit
/// mismatch decides false": a comparison that cannot be computed must never count as "stacking allowed". This is the batch's only behavioural
/// choice, and it lives here instead of being scattered across four policies.
/// </remarks>
internal static class CargoComparisons {
    internal static bool Greater(Quantity<FltX> left, Quantity<FltX> right) =>
        Compare(left, right) is { } sign && sign > 0;

    internal static bool NotGreater(Quantity<FltX> left, Quantity<FltX> right) =>
        Compare(left, right) is { } sign && sign <= 0;

    internal static bool Below(UInt64 layer, UInt64 limit) => layer.CompareTo(limit) < 0;

    internal static Quantity<FltX> Between(Quantity<FltX> left, Quantity<FltX> right) => Difference(left, right);

    internal static Quantity<FltX> HeightSum(Quantity<FltX> height, Quantity<FltX> itemHeight) =>
        Of(Value(height) + Value(itemHeight), height.Unit);

    internal static Quantity<FltX> BaseSizeDifference(ICargoView item, ICargoView bottomItem) {
        double widths = System.Math.Abs(Value(item.Width) - Value(bottomItem.Width));
        double depths = System.Math.Abs(Value(item.Depth) - Value(bottomItem.Depth));
        return Of(widths + depths, item.Width.Unit);
    }

    internal static Quantity<FltX> Difference(Quantity<FltX> left, Quantity<FltX> right) =>
        Of(Value(left) - Value(right), left.Unit);

    /// <summary>"一个该单位"的数量——基线里 `x * weight.unit` 的写法 / One unit of the given quantity's unit.</summary>
    /// <param name="quantity">取单位的数量 / The quantity whose unit is taken.</param>
    /// <returns>值为 1、单位相同的数量 / A quantity of value one in the same unit.</returns>
    internal static Quantity<FltX> UnitOf(Quantity<FltX> quantity) => new(new FltX(1m), quantity.Unit);

    internal static Quantity<FltX> Scaled(FltX factor, Quantity<FltX> quantity) =>
        Of(Value(quantity) * Value(factor), quantity.Unit);

    internal static FltX Volume(ICargoView item) =>
        item.Width.Value * item.Depth.Value * item.Height.Value;

    internal static Quantity<FltX> Abs(Quantity<FltX> quantity) =>
        Of(System.Math.Abs(Value(quantity)), quantity.Unit);

    private static double Value(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    private static double Value(FltX value) => value.ToFlt64().ToDouble();

    private static Quantity<FltX> Of(double value, PhysicalUnit unit) =>
        new(new Flt64(value).ToFltX(), unit);

    private static int? Compare(Quantity<FltX> left, Quantity<FltX> right) {
        // **单位不同就先换算；换不出来（量纲不匹配）返回 null，让上层判否——**算不出来的比较不能当作"允许堆叠"。**
        // **Convert first when units differ; a failed conversion returns null so the caller refuses — a comparison that cannot be computed must
        // never count as "stacking allowed".**
        if (left.Unit != right.Unit) {
            Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> converted = left.To(right.Unit);
            if (converted is not Ok<Quantity<FltX>, ErrorCode, Error<ErrorCode>> ok) {
                return null;
            }
            return ok.Value.Value.ToFlt64().CompareTo(right.Value.ToFlt64());
        }
        return left.Value.ToFlt64().CompareTo(right.Value.ToFlt64());
    }
}