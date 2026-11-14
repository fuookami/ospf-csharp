// 物品域：放置工厂与物品合并 / Item domain: placement factory and item merger.
// 与固定 Kotlin 基线 `item/model/PlacementFactory.kt`（96 行）与 `item/service/ItemMerger.kt`（84 行）对应。
// Mirrors the pinned baseline's `PlacementFactory.kt` and `ItemMerger.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 放置构造工厂 / Placement factory.
/// </summary>
public static class PlacementFactory {
    /// <summary>
    /// 由物品与落点构造放置 / Build a placement from an item and a position.
    /// </summary>
    /// <remarks>
    /// 旋转未获许可时返回失败，而不是静默地忽略 `rotated`：调用方传了 `rotated: true` 却拿到未旋转的放置，会在下游表现
    /// 为"排布与预期不符"而没有任何报错。
    /// Fails rather than silently ignoring `rotated` when rotation is not permitted: a caller asking for `rotated: true`
    /// and receiving an unrotated placement surfaces downstream as "the layout is not what I expected" with no error.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <param name="position">左下角落点 / Lower-left position.</param>
    /// <param name="rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
    /// <returns>放置或失败 / The placement, or a failure.</returns>
    public static Result<Placement2<V>, ErrorCode, Error<ErrorCode>> ItemPlacement2Of<V>(
        IAbstractItem<V> item,
        QuantityPoint2<V> position,
        bool rotated = false) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(position);

        if (rotated && !item.AllowRotate) {
            return Results.Failed<Placement2<V>>(new Bpp2dValidationError(
                $"物品 {item.Id} 未允许旋转 / Item {item.Id} does not allow rotation"));
        }
        PackingShape2<V> shape = rotated ? item.Shape.Rotated() : item.Shape;
        return Results.Ok(new Placement2<V>(
            Shape: shape,
            Position: position,
            Rotated: rotated,
            ItemId: item.Id.Value));
    }

    /// <summary>
    /// 由装载形状与落点构造放置 / Build a placement from a packing shape and a position.
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ItemPlacement2Of{V}"/> 不同，这里**不校验旋转许可**：形状本身不带许可信息，调用方必须自己保证。
    /// Unlike <see cref="ItemPlacement2Of{V}"/> this does **not** check rotation permission: a bare shape carries no such
    /// information, so the caller must guarantee it.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="position">左下角落点 / Lower-left position.</param>
    /// <param name="rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
    /// <param name="itemId">物品编号 / Item identifier.</param>
    /// <returns>放置 / The placement.</returns>
    public static Placement2<V> ShapePlacement2Of<V>(
        PackingShape2<V> shape,
        QuantityPoint2<V> position,
        bool rotated = false,
        ItemId? itemId = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(position);
        return new Placement2<V>(shape, position, rotated, itemId?.Value);
    }

    /// <summary>
    /// 构造与模板放置同形状、同旋转状态的图样放置 / Build a pattern placement sharing the template's shape and rotation.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="template">模板放置 / Template placement.</param>
    /// <param name="position">新的左下角落点 / New lower-left position.</param>
    /// <returns>放置 / The placement.</returns>
    public static Placement2<V> PlacedAt<V>(Placement2<V> template, QuantityPoint2<V> position)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(position);
        return template with { Position = position };
    }

    /// <summary>
    /// 以零点为原点构造落点 / Build a position anchored at the origin.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">提供单位与数值类型的样本量 / Sample quantity providing unit and numeric type.</param>
    /// <returns>原点落点 / The origin position.</returns>
    public static QuantityPoint2<V> OriginPoint2Of<V>(Quantity<V> sample)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(sample);
        Quantity<V> zero = QuantityOps.QuantityZeroOf(sample);
        return new QuantityPoint2<V>(zero, zero);
    }
}

