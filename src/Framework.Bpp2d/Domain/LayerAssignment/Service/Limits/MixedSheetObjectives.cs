// 混合板材目标 / Mixed sheet objectives.
//
// 的两个 `*Minimization` 类依赖 `PreciseAssignment`（C# 侧不存在），见文末说明。
// The code's two `*Minimization` classes depend on `PreciseAssignment`, which does not exist on the C# side — see the tail note.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 混合板材目标与余料统计 / Mixed sheet objectives and waste statistics.
/// </summary>
/// <remarks>
/// **余料率的分母是板材类型面积，不是层的外接矩形面积**。C# 侧 `Layer.Utilization` 的分母是后者，
/// 两者在同一层上会给出不同的数：一列只占了板材一角时，"利用率"按它自己的包围盒算可能接近 1，
/// 而按板材算可能只有 0.1。把它们混用会让"这块板浪费多少"这个问题得到两个都合理、但都不对的答案。
///
/// **The waste ratio divides by the sheet type's area, not by the layer's bounding rectangle.** C#'s `Layer.Utilization` divides by the latter, and the two give different
/// numbers for the same layer: when a column occupies one corner of a sheet, "utilisation" measured against its own bounding box can approach 1 while measured against the
/// sheet it may be 0.1. Mixing them gives two plausible but both wrong answers to "how much of this sheet is wasted".
/// </remarks>
public static class MixedSheetObjectives {
    /// <summary>
    /// 单列的余料面积 / The waste area of one column.
    /// </summary>
    /// <param name="layer">候选列 / The candidate column.</param>
    /// <returns>余料面积 / The waste area.</returns>
    /// <remarks>
    /// 列未绑定板材类型时返回零：**没有板材就无从谈起这块板的余料**。超出板材时也返回零，理由与 `BinWaste.Volume` 相同——
    /// 负余料会在求和里抵消别的列的余料。
    ///
    /// A column with no sheet type returns zero: **without a sheet there is no sheet waste to speak of**. Over-capacity also returns zero for the same reason as
    /// `BinWaste.Volume` — negative waste would cancel another column's waste in a sum.
    /// </remarks>
    public static FltX LayerWasteArea(Layer<FltX> layer) {
        System.ArgumentNullException.ThrowIfNull(layer);
        if (layer.Sheet is null) {
            return FltX.Zero;
        }

        FltX sheetArea = layer.Sheet.Area.Value;
        FltX occupied = layer.Area.Value;
        return sheetArea.CompareTo(occupied) > 0 ? sheetArea - occupied : FltX.Zero;
    }

    /// <summary>
    /// 单列的归一化余料率 / The normalised waste ratio of one column.
    /// </summary>
    /// <param name="layer">候选列 / The candidate column.</param>
    /// <returns>落在 `[0, 1]` 的余料率 / The waste ratio within `[0, 1]`.</returns>
    /// <remarks>
    /// 归一化的意义是**跨板材类型可比**：绝对余料面积对小板是灾难、对大板是常态，只有比率能直接比较。
    /// 分母为零或负时返回零，避免 `NaN` 让下游所有比较为假。
    ///
    /// Normalising makes the figure **comparable across sheet types**: an absolute waste area is a disaster on a small sheet and business as usual on a large one, and only
    /// the ratio can be compared directly. A zero or negative denominator returns zero so `NaN` cannot make every downstream comparison false.
    /// </remarks>
    public static FltX LayerWasteRatio(Layer<FltX> layer) {
        System.ArgumentNullException.ThrowIfNull(layer);
        if (layer.Sheet is null) {
            return FltX.Zero;
        }

        FltX sheetArea = layer.Sheet.Area.Value;
        if (sheetArea.CompareTo(FltX.Zero) <= 0) {
            return FltX.Zero;
        }
        return LayerWasteArea(layer) / sheetArea;
    }

