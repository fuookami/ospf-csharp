#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item;
/// <summary>
/// 物料聚合接口 / Item aggregation interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IItemAggregation<V> where V : struct, IFloatingNumber<V> {
    /// <summary>方案集合 / Schemas.</summary>
    IReadOnlyList<Schema<V>> Schemas { get; }

    /// <summary>箱型集合 / Bin types.</summary>
    IReadOnlyDictionary<BinType<V>, ulong?> Bins { get; }
}

/// <summary>
/// 物料聚合 / Item aggregation.
/// 聚合方案、箱型与使用统计 / Aggregates schemas, bin types, and usage statistics.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class ItemAggregation<V> : IItemAggregation<V> where V : struct, IFloatingNumber<V> {
    private readonly Dictionary<Item<V>, ulong> _usedItems = new();
    private readonly Dictionary<BinType<V>, ulong> _usedBins = new();

    public IReadOnlyList<Schema<V>> Schemas { get; }
    public IReadOnlyDictionary<BinType<V>, ulong?> Bins { get; }

    /// <summary>
    /// 构造物料聚合 / Construct item aggregation.
    /// </summary>
    public ItemAggregation(
        IReadOnlyList<Schema<V>> schemas,
        IReadOnlyDictionary<BinType<V>, ulong?> bins) {
        Schemas = schemas;
        Bins = bins;
    }

    /// <summary>批次号列表 / Batch number list.</summary>
    public IReadOnlyList<string> BatchNos => Schemas.Select(s => s.BatchNo).ToList();

    /// <summary>主箱型 / Main bin type.</summary>
    public BinType<V>? MainBin => Bins.Keys.FirstOrDefault(b => b.IsMain)
                                   ?? Bins.Keys.OrderByDescending(b => b.Volume).FirstOrDefault();

    /// <summary>剩余货物 / Rest items.</summary>
    public IReadOnlyDictionary<Item<V>, ulong> RestItems =>
        Schemas.SelectMany(s => s.PatternedItems)
            .GroupBy(p => p.Item)
            .ToDictionary(
                g => g.Key,
                g => {
                    long total = g.Aggregate(0L, (acc, p) => acc + (long)p.Amount.ToFlt64().ToDouble());
                    ulong used = _usedItems.TryGetValue(g.Key, out ulong u) ? u : 0UL;
                    return (ulong)global::System.Math.Max(0, total - (long)used);
                });

    /// <summary>
    /// 使用货物 / Use items.
    /// </summary>
    public void UseItems(IReadOnlyDictionary<Item<V>, ulong> items) {
        foreach ((Item<V>? item, ulong amount) in items) {
            _usedItems[item] = (_usedItems.TryGetValue(item, out ulong cur) ? cur : 0) + amount;
        }
    }

    /// <summary>
    /// 使用箱型 / Use bins.
    /// </summary>
    public void UseBins(IReadOnlyDictionary<BinType<V>, ulong> bins) {
        foreach ((BinType<V>? bin, ulong amount) in bins) {
            _usedBins[bin] = (_usedBins.TryGetValue(bin, out ulong cur) ? cur : 0) + amount;
        }
    }
}
