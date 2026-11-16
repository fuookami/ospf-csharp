// 层生成请求与圆形网格生成 / Layer generation request and circle grid generation.
// 与固定 Kotlin 与
//（259 行）对应。
// And.
//
// 这是 `layer_generation` 域的此前：物料与层模型在 earlier／83 落地后，请求与圆形网格生成器已无未实现依赖。
//（597 行）仍需后续批次。
// This is the first batch of the `layer_generation` domain: with the material and layer models landed in earlier/83,
// the request and the circle grid generator have no unimplemented dependencies left. (597 lines)
// still needs later batches.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 需求模式键 / Demand mode key.
/// </summary>
/// <param name="Mode">需求模式 / Demand mode.</param>
/// <param name="Key">需求键 / Demand key.</param>
/// <param name="QuantityUnit">量纲单位 / Quantity unit.</param>
public sealed record DemandModeKey(string Mode, string Key, PhysicalUnit? QuantityUnit = null);

/// <summary>
/// 层生成需求项 / Layer generation demand entry.
/// </summary>
/// <param name="Mode">需求模式 / Demand mode.</param>
/// <param name="Key">需求键 / Demand key.</param>
/// <param name="QuantityUnit">量纲单位 / Quantity unit.</param>
public sealed record LayerGenerationDemandEntry(
    string Mode,
    string Key,
    PhysicalUnit? QuantityUnit = null);

