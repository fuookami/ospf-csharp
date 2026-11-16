// 裁切图案、方案与几何小判据 / Cutting patterns, plans and small geometry checks.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;

/// <summary>
/// 裁切图案：一块料上的完整二维排样 / A cutting pattern: a complete 2D layout on one piece of material.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">图案编号 / The pattern identifier.</param>
/// <param name="Material">物料 / The material.</param>
/// <param name="Region">该图案占用的料域 / The region occupied by this pattern.</param>
/// <param name="Placements">成品件放置 / The finished part placements.</param>
/// <param name="CostarPlacements">副产物放置 / The by-product placements.</param>
/// <param name="CutTree">断头台切分树；任意排样为空 / The guillotine cut tree; null for arbitrary nesting.</param>
/// <param name="Process">裁切工艺 / The cutting process.</param>
/// <remarks>
/// **三处面积与利用率返回 `Result` 而不是静默取零**：`quantitySum(...) ?: quantityZeroOf(...)`
/// 在**单位不一致**时会把占用面积悄悄变成 0，于是"利用率 0%"看起来像一个合法结果，而真实情况是**这笔账根本算不成**。
/// 本实现把这一格显式报出来。
///
/// **Three area and utilization members return a `Result` instead of silently zeroing**: its
/// `quantitySum(...) ?: quantityZeroOf(...)` quietly turns the occupied area into 0 when the **units disagree**, so
/// "0% utilization" looks like a legal result while the real situation is that **the arithmetic cannot be done at all**.
/// This implementation reports that case explicitly.
/// </remarks>
public sealed record Pattern2<V>(
    IPatternId2 Id,
    Material2<V> Material,
    Region2<V> Region,
    IReadOnlyList<Placement2<V>> Placements,
    IReadOnlyList<Placement2<V>>? CostarPlacements = null,
    CutTree2<V>? CutTree = null,
    CuttingProcess2 Process = CuttingProcess2.Freeform)
    where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
    /// <summary>副产物放置，为空即空表 / The by-product placements; null means an empty list.</summary>
    public IReadOnlyList<Placement2<V>> Byproducts => CostarPlacements ?? new List<Placement2<V>>();

    /// <summary>成品件占用面积 / The area occupied by finished parts.</summary>
    /// <returns>面积或失败 / The area or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> UsedArea() =>
        SumAreas(Placements, Region.Area);

    /// <summary>副产物占用面积 / The area occupied by by-products.</summary>
    /// <returns>面积或失败 / The area or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> CostarArea() =>
        SumAreas(Byproducts, Region.Area);

    /// <summary>
    /// 余料面积 = 料域面积 − 成品面积 − 副产物面积，下限为零 / Remnant area: region area minus finished area minus by-product area, floored at zero.
    /// </summary>
    /// <returns>余料面积或失败 / The remnant area or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> WasteArea() {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> used = UsedArea();
        if (used is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> usedOk) {
            return used;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> costar = CostarArea();
        if (costar is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> costarOk) {
            return costar;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> occupied = usedOk.Value.Add(costarOk.Value);
        if (occupied is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> occupiedOk) {
            return occupied;
        }
        if (Region.Area.GreaterThan(occupiedOk.Value) is not true) {
            // **占用不小于料域时余料为零**，不是负数：负数余料在业务上不存在，而"负余料"会让复用阈值判定反过来。
            // **When the occupied area is not smaller than the region, the remnant is zero**, not negative: a negative remnant does not exist in the domain, and it would
            // invert the reusable-threshold comparison.
            return Results.Ok(new Quantity<V>(Region.Area.Value.Constants.Zero, Region.Area.Unit));
        }
        return Region.Area.Subtract(occupiedOk.Value);
    }

    /// <summary>
    /// 成品件件数映射，按产品编号 / Finished part counts indexed by product identifier.
    /// </summary>
    /// <returns>件数映射 / The piece count map.</returns>
    public IReadOnlyDictionary<string, ulong> ProductAmounts() {
        var counts = new Dictionary<string, ulong>();
        foreach (Placement2<V> placement in Placements) {
            if (placement.ItemId is not { } productId) {
                continue;
            }
            counts[productId] = counts.TryGetValue(productId, out ulong count) ? count + 1UL : 1UL;
        }
        return counts;
    }

    /// <summary>
    /// 利用率：成品面积与料域面积之比 / Utilization: finished part area over region area.
    /// </summary>
    /// <returns>利用率或失败 / The utilization or a failure.</returns>
    /// <remarks>
    /// **副产物不计入利用率**：它不是需求驱动的产出，算进去会让"排得满"与"卖得掉"混为一谈。
    ///
    /// **By-products are excluded from the utilization**: they are not demand-driven output, and counting them would conflate "the sheet is full" with "it sells".
    /// </remarks>
    public Result<V, ErrorCode, Error<ErrorCode>> Utilization() {
        double regionArea = Region.Area.Value.ToFlt64().ToDouble();
        if (regionArea <= 0.0) {
            return Results.Failed<V>(new Csp2dValidationError(
                "料域面积非正，无法计算利用率 / The region area is non-positive, so utilization is undefined"));
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> used = UsedArea();
        if (used is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> usedOk) {
            return Propagate<V, Quantity<V>>(used);
        }
        return Results.Ok(usedOk.Value.Value.Div(Region.Area.Value));
    }

    /// <summary>
    /// 校验图案：几何合法、互不重叠、工艺与切分树一致 / Validate the pattern: legal geometry, pairwise disjoint placements, and a process consistent with the cut tree.
    /// </summary>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate(Quantity<V> kerf) {
        if (Region.IsDegenerate()) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "图案料域退化 / Degenerate pattern region: " + Region));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> material = Material.Validate();
        if (material is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return material;
        }
        if (Process.RequiresCutTree() && CutTree is null) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "断头台工艺未提供切分树 / A guillotine process requires a cut tree"));
        }
        if (Placements.Count == 0 && Byproducts.Count == 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "图案没有任何放置 / The pattern has no placement"));
        }

        var all = new List<Placement2<V>>();
        all.AddRange(Placements);
        all.AddRange(Byproducts);
        for (int index = 0; index < all.Count; index++) {
            Result<bool, ErrorCode, Error<ErrorCode>> inside = Region.Contains(all[index].Box());
            if (inside is not Ok<bool, ErrorCode, Error<ErrorCode>> insideOk) {
                return Propagate<Success, bool>(inside);
            }
            if (!insideOk.Value) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "图案第 " + (index + 1) + " 个放置超出料域 / Placement " + (index + 1)
                    + " lies outside the pattern region"));
            }
        }

        for (int lhs = 0; lhs < all.Count; lhs++) {
            for (int rhs = lhs + 1; rhs < all.Count; rhs++) {
                Result<bool, ErrorCode, Error<ErrorCode>> overlapped = all[lhs].Overlapped(all[rhs]);
                if (overlapped is not Ok<bool, ErrorCode, Error<ErrorCode>> overlappedOk) {
                    return Propagate<Success, bool>(overlapped);
                }
                if (overlappedOk.Value) {
                    return Results.Failed<Success>(new Csp2dValidationError(
                        "图案第 " + (lhs + 1) + " 与第 " + (rhs + 1) + " 个放置重叠 / Placements "
                        + (lhs + 1) + " and " + (rhs + 1) + " overlap"));
                }
            }
        }

        if (CutTree is { } tree) {
            Result<Success, ErrorCode, Error<ErrorCode>> treeResult = tree.Validate(Process, kerf);
            if (treeResult is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return treeResult;
            }
            var actual = new Dictionary<string, ulong>();
            foreach (CutTree2<V>.Leaf2 leaf in tree.Leaves()) {
                if (leaf.PieceId is not { } pieceId) {
                    continue;
                }
                actual[pieceId] = actual.TryGetValue(pieceId, out ulong count) ? count + 1UL : 1UL;
            }
            IReadOnlyDictionary<string, ulong> expected = ProductAmounts();
            if (!SameCounts(actual, expected)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "切分树叶子件数与放置不一致 / The cut tree leaves do not match the placements"));
            }
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> used = UsedArea();
        if (used is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> usedOk) {
            return Propagate<Success, Quantity<V>>(used);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> costar = CostarArea();
        if (costar is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> costarOk) {
            return Propagate<Success, Quantity<V>>(costar);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> occupied = usedOk.Value.Add(costarOk.Value);
        if (occupied is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> occupiedOk) {
            return Propagate<Success, Quantity<V>>(occupied);
        }
        if (Region.Area.GreaterThanOrEqual(occupiedOk.Value) is not true) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "占用面积超过料域面积 / Occupied area exceeds the region area"));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 判断该图案的余料是否达到可复用阈值 / Decide whether the remnant meets the reusable threshold.
    /// </summary>
    /// <returns>是否可复用 / Whether it is reusable.</returns>
    /// <remarks>
    /// 余料面积算不出来时**返回 `false`**：判不了就不说"可复用"，否则会留下一块其实装不进任何件的余料。
    ///
    /// When the remnant area cannot be computed this **returns `false`**: if it cannot be judged, do not claim "reusable", or a remnant that fits nothing survives.
    /// </remarks>
    public bool HasUsableRemnant() =>
        WasteArea() is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> waste
        && Material.IsUsableRemnant(waste.Value);

    /// <summary>把若干放置的面积加起来 / Sum the areas of the given placements.</summary>
    /// <param name="placements">放置 / The placements.</param>
    /// <param name="sample">提供零值单位的**面积**样本 / An **area** sample supplying the zero unit.</param>
    /// <returns>面积或失败 / The area or a failure.</returns>
    private static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> SumAreas(
        IReadOnlyList<Placement2<V>> placements,
        Quantity<V> sample) {
        Quantity<V> accumulator = new(sample.Value.Constants.Zero, sample.Unit);
        foreach (Placement2<V> placement in placements) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> added = accumulator.Add(placement.Area);
            if (added is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> addedOk) {
                return added;
            }
            accumulator = addedOk.Value;
        }
        return Results.Ok(accumulator);
    }

    /// <summary>两组件数是否完全一致 / Whether the two count maps agree exactly.</summary>
    /// <param name="lhs">左 / The left map.</param>
    /// <param name="rhs">右 / The right map.</param>
    /// <returns>是否一致 / Whether they agree.</returns>
    private static bool SameCounts(
        IReadOnlyDictionary<string, ulong> lhs,
        IReadOnlyDictionary<string, ulong> rhs) {
        if (lhs.Count != rhs.Count) {
            return false;
        }
        foreach (KeyValuePair<string, ulong> pair in lhs) {
            if (!rhs.TryGetValue(pair.Key, out ulong other) || other != pair.Value) {
                return false;
            }
        }
        return true;
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal => new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}


