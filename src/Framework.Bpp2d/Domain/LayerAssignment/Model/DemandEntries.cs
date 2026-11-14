#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 从物品清单产出需求项 / Build demand entries from an item list.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationApplicationService.demandEntries` 对齐。四处必须照抄：
///
/// 1. **件数需求总是加**：面积需求是"额外"的一条，不是替代。只登记面积需求会让件数约束消失，
///    排出来的板材件数可以少到任意——件数守恒就没人管了。
/// 2. **面积需求按"单件包围面积 × 件数"**，用 `BoundingArea` 而不是 `Area`：圆形件的 `Area` 是圆面积，
///    但它在板材上占的是外接矩形，排版按包围面积算才不会低估。
/// 3. **两条需求共用同一个物品 id，按物品顺序"先件数后面积"**：顺序决定了需求约束的索引，
///    错位会让影子价格安到另一条需求上（B662 的教训）。
/// 4. **每个物品各出一条（或两条）**，不去重、不合并：同一个物品出现两次是数据问题，不是这里该收拾的。
///
/// Aligned with the baseline's `demandEntries`. Four things must be copied: **the amount demand is always added**, since the area one is an extra rather than a replacement and dropping the
/// amount constraint leaves sheet counts unbounded; **the area demand is per-piece bounding area times the count**, using `BoundingArea` rather than `Area`, because a circle's `Area` is the circle
/// itself while what it occupies on a sheet is its bounding box; **both entries share the item's id in "amount then area" order**, since that order fixes the demand constraint's index and a
/// mismatch would attach the shadow price to the wrong demand (the B662 lesson); and **each item contributes one or two entries** with no deduplication or merging, duplicate items being a data
/// problem rather than something to fix here.
/// </remarks>
public static class DemandEntries
{
    /// <summary>
    /// 产出需求项 / Build the demand entries.
    /// </summary>
    /// <param name="items">物品与件数 / The items with their amounts.</param>
    /// <param name="withAreaDemand">是否附带面积需求 / Whether to add the area demands.</param>
    /// <returns>需求项清单 / The demand entries.</returns>
    public static IReadOnlyList<Bpp2dAssignmentDemandEntry> Build(
        IReadOnlyDictionary<ActualItem<FltX>, ulong> items,
        bool withAreaDemand = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        var entries = new List<Bpp2dAssignmentDemandEntry>(items.Count * 2);
        foreach (KeyValuePair<ActualItem<FltX>, ulong> pair in items)
        {
            ActualItem<FltX> item = pair.Key;
            ulong amount = pair.Value;
            // **件数需求总是第一条**。
            // **The amount demand is always the first entry.**
            entries.Add(new Bpp2dAssignmentDemandEntry(
                Mode: Bpp2dAssignmentDemandMode.ItemAmount,
                ItemId: item.Id,
                Demand: new Bpp2dAssignmentDemandValue.Amount(amount),
                UpperBound: null));

            if (!withAreaDemand)
            {
                continue;
            }
            // ponytail: 基线用 `amountScaler` 把件数逐次加一累加成数值，为的是不引入 `Double`。
            // 这里直接走 `decimal` 转换——同样是精确整数、没有浮点中转，而且少一次 O(件数) 循环。
            // ponytail: the baseline accumulates the count one at a time via `amountScaler` to avoid introducing a `Double`. This casts straight to `decimal` instead — equally exact, still
            // floating-point free, and without an O(count) loop.
            FltX scaler = new((decimal)amount);
            Quantity<FltX> perPiece = item.Shape.BoundingArea;
            Quantity<FltX> total = new(perPiece.Value.Times(scaler), perPiece.Unit);
            entries.Add(new Bpp2dAssignmentDemandEntry(
                Mode: Bpp2dAssignmentDemandMode.ItemArea,
                ItemId: item.Id,
                Demand: new Bpp2dAssignmentDemandValue.Area(total),
                UpperBound: null));
        }
        return entries;
    }
}