/// <summary>
/// 层生成请求 / Layer generation request.
/// </summary>
/// <remarks>
/// `Duration` 表示时间限制，C# 对应 <see cref="TimeSpan"/>；`scoreByShadowPrice` 是可空委托，对应可空的
/// <see cref="Func{T1, T2, TResult}"/>。二者都不是降级——它们表达的是同一件事。
/// The code uses `Duration` for the time limit, mapped to <see cref="TimeSpan"/> here; `scoreByShadowPrice` is a
/// nullable delegate, mapped to a nullable <see cref="Func{T1, T2, TResult}"/>. Neither is a downgrade — they express
/// the same thing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Iteration">当前迭代 / Current iteration.</param>
/// <param name="Sheet">主板材类型 / Primary sheet type.</param>
/// <param name="Sheets">参与候选生成的全部板材类型；为空时退化为仅 <paramref name="Sheet"/> / All sheet types taking
/// part in candidate generation; empty means only <paramref name="Sheet"/>.</param>
/// <param name="Items">参与排样的物品与件数 / Items and amounts taking part in the layout.</param>
/// <param name="ExistingLayers">已有层，供历史生成器回放 / Existing layers replayed by the historical generator.</param>
/// <param name="DemandEntries">需求项 / Demand entries.</param>
/// <param name="ShadowPrices">影子价格 / Shadow prices.</param>
/// <param name="ScoreByShadowPrice">影子价格评分函数 / Shadow-price scoring function.</param>
/// <param name="Kerf">刀缝间距，null 表示无刀缝 / Kerf spacing, null meaning no kerf.</param>
/// <param name="TimeLimit">时间限制 / Time limit.</param>
/// <param name="MaxCandidates">最大候选数 / Maximum candidate count.</param>
public sealed record Bpp2dLayerGenerationRequest<V>(
    int Iteration,
    SheetType<V>? Sheet = null,
    IReadOnlyList<SheetType<V>>? Sheets = null,
    IReadOnlyList<(ActualItem<V> Item, int Amount)>? Items = null,
    IReadOnlyList<Layer<V>>? ExistingLayers = null,
    IReadOnlyList<LayerGenerationDemandEntry>? DemandEntries = null,
    IReadOnlyDictionary<DemandModeKey, V>? ShadowPrices = null,
    Func<Layer<V>, Bpp2dLayerGenerationRequest<V>, V>? ScoreByShadowPrice = null,
    Quantity<V>? Kerf = null,
    TimeSpan? TimeLimit = null,
    int MaxCandidates = 256) where V : struct, IFloatingNumber<V> {
    /// <summary>参与候选生成的全部板材类型 / All sheet types taking part.</summary>
    public IReadOnlyList<SheetType<V>> Participants =>
        Sheets is { Count: > 0 }
            ? Sheets
            : Sheet is null
                ? Array.Empty<SheetType<V>>()
                : new[] { Sheet };

    /// <summary>参与排样的物品与件数，永不为 null / Items and amounts, never null.</summary>
    public IReadOnlyList<(ActualItem<V> Item, int Amount)> ItemList =>
        Items ?? Array.Empty<(ActualItem<V>, int)>();

    /// <summary>已有层，永不为 null / Existing layers, never null.</summary>
    public IReadOnlyList<Layer<V>> LayerList => ExistingLayers ?? Array.Empty<Layer<V>>();

    /// <summary>需求项，永不为 null / Demand entries, never null.</summary>
    public IReadOnlyList<LayerGenerationDemandEntry> DemandEntryList =>
        DemandEntries ?? Array.Empty<LayerGenerationDemandEntry>();

    /// <summary>影子价格，永不为 null / Shadow prices, never null.</summary>
    public IReadOnlyDictionary<DemandModeKey, V> ShadowPriceMap =>
        ShadowPrices ?? new Dictionary<DemandModeKey, V>();

    /// <summary>时间限制，缺省为零 / Time limit, zero when unset.</summary>
    public TimeSpan EffectiveTimeLimit => TimeLimit ?? TimeSpan.Zero;

    /// <summary>
    /// 判断请求中是否含圆形件 / Check whether the request contains any circle item.
    /// </summary>
    /// <returns>是否含圆形件 / Whether any circle item is present.</returns>
    public bool HasCircleItems() => ItemList.Any(entry => entry.Item.IsCircleItem());

    /// <summary>
    /// 判断请求中是否含矩形件 / Check whether the request contains any rectangle item.
    /// </summary>
    /// <returns>是否含矩形件 / Whether any rectangle item is present.</returns>
    public bool HasRectangleItems() => ItemList.Any(entry => !entry.Item.IsCircleItem());

    /// <summary>
    /// 读取请求的刀缝间距 / Read the kerf spacing of the request.
    /// </summary>
    /// <remarks>
    /// 未设置刀缝时返回与样本同单位的零间距，而不是 `null`：调用方总是持有一个形状，零值一定可以构造，让下游
    /// 少一层判空。
    /// When the kerf is unset, a zero spacing in the sample's unit is returned rather than `null`: callers always hold
    /// a shape, so a zero value can always be built, sparing downstream one null check.
    /// </remarks>
    /// <param name="sample">样本量值，用于提供单位 / Sample quantity providing the unit.</param>
    /// <returns>刀缝间距 / Kerf spacing.</returns>
    public Quantity<V> KerfSpacing(Quantity<V> sample) =>
        Kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
            ? explicitKerf
            : QuantityOps.QuantityZeroOf(sample);
}

/// <summary>
/// 层生成结果 / Layer generation result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Layer">候选层 / Candidate layer.</param>
/// <param name="ReducedCost">检验数 / Reduced cost.</param>
/// <param name="Score">评分 / Score.</param>
/// <param name="Source">生成来源 / Generation source.</param>
public sealed record Bpp2dLayerGenerationResult<V>(
    Layer<V> Layer,
    V? ReducedCost = null,
    V? Score = null,
    string Source = "") where V : struct, IFloatingNumber<V>;

/// <summary>
/// 层生成器接口 / Layer generator interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IBpp2dLayerGenerator<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 生成候选层 / Generate candidate layers.
    /// </summary>
    /// <param name="request">层生成请求 / Layer generation request.</param>
    /// <returns>候选层结果列表 / Candidate layer results.</returns>
    IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(Bpp2dLayerGenerationRequest<V> request);
}

/// <summary>
/// 圆形网格布局模式 / Circle grid layout mode.
/// </summary>
public enum CircleGridMode {
    /// <summary>直角网格，按直径铺格 / Square grid using one diameter per cell.</summary>
    SquareGrid,

