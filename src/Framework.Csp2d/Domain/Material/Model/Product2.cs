// 待裁产品、需求与需求集合 / Products, demands and demand sets.
// 与固定 Kotlin 基线 `csp2d/domain/material/model/Product2.kt` 对应（单件面积见文件末尾说明）。
// Mirrors the pinned Kotlin baseline's `Product2.kt` (single-piece area is scoped out; see the tail note).

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;

/// <summary>需求口径 / Demand unit.</summary>
public enum DemandMode2 {
    /// <summary>件数 / Piece count.</summary>
    Amount,

    /// <summary>面积 / Area.</summary>
    Area,

    /// <summary>重量 / Weight.</summary>
    Weight,
}

/// <summary>
/// 需求量，按口径取值 / A demanded quantity, valued according to its unit.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Mode">需求口径 / The demand unit.</param>
public abstract record DemandValue2<V>(DemandMode2 Mode)
    where V : struct, IFloatingNumber<V> {
    /// <summary>件数口径的需求量 / A demand expressed as a piece count.</summary>
    /// <param name="Count">件数 / The piece count.</param>
    public sealed record Amount(ulong Count) : DemandValue2<V>(DemandMode2.Amount);

    /// <summary>面积口径的需求量 / A demand expressed as an area.</summary>
    /// <param name="QuantityArea">面积 / The area.</param>
    public sealed record Area(Quantity<V> QuantityArea) : DemandValue2<V>(DemandMode2.Area);

    /// <summary>重量口径的需求量 / A demand expressed as a weight.</summary>
    /// <param name="QuantityWeight">重量 / The weight.</param>
    public sealed record Weight(Quantity<V> QuantityWeight) : DemandValue2<V>(DemandMode2.Weight);
}

/// <summary>
/// 待裁产品（成品件） / A product to be cut (a finished part).
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">产品编号 / The product identifier.</param>
/// <param name="Name">产品名称 / The product name.</param>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <param name="AllowRotate">是否允许旋转 90° / Whether a 90-degree rotation is allowed.</param>
/// <param name="UnitWeight">单位重量 / The unit weight.</param>
/// <remarks>
/// **旋转限制是生产上的常见约束**（木纹、纸纹、贴面方向），所以它是件本身的属性，不是排样时的开关。
/// 放在排样配置里会让**同一批件在不同排样里行为不一致**。
///
/// **A rotation restriction is a common production constraint** (wood grain, paper grain, facing direction), so it belongs to the piece, not to the nesting run. Putting
/// it in the nesting configuration would let **the same batch behave differently across runs**.
/// </remarks>
public sealed record Product2<V>(
    IProductId2 Id,
    string Name,
    Quantity<V> Width,
    Quantity<V> Height,
    bool AllowRotate = true,
    Quantity<V>? UnitWeight = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 校验产品几何 / Validate the product geometry.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    /// <remarks>
    /// **宽高必须严格为正**：零宽或负宽的件在几何上不存在，放进排样会让后续所有重叠与越界判定失去意义。
    ///
    /// **Width and height must be strictly positive**: a zero- or negative-sized piece does not exist geometrically, and letting one in makes every later overlap and
    /// bounds check meaningless.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Width.Value.ToFlt64().ToDouble() <= 0.0 || Height.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "产品 " + Id.Value + " 的宽高必须为正 / Product dimensions must be positive"));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 允许的摆放方向集合 / The set of allowed orientations.
    /// </summary>
    /// <returns>方向列表，第一个为不旋转 / The orientation list, the first entry being unrotated.</returns>
    public IReadOnlyList<bool> Orientations() =>
        AllowRotate ? new List<bool> { false, true } : new List<bool> { false };
}

/// <summary>
/// 产品需求 / A product demand.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Product">产品 / The product.</param>
/// <param name="Demand">需求量 / The demanded quantity.</param>
public sealed record ProductDemand2<V>(Product2<V> Product, DemandValue2<V> Demand)
    where V : struct, IFloatingNumber<V> {
    /// <summary>需求口径 / The demand unit.</summary>
    public DemandMode2 Mode => Demand.Mode;

    /// <summary>
    /// 件数口径下的件数，其它口径返回空 / The piece count under the amount unit, null for other units.
    /// </summary>
    /// <returns>件数或空 / The piece count or null.</returns>
    /// <remarks>
    /// **其它口径返回 `null` 而不是 0**：面积口径下的 0 会被下游当成"不需要任何件"，
    /// 而真实语义是**"这个数在件数口径下没有意义"**。
    ///
    /// **Other units return `null`, never zero**: a zero under the area unit would be read downstream as "no pieces needed", while the real meaning is **"this number is
    /// meaningless in pieces"**.
    /// </remarks>
    public ulong? PiecesOrNull() => Demand is DemandValue2<V>.Amount amount ? amount.Count : null;
}

/// <summary>
/// 需求条目集合，按产品编号索引 / A demand entry set indexed by product identifier.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Demands">需求列表 / The demand list.</param>
public sealed record ProductDemands2<V>(IReadOnlyList<ProductDemand2<V>> Demands)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 产品编号到需求的映射 / The map from product identifier to demand.
    /// </summary>
    /// <returns>需求映射 / The demand map.</returns>
    /// <remarks>
    /// **后写覆盖先写**——所以调用前必须经过 `DuplicateProducts()` 的检查，否则**同一个产品只留下一条需求**，
    /// 而需求总量少了，**看起来像"需求更小、更容易做"**。
    ///
    /// **A later entry overwrites an earlier one**, so `DuplicateProducts()` must run first; otherwise **one demand survives per product**, the total demand shrinks,
    /// and it **looks like "a smaller, easier problem"**.
    /// </remarks>
    public IReadOnlyDictionary<string, ProductDemand2<V>> ByProduct() {
        var map = new Dictionary<string, ProductDemand2<V>>();
        foreach (ProductDemand2<V> demand in Demands) {
            map[demand.Product.Id.Value] = demand;
        }
        return map;
    }

    /// <summary>
    /// 重复的产品编号 / The duplicated product identifiers.
    /// </summary>
    /// <returns>重复编号，按字典序 / The duplicated identifiers in ordinal order.</returns>
    public IReadOnlyList<string> DuplicateProducts() {
        var counts = new Dictionary<string, int>();
        foreach (ProductDemand2<V> demand in Demands) {
            string key = demand.Product.Id.Value;
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
        }
        var duplicated = new List<string>();
        foreach (KeyValuePair<string, int> pair in counts) {
            if (pair.Value > 1) {
                duplicated.Add(pair.Key);
            }
        }
        duplicated.Sort(string.CompareOrdinal);
        return duplicated;
    }

    /// <summary>
    /// 校验全部需求，并拒绝重复产品 / Validate every demand and reject duplicate products.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        IReadOnlyList<string> duplicated = DuplicateProducts();
        if (duplicated.Count > 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "需求中存在重复产品：/ Duplicate products in the demands: " + string.Join(", ", duplicated)));
        }
        return Results.OkInstance;
    }
}
