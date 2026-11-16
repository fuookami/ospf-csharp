#nullable enable

using System;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.Demand;

/// <summary>
/// 需求量的量纲域 / The quantity domain of a demand.
/// </summary>
public enum Bpp2dDemandDomain
{
    /// <summary>离散（件数）/ Discrete (piece count).</summary>
    Discrete,

    /// <summary>连续（面积）/ Continuous (area).</summary>
    Continuous,
}

/// <summary>
/// 需求模式 / The demand mode.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// 与的 `Bpp2dDemandMode` 对齐。六种模式的差别是**按什么聚合**：
/// 按件数、按面积、按物料，以及"物料 × 件数/面积"三种组合。**它们不是同一件事的别名**——同一批货物按件数聚合与按面积聚合
/// 得到的合计不同，混用会让需求约束的方向都错。
///
/// Aligned with its `Bpp2dDemandMode`. What separates the six is **what gets aggregated**: piece counts, areas, materials, and the three material-times-count/area
/// combinations. **They are not aliases of one another** — the same item batch aggregated by count and by area yields different totals, and mixing them reverses the demand constraint's
/// direction.
/// </remarks>
public abstract record Bpp2dDemandMode<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>按货物聚合（件数）/ Aggregate by item (piece count).</summary>
    /// <param name="Source">货物 / The item.</param>
    public sealed record Item<W>(IAbstractItem<W> Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>按货物聚合（件数，显式）/ Aggregate by item with an explicit count.</summary>
    /// <param name="Source">货物 / The item.</param>
    public sealed record ItemAmount<W>(IAbstractItem<W> Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>按货物聚合（面积）/ Aggregate by item (area).</summary>
    /// <param name="Source">货物 / The item.</param>
    public sealed record ItemArea<W>(IAbstractItem<W> Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>按物料聚合（件数）/ Aggregate by material (piece count).</summary>
    /// <param name="Source">物料 / The material.</param>
    public sealed record Material<W>(MaterialKey Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>按物料聚合（零件数）/ Aggregate by material (piece count).</summary>
    /// <param name="Source">物料 / The material.</param>
    public sealed record ItemMaterialAmount<W>(MaterialKey Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>按物料聚合（面积）/ Aggregate by material (area).</summary>
    /// <param name="Source">物料 / The material.</param>
    public sealed record ItemMaterialArea<W>(MaterialKey Source) : Bpp2dDemandMode<W>
        where W : struct, IFloatingNumber<W>;
}

/// <summary>
/// 需求的聚合键 / The demand's aggregation key.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public abstract record Bpp2dDemandKey<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>货物键 / The item key.</summary>
    /// <param name="Source">货物 / The item.</param>
    public sealed record Item<W>(IAbstractItem<W> Source) : Bpp2dDemandKey<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>物料键 / The material key.</summary>
    /// <param name="Source">物料 / The material.</param>
    public sealed record Material<W>(MaterialKey Source) : Bpp2dDemandKey<W>
        where W : struct, IFloatingNumber<W>;
}

/// <summary>
/// 需求量 / The demand value.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **两种值互不相容**：`Amount` 是整数件、`Area` 是带量纲的面积。合并两者是错误而不是"取较大者"，
/// 见 <see cref="MergeDemandValue"/>。
///
/// **The two values are incompatible**: `Amount` is an integer count and `Area` carries a quantity. Merging them is an error rather than "take the larger one"; see
/// <see cref="MergeDemandValue"/>.
/// </remarks>
public abstract record Bpp2dDemandValue<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>件数 / Piece count.</summary>
    /// <param name="Value">件数 / The count.</param>
    public sealed record Amount<W>(ulong Value) : Bpp2dDemandValue<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>面积 / Area.</summary>
    /// <param name="Value">面积 / The area.</param>
    public sealed record Area<W>(Quantity<W> Value) : Bpp2dDemandValue<W>
        where W : struct, IFloatingNumber<W>;

    /// <summary>
    /// 合并两个需求量 / Merge two demand values.
    /// </summary>
    /// <param name="lhs">左 / The left one.</param>
    /// <param name="rhs">右 / The right one.</param>
    /// <returns>合并后的值，或类型不兼容的失败 / The merged value, or a failure on a type clash.</returns>
    /// <remarks>
    /// **同类才合并**：两个 `Amount` 相加，两个 `Area` 相加（量纲不一致时由 `Add` 报错），**跨类型直接失败**。
    /// 把 `Amount` 与 `Area` 之一静默换算掉，会让需求合计凭空多出一个"换算率"。
    ///
    /// **Only like merges**: two `Amount`s add, two `Area`s add (a unit mismatch is reported by `Add`), and **cross-type merging fails outright**. Silently converting one of `Amount`
    /// or `Area` would invent a conversion rate out of nowhere in the demand total.
    /// </remarks>
    public static Result<Bpp2dDemandValue<V>, ErrorCode, Error<ErrorCode>> MergeDemandValue(
        Bpp2dDemandValue<V> lhs,
        Bpp2dDemandValue<V> rhs)
    {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        if (lhs is Amount<V> leftAmount && rhs is Amount<V> rightAmount)
        {
            return Results.Ok<Bpp2dDemandValue<V>>(
                new Amount<V>(leftAmount.Value + rightAmount.Value));
        }
        if (lhs is Area<V> leftArea && rhs is Area<V> rightArea)
        {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> sum = leftArea.Value.Add(rightArea.Value);
            if (sum is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> ok)
            {
                return Results.Ok<Bpp2dDemandValue<V>>(new Area<V>(ok.Value));
            }
            if (sum is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failed)
            {
                return Results.Failed<Bpp2dDemandValue<V>>(failed.Error);
            }
        }
        return Results.Failed<Bpp2dDemandValue<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "需求值类型不兼容 / Incompatible demand values"));
    }

    /// <summary>
    /// 缩放需求量 / Scale a demand value.
    /// </summary>
    /// <param name="value">原值 / The value.</param>
    /// <param name="multiplier">倍数 / The multiplier.</param>
    /// <returns>缩放后的值 / The scaled value.</returns>
    public static Bpp2dDemandValue<V> ScaleDemandValue(Bpp2dDemandValue<V> value, ulong multiplier)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            Amount<V> amount => new Amount<V>(amount.Value * multiplier),
            Area<V> area => new Area<V>(ScaleArea(area.Value, multiplier)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unknown demand value"),
        };
    }

    /// <summary>
    /// 逐次累加得到缩放后的面积 / Accumulate the scaled area.
    /// </summary>
    /// <param name="value">面积 / The area.</param>
    /// <param name="multiplier">倍数 / The multiplier.</param>
    /// <returns>缩放后的面积 / The scaled area.</returns>
    /// <remarks>
    /// `V` 只保证 `IFloatingNumber`，**加号与乘号都没有泛型版本**（`FltX` 才有 `operator +`），所以整数倍只能用
    /// `Quantity.Add` 累加得到。需求倍数是几十的量级，`O(n)` 次累加够用。
    ///
    /// ponytail: 累加而非乘法，`O(n)`；倍数上到上万时再给 `IFloatingNumber` 补一个泛型标量乘。
    ///
    /// `V` only guarantees `IFloatingNumber`, and **neither addition nor multiplication has a generic form** (`FltX` alone defines `operator +`), so an integer multiple is accumulated
    /// through `Quantity.Add`. Demand multipliers are in the tens, so an `O(n)` accumulation is enough.
    ///
    /// ponytail: accumulation instead of multiplication, `O(n)`; add a generic scalar multiply to `IFloatingNumber` once multipliers reach the tens of thousands.
    /// </remarks>
    private static Quantity<V> ScaleArea(Quantity<V> value, ulong multiplier)
    {
        INumericConstants<V> constants = NumericConstantsRegistry.For<V>();
        Quantity<V> scaled = new(constants.Zero, value.Unit);
        for (ulong index = 0UL; index < multiplier; ++index)
        {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> added = scaled.Add(value);
            if (added is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> ok)
            {
                scaled = ok.Value;
            }
        }
        return scaled;
    }
}

/// <summary>
/// 一条需求量条目 / One demand entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Mode">模式 / The mode.</param>
/// <param name="Key">聚合键 / The key.</param>
/// <param name="Demand">需求量 / The demand.</param>
/// <param name="QuantityDomain">量纲域，默认离散 / The quantity domain, discrete by default.</param>
public sealed record Bpp2dDemandEntry<V>(
    Bpp2dDemandMode<V> Mode,
    Bpp2dDemandKey<V> Key,
    Bpp2dDemandValue<V> Demand,
    Bpp2dDemandDomain QuantityDomain = Bpp2dDemandDomain.Discrete)
    where V : struct, IFloatingNumber<V>;