/// <summary>
/// 方案对某个产品需求的贡献 / The contribution of a plan to one product demand.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Product">产品 / The product.</param>
/// <param name="Amount">该方案一次产出的件数 / The pieces produced by one usage of the plan.</param>
/// <param name="Quantity">贡献量（按需求口径），为空表示与件数口径一致 / The contribution quantity under the demand unit; null means it equals the piece count.</param>
public sealed record CuttingPlanDemandContribution2<V>(
    Product2<V> Product,
    ulong Amount,
    Quantity<V>? Quantity = null)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 裁切方案 / A cutting plan.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">方案编号 / The plan identifier.</param>
/// <param name="Material">物料 / The material.</param>
/// <param name="Pattern">裁切图案 / The cutting pattern.</param>
/// <param name="Contributions">需求贡献 / The demand contributions.</param>
/// <param name="CapacityConsumption">单次方案的产能消耗 / The capacity consumed by one plan usage.</param>
public sealed record CuttingPlan2<V>(
    IPlanId2 Id,
    Material2<V> Material,
    Pattern2<V> Pattern,
    IReadOnlyList<CuttingPlanDemandContribution2<V>>? Contributions = null,
    Quantity<V>? CapacityConsumption = null)
    where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
    /// <summary>设备编号，取自物料 / The machine identifier, taken from the material.</summary>
    public IMachineId2? MachineId => Material.MachineId;

    /// <summary>需求贡献，为空即空表 / The demand contributions; null means an empty list.</summary>
    public IReadOnlyList<CuttingPlanDemandContribution2<V>> ContributionList =>
        Contributions ?? new List<CuttingPlanDemandContribution2<V>>();

    /// <summary>成品件占用面积 / The area occupied by finished parts.</summary>
    /// <returns>面积或失败 / The area or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> UsedArea() => Pattern.UsedArea();

    /// <summary>余料面积 / The remnant area.</summary>
    /// <returns>面积或失败 / The area or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> WasteArea() => Pattern.WasteArea();

    /// <summary>利用率 / The utilization.</summary>
    /// <returns>利用率或失败 / The utilization or a failure.</returns>
    public Result<V, ErrorCode, Error<ErrorCode>> Utilization() => Pattern.Utilization();

    /// <summary>
    /// 校验方案：图案合法、贡献与图案一致、无重复产品 / Validate the plan: the pattern is legal, the contributions match the pattern, and no product is duplicated.
    /// </summary>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate(Quantity<V> kerf) {
        if (Material.Id.Value != Pattern.Material.Id.Value) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "方案的物料与图案物料不一致 / The plan material does not match the pattern material"));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> pattern = Pattern.Validate(kerf);
        if (pattern is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return pattern;
        }

        var seen = new Dictionary<string, int>();
        foreach (CuttingPlanDemandContribution2<V> contribution in ContributionList) {
            string key = contribution.Product.Id.Value;
            seen[key] = seen.TryGetValue(key, out int count) ? count + 1 : 1;
        }
        foreach (System.Collections.Generic.KeyValuePair<string, int> pair in seen) {
            if (pair.Value > 1) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "方案的贡献存在重复产品 / Duplicate products in the plan contributions: " + pair.Key));
            }
        }

        if (ContributionList.Count > 0) {
            var declared = new Dictionary<string, ulong>();
            foreach (CuttingPlanDemandContribution2<V> contribution in ContributionList) {
                declared[contribution.Product.Id.Value] = contribution.Amount;
            }
            IReadOnlyDictionary<string, ulong> actual = Pattern.ProductAmounts();
            if (!SameMap(actual, declared)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "方案的贡献与图案件数不一致 / The plan contributions do not match the pattern piece counts"));
            }
        }

        if (CapacityConsumption is { } capacity && capacity.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "方案的产能消耗必须为正 / The capacity consumption must be positive: " + capacity));
        }
        return Results.OkInstance;
    }

    /// <summary>两组件数是否完全一致 / Whether the two count maps agree exactly.</summary>
    /// <param name="lhs">左 / The left map.</param>
    /// <param name="rhs">右 / The right map.</param>
    /// <returns>是否一致 / Whether they agree.</returns>
    private static bool SameMap(
        IReadOnlyDictionary<string, ulong> lhs,
        IReadOnlyDictionary<string, ulong> rhs) {
        if (lhs.Count != rhs.Count) {
            return false;
        }
        foreach (KeyValuePair<string, ulong> pair in lhs) {
            if (!rhs.TryGetValue(pair.Key, out ulong other) || other != pair.Value) {
                return false;
            }
        }
        return true;
    }
}

