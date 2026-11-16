// 尾板优化器 / Tail sheet optimiser.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Application;

/// <summary>
/// 一个候选列对剩余需求的评估 / An evaluation of one candidate column against the residual demand.
/// </summary>
/// <param name="Layer">候选列 / The candidate column.</param>
/// <param name="Covered">覆盖的剩余需求件数 / Residual demand pieces covered.</param>
/// <param name="Wasted">超出需求的件数 / Pieces exceeding the demand.</param>
public sealed record LayerEvaluation(Layer<FltX> Layer, int Covered, int Wasted);

/// <summary>
/// 尾板优化器 / Tail sheet optimiser.
/// </summary>
/// <remarks>
/// 列生成保证"覆盖需求"，但尾板通常是一整块板材的排布，会**超出**需求。此前的裁剪是先放先得，
/// 尾板用哪一列完全取决于选列结果。本优化器在裁剪阶段重新选列：按剩余需求枚举候选，
/// 先要覆盖得多，再要超出得少。
///
/// Column generation guarantees **coverage** of demand, but the tail sheet is usually a whole-sheet layout and therefore **exceeds** demand. The previous trimming was
/// first-come-first-served, so the tail sheet kept whatever column the selection happened to pick. This optimiser re-selects during trimming: it enumerates the
/// candidates against the residual demand and prefers more coverage first, then less overshoot.
/// </remarks>
public sealed class TailSheetOptimizer {
    private readonly IReadOnlyList<Layer<FltX>> _candidates;

    /// <summary>
    /// 构造优化器 / Construct the optimiser.
    /// </summary>
    /// <param name="candidates">可用于尾板的候选列 / Candidate columns usable for the tail sheet.</param>
    public TailSheetOptimizer(IReadOnlyList<Layer<FltX>>? candidates = null) {
        _candidates = candidates ?? System.Array.Empty<Layer<FltX>>();
    }

    /// <summary>
    /// 评估一个候选列对剩余需求的覆盖与超出 / Evaluate how a candidate covers and exceeds the residual demand.
    /// </summary>
    /// <param name="layer">候选列 / The candidate column.</param>
    /// <param name="remaining">剩余需求 / The residual demand.</param>
    /// <returns>评估结果 / The evaluation.</returns>
    /// <remarks>
    /// **`Covered` 与 `Wasted` 是两件事，不能合并成一个"净收益"**：一个产量远超需求的列，覆盖数被封顶在需求量上，
    /// 差额全部进 `Wasted`。若把两者相减，"覆盖 100 浪费 100" 与 "覆盖 0 浪费 0" 会得到同一个数，
    /// 而它们对尾板的意义完全相反。
    ///
    /// **`Covered` and `Wasted` are two things and cannot be folded into one "net gain"**: for a column producing far more than demanded, coverage is capped at the demand
    /// and the excess goes to `Wasted`. Subtracting them would make "cover 100, waste 100" equal "cover 0, waste 0", when the two mean opposite things for a tail sheet.
    /// </remarks>
    public static LayerEvaluation Evaluate(
        Layer<FltX> layer,
        IReadOnlyDictionary<string, ulong> remaining) {
        System.ArgumentNullException.ThrowIfNull(layer);
        System.ArgumentNullException.ThrowIfNull(remaining);

        int covered = 0;
        int wasted = 0;
        foreach (KeyValuePair<ItemId, ulong> entry in layer.ItemAmounts) {
            int produced = (int)entry.Value;
            int demand = remaining.TryGetValue(entry.Key.Value, out ulong known) ? (int)known : 0;
            covered += System.Math.Min(produced, demand);
            if (produced > demand) {
                wasted += produced - demand;
            }
        }
        return new LayerEvaluation(layer, covered, wasted);
    }

