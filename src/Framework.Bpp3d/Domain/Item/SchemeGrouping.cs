// 方案分组：按图样归并物品 / Scheme grouping: merge items by pattern.
// 对应固定 Kotlin 基线 `item/model/Schema.kt`（41 行）中 `Scheme` 的分组与排序行为。
// Mirrors the grouping and ordering behaviour of `Scheme` in the pinned baseline's `Schema.kt`.
//
// **与基线的一处结构差异**：基线 `Scheme` 在 `init` 块里做分组，分组结果作为构造期算好的 `patternedItems` 字段；
// C# 的 `Schema<V>` 是纯数据 record（`BatchNo` + `PatternedItems`），没有构造期逻辑。这里把分组抽成**独立纯函数**
// `SchemeGrouping.GroupByPattern`，而不是给 record 加 init 逻辑——record 的值语义要求构造只做赋值，混入计算会让
// `Equals`／`GetHashCode` 与"实际内容"脱节。
// **One structural difference from the baseline**: the baseline's `Scheme` groups inside an `init` block and stores the
// result in a construction-time `patternedItems` field, whereas C#'s `Schema<V>` is a pure data record with no
// construction-time logic. extracts the grouping into a **standalone pure function**
// `SchemeGrouping.GroupByPattern` instead of adding init logic to the record: record value semantics require construction
// to only assign, and mixing in computation would divorce `Equals` / `GetHashCode` from the actual content.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain;

/// <summary>
/// 方案分组 / Scheme grouping.
/// </summary>
public static class SchemeGrouping {
    /// <summary>
    /// 按图样归并物品，组内按重量升序 / Group items by pattern, ordering within groups by ascending weight.
    /// </summary>
    /// <remarks>
    /// 两条规则都来自基线：
    /// 1. **先按重量升序排序再分组**——因此每组内部天然有序，重件在后。后续层生成按顺序取件时，轻件先上，重件压底。
    /// 2. **同图样归入同一组，组按首次出现顺序排列**。基线用插入序列表实现，因此"首次出现"就是排序后第一个该图样物品的位置。
    ///
    /// Two rules, both from the baseline:
    /// 1. **Sort by ascending weight before grouping** — each group is therefore internally ordered with heavier items last.
    /// Layer generation taking items in order puts light items on top and heavy ones at the bottom.
    /// 2. **Same pattern joins the same group, groups follow first-appearance order.** The baseline uses an insertion-ordered
    /// list, so "first appearance" is the position of the first item of that pattern after sorting.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <returns>按图样分组的物品与件数 / Items and amounts grouped by pattern.</returns>
    public static IReadOnlyList<IReadOnlyList<(Item<V> Item, UInt64 Amount)>> GroupByPattern<V>(
        IReadOnlyList<(Item<V> Item, UInt64 Amount)> items) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(items);

        var groups = new List<List<(Item<V>, UInt64)>>();
        var positions = new Dictionary<PatternKey, int>();
        foreach ((Item<V> item, UInt64 amount) in items
            .OrderBy(entry => entry.Item.Weight.Value.ToFlt64().ToDouble())) {
            PatternKey key = PatternKeyOf(item);
            if (positions.TryGetValue(key, out int index)) {
                groups[index].Add((item, amount));
                continue;
            }
            positions[key] = groups.Count;
            groups.Add(new List<(Item<V>, UInt64)> { (item, amount) });
        }
        return groups.Select(group => (IReadOnlyList<(Item<V>, UInt64)>)group).ToList();
    }

    /// <summary>
    /// 计算物品的图样键 / Compute an item's pattern key.
    /// </summary>
    /// <remarks>
    /// **图样由批次号、仓库与包装属性共同决定**，三者任一不同就视为不同图样。这是 C# 侧对基线 `ItemPattern` 的等价表达：
    /// 基线有独立的 `ItemPattern` record，C# 的 `Item&lt;V&gt;` 是扁平结构，因此在此把同样的字段组合成键。
    /// 用字符串签名而不是直接以 `PackageAttribute` 作字典键——已有三次"深值对象做字典键"失效的先例。
    ///
    /// **A pattern is determined jointly by batch number, warehouse and package attribute**, and any difference means a
    /// different pattern. This is the C# equivalent of the baseline's `ItemPattern`: the baseline has a dedicated record
    /// while C#'s `Item&lt;V&gt;` is flat, so the same fields are combined into a key here. A string signature is used instead of
    /// keying directly on `PackageAttribute` — this session has already seen three failures from deep value objects as
    /// dictionary keys.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <returns>图样键 / The pattern key.</returns>
    private static PatternKey PatternKeyOf<V>(Item<V> item) where V : struct, IFloatingNumber<V> =>
        new(
            item.BatchNo ?? string.Empty,
            item.Warehouse ?? string.Empty,
            item.PackageAttribute.PackageType.ToString());

    /// <summary>
    /// 图样键 / Pattern key.
    /// </summary>
    /// <param name="BatchNo">批次号 / Batch number.</param>
    /// <param name="Warehouse">仓库 / Warehouse.</param>
    /// <param name="PackageType">包装类型 / Package type.</param>
    private sealed record PatternKey(string BatchNo, string Warehouse, string PackageType);
}
