// 二维装箱标识与物品模型 / 2D packing identifiers and item model.
// 只实现排样算法真正需要的部分：
// 编号类型、抽象物品、实际物品，以及"候选形状 / 是否圆形件"两个派生判定。
// The pinned Kotlin: and, limited to what the layout algorithm actually needs:
// identifier types, the abstract item, the actual item, and the two derived predicates.
//
// **这里的能力边界**：`AbstractItem` 还带 `materialAmounts`（`Map<MaterialKey, UInt64>`）与 `batchNo`
// （`BatchNo?`），这两个类型依赖物料与批次模块，尚未实现，因此这里**不含这两个字段**，`ActualItem` 只承载
// 编号、名称、形状、旋转许可与重量。缺字段会在使用处暴露为编译错误，而不是运行期悄悄为 null。
// **Capability boundary**: its `AbstractItem` also carries `materialAmounts`
// (`Map<MaterialKey, UInt64>`) and `batchNo` (`BatchNo?`). Those types depend on the material and batch modules,
// which are not implemented, so **both are omitted** here; `ActualItem` carries identifier, name, shape, rotation
// allowance and weight. A missing field surfaces as a compile error at the use site instead of a silent null at run
// time.

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 板材类型编码 / Sheet type code.
/// </summary>
/// <param name="Value">编码文本 / Code text.</param>
public sealed record SheetTypeId(string Value) : IComparable<SheetTypeId> {
    /// <inheritdoc/>
    public int CompareTo(SheetTypeId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 物品编号 / Item identifier.
/// </summary>
/// <param name="Value">编号文本 / Identifier text.</param>
public sealed record ItemId(string Value) : IComparable<ItemId> {
    /// <inheritdoc/>
    public int CompareTo(ItemId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 批次号 / Batch number.
/// </summary>
/// <param name="Value">编号文本 / Number text.</param>
public sealed record BatchNo(string Value) : IComparable<BatchNo> {
    /// <inheritdoc/>
    public int CompareTo(BatchNo? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 物料编号 / Material number.
/// </summary>
/// <param name="Value">编号文本 / Number text.</param>
public sealed record MaterialNo(string Value) : IComparable<MaterialNo> {
    /// <inheritdoc/>
    public int CompareTo(MaterialNo? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 抽象物品 / Abstract item.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IAbstractItem<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>物品编号 / Item identifier.</summary>
    ItemId Id { get; }

    /// <summary>物品名称 / Item name.</summary>
    string Name { get; }

    /// <summary>装载形状 / Packing shape.</summary>
    PackingShape2<V> Shape { get; }

    /// <summary>是否允许旋转 90 度 / Whether rotation by 90 degrees is allowed.</summary>
    bool AllowRotate { get; }

    /// <summary>单件重量 / Per-piece weight.</summary>
    Quantity<V> Weight { get; }

    /// <summary>物料与件数映射 / Material-to-amount map.</summary>
    IReadOnlyDictionary<MaterialKey, ulong> MaterialAmounts { get; }

    /// <summary>批次号 / Batch number.</summary>
    BatchNo? BatchNo { get; }
}

/// <summary>
/// 实际物品：板材上真正被排样的物品 / Actual item: the item really placed on a sheet.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">物品编号 / Item identifier.</param>
/// <param name="Name">物品名称 / Item name.</param>
/// <param name="Shape">装载形状 / Packing shape.</param>
/// <param name="AllowRotate">是否允许旋转 / Whether rotation is allowed.</param>
/// <param name="Weight">单件重量 / Per-piece weight.</param>
/// <param name="MaterialAmounts">物料与件数映射 / Material-to-amount map.</param>
/// <param name="BatchNo">批次号 / Batch number.</param>
public sealed record ActualItem<V>(
    ItemId Id,
    string Name,
    PackingShape2<V> Shape,
    bool AllowRotate,
    Quantity<V> Weight,
    IReadOnlyDictionary<MaterialKey, ulong>? MaterialAmounts = null,
    BatchNo? BatchNo = null)
    : IAbstractItem<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    IReadOnlyDictionary<MaterialKey, ulong> IAbstractItem<V>.MaterialAmounts =>
        MaterialAmounts ?? new Dictionary<MaterialKey, ulong>();

    /// <inheritdoc/>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}-{1}", Id, Name);
}

/// <summary>
/// 物品派生量扩展 / Item derived-value extensions.
/// </summary>
public static class ItemExtensions {
    /// <summary>包围宽度 / Bounding width.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>包围宽度 / Bounding width.</returns>
    public static Quantity<V> Width<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> => item.Shape.BoundingWidth;

    /// <summary>包围高度 / Bounding height.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>包围高度 / Bounding height.</returns>
    public static Quantity<V> Height<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> => item.Shape.BoundingHeight;

    /// <summary>实际占用面积 / Actual occupied area.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>实际占用面积 / Actual occupied area.</returns>
    public static Quantity<V> Area<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> => item.Shape.Area;

    /// <summary>外接矩形面积 / Enclosing rectangle area.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>外接矩形面积 / Enclosing rectangle area.</returns>
    public static Quantity<V> BoundingArea<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> => item.Shape.BoundingArea;

    /// <summary>形状类型 / Shape type.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>形状类型 / Shape type.</returns>
    public static PackingShape2Type ShapeType<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> => item.Shape.ShapeType;

    /// <summary>
    /// 候选形状列表（圆形件与正方形恒为单个候选） / Candidate shapes; circles and squares always yield one.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>候选形状列表 / Candidate shape list.</returns>
    public static IReadOnlyList<Shape2Candidate<V>> CandidateShapes<V>(
        this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);
        return item.Shape.CandidateShapes(item.AllowRotate);
    }

    /// <summary>
    /// 是否圆形件 / Whether the item is circular.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>是否圆形件 / Whether circular.</returns>
    public static bool IsCircleItem<V>(this IAbstractItem<V> item)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);
        return item.Shape.IsCircle();
    }
}
