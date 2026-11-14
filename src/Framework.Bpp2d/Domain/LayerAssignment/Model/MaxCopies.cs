#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 每列的最大份数 / The maximum copies per column.
/// </summary>
/// <remarks>
/// 与基线 `Assignment.kt` 的 `maxCopiesPerColumn` 对齐：按**件数需求**与**该列对每种物品的覆盖量**估算
/// "这一列最多用几份"。
///
/// 四处必须照抄：
///
/// 1. **只看件数需求**（`ItemAmount`）。面积需求下"一份够不够"取决于板材面积，不是件数，硬套件数会给出错的份数上界；
///    基线对面积需求直接取 `1`，本实现同样如此。
/// 2. **取所有物品里最小的份数**（`minOf`），不是最大：一列要同时满足它覆盖的每种物品，**最紧的那个决定份数**。
///    取最大会让某种物品的需求永远欠着。
/// 3. **向上取整**：`(demand + covered - 1) / covered`。整数除法向下取整会让 `demand = 5, covered = 2` 得到 2，
///    少一份就欠一件。
/// 4. **未出现在任何需求量中的列限制为一份**（且上界至少为 1）。没有覆盖价值的列如果允许无限份，
///    求解器会拿它凑数。
///
/// Aligned with the baseline's `maxCopiesPerColumn`: it estimates how many copies a column may use from the **item-amount demands** and that column's **coverage of each item**. Four things must be
/// copied: **only item-amount demands count**, since under an area demand whether one copy suffices depends on sheet area rather than item count and forcing a count would give a wrong bound —
/// the baseline uses 1 for area demands and so does this implementation; **the minimum across items is taken** rather than the maximum, because a column must satisfy every item it covers and
/// **the tightest one decides**, while the maximum would leave some item permanently short; **the division rounds up**, since integer division would make `demand = 5, covered = 2` give 2 and
/// leave one item uncovered; and **a column absent from every demand is limited to one copy** (with a floor of 1), because a column with no coverage value and no bound would be used as filler.
/// </remarks>
public static class MaxCopies
{
    /// <summary>
    /// 计算每列的最大份数 / Compute the maximum copies per column.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <returns>与层一一对应的份数 / One copy count per layer.</returns>
    public static IReadOnlyList<ulong> PerColumn(
        IReadOnlyList<Layer<FltX>> layers,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(demandEntries);

        // **只收集件数需求**：面积需求不参与份数估算。
        // **Only item-amount demands are collected**: area demands do not take part in the copy estimate.
        var amountDemands = new Dictionary<ItemId, ulong>();
        foreach (Bpp2dAssignmentDemandEntry entry in demandEntries)
        {
            if (entry.Mode == Bpp2dAssignmentDemandMode.ItemAmount
                && entry.Demand is Bpp2dAssignmentDemandValue.Amount amount)
            {
                amountDemands[entry.ItemId] = amount.Value;
            }
        }

        var result = new List<ulong>(layers.Count);
        foreach (Layer<FltX> layer in layers)
        {
            ulong? bound = null;
            foreach (KeyValuePair<ItemId, ulong> coverage in layer.ItemAmounts)
            {
                if (coverage.Value == 0UL)
                {
                    continue;
                }
                if (!amountDemands.TryGetValue(coverage.Key, out ulong demand))
                {
                    continue;
                }
                ulong covered = coverage.Value < 1UL ? 1UL : coverage.Value;
                // **向上取整**：整数除法向下取整会少一份。
                // **Round up**: integer division would come up one copy short.
                ulong copies = (demand + covered - 1UL) / covered;
                // **取最小值**：最紧的那种物品决定份数。
                // **Take the minimum**: the tightest item decides the copy count.
                bound = bound is null ? copies : (copies < bound.Value ? copies : bound.Value);
            }
            // **没有覆盖价值的列限制为一份。**
            // **A column with no coverage value is limited to one copy.**
            ulong bounded = bound ?? 1UL;
            result.Add(bounded < 1UL ? 1UL : bounded);
        }
        return result;
    }
}
