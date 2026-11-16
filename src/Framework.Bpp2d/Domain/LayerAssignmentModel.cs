// 层分配模型：聚合、需求、检验数 / Layer assignment model: aggregation, demand and reduced cost.
//
// **这里不含求解器符号层**：`Load`／`Capacity`／`Assignment`／`Objectives` 需要列生成求解器的线性符号与模型机制
// （`LinearExpressionSymbols1` 等）。这里只实现**不依赖求解器**的那部分：聚合管理、需求结构、检验数计算。
// **The solver-symbol layer is not covered here**: `Load` / `Capacity` / `Assignment` / `Objectives` need the column
// generation solver's linear symbols and model machinery. Only the **solver-independent** portion is implemented here:
// aggregation management, demand structures and reduced-cost computation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 层聚合，管理列生成中的层集合 / Layer aggregation managing the layer set during column generation.
/// </summary>
/// <remarks>
/// 保持不变式：`Layers` 与 `LayersIteration` 的并集一致，且 `RemovedLayers` 中的层不会回到 `Layers`。移除是**记账**
/// 而不是删除记录——上层需要知道哪些列被踢过，才能在下一轮 RMP 里避免重复生成。
/// Keeps the invariant that `Layers` equals the union of `LayersIteration`, and that anything in `RemovedLayers` never
/// comes back into `Layers`. Removal is **bookkeeping** rather than forgetting: the caller needs to know which columns
/// were dropped so the next RMP round does not regenerate them.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class LayerAggregation<V> where V : struct, IFloatingNumber<V> {
    private readonly List<List<Layer<V>>> _layersIteration;
    private readonly List<Layer<V>> _layers;
    private readonly HashSet<Layer<V>> _removedLayers;

    /// <summary>
    /// 构造层聚合 / Construct a layer aggregation.
    /// </summary>
    /// <param name="layersIteration">每次迭代新增的层 / Layers added per iteration.</param>
    /// <param name="layers">当前全部层 / All current layers.</param>
    /// <param name="removedLayers">已移除的层 / Removed layers.</param>
    public LayerAggregation(
        IReadOnlyList<IReadOnlyList<Layer<V>>>? layersIteration = null,
        IReadOnlyList<Layer<V>>? layers = null,
        IReadOnlySet<Layer<V>>? removedLayers = null) {
        _layersIteration = layersIteration is null
            ? new List<List<Layer<V>>>()
            : layersIteration.Select(batch => batch.ToList()).ToList();
        _layers = layers is null ? new List<Layer<V>>() : layers.ToList();
        _removedLayers = removedLayers is null
            ? new HashSet<Layer<V>>()
            : new HashSet<Layer<V>>(removedLayers);
    }

    /// <summary>每次迭代新增的层 / Layers added per iteration.</summary>
    public IReadOnlyList<IReadOnlyList<Layer<V>>> LayersIteration => _layersIteration;

    /// <summary>当前全部层 / All current layers.</summary>
    public IReadOnlyList<Layer<V>> Layers => _layers;

    /// <summary>已移除的层 / Removed layers.</summary>
    public IReadOnlySet<Layer<V>> RemovedLayers => _removedLayers;

    /// <summary>最近一次迭代的层 / Layers from the last non-empty iteration.</summary>
    public IReadOnlyList<Layer<V>> LastIterationLayers =>
        _layersIteration.LastOrDefault(batch => batch.Count > 0) ?? (IReadOnlyList<Layer<V>>)Array.Empty<Layer<V>>();

    /// <summary>
    /// 添加新列并去重 / Add new columns with deduplication.
    /// </summary>
    /// <remarks>
    /// 双重去重：先在这里内部去重，再与已有层去重。只做后者会把同一批里的重复列都留下，随后候选数统计与列数不符。
    /// Adds each batch to <see cref="LayersIteration"/> **even when it is empty** after deduplication: the iteration
    /// history must stay aligned with the iteration index so callers can correlate rounds.
    /// </remarks>
    /// <param name="newLayers">新层列表 / New layer list.</param>
    /// <returns>去重后实际新增的层 / Deduplicated newly added layers.</returns>
    public IReadOnlyList<Layer<V>> AddColumns(IReadOnlyList<Layer<V>> newLayers) {
        ArgumentNullException.ThrowIfNull(newLayers);

        var unduplicated = new List<Layer<V>>();
        foreach (Layer<V> layer in newLayers) {
            if (unduplicated.Contains(layer) || _layers.Contains(layer)) {
                continue;
            }
            unduplicated.Add(layer);
        }
        _layersIteration.Add(unduplicated);
        _layers.AddRange(unduplicated);
        return unduplicated;
    }

    /// <summary>
    /// 移除单个列 / Remove a single column.
    /// </summary>
    /// <param name="layer">要移除的层 / Layer to remove.</param>
    public void RemoveColumn(Layer<V> layer) {
        ArgumentNullException.ThrowIfNull(layer);
        if (_removedLayers.Contains(layer)) {
            return;
        }
        _removedLayers.Add(layer);
        _layers.Remove(layer);
    }

    /// <summary>
    /// 移除多个列 / Remove multiple columns.
    /// </summary>
    /// <param name="layers">要移除的层列表 / Layers to remove.</param>
    public void RemoveColumns(IReadOnlyList<Layer<V>> layers) {
        ArgumentNullException.ThrowIfNull(layers);
        foreach (Layer<V> layer in layers) {
            RemoveColumn(layer);
        }
    }
}