    /// <summary>
    /// 按板材类型取值的成本函数 / A cost lookup keyed by sheet type code.
    /// </summary>
    /// <param name="typeCost">板材类型到成本的映射 / The sheet type to cost map.</param>
    /// <param name="defaultCost">未配置类型的缺省成本 / The default cost for unconfigured types.</param>
    /// <returns>以列为输入的成本函数 / The cost function taking a column.</returns>
    /// <remarks>
    /// **"列没有板材类型"与"板材类型不在价目表里"都落回缺省成本**：`layer.sheet?.let { ... } ?: default` 把两种情形合并了，
    /// 这里保持同样的口径。若前者给零，一个还没绑定板材的列会在成本上免费，求解器于是偏爱它。
    ///
    /// **Both "the column has no sheet type" and "the type is absent from the price table" fall back to the default**: its `layer.sheet?.let { ... } ?: default`
    /// merges the two, and the same convention is kept here. Returning zero for the former would make an unbound column free, and the solver would then prefer it.
    /// </remarks>
    public static System.Func<Layer<FltX>, FltX> PerTypeCost(
        IReadOnlyDictionary<SheetTypeId, FltX> typeCost,
        FltX? defaultCost = null) {
        System.ArgumentNullException.ThrowIfNull(typeCost);
        FltX fallback = defaultCost ?? FltX.Zero;
        return layer => layer.Sheet is { } sheet && typeCost.TryGetValue(sheet.TypeCode, out FltX configured)
            ? configured
            : fallback;
    }

    /// <summary>
    /// 按板材类型取值的数量系数函数 / An amount-coefficient lookup keyed by sheet type code.
    /// </summary>
    /// <param name="typeCoefficient">板材类型到系数的映射 / The sheet type to coefficient map.</param>
    /// <param name="defaultCoefficient">未配置类型的缺省系数 / The default coefficient for unconfigured types.</param>
    /// <returns>以列为输入的系数函数 / The coefficient function taking a column.</returns>
    /// <remarks>
    /// **缺省系数是 1，不是 0**。系数在目标里乘的是"用了几块这种板"，缺省给 0 等于说"没配系数的板材不计入用量"，
    /// 于是求解器可以把任意多块板塞进一个恰好没配系数的类型里，而目标值一点都不变。
    ///
    /// **The default coefficient is 1, not 0.** The coefficient multiplies "how many sheets of this type are used", so a default of 0 would mean "unconfigured sheet types
    /// do not count towards usage": the solver could then pack arbitrarily many sheets of a type that happens to be missing from the table without moving the objective.
    /// </remarks>
    public static System.Func<Layer<FltX>, FltX> PerTypeAmountCoefficient(
        IReadOnlyDictionary<SheetTypeId, FltX> typeCoefficient,
        FltX? defaultCoefficient = null) {
        System.ArgumentNullException.ThrowIfNull(typeCoefficient);
        FltX fallback = defaultCoefficient ?? FltX.One;
        return layer => layer.Sheet is { } sheet && typeCoefficient.TryGetValue(sheet.TypeCode, out FltX configured)
            ? configured
            : fallback;
    }

    /// <summary>
    /// 判断两个板材类型是否等价 / Whether two sheet types are equivalent.
    /// </summary>
    /// <param name="lhs">左板材类型 / The left sheet type.</param>
    /// <param name="rhs">右板材类型 / The right sheet type.</param>
    /// <returns>是否等价 / Whether they are equivalent.</returns>
    /// <remarks>
    /// **只比类型编码，不比尺寸**：同编码不同尺寸在数据里是矛盾的输入，但判等时按编码走才能让"同类型的两列合并"生效。
    /// **两侧都为空时返回真**：两个都没有板材类型的列都是未绑定的，可以互换——这不是漏判，是有意为之。
    ///
    /// **Only the type code is compared, not the dimensions**: same code with different dimensions is contradictory input, but comparing codes is what lets two columns of
    /// the same type be merged. **Both sides null returns true**: two columns without a sheet type are both unbound and interchangeable — that is deliberate, not an
    /// oversight.
    /// </remarks>
    public static bool SameSheetType(SheetType<FltX>? lhs, SheetType<FltX>? rhs) {
        if (lhs is null || rhs is null) {
            return lhs is null && rhs is null;
        }
        return EqualityComparer<SheetTypeId>.Default.Equals(lhs.TypeCode, rhs.TypeCode);
    }
}