    /// <summary>六角网格，行距约 `√3/2·d` 且错行 / Hexagonal grid with row pitch about `√3/2·d` and offset rows.</summary>
    HexGrid,
}

/// <summary>
/// 圆形件网格布局结果 / Circle grid layout result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Placements">放置列表 / Placement list.</param>
/// <param name="Mode">布局模式 / Layout mode.</param>
/// <param name="ItemId">物品编号 / Item identifier.</param>
public sealed record CircleGridLayout<V>(
    IReadOnlyList<Placement2<V>> Placements,
    CircleGridMode Mode,
    string ItemId) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 圆形件网格生成器 / Circle grid generator.
/// </summary>
/// <remarks>
/// 六角行距取 `26/30·d`，略大于 `√3/2 ≈ 0.866025`。取偏大的行距是**保守**的：行与行之间比理论值更远，因此不会
/// 产生重叠，同时仍比直角网格更密。这样既避免了对泛型数值类型求平方根，也保持了正确性。
///
/// **必须用标量比 `26/30` 去缩放直径量，而不是「26d / 30d」**：后者得到一个无量纲的 0.8667，只在 `d = 1` 时碰巧
/// 正确。基线注释记录了这个真实缺陷——修正前在 10×8 板材、直径 2 的圆形件上，相邻圆心距仅 1.32（小于直径 2），
/// 会实际产出重叠的圆。本实现保持标量比写法，并用测试固定该场景。
///
/// The hexagonal row pitch uses `26/30·d`, slightly larger than `√3/2`. A larger pitch is conservative: rows sit
/// farther apart than theory requires, so overlap cannot occur while density still beats the square grid, and it avoids
/// a square root on a generic numeric type.
/// **The diameter must be scaled by the scalar ratio `26/30`, not written as "26d / 30d"**: the latter yields the
/// dimensionless 0.8667 and is only accidentally correct when `d = 1`. The code records this real defect — before
/// the fix, a 10×8 sheet with diameter-2 circles had adjacent centres only 1.32 apart (below the diameter 2) and
/// produced actually overlapping circles. This implementation keeps the scalar-ratio form and pins that scenario with a test.
/// </remarks>
public static class CircleGridGenerator {
    /// <summary>六角网格最大行数，避免病态输入造成的长时间循环 / Maximum rows, guarding pathological inputs.</summary>
    private const int MaxHexRows = 10000;

    /// <summary>六角行距分子 / Hexagonal pitch numerator.</summary>
    private const int HexPitchNumerator = 26;

    /// <summary>六角行距分母（26/30 略大于 √3/2，保守且无重叠）/ Hexagonal pitch denominator.</summary>
    private const int HexPitchDenominator = 30;

    /// <summary>
    /// 为单个圆形件生成全部网格候选 / Generate every grid candidate for a single circle item.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">圆形件 / Circle item.</param>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>网格布局列表，按件数降序 / Grid layouts, ordered by descending amount.</returns>
    public static IReadOnlyList<CircleGridLayout<V>> Invoke<V>(
        ActualItem<V> item,
        SheetType<V> sheet,
        Quantity<V>? kerf = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(sheet);

        if (!item.IsCircleItem() || item.Shape is not CirclePackingShape2<V> circle) {
            return Array.Empty<CircleGridLayout<V>>();
        }
        Quantity<V> diameter = circle.Diameter;
        if (!QuantityCore.IsPositive(diameter)) {
            return Array.Empty<CircleGridLayout<V>>();
        }

        // 圆形件的刀缝同样通过节距体现：节距 = 直径 + kerf。
        // For circles the kerf is also expressed through the pitch: pitch = diameter + kerf.
        Quantity<V> spacing = kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
            ? explicitKerf
            : QuantityOps.QuantityZeroOf(diameter);

        var layouts = new List<CircleGridLayout<V>>();
        if (SquareGrid(item, circle, diameter, spacing, sheet) is { } square) {
            layouts.Add(square);
        }
        if (HexGrid(item, circle, diameter, spacing, sheet) is { } hex) {
            layouts.Add(hex);
        }
        return layouts.OrderByDescending(layout => layout.Placements.Count).ToList();
    }