/// <summary>
/// 层聚合的件数换算 / Count conversion for layer aggregation.
/// </summary>
public static class LayerAggregationMath {
    /// <summary>
    /// 把件数换算为无符号整数 / Convert a count to an unsigned integer.
    /// </summary>
    /// <remarks>
    /// 非正件数一律折算为 0：负件数没有语义，若原样保留会在下游被当成"欠需求"。
    /// Non-positive counts collapse to 0: a negative count is meaningless and would be read downstream as a demand
    /// shortfall.
    /// </remarks>
    /// <param name="value">件数 / Count.</param>
    /// <returns>无符号整数 / Unsigned integer.</returns>
    public static ulong CountOf(int value) => value <= 0 ? 0UL : (ulong)value;
}

/// <summary>
/// 层分配需求种类 / Layer assignment demand kind.
/// </summary>
public enum Bpp2dAssignmentDemandMode {
    /// <summary>物品件数需求 / Item amount demand.</summary>
    ItemAmount,

    /// <summary>物品面积需求 / Item area demand.</summary>
    ItemArea,
}

/// <summary>
/// 层分配需求值 / Layer assignment demand value.
/// </summary>
public abstract record Bpp2dAssignmentDemandValue {
    /// <summary>件数需求 / Amount demand.</summary>
    /// <param name="Value">件数 / Amount.</param>
    public sealed record Amount(ulong Value) : Bpp2dAssignmentDemandValue;

    /// <summary>面积需求 / Area demand.</summary>
    /// <param name="Value">面积量 / Area quantity.</param>
    public sealed record Area(Quantity<FltX> Value) : Bpp2dAssignmentDemandValue;

    /// <summary>
    /// 计算需求值对应的求解器数值 / Compute the solver number of a demand value.
    /// </summary>
    /// <returns>求解器数值 / Solver number.</returns>
    public FltX ToSolver() => this switch {
        Amount amount => LayerAssignmentMath.Bpp2dSolverAmount(amount.Value),
        Area area => area.Value.Value,
        _ => throw new InvalidOperationException(
            "未知的需求值类型 / Unknown demand value kind"),
    };
}

/// <summary>
/// 层分配需求项 / Layer assignment demand entry.
/// </summary>
/// <remarks>
/// `Demand` 是必须被**覆盖**的下界（装箱语义：需求必须装完），`UpperBound` 是可选的额外上界（业务上限）。
/// 若把需求只当作上界，最小化目标会退化为"一块板材都不用"。
/// `Demand` is a lower bound that must be **covered** (packing semantics: the demand has to ship) while `UpperBound` is
/// an optional business cap. Treating demand as an upper bound only would let minimisation degenerate to "use no sheet".
/// </remarks>
/// <param name="Mode">需求种类 / Demand kind.</param>
/// <param name="ItemId">物品编号 / Item identifier.</param>
/// <param name="Demand">必须覆盖的需求下界 / Demand lower bound to cover.</param>
/// <param name="UpperBound">可选的需求上界 / Optional demand upper bound.</param>
public sealed record Bpp2dAssignmentDemandEntry(
    Bpp2dAssignmentDemandMode Mode,
    ItemId ItemId,
    Bpp2dAssignmentDemandValue Demand,
    Bpp2dAssignmentDemandValue? UpperBound = null);