/// <summary>由图案构造方案的工厂 / Factory building a plan from a pattern.</summary>
public static class CuttingPlan2Factory {
    /// <summary>
    /// 由图案构造方案，贡献按图案件数自动生成 / Build a plan from a pattern, deriving the contributions from the pattern piece counts.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="id">方案编号 / The plan identifier.</param>
    /// <param name="pattern">裁切图案 / The cutting pattern.</param>
    /// <param name="products">产品目录，按编号索引 / The product catalogue indexed by identifier.</param>
    /// <param name="capacityConsumption">单次方案的产能消耗 / The capacity consumed by one plan usage.</param>
    /// <returns>方案或失败 / The plan, or a failure.</returns>
    /// <remarks>
    /// **自动生成贡献**可以避免"方案与图案件数不一致"这类手工错误；需要覆盖时再显式构造 `CuttingPlan2`。
    /// 图案引用了**未知产品**时整次构造失败——**不静默跳过**，否则少一条贡献会让需求凭空变小。
    ///
    /// **Deriving the contributions** avoids hand-made inconsistencies between a plan and its pattern; construct `CuttingPlan2` explicitly only when overriding.
    /// An **unknown product** referenced by the pattern fails the whole build — **never skip silently**, or a missing contribution shrinks the demand out of thin air.
    /// </remarks>
    public static Result<CuttingPlan2<V>, ErrorCode, Error<ErrorCode>> CuttingPlan2Of<V>(
        IPlanId2 id,
        Pattern2<V> pattern,
        IReadOnlyDictionary<string, Product2<V>> products,
        Quantity<V>? capacityConsumption = null)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
        var contributions = new List<CuttingPlanDemandContribution2<V>>();
        foreach (KeyValuePair<string, ulong> pair in pattern.ProductAmounts()) {
            if (!products.TryGetValue(pair.Key, out Product2<V>? product)) {
                return Results.Failed<CuttingPlan2<V>>(new Csp2dValidationError(
                    "图案引用了未知产品 / The pattern references an unknown product: " + pair.Key));
            }
            contributions.Add(new CuttingPlanDemandContribution2<V>(product, pair.Value));
        }
        return Results.Ok(new CuttingPlan2<V>(
            id, pattern.Material, pattern, contributions, capacityConsumption));
    }
}

