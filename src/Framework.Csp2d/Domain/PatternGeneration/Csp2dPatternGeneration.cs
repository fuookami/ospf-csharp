// 图案生成请求、结果与纯函数助手 / Pattern generation requests, results and pure-function helpers.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;

/// <summary>
/// 图案生成请求 / A pattern generation request.
/// </summary>
/// <param name="Material">目标物料 / The target material.</param>
/// <param name="Products">可选产品及其剩余件数 / The candidate products and their remaining piece counts.</param>
/// <param name="Kerf">刀缝宽度 / The kerf width.</param>
/// <param name="Process">裁切工艺 / The cutting process.</param>
/// <param name="Costars">可填充的副产物 / The by-products available for filling.</param>
/// <param name="MaxSegmentHeight">卷材模式的最大段高，为空表示按需求推导 / The maximum segment height in coil mode; null means derived from the demand.</param>
/// <param name="MaxCandidates">最大候选数 / The maximum candidate count.</param>
/// <param name="Iteration">迭代序号 / The iteration index.</param>
/// <param name="ExistingPatterns">历史图案，用于回放 / Historical patterns for replay.</param>
/// <param name="ShadowPrices">产品编号到影子价格的映射（按面积计价）/ Product identifier to shadow price, priced by area.</param>
/// <param name="WasteWeight">余料权重 / The waste weight.</param>
public sealed record PatternGenerationRequest2(
    Material2<Flt64> Material,
    IReadOnlyList<(Product2<Flt64> Product, ulong Amount)> Products,
    Quantity<Flt64> Kerf,
    CuttingProcess2 Process = CuttingProcess2.Freeform,
    IReadOnlyList<Costar2<Flt64>>? Costars = null,
    Quantity<Flt64>? MaxSegmentHeight = null,
    int MaxCandidates = 32,
    int Iteration = 0,
    IReadOnlyList<Pattern2<Flt64>>? ExistingPatterns = null,
    IReadOnlyDictionary<string, Flt64>? ShadowPrices = null,
    Flt64? WasteWeight = null) {
    /// <summary>副产物列表，缺省为空 / The by-product list, defaulting to empty.</summary>
    public IReadOnlyList<Costar2<Flt64>> CostarList => Costars ?? new List<Costar2<Flt64>>();

    /// <summary>历史图案，缺省为空 / The historical patterns, defaulting to empty.</summary>
    public IReadOnlyList<Pattern2<Flt64>> History => ExistingPatterns ?? new List<Pattern2<Flt64>>();

    /// <summary>影子价格表，缺省为空 / The shadow price map, defaulting to empty.</summary>
    public IReadOnlyDictionary<string, Flt64> Prices => ShadowPrices ?? new Dictionary<string, Flt64>();

    /// <summary>
    /// 校验请求 / Validate the request.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    /// <remarks>
    /// **负刀缝先查**：它会让 `<see cref="Region2{V}.Cut"/>` 的两块区域重叠，而那是几何层的事后补救，
    /// 在这里挡掉比在几何层兜底便宜得多。
    ///
    /// **A negative kerf is checked first**: it would make `Region2.Cut` produce overlapping halves, and catching it here is far cheaper than compensating in the
    /// geometry layer.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Kerf.Value.ToDouble() < 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "刀缝不能为负 / The kerf must not be negative: " + Kerf));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> material = Material.Validate();
        if (material is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return material;
        }
        if (Products.Count == 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "图案生成请求没有可选产品 / The request has no candidate product"));
        }
        foreach ((Product2<Flt64> product, ulong amount) in Products) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = product.Validate();
            if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return result;
            }
            if (amount == 0UL) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "产品剩余件数为零 / The remaining amount is zero: " + product.Id.Value));
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 料域高度上界 / The region height upper bound.
    /// </summary>
    /// <returns>高度上界 / The height upper bound.</returns>
    /// <remarks>
    /// 定尺模式直接取板高；卷材模式最坏情况是**每件各占一行**，故取「全部件高之和 + 行间刀缝」，再由 `MaxSegmentHeight` 截断。
    /// **上界必须计入件数**——只按单件高度算会让两块以上的排样装不下，而报错看起来像"物料不够"。
    ///
    /// Sheet mode uses the sheet height directly. In coil mode the worst case puts **every piece in its own row**, so the bound is the sum of all piece heights plus
    /// the row kerfs, truncated by `MaxSegmentHeight`. **The bound must account for the amounts** — using one piece's height alone would make any multi-piece layout
    /// overflow, and the failure would look like "not enough material".
    /// </remarks>
    public Quantity<Flt64> RegionHeightBound() {
        if (Material.Height is { } materialHeight) {
            return materialHeight;
        }
        Quantity<Flt64> zero = new(MetresZero, Material.Width.Unit);
        Quantity<Flt64> total = new(MetresZero, Material.Width.Unit);
        foreach ((Product2<Flt64> product, ulong amount) in Products) {
            int count = amount > 4096UL ? 4096 : (int)amount;
            for (int index = 0; index < count; index++) {
                if (total.Add(product.Height) is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> added) {
                    return zero;
                }
                if (added.Value.Add(Kerf) is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> withKerf) {
                    return zero;
                }
                total = withKerf.Value;
            }
        }
        if (total.Value.ToDouble() <= 0.0) {
            return zero;
        }
        Quantity<Flt64> derived = total;
        if (total.Subtract(Kerf) is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> minusKerf) {
            derived = minusKerf.Value;
        }
        if (MaxSegmentHeight is { } limit
            && limit.Value.ToDouble() > 0.0
            && limit.Value.ToDouble() < derived.Value.ToDouble()) {
            return limit;
        }
        return derived;
    }

    /// <summary>
    /// 目标料域高度：定尺为板高，卷材为段高上界 / The target region height: the sheet height in sheet mode and the segment height bound in coil mode.
    /// </summary>
    /// <returns>料域高度 / The region height.</returns>
    public Quantity<Flt64> TargetRegionHeight() => Material.Height ?? RegionHeightBound();

    /// <summary>零长度基准 / The zero length baseline.</summary>
    private static Flt64 MetresZero => Flt64.Zero;
}

