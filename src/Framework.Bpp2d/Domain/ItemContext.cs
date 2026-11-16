// 物品领域上下文 / Item domain context.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 物品领域上下文 / Item domain context.
/// </summary>
/// <remarks>
/// 无状态配置载体：物品目录、板材类型、件数需求三个数据加上两个服务。`Aggregate` 每次调用都构造**新的**
/// <see cref="Aggregation{V}"/>，因此同一个上下文可以被多个并行排布复用而不共享记账。
/// A stateless configuration carrier: three data sets (item catalogue, sheet types, amount demands) plus two services.
/// `Aggregate` builds a **fresh** <see cref="Aggregation{V}"/> on every call, so one context can be reused by parallel
/// packing runs without sharing the used-amount ledger.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ItemContext<V> where V : struct, IFloatingNumber<V> {
    private readonly ItemMerger<V> _merger;

    /// <summary>
    /// 构造物品领域上下文 / Construct an item domain context.
    /// </summary>
    /// <param name="items">物品目录 / Item catalogue.</param>
    /// <param name="sheetTypes">板材类型列表 / Sheet type list.</param>
    /// <param name="itemDemands">物品件数需求 / Item amount demands.</param>
    /// <param name="merger">物品合并服务 / Item merger service.</param>
    public ItemContext(
        IReadOnlyDictionary<ItemId, ActualItem<V>>? items = null,
        IReadOnlyList<SheetType<V>>? sheetTypes = null,
        IReadOnlyDictionary<ItemId, ulong>? itemDemands = null,
        ItemMerger<V>? merger = null) {
        Items = items ?? new Dictionary<ItemId, ActualItem<V>>();
        SheetTypes = sheetTypes ?? Array.Empty<SheetType<V>>();
        ItemDemands = itemDemands ?? new Dictionary<ItemId, ulong>();
        _merger = merger ?? new ItemMerger<V>();
    }

    /// <summary>物品目录 / Item catalogue.</summary>
    public IReadOnlyDictionary<ItemId, ActualItem<V>> Items { get; }

    /// <summary>板材类型列表 / Sheet type list.</summary>
    public IReadOnlyList<SheetType<V>> SheetTypes { get; }

    /// <summary>物品件数需求 / Item amount demands.</summary>
    public IReadOnlyDictionary<ItemId, ulong> ItemDemands { get; }

    /// <summary>物品合并服务 / Item merger service.</summary>
    public ItemMerger<V> Merger => _merger;

    /// <summary>
    /// 构建聚合 / Build the aggregation.
    /// </summary>
    /// <remarks>
    /// 未给板材上限时，把**全部已知板材类型**按不限量登记；给了上限则**完全采用给定的映射**，不再补入未列出的类型。
    /// 两者混用会让"我只要这两种板"变成"我还要所有已知板"，是最容易静默出错的一处。
    /// With no limits given, **every known sheet type** is registered as unlimited; when limits are given the mapping is
    /// used **as-is**, with no unlisted type added. Mixing the two turns "I only want these two sheets" into "plus all
    /// known sheets" — the easiest place to go silently wrong.
    /// </remarks>
    /// <param name="sheetLimits">板材类型到可用数量，null 表示不限 / Sheet type to available amount, null unlimited.</param>
    /// <returns>需求聚合 / The demand aggregation.</returns>
    public Aggregation<V> Aggregate(IReadOnlyDictionary<SheetType<V>, ulong?>? sheetLimits = null) {
        IReadOnlyDictionary<SheetType<V>, ulong?> limits = sheetLimits is { Count: > 0 }
            ? sheetLimits
            : SheetTypes.ToDictionary(type => type, _ => (ulong?)null);
        return new Aggregation<V>(ItemDemands, limits);
    }

    /// <summary>
    /// 主板材类型，未配置时返回 null / Main sheet type, or null when none is configured.
    /// </summary>
    /// <param name="sheetLimits">板材上限，用于优先在可用板材中挑选 / Sheet limits, so the choice prefers available sheets.</param>
    /// <returns>主板材类型 / Main sheet type.</returns>
    public SheetType<V>? MainSheetType(IReadOnlyDictionary<SheetType<V>, ulong?>? sheetLimits = null) =>
        Aggregate(sheetLimits).MainSheet;
}