    /// <summary>
    /// 生成直角网格布局 / Generate the square grid layout.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">圆形件 / Circle item.</param>
    /// <param name="circle">圆形形状 / Circle shape.</param>
    /// <param name="diameter">直径 / Diameter.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <returns>网格布局，无可行放置时返回 null / The grid layout, or null when nothing fits.</returns>
    private static CircleGridLayout<V>? SquareGrid<V>(
        ActualItem<V> item,
        CirclePackingShape2<V> circle,
        Quantity<V> diameter,
        Quantity<V> spacing,
        SheetType<V> sheet) where V : struct, IFloatingNumber<V> {
        if (AddOrNull(diameter, spacing) is not { } pitch || !QuantityCore.IsPositive(pitch)) {
            return null;
        }
        int cols = QuantityCore.CountFitting(sheet.Width, pitch);
        int rows = QuantityCore.CountFitting(sheet.Height, pitch);
        if (cols <= 0 || rows <= 0) {
            return null;
        }

        var placements = new List<Placement2<V>>(cols * rows);
        for (int row = 0; row < rows; row++) {
            Quantity<V> y = QuantityCore.ScaledQuantity(pitch, row);
            for (int col = 0; col < cols; col++) {
                placements.Add(new Placement2<V>(
                    Shape: circle,
                    Position: new QuantityPoint2<V>(QuantityCore.ScaledQuantity(pitch, col), y),
                    Rotated: false,
                    ItemId: item.Id.Value));
            }
        }
        return new CircleGridLayout<V>(placements, CircleGridMode.SquareGrid, item.Id.Value);
    }

    /// <summary>
    /// 生成六角网格布局 / Generate the hexagonal grid layout.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">圆形件 / Circle item.</param>
    /// <param name="circle">圆形形状 / Circle shape.</param>
    /// <param name="diameter">直径 / Diameter.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <returns>网格布局，无可行放置时返回 null / The grid layout, or null when nothing fits.</returns>
    private static CircleGridLayout<V>? HexGrid<V>(
        ActualItem<V> item,
        CirclePackingShape2<V> circle,
        Quantity<V> diameter,
        Quantity<V> spacing,
        SheetType<V> sheet) where V : struct, IFloatingNumber<V> {
        if (AddOrNull(diameter, spacing) is not { } pitch || !QuantityCore.IsPositive(pitch)) {
            return null;
        }

        // 六角网格的行距与列距都按「直径 + kerf」推导，保证任意相邻件的真实间隙不小于 kerf。
        // Both the row and column pitch derive from diameter plus kerf, so every neighbouring pair keeps at least the
        // kerf as a real gap.
        Quantity<V> rowPitch = HexRowPitch(pitch);
        if (!QuantityCore.IsPositive(rowPitch)) {
            return null;
        }
        Quantity<V> halfPitch = QuantityOperators.DivideScalar(pitch, TwoOf(pitch));

        var placements = new List<Placement2<V>>();
        int row = 0;
        while (row < MaxHexRows) {
            Quantity<V> y = QuantityCore.ScaledQuantity(rowPitch, row);
            // 超过板材高度即停止加行；这是循环唯一的正常出口，MAX_HEX_ROWS 只防病态输入。
            // Stop adding rows once the sheet height is exceeded; this is the loop's only normal exit, while
            // MAX_HEX_ROWS merely guards pathological input.
            if (AddOrNull(y, pitch) is not { } rowTop
                || QuantityCompare.StrictlyGreater(rowTop, sheet.Height, "rowTop")) {
                break;
            }

            Quantity<V> rowOffset = row % 2 == 0
                ? QuantityOps.QuantityZeroOf(pitch)
                : halfPitch;
            if (QuantityOperators.Subtract(sheet.Width, rowOffset) is not { IsOk: true } available) {
                return null;
            }
            int cols = QuantityCore.CountFitting(available.Value, pitch);
            for (int col = 0; col < cols; col++) {
                if (AddOrNull(rowOffset, QuantityCore.ScaledQuantity(pitch, col)) is not { } x) {
                    return null;
                }
                placements.Add(new Placement2<V>(
                    Shape: circle,
                    Position: new QuantityPoint2<V>(x, y),
                    Rotated: false,
                    ItemId: item.Id.Value));
            }
            row++;
        }

        return placements.Count == 0
            ? null
            : new CircleGridLayout<V>(placements, CircleGridMode.HexGrid, item.Id.Value);
    }