/// <summary>
/// 物品合并键 / Item merge key.
/// </summary>
/// <remarks>
/// **不按 `PackingShape2` 自身哈希分组**。`PackingShape2` 是抽象 record，其实例哈希在本会话中已被证明不稳定
/// （`Placement2`／`Layer` 都栽在同一处深值对象上），直接拿形状作字典键会让"两个同形状物品"分到不同组。
/// 这里改为按**形状的结构签名**（类型 + 关键尺寸）取键：签名是字符串，稳定且可读，碰撞由后续 `Equals` 兜底。
///
/// **Does not group by `PackingShape2`'s own hash.** `PackingShape2` is an abstract record whose instance hash proved
/// unstable in this session (`Placement2` and `Layer` both fell to the same deep-value-object problem), so using the
/// shape as a dictionary key would split two identically shaped items into different groups. The key is instead the
/// shape's **structural signature** (kind plus key dimensions): a string, stable and readable, with collisions settled by
/// the subsequent `Equals`.
/// </remarks>
/// <param name="Signature">形状结构签名 / Shape structural signature.</param>
/// <param name="AllowRotate">是否允许旋转 / Whether rotation is allowed.</param>
/// <param name="BatchNo">批次号 / Batch number.</param>
internal sealed record ItemMergeKey(
    string Signature,
    bool AllowRotate,
    BatchNo? BatchNo);

/// <summary>
/// 物品合并服务 / Item merger service.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ItemMerger<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 合并同形状物品的件数需求 / Merge amount demands of identically shaped items.
    /// </summary>
    /// <remarks>
    /// 合并键相同的物品累加件数，代表物品取**第一个出现**的实例——因此顺序决定合并后哪个物品编号留在结果里，
    /// 这里用有序字典保留首次出现顺序，与基线一致。
    /// Items sharing a merge key accumulate amounts and the representative is the **first occurrence**, so ordering decides
    /// which item id survives; an insertion-ordered dictionary preserves first-appearance order, matching the baseline.
    /// </remarks>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <returns>合并后的物品与件数 / Merged items and amounts.</returns>
    public IReadOnlyList<(ActualItem<V> Item, ulong Amount)> Merge(
        IReadOnlyList<(ActualItem<V> Item, ulong Amount)> items) {
        ArgumentNullException.ThrowIfNull(items);

        var merged = new List<(ActualItem<V> Item, ulong Amount)>();
        var positions = new Dictionary<ItemMergeKey, int>();
        foreach ((ActualItem<V> item, ulong amount) in items) {
            ItemMergeKey key = KeyOf(item);
            if (positions.TryGetValue(key, out int index)) {
                merged[index] = (merged[index].Item, merged[index].Amount + amount);
                continue;
            }
            positions[key] = merged.Count;
            merged.Add((item, amount));
        }
        return merged;
    }

    /// <summary>
    /// 按形状分组物品，保持首次出现顺序 / Group items by shape, preserving first-occurrence order.
    /// </summary>
    /// <param name="items">物品列表 / Item list.</param>
    /// <returns>形状分组 / Shape groups.</returns>
    public IReadOnlyList<IReadOnlyList<ActualItem<V>>> GroupByShape(IReadOnlyList<ActualItem<V>> items) {
        ArgumentNullException.ThrowIfNull(items);

        var groups = new List<List<ActualItem<V>>>();
        var positions = new Dictionary<ItemMergeKey, int>();
        foreach (ActualItem<V> item in items) {
            ItemMergeKey key = KeyOf(item);
            if (positions.TryGetValue(key, out int index)) {
                groups[index].Add(item);
                continue;
            }
            positions[key] = groups.Count;
            groups.Add(new List<ActualItem<V>> { item });
        }
        return groups.Select(group => (IReadOnlyList<ActualItem<V>>)group).ToList();
    }

    /// <summary>
    /// 计算合并键 / Compute the merge key.
    /// </summary>
    /// <param name="item">物品 / Item.</param>
    /// <returns>合并键 / The merge key.</returns>
    private static ItemMergeKey KeyOf(ActualItem<V> item) => new(
        Signature: SignatureOf(item.Shape),
        AllowRotate: item.AllowRotate,
        BatchNo: item.BatchNo);

    /// <summary>
    /// 计算形状的结构签名 / Compute a shape's structural signature.
    /// </summary>
    /// <remarks>
    /// 用**不变文化**格式化尺寸，避免区域设置改变小数点后同一形状得到不同签名。签名把类型名放在首位，因此矩形与圆
    /// 即使尺寸相同也不会合并。
    /// Formats dimensions with the **invariant culture** so a locale change cannot give one shape two signatures. The kind
    /// name leads the signature, so a rectangle and a circle with equal dimensions never merge.
    /// </remarks>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>结构签名 / Structural signature.</returns>
    private static string SignatureOf(PackingShape2<V> shape) {
        ArgumentNullException.ThrowIfNull(shape);
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1}|{2}",
            shape.GetType().Name,
            shape.BoundingWidth.Value.ToFlt64().ToDouble(),
            shape.BoundingHeight.Value.ToFlt64().ToDouble());
    }
}