    /// <summary>
    /// 在候选中选出对剩余需求最优的列 / Pick the candidate that fits the residual demand best.
    /// </summary>
    /// <param name="original">原列，可为空 / The original column, possibly null.</param>
    /// <param name="candidates">候选列 / The candidate columns.</param>
    /// <param name="remaining">剩余需求 / The residual demand.</param>
    /// <returns>最优列评估，无候选时为空 / The best evaluation, or null when there is no candidate.</returns>
    /// <remarks>
    /// 排序是**词典序**：覆盖数降序 → 超出数升序 → 原列优先。
    /// 只按覆盖排的话，一个覆盖相同但浪费巨大的列会被选中；只按浪费排的话，一个几乎不覆盖需求的列会赢。
    /// 最后那一条不是美观问题：**无谓换列会让尾板的排布与前面几板不一致**，操作上更难执行。
    ///
    /// The ordering is **lexicographic**: coverage descending, then overshoot ascending, then the original column first. Ordering by coverage alone would pick a
    /// column that covers the same amount while wasting far more; ordering by waste alone would let a column that barely covers anything win. The last key is not
    /// cosmetic: **a needless swap makes the tail sheet's layout inconsistent with the earlier sheets**, which is harder to execute on the floor.
    /// </remarks>
    public static LayerEvaluation? Choose(
        Layer<FltX>? original,
        IReadOnlyList<Layer<FltX>> candidates,
        IReadOnlyDictionary<string, ulong> remaining) {
        System.ArgumentNullException.ThrowIfNull(candidates);
        System.ArgumentNullException.ThrowIfNull(remaining);

        // 原列始终是候选之一：池子里先放它，再放显式候选，按 `Layer` 的相等语义去重。
        // The original is always a candidate: it goes in the pool first, then the explicit candidates, deduplicated by `Layer` equality.
        var pool = new List<Layer<FltX>>();
        if (original is not null) {
            pool.Add(original);
        }
        foreach (Layer<FltX> candidate in candidates) {
            if (!pool.Contains(candidate)) {
                pool.Add(candidate);
            }
        }
        if (pool.Count == 0) {
            return null;
        }

        LayerEvaluation? best = null;
        foreach (Layer<FltX> layer in pool) {
            LayerEvaluation current = Evaluate(layer, remaining);
            if (best is null || IsBetter(current, best)) {
                best = current;
            }
        }
        return best;
    }

    /// <summary>
    /// 按剩余需求裁剪一层的放置 / Trim a layer's placements to the residual demand.
    /// </summary>
    /// <param name="layer">候选列 / The candidate column.</param>
    /// <param name="remaining">剩余需求 / The residual demand.</param>
    /// <returns>裁剪后的层 / The trimmed layer.</returns>
    /// <remarks>
    /// **逐件消耗预算，而不是按比例缩放**：每保留一件就把该物料的预算减一，预算用完就跳过后续同类件。
    /// 按比例缩放会造出半件物品——而"半件"在排版里没有意义。
    ///
    /// **The budget is consumed piece by piece rather than scaled proportionally**: keeping one piece decrements that item's budget, and once it is exhausted further
    /// pieces of the same item are skipped. Scaling would produce fractions of a piece, which mean nothing in a layout.
    /// </remarks>
    public static Layer<FltX> TrimLayer(
        Layer<FltX> layer,
        IReadOnlyDictionary<string, ulong> remaining) {
        System.ArgumentNullException.ThrowIfNull(layer);
        System.ArgumentNullException.ThrowIfNull(remaining);

        var budget = new Dictionary<string, int>();
        foreach (KeyValuePair<string, ulong> entry in remaining) {
            budget[entry.Key] = (int)entry.Value;
        }

        var units = new List<Placement2<FltX>>(layer.Units.Count);
        foreach (Placement2<FltX> unit in layer.Units) {
            if (unit.ItemId is null) {
                continue;
            }
            int left = budget.TryGetValue(unit.ItemId, out int known) ? known : 0;
            if (left <= 0) {
                continue;
            }
            budget[unit.ItemId] = left - 1;
            units.Add(unit);
        }

        return new Layer<FltX>(
            iteration: layer.Iteration,
            from: layer.From,
            sheet: layer.Sheet,
            shape: layer.Shape,
            units: units);
    }

