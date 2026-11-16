// 板材与层模型 / Sheet and layer model.
// 与固定 Kotlin 基线 `bpp2d/domain/item/model/Sheet.kt`（123 行）与 `Layer.kt`（199 行）对应：
// 层是一块板材上的排样单元，由若干二维放置组成；`SheetLayer` 组合多个层，`Sheet` 是一块板材实例。
// Mirrors the pinned baseline: a layer is a packing unit on one sheet made of 2D placements; `SheetLayer` combines
// layers and `Sheet` is one sheet instance.
//
// **这是解除 Bpp2d 业务层阻塞的关键一批**：`layer_generation`／`layer_assignment`／物料汇总／`Sheet` 重载都卡在
// 这四个类型上。移植它们之后，先前刻意用"扁平放置列表"替代 `Sheet` 的地方可以补回正规入口。
// ** unblocks the Bpp2d business layer**: `layer_generation`, `layer_assignment`, the material summary and
// the `Sheet` overloads were all stuck on these four types. With them ported, places that deliberately used a flat
// placement list instead of `Sheet` can regain their proper entry point.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 板材类型描述 / Sheet type descriptor.
/// </summary>
/// <remarks>
/// 相等性由**类型编码 + 是否主板材 + 宽高 + 最大承重**共同决定：同尺寸但不同编码的板材是两种板材，若不带编码比较，
/// 多板材类型的排样结果会被错误去重。
/// Equality is decided by **type code + main flag + width/height + maximum load**: sheets of equal size but different
/// codes are different sheet types, and comparing without the code would wrongly deduplicate results across sheet types.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public class SheetType<V> : ISheet2Geometry<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 构造板材类型 / Construct a sheet type.
    /// </summary>
    /// <param name="width">板材宽度 / Sheet width.</param>
    /// <param name="height">板材高度 / Sheet height.</param>
    /// <param name="maxLoad">最大承重 / Maximum load.</param>
    /// <param name="typeCode">板材类型编码 / Sheet type code.</param>
    /// <param name="isMain">是否为主板材 / Whether it is the main sheet type.</param>
    public SheetType(
        Quantity<V> width,
        Quantity<V> height,
        Quantity<V> maxLoad,
        SheetTypeId typeCode,
        bool isMain = false) {
        Width = width;
        Height = height;
        MaxLoad = maxLoad;
        TypeCode = typeCode;
        IsMain = isMain;
        Area = QuantityOps.QuantityProduct(width, height);
    }

    /// <inheritdoc/>
    public Quantity<V> Width { get; }

    /// <inheritdoc/>
    public Quantity<V> Height { get; }

    /// <summary>最大承重 / Maximum load.</summary>
    public Quantity<V> MaxLoad { get; }

    /// <summary>板材类型编码 / Sheet type code.</summary>
    public SheetTypeId TypeCode { get; }

    /// <summary>是否为主板材 / Whether it is the main sheet type.</summary>
    public bool IsMain { get; }

    /// <inheritdoc/>
    public Quantity<V> Area { get; }

    /// <summary>
    /// 创建覆盖部分属性的副本 / Create a copy overriding selected properties.
    /// </summary>
    /// <param name="width">覆盖宽度 / Overridden width.</param>
    /// <param name="height">覆盖高度 / Overridden height.</param>
    /// <param name="maxLoad">覆盖最大承重 / Overridden maximum load.</param>
    /// <param name="typeCode">覆盖类型编码 / Overridden type code.</param>
    /// <param name="isMain">覆盖是否为主板材 / Overridden main flag.</param>
    /// <returns>新的板材类型 / A new sheet type.</returns>
    public SheetType<V> New(
        Quantity<V>? width = null,
        Quantity<V>? height = null,
        Quantity<V>? maxLoad = null,
        SheetTypeId? typeCode = null,
        bool? isMain = null) =>
        new(width ?? Width,
            height ?? Height,
            maxLoad ?? MaxLoad,
            typeCode ?? TypeCode,
            isMain ?? IsMain);

    /// <summary>
    /// 生成以原点为左下角的板材包围盒 / Build the sheet bounding box anchored at the origin.
    /// </summary>
    /// <returns>板材包围盒 / Sheet bounding box.</returns>
    public Box2Geometry<V> OriginBox() => new(
        QuantityCompare.ZeroOf(Width),
        QuantityCompare.ZeroOf(Height),
        new QuantityRectangle2<V>(Width, Height));

    /// <summary>
    /// 判断尺寸能否放入板材 / Check whether a size fits the sheet.
    /// </summary>
    /// <param name="boxWidth">包围盒宽度 / Bounding-box width.</param>
    /// <param name="boxHeight">包围盒高度 / Bounding-box height.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    public Utils.Functional.Result<bool, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Enabled(
        Quantity<V> boxWidth,
        Quantity<V> boxHeight) =>
        Utils.Functional.Results.Ok(
            QuantityCompare.AtMost(boxWidth, Width, "boxWidth")
            && QuantityCompare.AtMost(boxHeight, Height, "boxHeight"));

    /// <summary>
    /// 判断装载形状能否放入板材 / Check whether a packing shape fits the sheet.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    public Utils.Functional.Result<bool, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Enabled(
        PackingShape2<V> shape) {
        ArgumentNullException.ThrowIfNull(shape);
        return Enabled(shape.BoundingWidth, shape.BoundingHeight);
    }

    /// <inheritdoc/>
    public override bool Equals(object? other) =>
        other is SheetType<V> typed
        && TypeCode.Equals(typed.TypeCode)
        && IsMain == typed.IsMain
        && QuantityCore.Equal(Width, typed.Width)
        && QuantityCore.Equal(Height, typed.Height)
        && QuantityCore.Equal(MaxLoad, typed.MaxLoad);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(TypeCode, IsMain, Width.Value, Height.Value);

    /// <inheritdoc/>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}-{1}×{2}",
        TypeCode,
        Width.Value,
        Height.Value);
}

