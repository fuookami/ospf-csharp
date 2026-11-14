#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 从对偶解里取出需求影子价格 / Extract demand shadow prices from a dual solution.
/// </summary>
/// <remarks>
/// 与基线的影子价格链路对齐：RMP 求解完 LP 后，把每条需求约束的对偶值按 `(模式, 物品)` 收进影子价格表，
/// 列生成的检验数再按这张表算。
///
/// 四处必须照抄：
///
/// 1. **取下界那条约束的对偶值**（`_lb_`），不是上界。需求约束是"装载 ≥ 需求量"，
///    下界才是"多装一件能省多少成本"的边际值；上界说的是另一件事。
///    取错会让检验数整体偏符号，选出来的列全是错的方向。
/// 2. **缺项跳过**，不补零：某种需求没有对偶值说明它没进模型，补零等于宣称"它的价格是零"。
/// 3. **键用 `DemandConstraint.ModeTag`**，不自己拼字符串——两处各写一份会让键悄悄不匹配（B659 的教训）。
/// 4. **索引与需求项一致**：约束名里的下标就是需求项在清单里的位置。
///
/// Aligned with the baseline's shadow-price chain: after the RMP solves the LP, each demand constraint's dual value goes into the shadow-price table keyed by `(mode, item)`, and the column
/// generation's reduced cost reads that table. Four things must be copied: **take the lower-bound constraint's dual** (`_lb_`), not the upper one, since a demand constraint reads "load ≥ demand"
/// and only the lower bound is the marginal value of loading one more unit, while the upper bound says something else entirely — getting it backwards flips the sign of every reduced cost and
/// selects columns in the wrong direction; **skip missing entries** rather than filling zero, since a demand without a dual never entered the model and zero would claim its price is zero; **key
/// with `DemandConstraint.ModeTag`** rather than formatting here, because two copies of the mapping let the key silently mismatch (the B659 lesson); and **the index matches the demand entry's
/// position**, since the constraint name's index is that entry's index.
/// </remarks>
public static class DemandShadowPrices
{
    /// <summary>
    /// 需求约束的默认名 / The default demand-constraint name.
    /// </summary>
    /// <remarks>
    /// **与 `DemandConstraint` 的默认名同一个值**，两边都从这一个常亮处取——写两遍会让提取键与约束名悄悄分家（B659 的教训）。
    ///
    /// **The same value as `DemandConstraint`'s default name**, both sides reading from this one place; two copies let the extraction key and the constraint name silently drift apart (the
    /// B659 lesson).
    /// </remarks>
    public const string DefaultConstraintName = "demand";

    /// <summary>
    /// 取出需求影子价格 / Extract the demand shadow prices.
    /// </summary>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="dualSolution">对偶解 / The dual solution.</param>
    /// <param name="constraintNamePrefix">约束名前缀 / The constraint-name prefix.</param>
    /// <returns>影子价格表 / The shadow-price table.</returns>
    /// <remarks>
    /// 前缀默认是 `demand`（`DemandConstraint` 的默认名）。
    ///
    /// The prefix defaults to `demand`, the `DemandConstraint` default name.
    /// </remarks>
    public static IReadOnlyDictionary<DemandModeKey, FltX> Extract(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        MetaDualSolution dualSolution,
        string constraintNamePrefix = DefaultConstraintName)
    {
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(dualSolution);
        ArgumentException.ThrowIfNullOrEmpty(constraintNamePrefix);

        var byName = new Dictionary<string, Flt64>(dualSolution.Constraints.Count);
        foreach (KeyValuePair<MathConstraint, Flt64> pair in dualSolution.Constraints)
        {
            byName[pair.Key.Name] = pair.Value;
        }

        var result = new Dictionary<DemandModeKey, FltX>();
        for (int index = 0; index < demandEntries.Count; ++index)
        {
            Bpp2dAssignmentDemandEntry entry = demandEntries[index];
            string name = constraintNamePrefix + "_" + DemandConstraint.ModeTag(entry.Mode) + "_lb_" + index;
            if (!byName.TryGetValue(name, out Flt64 price))
            {
                continue;
            }
            // ponytail: 同一个 `(模式, 物品)` 出现两次时后者覆盖前者。需求清单里重复登记同一物品是数据问题，
            // 这里不抛错——列生成仍能用其中一条继续跑。要严格化就在登记阶段拒绝重复。
            // ponytail: when the same `(mode, item)` appears twice the later entry overwrites the earlier one. Duplicate registration in the demand list is a data problem and this does not
            // throw, since column generation can still proceed on one of them; tightening means rejecting duplicates at registration.
            result[new DemandModeKey(DemandConstraint.ModeTag(entry.Mode), entry.ItemId.Value)] =
                new FltX(price.ToDouble());
        }
        return result;
    }
}