    /// <summary>
    /// 按需求裁剪板材，并优化尾板选列 / Trim the sheets to the demand and optimise the tail column.
    /// </summary>
    /// <param name="sheets">列生成产出的板材 / The sheets produced by column generation.</param>
    /// <param name="demands">物品编号到需求件数 / Item identifier to demanded amount.</param>
    /// <returns>裁剪后的板材 / The trimmed sheets.</returns>
    /// <remarks>
    /// **需求为空时返回空列表，而不是把原板材原样返回**：需求为空意味着没有要装的东西，
    /// 原样返回会让调用方以为"这些板都得做"。
    ///
    /// **An empty demand returns an empty list rather than the sheets unchanged**: empty demand means nothing needs packing, and returning the input would make the caller
    /// believe every sheet still has to be produced.
    /// </remarks>
    public IReadOnlyList<Sheet<FltX>> Optimize(
        IReadOnlyList<Sheet<FltX>> sheets,
        IReadOnlyDictionary<string, ulong> demands) {
        System.ArgumentNullException.ThrowIfNull(sheets);
        System.ArgumentNullException.ThrowIfNull(demands);

        if (sheets.Count == 0) {
            return System.Array.Empty<Sheet<FltX>>();
        }
        var remaining = new Dictionary<string, ulong>();
        foreach (KeyValuePair<string, ulong> entry in demands) {
            if (entry.Value > 0UL) {
                remaining[entry.Key] = entry.Value;
            }
        }
        if (remaining.Count == 0) {
            return System.Array.Empty<Sheet<FltX>>();
        }

        var result = new List<Sheet<FltX>>(sheets.Count);
        foreach (Sheet<FltX> sheet in sheets) {
            if (AllConsumed(remaining)) {
                break;
            }
            Layer<FltX>? original = sheet.SheetLayers.Count > 0 && sheet.SheetLayers[0].Layers.Count > 0
                ? sheet.SheetLayers[0].Layers[0]
                : null;
            LayerEvaluation? best = Choose(original, _candidates, remaining);
            if (best is null || best.Covered == 0) {
                continue;
            }

            Layer<FltX> trimmed = TrimLayer(best.Layer, remaining);
            if (trimmed.Units.Count == 0) {
                continue;
            }
            SheetLayer<FltX> template = sheet.SheetLayers[0];
            result.Add(sheet with {
                Layers = new List<SheetLayer<FltX>> {
                    new(template.Type, trimmed.Shape, new List<Layer<FltX>> { trimmed }, template.BatchNo),
                },
            });
            foreach (KeyValuePair<ItemId, ulong> entry in trimmed.ItemAmounts) {
                ulong used = entry.Value;
                ulong left = remaining.TryGetValue(entry.Key.Value, out ulong known) ? known : 0UL;
                remaining[entry.Key.Value] = left <= used ? 0UL : left - used;
            }
        }
        return result;
    }

    /// <summary>判断剩余需求是否已全部用尽 / Whether the residual demand is exhausted.</summary>
    /// <param name="remaining">剩余需求 / The residual demand.</param>
    /// <returns>是否用尽 / Whether it is exhausted.</returns>
    private static bool AllConsumed(IReadOnlyDictionary<string, ulong> remaining) {
        foreach (ulong amount in remaining.Values) {
            if (amount != 0UL) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判断一个评估是否优于另一个 / Whether one evaluation beats another.
    /// </summary>
    /// <param name="candidate">候选评估 / The candidate evaluation.</param>
    /// <param name="incumbent">当前最优 / The current best.</param>
    /// <returns>候选是否更优 / Whether the candidate is better.</returns>
    /// <remarks>
    /// 只有两个比较键：覆盖降序、超出升序。**"原列优先"不在这里实现**——`Choose` 把原列放在池子首位，
    /// 而这里用的是严格比较，平局时保留首个候选，于是原列在平局时自然胜出。
    ///
    /// 这里原本还有一个 `ReferenceEquals` 的第三键。它不是错的，而是**跑不到**：池子顺序已经保证原列先到，
    /// 平局分支永远不会看到"原列在候选之后"。一段跑不到的分支留在代码里，比删掉它更危险——它的变异体存活，
    /// 而人只会以为判据没写好。
    ///
    /// Only two keys: coverage descending, then overshoot ascending. **"The original column wins" is not implemented here** — `Choose` puts the original first in the pool,
    /// and the comparisons here are strict, so on a tie the first candidate is kept and the original wins naturally.
    ///
    /// There used to be a third `ReferenceEquals` key. It was not wrong, it was **unreachable**: the pool order already guarantees the original arrives first, so the tie
    /// branch can never see "the original after a candidate". Leaving an unreachable branch in the code is worse than deleting it — its mutant survives, and a reader will
    /// only conclude the criterion was badly written.
    /// </remarks>
    private static bool IsBetter(LayerEvaluation candidate, LayerEvaluation incumbent) {
        if (candidate.Covered != incumbent.Covered) {
            return candidate.Covered > incumbent.Covered;
        }
        return candidate.Wasted < incumbent.Wasted;
    }
}
