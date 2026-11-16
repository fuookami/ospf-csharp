#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 列的检验数 / The reduced cost of a column.
/// </summary>
/// <remarks>
/// 与的 `layerReducedCost` 对齐：`检验数 = 该列实际面积 × 面积系数 − Σ(需求影子价格 × 该列对该需求的系数)`。
///
/// 三处必须沿用：
///
/// 1. **影子价格为零的需求直接跳过**：乘零不改变结果，但会让"没有定价的需求"和"定价为零的需求"在求和里混在一起，
///    排查时看不出差别。
/// 2. **系数用 `LayerDemandCoefficient`**，不是件数：面积需求下件数完全不对（已记档同一处）。
/// 3. **没有影子价格的需求跳过**（`continue`），不按零参与——缺失与零是两回事，与 的教训相同。
///
/// Aligned with its `layerReducedCost`: `reduced cost = the column's actual area × the area coefficient − Σ(demand shadow price × that column's coefficient for the demand)`. Three
/// things must be copied: **demands with a zero shadow price are skipped**, since multiplying by zero changes no total but blurs "unpriced demand" against "demand priced at zero" when
/// debugging; **the coefficient comes from `LayerDemandCoefficient`** rather than the unit count, which is simply wrong for area demands (recorded); and **demands without a shadow price
/// are skipped** rather than entering as zero, since missing and zero differ — the same lesson.
/// </remarks>
public static class ReducedCost
{
    /// <summary>
    /// 计算一列的检验数 / Compute one column's reduced cost.
    /// </summary>
    /// <param name="layer">层 / The layer.</param>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="shadowPrices">影子价格 / The shadow prices.</param>
    /// <param name="areaCoefficient">面积系数 / The area coefficient.</param>
    /// <returns>检验数 / The reduced cost.</returns>
    public static FltX LayerReducedCost(
        Layer<FltX> layer,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> shadowPrices,
        FltX? areaCoefficient = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        FltX coefficient = areaCoefficient ?? new FltX(1.0);

        FltX dualValue = new(0.0);
        foreach (Bpp2dAssignmentDemandEntry entry in demandEntries)
        {
            var key = new Bpp2dDemandShadowPriceKey(entry.Mode, entry.ItemId);
            if (!shadowPrices.TryGetValue(key, out FltX price))
            {
                continue;
            }
            // ponytail: **零价格/缺价的跳过在数值上不可观测**——`0 × 有限系数 = 0`，加进去结果一模一样。
            // 保留它是为了与基线逐条对齐，并在系数万一非有限时避免 `0 × ∞ = NaN` 污染整个求和。
            // 想让它可观测，得让 `LayerDemandCoefficient` 返回非有限值，而它现在只能返回面积或件数，做不到。
            // ponytail: **the zero/unpriced skips are numerically unobservable** — `0 × finite coefficient = 0`, so including them leaves the total identical. They are kept for parity with the
            // baseline and to avoid `0 × ∞ = NaN` poisoning the sum if a coefficient were ever non-finite. Making them observable would require `LayerDemandCoefficient` to return a non-finite
            // value, which it cannot do today (it returns an area or a count).
            if (price.ToFlt64().ToDouble() == 0.0)
            {
                continue;
            }
            dualValue = dualValue.Plus(
                price.Times(LayerAssignmentMath.LayerDemandCoefficient(layer, entry)));
        }
        return layer.Area.Value.ToFltX().Times(coefficient).Minus(dualValue);
    }

    /// <summary>
    /// 把字符串键的影子价格转成值对象键 / Convert string-keyed shadow prices to value-object keys.
    /// </summary>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="shadowPrices">字符串键的影子价格 / The string-keyed shadow prices.</param>
    /// <returns>值对象键的影子价格 / The value-object-keyed shadow prices.</returns>
    /// <remarks>
    /// 与基线 `shadowPriceKeysOf` 对齐。**只保留需求项里出现过的键**：求解器可能返回额外的键（比如松弛变量的），
    /// 全部收下会让检验数里混进不相关的价格。**找不到就跳过**，不按零补。
    ///
    /// Aligned with its `shadowPriceKeysOf`. **Only keys present in the demand entries survive**: the solver may return extras such as slack-variable prices, and taking them all would
    /// mix unrelated prices into the reduced cost. **A missing key is skipped**, not filled with zero.
    /// </remarks>
    public static IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> ShadowPriceKeysOf(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyDictionary<DemandModeKey, FltX> shadowPrices)
    {
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        var result = new Dictionary<Bpp2dDemandShadowPriceKey, FltX>();
        foreach (Bpp2dAssignmentDemandEntry entry in demandEntries)
        {
            // **复用 `DemandConstraint.ModeTag`**，不自己拼字符串：两处各写一份会让键在某一侧悄悄不匹配
            // （第一次用 `ToString.ToLowerInvariant`，得到 `itemamount` 而不是 `item_amount`，价格全查不到）。
            // **Reuse `DemandConstraint.ModeTag`** rather than formatting here: two copies of the mapping let the key silently mismatch on one side. first used
            // `ToString.ToLowerInvariant`, which yields `itemamount` instead of `item_amount`, so no price was ever found.
            var modeKey = new DemandModeKey(
                Service.Limits.DemandConstraint.ModeTag(entry.Mode), entry.ItemId.Value);
            if (!shadowPrices.TryGetValue(modeKey, out FltX price))
            {
                continue;
            }
            result[new Bpp2dDemandShadowPriceKey(entry.Mode, entry.ItemId)] = price;
        }
        return result;
    }
}