/// <summary>
/// 图案生成结果 / A pattern generation result.
/// </summary>
/// <param name="Pattern">生成的图案 / The generated pattern.</param>
/// <param name="Source">生成器名称 / The generator name.</param>
/// <param name="ReducedCost">检验数（面积口径），供定价排序 / The area-valued reduced cost, used for pricing order.</param>
public sealed record PatternGenerationResult2(
    Pattern2<Flt64> Pattern,
    string Source,
    Quantity<Flt64>? ReducedCost = null);

/// <summary>
/// 图案生成器 / A pattern generator.
/// </summary>
/// <remarks>
/// 用接口而不是抽象类：生成器是**无状态策略**，实现方只需要一个方法，抽象基类会逼它多实现一堆用不到的成员。
///
/// An interface rather than an abstract class: a generator is a **stateless strategy** with a single method, and an abstract base would force implementations to
/// declare members they never use.
/// </remarks>
public interface IPatternGenerator2 {
    /// <summary>生成候选图案 / Generate candidate patterns.</summary>
    /// <param name="request">生成请求 / The generation request.</param>
    /// <returns>候选图案列表 / The candidate pattern list.</returns>
    Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> Generate(
        PatternGenerationRequest2 request);
}

/// <summary>图案生成与定价的纯函数 / Pure functions for generation and pricing.</summary>
public static class Csp2dPatternGenerationSupport {
    /// <summary>
    /// 计算图案的检验数（面积口径）/ Compute the area-valued reduced cost of a pattern.
    /// </summary>
    /// <param name="pattern">裁切图案 / The cutting pattern.</param>
    /// <param name="shadowPrices">产品编号到影子价格的映射 / Product identifier to shadow price.</param>
    /// <param name="wasteWeight">余料权重 / The waste weight.</param>
    /// <returns>检验数 / The reduced cost.</returns>
    /// <remarks>
    /// 需求覆盖模型为 `min Σ 余料面积 · 使用次数 s.t. 覆盖 ≥ 需求`，对偶 `π ≥ 0`（按面积计价），因此
    /// `rc = 余料面积 · wasteWeight − Σ_放置 π_产品 · 放置面积`。**`rc` 为负表示该图案能改进目标。**
    ///
    /// 用「逐放置」而不是「逐产品乘以件数」求和，可以避免在泛型代码里把整数件数换算成数值类型。
    ///
    /// The coverage model is `min Σ waste area · usage s.t. coverage ≥ demand` with duals `π ≥ 0` priced by area, so the reduced cost is as above; **a negative value
    /// means the pattern can improve the objective**. Summing per placement instead of per product times its count avoids converting integer counts inside generic
    /// code.
    /// </remarks>
    public static Quantity<Flt64> PatternReducedCost(
        Pattern2<Flt64> pattern,
        IReadOnlyDictionary<string, Flt64> shadowPrices,
        Flt64 wasteWeight) {
        Quantity<Flt64> dual = new(Flt64.Zero, pattern.Region.Area.Unit);
        foreach (Placement2<Flt64> placement in pattern.Placements) {
            if (placement.ItemId is not { } productId) {
                continue;
            }
            if (!shadowPrices.TryGetValue(productId, out Flt64 price)) {
                continue;
            }
            if (dual.Add(ScaledBy(placement.Area, price)) is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> added) {
                dual = added.Value;
            }
        }
        Quantity<Flt64> waste = ScaledBy(
            pattern.WasteArea() is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> value
                ? value.Value
                : new Quantity<Flt64>(Flt64.Zero, pattern.Region.Area.Unit),
            wasteWeight);
        return waste.Subtract(dual) is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> reduced
            ? reduced.Value
            : waste;
    }

