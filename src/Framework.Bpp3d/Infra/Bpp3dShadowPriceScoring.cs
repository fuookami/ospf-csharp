#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 影子价格感知的层评分 / Shadow-price-aware layer scoring.
/// </summary>
/// <remarks>
/// 移植基线 `bpp3d-domain-layer-generation-context` 的 `shadowPriceAwareLayerScore`：
/// 对每个活跃需求条目求 `影子价格 × 该层对该条目的贡献` 之和。
///
/// Ports its `shadowPriceAwareLayerScore`: for every active demand entry, sum `shadow price × the layer's contribution to that entry`.
///
/// **修复前** `Bpp3dLayerGenerationRequest.ScoreByShadowPrice` 只是一个 `object?`——谁都能塞，但**没有任何代码读取**，
/// 于是 `EnableShadowPriceAwareRequestScore` 这个开关开与不开都不改变任何行为。`Bpp2d` 侧早已是强类型函数，只有 Bpp3d 留了这个洞。
///
/// **Before the fix** `Bpp3dLayerGenerationRequest.ScoreByShadowPrice` was just an `object?` — anyone could fill it, but **no code ever read it**, so
/// `EnableShadowPriceAwareRequestScore` changed nothing whether on or off. `Bpp2d` has long had a strongly typed function; only Bpp3d had the hole.
///
/// **评分用 `double` 而不是 `V`**：`V` 是泛型参数，从 `double` 构造它需要一个转换器实例，而排序只关心**顺序**。
/// 把评分留在标量域里既省掉转换器，也让"排序结果"与"数值精度"解耦。
///
/// **The score is a `double`, not a `V`**: `V` is a generic parameter and constructing one from a `double` needs a converter instance, while ranking only
/// cares about **order**. Keeping the score in the scalar domain drops the converter and decouples "the ordering" from "numeric precision".
/// </remarks>
public static class Bpp3dShadowPriceScoring {
    /// <summary>
    /// 构造评分函数 / Build the scoring function.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <returns>评分函数 / The scoring function.</returns>
    public static Func<BinLayer, Bpp3dLayerGenerationRequest<V>, double> Scorer<V>()
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Score<V>;

    /// <summary>
    /// 对一层按影子价格评分 / Score one layer by its shadow prices.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="layer">层 / The layer.</param>
    /// <param name="request">层生成请求 / The layer generation request.</param>
    /// <returns>评分 / The score.</returns>
    public static double Score<V>(BinLayer layer, Bpp3dLayerGenerationRequest<V> request)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShadowPrices is null) {
            return 0.0;
        }

        // 活跃条目：请求显式给了就按它，否则退回影子价格表的键。
        // Active entries: the request's explicit list when present, otherwise the shadow-price keys (matching the code).
        var activeEntries = new List<DemandModeKey>();
        if (request.DemandEntries is { Count: > 0 } entries) {
            foreach (LayerGenerationDemandEntry entry in entries) {
                activeEntries.Add(new DemandModeKey(entry.Mode, entry.Key, entry.QuantityUnit));
            }
        }
        else {
            activeEntries.AddRange(request.ShadowPrices.Keys);
        }

        double total = 0.0;
        foreach (DemandModeKey entry in activeEntries) {
            if (!request.ShadowPrices.TryGetValue(entry, out V price)) {
                continue;
            }
            total += price.ToFlt64().ToDouble() * ContributionOf(layer, entry.Key);
        }
        return total;
    }

    /// <summary>
    /// 一层对某个需求条目的贡献 / A layer's contribution to one demand entry.
    /// </summary>
    /// <remarks>
    /// 只处理 `ItemKey`：`MaterialKeyRef` 需要物料的配比信息，而 `BinLayer` 目前不携带它。
    /// **不认识的键返回 0，而不是猜一个数**——猜出来的评分会让排序看起来正常但排错。
    ///
    /// Only `ItemKey` is handled: `MaterialKeyRef` would need the item's material composition, which `BinLayer` does not carry today.
    /// **An unrecognised key contributes 0 rather than a guessed number** — a guessed score would make the ordering look plausible while being wrong.
    /// </remarks>
    /// <param name="layer">层 / The layer.</param>
    /// <param name="key">需求键 / The demand key.</param>
    /// <returns>贡献量 / The contribution.</returns>
    public static double ContributionOf(BinLayer layer, Bpp3dDemandKey key) {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(key);
        if (key is not Bpp3dDemandKey.ItemKey itemKey) {
            return 0.0;
        }
        double total = 0.0;
        foreach (QuantityPlacement3<Item, FltX> placement in layer.Units) {
            if (placement.Unit.Id == itemKey.ItemId) {
                total += 1.0;
            }
        }
        return total;
    }
}