    /// <summary>
    /// 计算六角网格行距 `26/30·d` / Compute the hexagonal row pitch `26/30·d`.
    /// </summary>
    /// <remarks>
    /// 用**标量比** 26/30 缩放直径量，结果仍是长度量，等于 0.8667·d。写成「26d / 30d」会得到无量纲的 0.8667，
    /// 只在 d = 1 时碰巧正确，并实际产出重叠的圆。
    /// The diameter is scaled by the **scalar ratio** 26/30, so the result is still a length equal to 0.8667·d. Writing
    /// it as "26d / 30d" yields the dimensionless 0.8667, only accidentally correct at d = 1, and actually produces
    /// overlapping circles.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="diameter">直径 / Diameter.</param>
    /// <returns>行距 / Row pitch.</returns>
    private static Quantity<V> HexRowPitch<V>(Quantity<V> diameter)
        where V : struct, IFloatingNumber<V> {
        if (!QuantityCore.IsPositive(diameter)) {
            return QuantityOps.QuantityZeroOf(diameter);
        }
        V numerator = IntegerScalarOf(diameter.Value, HexPitchNumerator);
        V denominator = IntegerScalarOf(diameter.Value, HexPitchDenominator);
        V ratio = numerator.Div(denominator);
        return QuantityOperators.MultiplyScalar(diameter, ratio);
    }

    /// <summary>
    /// 由样本数值构造整数标量 / Build an integer scalar from the sample value.
    /// </summary>
    /// <remarks>
    /// 逐次加一而不是拼字面量：`V` 只有数值约束，没有可用的静态常量访问面；实例的 `Constants` 是唯一入口
    /// （`V.Constants` 曾被判为 `CS0704`）。
    /// Increments one at a time rather than assembling a literal: `V` carries only numeric constraints, so the
    /// instance-level `Constants` is the only entry point (an earlier round in this session hit `CS0704` trying
    /// `V.Constants`).
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">样本数值 / Sample value.</param>
    /// <param name="times">倍数 / Multiplier.</param>
    /// <returns>标量 / Scalar.</returns>
    private static V IntegerScalarOf<V>(V sample, int times) where V : struct, IFloatingNumber<V> {
        V one = sample.Constants.One;
        V acc = sample.Constants.Zero;
        for (int index = 0; index < times; index++) {
            acc = acc.Plus(one);
        }
        return acc;
    }

    /// <summary>取量值单位下的标量二 / The scalar two in the quantity's unit.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">样本量值 / Sample quantity.</param>
    /// <returns>标量二 / The scalar two.</returns>
    private static V TwoOf<V>(Quantity<V> sample) where V : struct, IFloatingNumber<V> =>
        IntegerScalarOf(sample.Value, 2);

    /// <summary>
    /// 量值相加，量纲不符时返回 null / Add quantities, returning null on dimension mismatch.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左值 / Left value.</param>
    /// <param name="rhs">右值 / Right value.</param>
    /// <returns>和或 null / The sum, or null.</returns>
    private static Quantity<V>? AddOrNull<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        QuantityOperators.Add<V>(lhs, rhs) is { IsOk: true } sum ? sum.Value : null;
}