    /// <summary>
    /// 丢弃非改进图案，并按检验数升序排列 / Drop non-improving patterns and order the rest by ascending reduced cost.
    /// </summary>
    /// <param name="results">候选列表 / The candidate list.</param>
    /// <returns>改进图案列表 / The improving pattern list.</returns>
    /// <remarks>
    /// **没有检验数的结果一律保留**：缺检验数说明还没定价，而不是"已知不改进"——丢掉它会丢掉唯一可能的新方向。
    /// **排序时两侧缺检验数按相等处理**，不做插入位置猜测。
    ///
    /// **A result without a reduced cost is always kept**: a missing value means "not priced yet", not "known not to improve" — dropping it would drop the only possible
    /// new direction. **A missing value on either side compares as equal**, with no guessed insertion point.
    /// </remarks>
    public static IReadOnlyList<PatternGenerationResult2> ImprovingPatterns(
        IReadOnlyList<PatternGenerationResult2> results) {
        var improving = new List<PatternGenerationResult2>();
        foreach (PatternGenerationResult2 result in results) {
            if (result.ReducedCost is { } reducedCost && reducedCost.Value.ToDouble() >= 0.0) {
                continue;
            }
            improving.Add(result);
        }
        improving.Sort((lhs, rhs) => {
            if (lhs.ReducedCost is not { } left || rhs.ReducedCost is not { } right) {
                return 0;
            }
            if (left.Value.ToDouble() < right.Value.ToDouble()) {
                return -1;
            }
            return left.Value.ToDouble() > right.Value.ToDouble() ? 1 : 0;
        });
        return improving;
    }

    /// <summary>
    /// 图案去重键：放置集合与料域共同决定一个图案 / The deduplication key: the placement set and the region decide one pattern.
    /// </summary>
    /// <param name="pattern">裁切图案 / The cutting pattern.</param>
    /// <returns>去重键 / The deduplication key.</returns>
    /// <remarks>
    /// 放置串**排序后再拼**：放置顺序不影响几何，但会影响键，**不排序会让同一张排样生成两个键**。
    ///
    /// The placement strings are **sorted before joining**: their order does not affect geometry but does affect the key, and **not sorting gives the same layout two
    /// keys**.
    /// </remarks>
    public static string PatternKey(Pattern2<Flt64> pattern) {
        var pieces = new List<string>();
        foreach (Placement2<Flt64> placement in pattern.Placements) {
            pieces.Add(
                (placement.ItemId ?? "null") + "@" + placement.X.Value.ToDouble()
                + "~" + placement.Y.Value.ToDouble() + ":" + placement.Width.Value.ToDouble()
                + "x" + placement.Height.Value.ToDouble() + ":" + placement.Rotated);
        }
        pieces.Sort(System.StringComparer.Ordinal);
        return pattern.Region.X.Value.ToDouble() + "," + pattern.Region.Y.Value.ToDouble() + ","
            + pattern.Region.Width.Value.ToDouble() + "x" + pattern.Region.Height.Value.ToDouble()
            + "#" + string.Join("|", pieces);
    }

    /// <summary>
    /// 按去重键去重并截断候选数 / Deduplicate by key and truncate to the candidate limit.
    /// </summary>
    /// <param name="results">候选列表 / The candidate list.</param>
    /// <param name="limit">上限 / The limit.</param>
    /// <returns>去重后的候选 / The deduplicated candidates.</returns>
    /// <remarks>
    /// **先截断再判重会让重复把名额吃光**：达到 `limit` 就停，重复的键不占名额。
    ///
    /// **Truncating before deduplicating would let duplicates eat the quota**: the loop stops at `limit` and a duplicate key does not consume a slot.
    /// </remarks>
    public static IReadOnlyList<PatternGenerationResult2> DistinctPatterns(
        IReadOnlyList<PatternGenerationResult2> results,
        int limit) {
        var seen = new HashSet<string>();
        var kept = new List<PatternGenerationResult2>();
        foreach (PatternGenerationResult2 result in results) {
            if (kept.Count >= limit) {
                break;
            }
            if (seen.Add(PatternKey(result.Pattern))) {
                kept.Add(result);
            }
        }
        return kept;
    }

    /// <summary>按纯数值缩放一个量，单位不变 / Scale a quantity by a pure number, keeping the unit.</summary>
    /// <param name="quantity">量 / The quantity.</param>
    /// <param name="factor">系数 / The factor.</param>
    /// <returns>缩放后的量 / The scaled quantity.</returns>
    private static Quantity<Flt64> ScaledBy(Quantity<Flt64> quantity, Flt64 factor) =>
        new(quantity.Value.Times(factor), quantity.Unit);
}