/// <summary>图案与余料的小判据 / Small pattern and remnant checks.</summary>
public static class Pattern2Checks {
    /// <summary>
    /// 判断料域是否足够容纳给定尺寸的件 / Decide whether a region can hold a piece of the given size.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="region">料域 / The region.</param>
    /// <param name="width">件宽 / The piece width.</param>
    /// <param name="height">件高 / The piece height.</param>
    /// <param name="allowRotate">是否允许旋转 90° / Whether a 90-degree rotation is allowed.</param>
    /// <returns>是否可容纳 / Whether it fits.</returns>
    /// <remarks>
    /// **旋转只在直放失败后才试**，且不允许旋转时**直接返回 false**——"反正转一下也行"是排样最常见的隐性放宽。
    ///
    /// **A rotation is only tried after the direct fit fails**, and when rotation is forbidden this **returns false outright**: "it also fits rotated" is the most
    /// common silent relaxation in nesting.
    /// </remarks>
    public static bool FitsIn<V>(
        Region2<V> region,
        Quantity<V> width,
        Quantity<V> height,
        bool allowRotate)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
        if (width.LessThanOrEqual(region.Width) is true && height.LessThanOrEqual(region.Height) is true) {
            return true;
        }
        if (!allowRotate) {
            return false;
        }
        return height.LessThanOrEqual(region.Width) is true && width.LessThanOrEqual(region.Height) is true;
    }

    /// <summary>
    /// 判断余料是否达到阈值 / Decide whether a remnant reaches the threshold.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="area">余料面积 / The remnant area.</param>
    /// <param name="threshold">阈值，为空表示只要为正即可 / The threshold; null means "any positive area".</param>
    /// <returns>是否满足 / Whether it is satisfied.</returns>
    public static bool RemnantReaches<V>(Quantity<V> area, Quantity<V>? threshold)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
        if (threshold is { } limit) {
            return area.GreaterThanOrEqual(limit) is true;
        }
        return area.Value.ToFlt64().ToDouble() > 0.0;
    }
}