/// <summary>
/// 需求影子价格键 / Demand shadow price key.
/// </summary>
/// <remarks>
/// **一处结构差异**：该键不继承框架的 `ShadowPriceKey`，因为 `Framework.Bpp2d` 目前不引用
/// `Framework` 项目（`Framework.Csp1d` 才有该引用）。本类因此是**独立的值对象**，自带 `Equals`／`GetHashCode`。
/// 语义不受影响（相等性只看需求种类与物品编号），但若要接入共享的 `AbstractShadowPriceMap`，需先给
/// `Framework.Bpp2d` 加上对 `Framework` 的项目引用，再让本类继承 `ShadowPriceKey`。
///
/// **One structural difference**: this key does not derive from the framework's `ShadowPriceKey`,
/// because `Framework.Bpp2d` does not reference the `Framework` project today (only `Framework.Csp1d` does). This class is
/// therefore a **standalone value object** with its own `Equals` / `GetHashCode`. Semantics are unaffected (equality
/// looks only at demand kind and item id), but wiring it into the shared `AbstractShadowPriceMap` would require adding a
/// `Framework` project reference to `Framework.Bpp2d` first.
/// </remarks>
public sealed class Bpp2dDemandShadowPriceKey : IEquatable<Bpp2dDemandShadowPriceKey> {
    /// <summary>
    /// 构造需求影子价格键 / Construct a demand shadow price key.
    /// </summary>
    /// <param name="mode">需求种类 / Demand kind.</param>
    /// <param name="itemId">物品编号 / Item identifier.</param>
    public Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode mode, ItemId itemId) {
        Mode = mode;
        ItemId = itemId;
    }

    /// <summary>需求种类 / Demand kind.</summary>
    public Bpp2dAssignmentDemandMode Mode { get; }

    /// <summary>物品编号 / Item identifier.</summary>
    public ItemId ItemId { get; }

    /// <summary>键名称 / Key name.</summary>
    public string Name => string.Format(
        CultureInfo.InvariantCulture,
        "bpp2d-demand:{0}:{1}",
        Mode,
        ItemId);

    /// <inheritdoc/>
    public bool Equals(Bpp2dDemandShadowPriceKey? other) =>
        other is not null && Mode == other.Mode && ItemId.Equals(other.ItemId);

    /// <inheritdoc/>
    public override bool Equals(object? other) => Equals(other as Bpp2dDemandShadowPriceKey);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Mode, ItemId);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// 层分配的数值换算与检验数 / Numeric conversion and reduced cost for layer assignment.
/// </summary>
public static class LayerAssignmentMath {
    /// <summary>
    /// 件数换算为求解器数值 / Convert an amount to a solver number.
    /// </summary>
    /// <param name="value">件数 / Amount.</param>
    /// <returns>求解器数值 / Solver number.</returns>
    public static FltX Bpp2dSolverAmount(ulong value) => new((decimal)value);

    /// <summary>
    /// 计算候选层对某个需求的贡献系数 / Compute the contribution coefficient of a candidate layer for a demand.
    /// </summary>
    /// <remarks>
    /// 面积需求用**层内该物品的单件面积乘以件数**，而不是层总面积：一层里可能混有多种物品，用总面积会把别的物品的
    /// 面积记到这个需求头上。
    /// An area demand uses **the item's per-piece area in the layer times its amount**, not the layer's total area: a layer
    /// may mix items, and the total would charge other items' area to this demand.
    /// </remarks>
    /// <param name="layer">候选层 / Candidate layer.</param>
    /// <param name="entry">需求项 / Demand entry.</param>
    /// <returns>贡献系数 / Contribution coefficient.</returns>
    public static FltX LayerDemandCoefficient(Layer<FltX> layer, Bpp2dAssignmentDemandEntry entry) {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(entry);

        if (!layer.ItemAmounts.TryGetValue(entry.ItemId, out ulong amount)) {
            return new FltX(0);
        }
        return entry.Mode switch {
            Bpp2dAssignmentDemandMode.ItemAmount => Bpp2dSolverAmount(amount),
            Bpp2dAssignmentDemandMode.ItemArea => UnitAreaOf(layer, entry.ItemId) is { } unitArea
                ? unitArea.Times(Bpp2dSolverAmount(amount))
                : new FltX(0),
            _ => new FltX(0),
        };
    }