/// <summary>
/// 检验数筛选器 / The reduced-cost filter.
/// </summary>
/// <remarks>
/// 与基线 `reducedCostFilter` 对齐：给每个候选列算检验数，**只留检验数小于负容差的**（真正能改进目标的），
/// 并按检验数升序排（最负的在前，先被用上）。
///
/// 两处必须沿用：
///
/// 1. **阈值是 `-容差`**，不是零：浮点检验数不会精确为零，用零会让"恰好为零"的候选被当成改进列，
///    每轮都收一批无用的列，直到迭代上限。
/// 2. **排序在筛选之后**：先筛后排与先排后筛结果相同，但先筛能少排一批——**这里是"顺序不影响正确性、但影响开销"的那类**，
///    所以只在注释里记下，不当作正确性要求。
///
/// Aligned with its `reducedCostFilter`: every candidate gets a reduced cost, **only those below the negative tolerance survive** (the ones that genuinely improve), and they are sorted
/// ascending by reduced cost so the most negative is used first. Two things must be copied: **the threshold is `-tolerance` rather than zero**, since a floating-point reduced cost is never
/// exactly zero and a zero threshold would treat "exactly zero" as improving and collect a batch of useless columns each round until the limit; and **sorting happens after filtering**, which
/// gives the same result either way but filters fewer — **an order that does not affect correctness but does affect cost**, recorded here rather than treated as a correctness rule.
/// </remarks>
public sealed class ReducedCostFilter
{
    private readonly IReadOnlyList<Bpp2dAssignmentDemandEntry> _demandEntries;
    private readonly FltX _areaCoefficient;
    private readonly FltX _tolerance;

    /// <summary>
    /// 构造检验数筛选器 / Construct the reduced-cost filter.
    /// </summary>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="areaCoefficient">面积系数 / The area coefficient.</param>
    /// <param name="tolerance">容差 / The tolerance.</param>
    public ReducedCostFilter(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        FltX? areaCoefficient = null,
        FltX? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(demandEntries);
        _demandEntries = demandEntries;
        _areaCoefficient = areaCoefficient ?? new FltX(1.0);
        _tolerance = tolerance ?? new FltX(1e-6);
    }

    /// <summary>面积系数 / The area coefficient</summary>
    public FltX AreaCoefficient => _areaCoefficient;

    /// <summary>容差 / The tolerance</summary>
    public FltX Tolerance => _tolerance;

    /// <summary>
    /// 筛选候选列 / Filter the candidate columns.
    /// </summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <returns>改进的候选，按检验数升序 / The improving candidates, ascending by reduced cost.</returns>
    public IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> Filter(
        ColumnGeneration.ColumnGenerationState state,
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> candidates)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(candidates);

        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> prices =
            ReducedCost.ShadowPriceKeysOf(
                _demandEntries,
                state.ShadowPrices ?? new Dictionary<DemandModeKey, FltX>());
        double threshold = -_tolerance.ToFlt64().ToDouble();

        var scored = new List<(Bpp2dLayerGenerationResult<FltX> Candidate, FltX Cost)>(candidates.Count);
        foreach (Bpp2dLayerGenerationResult<FltX> candidate in candidates)
        {
            FltX cost = ReducedCost.LayerReducedCost(candidate.Layer, _demandEntries, prices, _areaCoefficient);
            // **阈值是负容差**，不是零。
            // **The threshold is the negative tolerance**, not zero.
            if (cost.ToFlt64().ToDouble() < threshold)
            {
                scored.Add((candidate, cost));
            }
        }
        scored.Sort((left, right) =>
            left.Cost.ToFlt64().ToDouble().CompareTo(right.Cost.ToFlt64().ToDouble()));
        return scored
            .Select(entry => entry.Candidate with { ReducedCost = entry.Cost })
            .ToArray();
    }
}
