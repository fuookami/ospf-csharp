#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 长度上界剪枝过滤结果 / Length-bound pruning filter result.
/// </summary>
/// <typeparam name="TEntry">条目类型 / Entry type.</typeparam>
/// <param name="FilteredEntries">过滤后的条目 / Filtered entries.</param>
/// <param name="PrunedCount">被剪枝的条目数 / Pruned entry count.</param>
internal sealed record LengthBoundFilterResult<TEntry>(
    IReadOnlyList<TEntry> FilteredEntries,
    long PrunedCount
);

/// <summary>
/// 长度上界剪枝辅助方法 / Length-bound pruning helpers.
/// </summary>
internal static class GenerationLengthPruning {
    /// <summary>
    /// 检查产品是否满足长度上界 / Check whether a product fits the length bound.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="productLength">产品长度（可空）/ Product length (nullable).</param>
    /// <param name="maxOverProduceLength">最大超产长度（可空）/ Max over-produce length (nullable).</param>
    /// <returns>true 如果满足长度约束 / true if fits the length bound.</returns>
    public static bool FitsLengthBound<V>(Quantity<V>? productLength, Quantity<V>? maxOverProduceLength)
        where V : struct, IComparable<V> {
        if (maxOverProduceLength is not { } maxLength) {
            return true;
        }

        if (productLength is not { } pLength) {
            return true;
        }

        return pLength.Value.CompareTo(maxLength.Value) <= 0;
    }

    /// <summary>
    /// 按长度上界过滤条目 / Filter entries by length bound.
    /// </summary>
    /// <typeparam name="TEntry">条目类型 / Entry type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="entries">条目列表 / Entry list.</param>
    /// <param name="maxOverProduceLength">最大超产长度（可空）/ Max over-produce length (nullable).</param>
    /// <param name="getProductLength">获取产品长度的函数 / Function to get product length.</param>
    /// <returns>过滤结果 / Filter result.</returns>
    public static LengthBoundFilterResult<TEntry> FilterByLengthBound<TEntry, V>(
        IReadOnlyList<TEntry> entries,
        Quantity<V>? maxOverProduceLength,
        Func<TEntry, Quantity<V>?> getProductLength)
        where V : struct, IComparable<V> {
        if (maxOverProduceLength is null) {
            return new LengthBoundFilterResult<TEntry>(entries, 0L);
        }

        var filtered = new List<TEntry>();
        long prunedCount = 0L;
        foreach (TEntry? entry in entries) {
            if (FitsLengthBound(getProductLength(entry), maxOverProduceLength)) {
                filtered.Add(entry);
            }
            else {
                prunedCount++;
            }
        }
        return new LengthBoundFilterResult<TEntry>(filtered, prunedCount);
    }
}
