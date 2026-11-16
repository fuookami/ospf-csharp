// 一维装箱领域模型 / One-dimensional packing domain model.

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;

/// <summary>
/// 一维物品 / One-dimensional item.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">物品标识 / Item identity.</param>
/// <param name="Length">单件长度 / Piece length.</param>
/// <param name="Weight">单件重量 / Piece weight.</param>
/// <param name="Amount">精确需求件数 / Exact demand.</param>
/// <param name="Materials">单件物料贡献 / Per-piece material contributions.</param>
public sealed record Item<V>(
    string Id,
    Quantity<V> Length,
    Quantity<V> Weight,
    ulong Amount,
    IReadOnlyDictionary<string, ulong>? Materials = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>单件物料贡献；缺省为空表 / Per-piece material contributions; empty when absent.</summary>
    public IReadOnlyDictionary<string, ulong> MaterialsValue => Materials ?? EmptyMaterials;

    /// <summary>空物料表 / Empty material map.</summary>
    public static readonly IReadOnlyDictionary<string, ulong> EmptyMaterials = new Dictionary<string, ulong>();
}

/// <summary>
/// 箱型 / Bin type.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">箱型标识 / Bin type identity.</param>
/// <param name="Length">容量长度 / Length capacity.</param>
/// <param name="MaxWeight">承重 / Weight capacity.</param>
/// <param name="Cost">无量纲成本 / Dimensionless cost.</param>
/// <param name="Inventory">可用箱数，`null` 表示不限 / Available count, `null` for unlimited.</param>
public sealed record BinType<V>(
    string Id,
    Quantity<V> Length,
    Quantity<V> MaxWeight,
    V Cost,
    ulong? Inventory = null)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 实例化箱体 / Instantiated bin.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">箱体标识 / Bin identity.</param>
/// <param name="Type">箱型 / Bin type.</param>
public sealed record Bin<V>(string Id, BinType<V> Type)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 一维放置 / One-dimensional placement.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="ItemId">物品身份 / Item identity.</param>
/// <param name="Start">起点 / Start.</param>
/// <param name="Length">长度 / Length.</param>
public sealed record Placement<V>(string ItemId, Quantity<V> Start, Quantity<V> Length)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 组合列，以箱型和逐物品件数去重 / Pattern column, deduplicated by bin type and item counts.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="BinTypeId">箱型 / Bin type.</param>
/// <param name="Amounts">物品件数 / Item counts.</param>
public sealed record PackingPattern<V>(string BinTypeId, IReadOnlyDictionary<string, ulong> Amounts)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 稳定组合键 / Stable composition key.
    /// </summary>
    /// <remarks>
    /// **键里带长度前缀**（`id.Length + ":" + id`）：只用 `;` 与 `=` 分隔，`"a=1;b"` 与 `"a=1"` + `"b"`
    /// 这类带分隔符的标识会撞键。同样的长度前缀编码。
    ///
    /// **The key carries length prefixes** (`id.Length + ":" + id`): with only `;` and `=` separators, identifiers containing them (`"a=1;b"`) would collide. The code
    /// uses the same length-prefix encoding.
    /// </remarks>
    public string Key {
        get {
            var parts = new List<string>(Amounts.Count);
            var keys = new List<string>(Amounts.Count);
            foreach (KeyValuePair<string, ulong> pair in Amounts) {
                keys.Add(pair.Key);
            }
            keys.Sort(global::System.StringComparer.Ordinal);
            foreach (string key in keys) {
                parts.Add($"{key.Length}:{key}={Amounts[key]}");
            }
            return $"{BinTypeId.Length}:{BinTypeId}|" + string.Join(";", parts);
        }
    }
}