    /// <summary>
    /// 计算候选列的检验数 / Compute the reduced cost of a candidate column.
    /// </summary>
    /// <remarks>
    /// `rc_j = area_j · c_area − Σ_i π_i · a_ij`；`rc_j &lt; 0` 表示这一列能改进目标。
    /// 影子价格为零的需求被**跳过**——零乘任何系数都是零，但跳过省掉了逐项乘法。
    /// `rc_j = area_j · c_area − Σ_i π_i · a_ij`; `rc_j &lt; 0` means the column improves the objective. Demands with a
    /// zero shadow price are **skipped**: zero times any coefficient is zero, and skipping saves the multiplications.
    /// </remarks>
    /// <param name="layer">候选列 / Candidate column.</param>
    /// <param name="demandEntries">需求项列表 / Demand entry list.</param>
    /// <param name="shadowPrices">需求影子价格 / Demand shadow prices.</param>
    /// <param name="areaCoefficient">面积目标系数 / Area objective coefficient.</param>
    /// <returns>检验数 / Reduced cost.</returns>
    public static FltX LayerReducedCost(
        Layer<FltX> layer,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> shadowPrices,
        FltX? areaCoefficient = null) {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        FltX coefficient = areaCoefficient ?? new FltX(1);
        var dualValue = new FltX(0);
        foreach (Bpp2dAssignmentDemandEntry entry in demandEntries) {
            var key = new Bpp2dDemandShadowPriceKey(entry.Mode, entry.ItemId);
            if (!shadowPrices.TryGetValue(key, out FltX price) || IsZero(price)) {
                continue;
            }
            dualValue = dualValue.Plus(price.Times(LayerDemandCoefficient(layer, entry)));
        }
        return layer.Area.Value.Times(coefficient).Minus(dualValue);
    }

    /// <summary>
    /// 判断候选列是否为改进列 / Decide whether a candidate column is an improving column.
    /// </summary>
    /// <remarks>
    /// 用**严格小于 `-tolerance`** 而不是 `&lt; 0`：等式约束的对偶常见 1e-12 量级的噪声，阈值 0 会把噪声当改进列。
    /// Uses **strictly below `-tolerance`** rather than `&lt; 0`: duals on equality constraints routinely carry 1e-12 noise,
    /// and a zero threshold would treat noise as an improving column.
    /// </remarks>
    /// <param name="layer">候选列 / Candidate column.</param>
    /// <param name="demandEntries">需求项列表 / Demand entry list.</param>
    /// <param name="shadowPrices">需求影子价格 / Demand shadow prices.</param>
    /// <param name="areaCoefficient">面积目标系数 / Area objective coefficient.</param>
    /// <param name="tolerance">容差 / Tolerance.</param>
    /// <returns>是否为改进列 / Whether the column is improving.</returns>
    public static bool IsImprovingColumn(
        Layer<FltX> layer,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> shadowPrices,
        FltX? areaCoefficient = null,
        FltX? tolerance = null) {
        FltX limit = tolerance ?? new FltX(0.000001m);
        FltX reducedCost = LayerReducedCost(layer, demandEntries, shadowPrices, areaCoefficient);
        return reducedCost.CompareTo(limit.Times(new FltX(-1))) < 0;
    }

    /// <summary>
    /// 按检验数升序的比较器，便于挑选最有改进的候选 / Comparator ordering columns by ascending reduced cost.
    /// </summary>
    /// <param name="demandEntries">需求项列表 / Demand entry list.</param>
    /// <param name="shadowPrices">需求影子价格 / Demand shadow prices.</param>
    /// <param name="areaCoefficient">面积目标系数 / Area objective coefficient.</param>
    /// <returns>按检验数升序的比较器 / Comparator ordering by ascending reduced cost.</returns>
    public static IComparer<Layer<FltX>> ReducedCostComparator(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> shadowPrices,
        FltX? areaCoefficient = null) =>
        Comparer<Layer<FltX>>.Create((lhs, rhs) => LayerReducedCost(
            lhs, demandEntries, shadowPrices, areaCoefficient)
            .CompareTo(LayerReducedCost(rhs, demandEntries, shadowPrices, areaCoefficient)));

    /// <summary>取层内某物品的单件面积 / The per-piece area of one item inside the layer.</summary>
    /// <param name="layer">候选层 / Candidate layer.</param>
    /// <param name="itemId">物品编号 / Item identifier.</param>
    /// <returns>单件面积或 null / The unit area, or null.</returns>
    private static FltX? UnitAreaOf(Layer<FltX> layer, ItemId itemId) {
        foreach (Placement2<FltX> unit in layer.Units) {
            if (unit.ItemId is { } text && string.Equals(text, itemId.Value, StringComparison.Ordinal)) {
                return unit.Shape.Area.Value;
            }
        }
        return null;
    }

    /// <summary>判断数值是否为零 / Whether the value is zero.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>是否为零 / Whether zero.</returns>
    private static bool IsZero(FltX value) => value.ToFlt64().ToDouble() == 0.0;
}