/// <summary>
/// 二维层：一块板材上的一组放置 / 2D layer: a group of placements on a sheet.
/// </summary>
/// <remarks>
/// 相等性**必须包含板材类型编码**：同一排布放在不同板材上是不同的列，不带编码会让多板材类型被错误去重。
/// Equality **must include the sheet type code**: the same layout on different sheets is a different column, and
/// leaving the code out deduplicates across sheet types incorrectly.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public class Layer<V> : IComparable<Layer<V>> where V : struct, IFloatingNumber<V> {
    private readonly List<Placement2<V>> _units;

    /// <summary>
    /// 构造层 / Construct a layer.
    /// </summary>
    /// <param name="iteration">生成迭代 / Generation iteration.</param>
    /// <param name="from">生成来源类型 / Generation source type.</param>
    /// <param name="sheet">所属板材类型 / Owning sheet type.</param>
    /// <param name="shape">层的包围形状 / Bounding shape of the layer.</param>
    /// <param name="units">放置列表 / Placement list.</param>
    public Layer(
        long iteration,
        Type? from,
        SheetType<V>? sheet,
        Sheet2Shape<V> shape,
        IReadOnlyList<Placement2<V>> units) {
        Iteration = iteration;
        From = from;
        Sheet = sheet;
        Shape = shape ?? throw new ArgumentNullException(nameof(units));
        _units = units is null ? throw new ArgumentNullException(nameof(units)) : new List<Placement2<V>>(units);
    }

    /// <summary>生成迭代 / Generation iteration.</summary>
    public long Iteration { get; }

    /// <summary>生成来源类型 / Generation source type.</summary>
    public Type? From { get; }

    /// <summary>所属板材类型 / Owning sheet type.</summary>
    public SheetType<V>? Sheet { get; }

    /// <summary>层的包围形状 / Bounding shape of the layer.</summary>
    public Sheet2Shape<V> Shape { get; }

    /// <summary>放置列表 / Placement list.</summary>
    public IReadOnlyList<Placement2<V>> Units => _units;

    /// <summary>层内件数 / Amount of items in the layer.</summary>
    public ulong Amount => (ulong)_units.Count;

    /// <summary>层内实际占用面积 / Actually occupied area of the layer.</summary>
    public Quantity<V> Area =>
        QuantityCore.Sum(_units.Select(unit => unit.Shape.Area))
        ?? QuantityOps.QuantityZeroOf(Shape.Width);

    /// <summary>层的外接矩形面积 / Enclosing rectangle area of the layer.</summary>
    public Quantity<V> BoundingArea => Shape.Area;

    /// <summary>层的件数映射 / Item amount map of the layer.</summary>
    public IReadOnlyDictionary<ItemId, ulong> ItemAmounts =>
        _units
            .Where(unit => unit.ItemId is not null)
            .GroupBy(unit => new ItemId(unit.ItemId!), EqualityComparer<ItemId>.Default)
            .ToDictionary(
                group => group.Key,
                group => (ulong)group.Count(),
                EqualityComparer<ItemId>.Default);

    /// <summary>层内矩形件与圆形件各自的件数 / Rectangle and circle counts inside the layer.</summary>
    public IReadOnlyDictionary<PackingShape2Type, ulong> ShapeCounts =>
        _units
            .GroupBy(unit => unit.Shape.ShapeType)
            .ToDictionary(
                group => group.Key,
                group => (ulong)group.Count());

    /// <summary>
    /// 层的利用率：实际占用面积与外接矩形面积之比，分母为零时为零 / Utilisation, zero when the denominator is zero.
    /// </summary>
    /// <returns>利用率 / Utilisation ratio.</returns>
    public V Utilization() {
        Quantity<V> boundingArea = BoundingArea;
        if (!QuantityCore.IsPositive(boundingArea)) {
            return QuantityOps.QuantityZeroOf(Shape.Width).Value;
        }
        return Area.Value.Div(boundingArea.Value);
    }

    /// <inheritdoc/>
    public int CompareTo(Layer<V>? other) {
        if (other is null) {
            return 1;
        }
        int byArea = CompareOrder(QuantityCore.OrdOrNull(Area, other.Area));
        return byArea != 0 ? byArea : Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc/>
    public override bool Equals(object? other) {
        if (ReferenceEquals(this, other)) {
            return true;
        }
        if (other is not Layer<V> typed) {
            return false;
        }
        if (!Equals(Sheet?.TypeCode, typed.Sheet?.TypeCode)) {
            return false;
        }
        if (_units.Count != typed._units.Count) {
            return false;
        }
        if (!QuantityCore.Equal(Shape.Width, typed.Shape.Width)
            || !QuantityCore.Equal(Shape.Height, typed.Shape.Height)) {
            return false;
        }
        return _units.SequenceEqual(typed._units);
    }

    /// <summary>
    /// 哈希只取**稳定且廉价**的字段 / Hash on stable, cheap fields only.
    /// </summary>
    /// <remarks>
    /// 不用 `_units[0].GetHashCode`：`Placement2` 是含形状与量值的深值对象，其哈希实测**不稳定**
    /// （同一实例两次取值不同），会让 `HashSet` 判重失效——`LayerAggregation.RemoveColumn` 的幂等性正是这样被破坏的。
    /// 按「件数 + 板材类型编码」取哈希既与 `Equals` 一致（两者都是必需的相等条件），又不会漂移；碰撞由 `Equals` 兜底。
    /// Does **not** use `_units[0].GetHashCode`: `Placement2` is a deep value object over shapes and quantities whose
    /// hash measured **unstable** in this session (different across calls on the same instance), which breaks `HashSet`
    /// deduplication — exactly how `LayerAggregation.RemoveColumn` lost idempotence. Hashing on "count + sheet type code"
    /// is consistent with `Equals` (both are required equality conditions) and cannot drift; collisions fall back to
    /// `Equals`.
    /// </remarks>
    /// <returns>哈希值 / Hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(_units.Count, Sheet?.TypeCode?.Value ?? string.Empty);

    /// <inheritdoc/>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "Layer({0}, {1}×{2})",
        _units.Count,
        Shape.Width.Value,
        Shape.Height.Value);

    /// <summary>把三路比较转成比较器整数 / Convert a three-way order into a comparer integer.</summary>
    /// <param name="order">比较结果 / The order.</param>
    /// <returns>负数、零或正数 / Negative, zero or positive.</returns>
    private static int CompareOrder(Utils.Functional.Order? order) => order switch {
        Utils.Functional.Order.Less => -1,
        Utils.Functional.Order.Greater => 1,
        _ => 0,
    };
}

