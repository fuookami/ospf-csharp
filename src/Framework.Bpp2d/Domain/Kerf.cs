// 刀缝与间距 / Kerf and spacing.
// 对应。排样中的 kerf（刀缝/锯缝）要求相邻件之间保留最小间隙。
// 实现方式是**放大参与重叠判定的形状**：把一件的覆盖形状按 kerf 膨胀后与另一件的真实形状比较，若仍相交则说明
// 真实间隙小于 kerf。这样无需在每个放置上记录额外偏移，坐标仍是真实坐标。
// . Kerf (saw blade width) requires a minimum gap between
// neighbouring pieces, implemented by **inflating the shape used in the overlap test**: inflating one piece's
// footprint by the kerf and comparing it with the other piece's real shape reports intersection exactly when the
// real gap is smaller than the kerf. Placement coordinates therefore stay physical.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 刀缝与间距 / Kerf and spacing.
/// </summary>
public static class Kerf {
    /// <summary>
    /// 按间距膨胀装载形状 / Inflate a packing shape by a spacing.
    /// </summary>
    /// <remarks>
    /// 矩形件宽高各加 <paramref name="spacing"/>；圆形件直径加 <paramref name="spacing"/>，即半径加
    /// <paramref name="spacing"/> 的一半。**非正间距返回原形状**：负的刀缝没有物理意义，把它当成"负膨胀"会让
    /// 相邻件反而可以相互嵌入。
    ///
    /// A rectangle grows by the spacing in both width and height; a circle grows in diameter, i.e. by half the
    /// spacing in radius. **A non-positive spacing returns the shape unchanged**: a negative kerf is physically
    /// meaningless, and treating it as negative inflation would let neighbours interpenetrate.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <returns>膨胀后的形状 / Inflated shape.</returns>
    public static PackingShape2<V> InflatedBy<V>(
        this PackingShape2<V> shape,
        Quantity<V> spacing)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        if (!IsPositive(spacing)) {
            return shape;
        }
        return shape switch {
            RectanglePackingShape2<V> rectangle => new RectanglePackingShape2<V>(
                QuantityOps.PlusSafe(rectangle.Width, spacing).Value,
                QuantityOps.PlusSafe(rectangle.Height, spacing).Value),
            CirclePackingShape2<V> circle => new CirclePackingShape2<V>(
                QuantityOps.PlusSafe(circle.Radius, Half(spacing)).Value),
            _ => shape,
        };
    }

    /// <summary>
    /// 按间距膨胀放置的形状，位置保持不变 / Inflate the shape of a placement, keeping its position.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="placement">放置 / Placement.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <returns>膨胀后的放置 / Inflated placement.</returns>
    public static Placement2<V> InflatedBy<V>(
        this Placement2<V> placement,
        Quantity<V> spacing)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(placement);
        return placement with { Shape = placement.Shape.InflatedBy(spacing) };
    }

    /// <summary>
    /// 计算考虑刀缝后的节距 / Compute the pitch including the kerf.
    /// </summary>
    /// <remarks>
    /// 规则网格按节距排布时，节距等于包围尺寸加 kerf 即可让真实间隙恰为 kerf。非正间距按零处理，节距退化为包围
    /// 尺寸——此时相邻件恰好贴边，而贴边在重叠判定里**不算重叠**（见 `QuantityBox2.Overlapped`），所以这是一个
    /// 自洽的退化情形。
    ///
    /// With a regular grid, a pitch equal to the bounding size plus the kerf makes the real gap exactly the kerf.
    /// A non-positive spacing degrades to zero, so the pitch equals the bounding size: neighbours then touch
    /// exactly, and touching is **not an overlap** (see `QuantityBox2.Overlapped`), keeping the case self-consistent.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <returns>水平与垂直节距 / Horizontal and vertical pitch.</returns>
    public static QuantityVector2<V> PitchOf<V>(
        PackingShape2<V> shape,
        Quantity<V> spacing)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        Quantity<V> effective = IsPositive(spacing)
            ? spacing
            : QuantityCompare.ZeroOf(shape.BoundingWidth);
        return new QuantityVector2<V>(
            QuantityOps.PlusSafe(shape.BoundingWidth, effective).Value,
            QuantityOps.PlusSafe(shape.BoundingHeight, effective).Value);
    }

    /// <summary>
    /// 由样本构造零间距 / Build a zero spacing from a sample.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">样本量值 / Sample quantity.</param>
    /// <returns>零间距 / Zero spacing.</returns>
    public static Quantity<V> ZeroSpacingOf<V>(Quantity<V> sample)
        where V : struct, IFloatingNumber<V> => QuantityCompare.ZeroOf(sample);

    /// <summary>取半 / Halve a quantity.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <returns>一半 / Half of it.</returns>
    private static Quantity<V> Half<V>(Quantity<V> value)
        where V : struct, IFloatingNumber<V> =>
        QuantityOps.QuantityProduct(value, value.Value.Constants.Half);

    /// <summary>是否严格为正 / Whether the quantity is strictly positive.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <returns>是否严格为正 / Whether strictly positive.</returns>
    private static bool IsPositive<V>(Quantity<V> value)
        where V : struct, IFloatingNumber<V> =>
        QuantityCompare.StrictlyGreater(value, QuantityCompare.ZeroOf(value), "spacing");
}
