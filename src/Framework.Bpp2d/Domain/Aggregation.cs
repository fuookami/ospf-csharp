// 物品与板材需求聚合 / Item and sheet demand aggregation.
// 与固定 Kotlin 对应，并补齐 `ItemContext` 所需的输入模型。
// And supplies the input model `ItemContext` needs.
//
// **与 `PackingAggregation` 的区别必须记住**：本类是同名概念的**入参**侧（需求 + 板材上限，随排布推进而消耗），
// 而 `PackingAggregation` 是**出参**侧（装 `PackedSheet` 的结果容器）。两个类型语义相反，不能互换。
// **The distinction from `PackingAggregation` matters**: this class is the **input** side of the same name (demands and
// sheet limits, consumed as packing proceeds), while `PackingAggregation` is the **output** side (a result container of
// `PackedSheet`s). The two are semantically opposite and must not be interchanged.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 物品与板材需求聚合 / Item and sheet demand aggregation.
/// </summary>
/// <remarks>
/// 维护"已用"记账并由此推导剩余量，而不是在消耗时直接扣减需求：需求是**不可变的业务事实**，扣减会让"原始需求"与
/// "剩余需求"混为一谈，回滚（<see cref="ReleaseSheets"/>）也就无从下手。
/// Keeps a "used" ledger and derives the remainder from it instead of decrementing demands on consumption: demands are an
/// **immutable business fact**, and decrementing would conflate "original demand" with "remaining demand", leaving no
/// basis for rollback (<see cref="ReleaseSheets"/>).
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class Aggregation<V> where V : struct, IFloatingNumber<V> {
    private readonly Dictionary<ItemId, ulong> _usedItems;
    private readonly Dictionary<SheetType<V>, ulong> _usedSheets;

    /// <summary>
    /// 构造需求聚合 / Construct a demand aggregation.
    /// </summary>
    /// <param name="itemDemands">物品件数需求 / Item amount demands.</param>
    /// <param name="sheetTypes">板材类型与可用数量，null 表示不限 / Sheet types with available amounts, null unlimited.</param>
    public Aggregation(
        IReadOnlyDictionary<ItemId, ulong> itemDemands,
        IReadOnlyDictionary<SheetType<V>, ulong?>? sheetTypes = null) {
        ArgumentNullException.ThrowIfNull(itemDemands);

        ItemDemands = itemDemands;
        SheetTypes = sheetTypes ?? new Dictionary<SheetType<V>, ulong?>();
        _usedItems = ItemDemands.Keys.ToDictionary(id => id, _ => 0UL);
        _usedSheets = SheetTypes.Keys.ToDictionary(type => type, _ => 0UL);
        MainSheet = SheetTypes.Keys.FirstOrDefault(type => type.IsMain)
            ?? SheetTypes.Keys.MaxByQuantityOrNull(type => type.Area);
    }

    /// <summary>物品件数需求 / Item amount demands.</summary>
    public IReadOnlyDictionary<ItemId, ulong> ItemDemands { get; }

    /// <summary>板材类型与可用数量 / Sheet types with available amounts.</summary>
    public IReadOnlyDictionary<SheetType<V>, ulong?> SheetTypes { get; }

    /// <summary>
    /// 主板材：显式标记优先，否则取面积最大者 / Main sheet: the explicitly flagged one, otherwise the largest by area.
    /// </summary>
    /// <remarks>
    /// 两种规则按此顺序：显式标记是**业务意图**，面积最大只是**兜底猜测**。反过来会让运营配置的主板材被一块更大的板材顶掉。
    /// The order matters: an explicit flag is **business intent** while the largest-by-area is only a **fallback guess**.
    /// Reversing them would let a larger sheet displace the operator's configured main sheet.
    /// </remarks>
    public SheetType<V>? MainSheet { get; }

    /// <summary>剩余物品需求，只含仍有剩余的项 / Remaining item demands, only entries with a remainder.</summary>
    public IReadOnlyDictionary<ItemId, ulong> RestItems => ItemDemands
        .Where(pair => DemandRestOf(pair.Key, pair.Value) > 0)
        .ToDictionary(pair => pair.Key, pair => DemandRestOf(pair.Key, pair.Value));

    /// <summary>剩余板材数量，不限数量以 <see cref="ulong.MaxValue"/> 表示 / Remaining sheet amounts, unlimited as MaxValue.</summary>
    public IReadOnlyDictionary<SheetType<V>, ulong> RestSheets {
        get {
            var rest = new Dictionary<SheetType<V>, ulong>();
            foreach ((SheetType<V> type, ulong? limit) in SheetTypes) {
                ulong remaining = SheetRestOf(type, limit);
                if (remaining > 0) {
                    rest[type] = remaining;
                }
            }
            return rest;
        }
    }

    /// <summary>
    /// 标记物品已使用 / Mark items as used.
    /// </summary>
    /// <param name="items">物品编号到件数 / Item identifier to amount.</param>
    public void UseItems(IReadOnlyDictionary<ItemId, ulong> items) {
        ArgumentNullException.ThrowIfNull(items);
        foreach ((ItemId id, ulong amount) in items) {
            _usedItems[id] = (_usedItems.TryGetValue(id, out ulong used) ? used : 0UL) + amount;
        }
    }

    /// <summary>
    /// 标记板材已使用 / Mark sheets as used.
    /// </summary>
    /// <param name="sheets">板材类型到数量 / Sheet type to amount.</param>
    public void UseSheets(IReadOnlyDictionary<SheetType<V>, ulong> sheets) {
        ArgumentNullException.ThrowIfNull(sheets);
        foreach ((SheetType<V> type, ulong amount) in sheets) {
            _usedSheets[type] = (_usedSheets.TryGetValue(type, out ulong used) ? used : 0UL) + amount;
        }
    }

    /// <summary>
    /// 释放已用板材 / Release used sheets.
    /// </summary>
    /// <remarks>
    /// 释放量超过已用量时**钳到零**而不是变成负数：负的已用量会让剩余量超过上限，比少释放更危险。
    /// Over-releasing **clamps to zero** rather than going negative: a negative used amount would push the remainder above
    /// the limit, which is more dangerous than under-releasing.
    /// </remarks>
    /// <param name="sheets">板材类型到数量 / Sheet type to amount.</param>
    public void ReleaseSheets(IReadOnlyDictionary<SheetType<V>, ulong> sheets) {
        ArgumentNullException.ThrowIfNull(sheets);
        foreach ((SheetType<V> type, ulong amount) in sheets) {
            ulong current = _usedSheets.TryGetValue(type, out ulong used) ? used : 0UL;
            _usedSheets[type] = current <= amount ? 0UL : current - amount;
        }
    }

    /// <summary>
    /// 汇总需求面积：满足全部物品需求所需的最小总面积 / The minimum total area needed to satisfy all item demands.
    /// </summary>
    /// <remarks>
    /// 目录缺失任一需求物品时返回 null 而不是跳过该物品：跳过会让面积偏小，下游据此估算的板材数不足，表现为"排不完"。
    /// Returns null when the catalogue lacks any demanded item rather than skipping it: skipping would understate the area,
    /// downstream would estimate too few sheets, and the symptom would be "it never finishes packing".
    /// </remarks>
    /// <param name="items">物品目录 / Item catalogue.</param>
    /// <returns>需求总面积，缺失物品时为 null / Demanded total area, or null when an item is missing.</returns>
    public Quantity<V>? DemandArea(IReadOnlyDictionary<ItemId, IAbstractItem<V>> items) {
        ArgumentNullException.ThrowIfNull(items);

        var areas = new List<Quantity<V>>(ItemDemands.Count);
        foreach ((ItemId id, ulong amount) in ItemDemands) {
            if (!items.TryGetValue(id, out IAbstractItem<V>? item)) {
                return null;
            }
            // 单件包围面积乘以件数即该物品的需求面积。
            // Unit bounding area times the amount is the demanded area for that item.
            areas.Add(QuantityCore.ScaledQuantity(item.Shape.BoundingArea, (int)amount));
        }
        return QuantityCore.Sum(areas);
    }

    /// <summary>
    /// 按板材装载率需求估算板材数量 / Estimate sheet amounts from a target loading rate.
    /// </summary>
    /// <param name="totalArea">需求总面积 / Demanded total area.</param>
    /// <param name="type">板材类型 / Sheet type.</param>
    /// <param name="estimatedLoadingRate">预估装载率 / Estimated loading rate.</param>
    /// <returns>估算的板材数量 / Estimated sheet amount.</returns>
    public V EstimateSheetAmount(Quantity<V> totalArea, SheetType<V> type, V estimatedLoadingRate) {
        ArgumentNullException.ThrowIfNull(totalArea);
        ArgumentNullException.ThrowIfNull(type);

        V zero = type.Area.Value.Constants.Zero;
        if (!QuantityCore.IsPositive(type.Area) || estimatedLoadingRate.CompareTo(zero) <= 0) {
            return zero;
        }
        return totalArea.Value.Div(type.Area.Value).Div(estimatedLoadingRate);
    }

    /// <summary>
    /// 取某物品的剩余需求 / The remaining demand of one item.
    /// </summary>
    /// <param name="id">物品编号 / Item identifier.</param>
    /// <param name="demand">需求件数 / Demanded amount.</param>
    /// <returns>剩余件数 / Remaining amount.</returns>
    private ulong DemandRestOf(ItemId id, ulong demand) {
        ulong used = _usedItems.TryGetValue(id, out ulong value) ? value : 0UL;
        return used >= demand ? 0UL : demand - used;
    }

    /// <summary>
    /// 取某板材类型的剩余数量 / The remaining amount of one sheet type.
    /// </summary>
    /// <param name="type">板材类型 / Sheet type.</param>
    /// <param name="limit">可用上限，null 表示不限 / Available limit, null meaning unlimited.</param>
    /// <returns>剩余数量 / Remaining amount.</returns>
    private ulong SheetRestOf(SheetType<V> type, ulong? limit) {
        ulong used = _usedSheets.TryGetValue(type, out ulong value) ? value : 0UL;
        if (limit is null) {
            return ulong.MaxValue;
        }
        return used >= limit.Value ? 0UL : limit.Value - used;
    }
}