/// <summary>
/// 板材层组合：一块板材上叠加的层 / Sheet layer combination: layers stacked on one sheet.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public class SheetLayer<V> : IComparable<SheetLayer<V>> where V : struct, IFloatingNumber<V> {
    private readonly List<Layer<V>> _layers;

    /// <summary>
    /// 构造板材层组合 / Construct a sheet layer combination.
    /// </summary>
    /// <param name="type">板材类型 / Sheet type.</param>
    /// <param name="shape">组合后的包围形状 / Combined bounding shape.</param>
    /// <param name="layers">层列表 / Layer list.</param>
    /// <param name="batchNo">批次号 / Batch number.</param>
    public SheetLayer(
        SheetType<V> type,
        Sheet2Shape<V> shape,
        IReadOnlyList<Layer<V>> layers,
        BatchNo? batchNo = null) {
        Type = type;
        Shape = shape;
        _layers = layers is null ? throw new ArgumentNullException(nameof(layers)) : new List<Layer<V>>(layers);
        BatchNo = batchNo;
    }

    /// <summary>板材类型 / Sheet type.</summary>
    public SheetType<V> Type { get; }

    /// <summary>组合后的包围形状 / Combined bounding shape.</summary>
    public Sheet2Shape<V> Shape { get; }

    /// <summary>层列表 / Layer list.</summary>
    public IReadOnlyList<Layer<V>> Layers => _layers;

    /// <summary>批次号 / Batch number.</summary>
    public BatchNo? BatchNo { get; }

    /// <summary>组合内件数 / Amount inside the combination.</summary>
    public ulong Amount => (ulong)_layers.Sum(layer => layer.Units.Count);

    /// <summary>组合内实际占用面积 / Actually occupied area of the combination.</summary>
    public Quantity<V> Area =>
        QuantityCore.Sum(_layers.Select(layer => layer.Area))
        ?? QuantityOps.QuantityZeroOf(Shape.Width);

    /// <summary>板材装载率，板材面积非正时为零 / Sheet loading rate, zero when the sheet area is not positive.</summary>
    public V LoadingRate {
        get {
            Quantity<V> sheetArea = Type.Area;
            if (!QuantityCore.IsPositive(sheetArea)) {
                return QuantityOps.QuantityZeroOf(Shape.Width).Value;
            }
            return Area.Value.Div(sheetArea.Value);
        }
    }

    /// <summary>组合内件数映射 / Item amount map of the combination.</summary>
    public IReadOnlyDictionary<ItemId, ulong> ItemAmounts {
        get {
            var counter = new Dictionary<ItemId, ulong>();
            foreach (Layer<V> layer in _layers) {
                foreach (KeyValuePair<ItemId, ulong> pair in layer.ItemAmounts) {
                    counter[pair.Key] = (counter.TryGetValue(pair.Key, out ulong previous) ? previous : 0UL)
                        + pair.Value;
                }
            }
            return counter;
        }
    }

    /// <inheritdoc/>
    public int CompareTo(SheetLayer<V>? other) {
        if (other is null) {
            return 1;
        }
        int byArea = CompareOrder(QuantityCore.OrdOrNull(Area, other.Area));
        return byArea != 0 ? byArea : Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc/>
    public override bool Equals(object? other) =>
        ReferenceEquals(this, other)
        || (other is SheetLayer<V> typed && _layers.SequenceEqual(typed._layers));

    /// <summary>
    /// 哈希只取件数，理由见 <see cref="Layer{V}.GetHashCode"/> / Hash on the layer count only; see
    /// <see cref="Layer{V}.GetHashCode"/>.
    /// </summary>
    /// <returns>哈希值 / Hash code.</returns>
    public override int GetHashCode() => _layers.Count;

    /// <inheritdoc/>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "SheetLayer({0} layers, rate={1})",
        _layers.Count,
        LoadingRate);

    /// <summary>把三路比较转成比较器整数 / Convert a three-way order into a comparer integer.</summary>
    /// <param name="order">比较结果 / The order.</param>
    /// <returns>负数、零或正数 / Negative, zero or positive.</returns>
    private static int CompareOrder(Utils.Functional.Order? order) => order switch {
        Utils.Functional.Order.Less => -1,
        Utils.Functional.Order.Greater => 1,
        _ => 0,
    };
}