/// <summary>
/// 箱内完整放置 / Complete bin placement.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Bin">箱体 / Bin.</param>
/// <param name="Placements">单件放置 / Piece placements.</param>
public sealed record PackingPlan<V>(Bin<V> Bin, IReadOnlyList<Placement<V>> Placements)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 装箱目标 / Packing objective.
/// </summary>
public enum PackingObjective {
    /// <summary>箱数 / Bin count.</summary>
    BinCount,
    /// <summary>消耗长度 / Consumed length.</summary>
    ConsumedLength,
    /// <summary>成本 / Cost.</summary>
    Cost,
}

/// <summary>
/// 一维装箱输入 / One-dimensional packing input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Items">物品精确需求 / Exact item demands.</param>
/// <param name="BinTypes">箱型与库存 / Bin types and inventory.</param>
/// <param name="Spacing">相邻物品间距，无额外尾部间距 / Inter-piece spacing, no trailing spacing.</param>
/// <param name="Objective">目标口径 / Objective policy.</param>
public sealed record PackingProblem<V>(
    IReadOnlyList<Item<V>> Items,
    IReadOnlyList<BinType<V>> BinTypes,
    Quantity<V> Spacing,
    PackingObjective Objective = PackingObjective.BinCount)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 验证量纲、有限值、身份和容量 / Validate dimensions, finite values, identities and capacities.
    /// </summary>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Validate() {
        if (HasDuplicateOrBlank(Items, item => item.Id)) {
            return Invalid("item IDs");
        }
        if (BinTypes.Count == 0 || HasDuplicateOrBlank(BinTypes, bin => bin.Id)) {
            return Invalid("bin types");
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> gapResult = Spacing.To(SIBaseUnits.Meter);
        if (gapResult is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> gapOk) {
            return Invalid("spacing unit");
        }
        V gap = gapOk.Value.Value;
        if (!gap.IsFinite() || gap.CompareTo(Zero) < 0) {
            return Invalid("spacing");
        }

        foreach (Item<V> item in Items) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> length = item.Length.To(SIBaseUnits.Meter);
            if (length is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthOk) {
                return Invalid($"length unit {item.Id}");
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> weight = item.Weight.To(SIBaseUnits.Kilogram);
            if (weight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> weightOk) {
                return Invalid($"weight unit {item.Id}");
            }
            V lengthValue = lengthOk.Value.Value;
            V weightValue = weightOk.Value.Value;
            if (!lengthValue.IsFinite() || lengthValue.CompareTo(Zero) <= 0
                || !weightValue.IsFinite() || weightValue.CompareTo(Zero) < 0) {
                return Invalid($"item {item.Id}");
            }
        }

        foreach (BinType<V> bin in BinTypes) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> length = bin.Length.To(SIBaseUnits.Meter);
            if (length is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthOk) {
                return Invalid($"length unit {bin.Id}");
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> weight = bin.MaxWeight.To(SIBaseUnits.Kilogram);
            if (weight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> weightOk) {
                return Invalid($"weight unit {bin.Id}");
            }
            V lengthValue = lengthOk.Value.Value;
            V weightValue = weightOk.Value.Value;
            if (!lengthValue.IsFinite() || lengthValue.CompareTo(Zero) <= 0
                || !weightValue.IsFinite() || weightValue.CompareTo(Zero) < 0
                || !bin.Cost.IsFinite() || bin.Cost.CompareTo(Zero) <= 0) {
                return Invalid($"bin {bin.Id}");
            }
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// 验证外部或定价产生的组合列 / Validate an externally supplied or priced pattern.
    /// </summary>
    /// <param name="pattern">待验证列 / Pattern to validate.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **两处容易写错，对的**：
    ///
    /// 1. **间距只加在物品之间，不加尾部**：第一个物品加 `amount - 1` 份间距，其余加 `amount` 份。
    ///    若第一个也加 `amount`，每列都会凭空多出一段尾部间距，容量判定会**偏保守**。
    /// 2. **件数用二进制倍增，不逐件累加**：需求可以很大（`ulong`），逐件循环会卡住。
    ///
    /// **Two easy mistakes the code gets right**: (1) **spacing goes between pieces only, never trailing** — the first item adds `amount - 1` gaps and the rest add
    /// `amount`; adding `amount` for the first would invent a trailing gap per pattern and make the capacity check **conservatively wrong**. (2) **counts use binary
    /// scaling, not per-piece accumulation** — demand can be large (`ulong`) and a per-piece loop would stall.
    /// </remarks>
    public Try ValidatePattern(PackingPattern<V> pattern) {
        Try validation = Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        BinType<V>? bin = null;
        foreach (BinType<V> candidate in BinTypes) {
            if (candidate.Id == pattern.BinTypeId) {
                bin = candidate;
                break;
            }
        }
        if (bin is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "未知箱型 / Unknown bin type"));
        }

        V length = Zero;
        V weight = Zero;
        bool nonEmpty = false;

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> gapResult = Spacing.To(bin.Length.Unit);
        if (gapResult is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> gapOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "间距量纲与箱体长度不一致 / Spacing unit differs from the bin length unit"));
        }
        V gap = gapOk.Value.Value;

        foreach (KeyValuePair<string, ulong> pair in pattern.Amounts) {
            Item<V>? item = null;
            foreach (Item<V> candidate in Items) {
                if (candidate.Id == pair.Key) {
                    item = candidate;
                    break;
                }
            }
            if (item is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"未知物品 / Unknown item: {pair.Key}"));
            }
            if (pair.Value == 0 || pair.Value > item.Amount) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "方案件数无效 / Invalid pattern count"));
            }

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> itemLength = item.Length.To(bin.Length.Unit);
            if (itemLength is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> itemLengthOk) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"物品长度量纲不一致 / Item length unit differs: {item.Id}"));
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> itemWeight = item.Weight.To(bin.MaxWeight.Unit);
            if (itemWeight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> itemWeightOk) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"物品重量量纲不一致 / Item weight unit differs: {item.Id}"));
            }

            length = length.Plus(Scale(itemLengthOk.Value.Value, pair.Value));
            length = length.Plus(Scale(gap, nonEmpty ? pair.Value : pair.Value - 1));
            weight = weight.Plus(Scale(itemWeightOk.Value.Value, pair.Value));
            nonEmpty = true;
        }

        if (!nonEmpty || !length.IsFinite() || !weight.IsFinite()
            || length.CompareTo(bin.Length.Value) > 0 || weight.CompareTo(bin.MaxWeight.Value) > 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "方案超过箱体容量 / Pattern exceeds bin capacity"));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 按件数做二进制倍增 / Scale a value by a count using binary doubling.
    /// </summary>
    /// <param name="value">被缩放的值 / Value to scale.</param>
    /// <param name="count">件数 / Count.</param>
    /// <returns>缩放结果 / The scaled value.</returns>
    private static V Scale(V value, ulong count) {
        V factor = value;
        ulong remaining = count;
        V result = Zero;
        while (remaining > 0) {
            if ((remaining & 1UL) != 0) {
                result = result.Plus(factor);
            }
            remaining >>= 1;
            if (remaining > 0) {
                factor = factor.Plus(factor);
            }
        }
        return result;
    }

    /// <summary>标识是否重复或空白 / Whether identities are duplicated or blank.</summary>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <param name="values">元素列表 / Element list.</param>
    /// <param name="selector">标识选择器 / Identity selector.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool HasDuplicateOrBlank<T>(IReadOnlyList<T> values, global::System.Func<T, string> selector) {
        var seen = new HashSet<string>(values.Count);
        foreach (T value in values) {
            string id = selector(value);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>零值 / The additive zero.</summary>
    private static V Zero => NumericConstantsRegistry.For<V>().Zero;

    /// <summary>构造参数非法失败 / Build an invalid-argument failure.</summary>
    /// <param name="message">细节 / Detail.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Try Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"一维装箱输入无效 / Invalid BPP1D input: {message}"));
}