/// <summary>
/// 板材实例：某种板材类型加其上排好的层 / Sheet instance: a sheet type plus the layers laid out on it.
/// </summary>
/// <param name="Type">板材类型 / Sheet type.</param>
/// <param name="Layers">层列表 / Layer list.</param>
/// <param name="BatchNo">批次号 / Batch number.</param>
public sealed record Sheet<V>(
    SheetType<V> Type,
    IReadOnlyList<SheetLayer<V>>? Layers = null,
    BatchNo? BatchNo = null) where V : struct, IFloatingNumber<V> {
    /// <summary>层列表，永不为 null / Layers, never null.</summary>
    public IReadOnlyList<SheetLayer<V>> SheetLayers => Layers ?? Array.Empty<SheetLayer<V>>();

    /// <summary>板材宽度 / Sheet width.</summary>
    public Quantity<V> Width => Type.Width;

    /// <summary>板材高度 / Sheet height.</summary>
    public Quantity<V> Height => Type.Height;

    /// <summary>板材上全部放置，按层依次展开 / Every placement on the sheet, flattened by layer.</summary>
    public IReadOnlyList<Placement2<V>> Placements =>
        SheetLayers
            .SelectMany(sheetLayer => sheetLayer.Layers)
            .SelectMany(layer => layer.Units)
            .ToList();

    /// <summary>已排样物品的件数映射 / Amount map of placed items.</summary>
    public IReadOnlyDictionary<ItemId, ulong> ItemAmounts {
        get {
            var counter = new Dictionary<ItemId, ulong>();
            foreach (SheetLayer<V> sheetLayer in SheetLayers) {
                foreach (KeyValuePair<ItemId, ulong> pair in sheetLayer.ItemAmounts) {
                    counter[pair.Key] = (counter.TryGetValue(pair.Key, out ulong previous) ? previous : 0UL)
                        + pair.Value;
                }
            }
            return counter;
        }
    }
}
