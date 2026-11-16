// 二维几何类型扩展 / Extensions for the 2D quantity geometry types.
// 与固定 Kotlin 对应。基线注释说明了取舍：quantities 层已提供矩形、圆、
// 包围盒及其重叠判定，本文件**只补装箱需要的派生量，不重定义既有类型**。
// The pinned Kotlin: . The code states the trade-off:
// the quantity layer already ships rectangles, circles, boxes and their overlap tests, so this file **only adds
// packing-oriented derived values without redefining them**.
//
// 因此这里**刻意不移植**的 `boundingWidth`／`boundingHeight`／`minX`／`minY`／`maxX`／`maxY`／
// `areaOf`（矩形）：C# 的 `QuantityBox2.Width`／`Height`／`X`／`Y`／`MaxX`／`MaxY` 与
// `QuantityRectangle2.Area` 已经是同名能力，再加一层只是别名。只有**确实缺失**的两项被补上。
// Therefore its `boundingWidth`/`boundingHeight`/`minX`/`minY`/`maxX`/`maxY`/
// `areaOf` (rectangle) are **deliberately not implemented**: C# already has `QuantityBox2.Width`/`Height`/`X`/`Y`/
// `MaxX`/`MaxY` and `QuantityRectangle2.Area` under those names, so another layer would be aliases only. Just
// the two genuinely missing pieces are added.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维几何类型扩展 / Extensions for the 2D quantity geometry types.
/// </summary>
public static class Geometry2Extensions {
    /// <summary>
    /// 由圆形半径计算面积 πr²，量纲为面积 / Area of a circle πr², with the area dimension.
    /// </summary>
    /// <remarks>
    /// C# 的 <see cref="QuantityCircle2{V}.Area"/> 需要调用方传入 π，而 π 本就属于数值类型自身
    /// （<c>V.Constants.Pi</c>），让每个调用点各自填一个 π 既啰嗦又可能不一致。本方法从半径的数值类型取 π。
    ///
    /// C#'s <see cref="QuantityCircle2{V}.Area"/> requires the caller to pass π, although π belongs to the numeric
    /// type itself (<c>V.Constants.Pi</c>); making every call site supply its own π is both noisy and a source of
    /// inconsistency. This takes π from the radius's own numeric type.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="circle">圆形投影 / Circle projection.</param>
    /// <returns>面积 / Area.</returns>
    public static Quantity<V> AreaOf<V>(this QuantityCircle2<V> circle)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(circle);
        return QuantityOps.QuantityProduct(
            QuantityOps.QuantityProduct(circle.Radius, circle.Radius),
            circle.Radius.Value.Constants.Pi);
    }

    /// <summary>
    /// 包围盒的**外接矩形**面积 / The **enclosing rectangle** area of a bounding box.
    /// </summary>
    /// <remarks>
    /// 与圆形件的实际面积 πr² 不同：包围盒面积恒为 `(2r)² = 4r²`。利用率计算若误用这一项，圆形件的占用会被高估。
    ///
    /// This differs from a circle's actual πr² area: the enclosing box is always `(2r)² = 4r²`. Using this value for
    /// utilization would overstate how much a circular item occupies.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="box">包围盒 / Bounding box.</param>
    /// <returns>外接矩形面积 / Enclosing rectangle area.</returns>
    public static Quantity<V> BoundingArea<V>(this QuantityBox2<V> box)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(box);
        return QuantityOps.QuantityProduct(box.Width, box.Height);
    }

    /// <summary>
    /// 判断包围盒是否完全落在给定板材范围内，**允许贴边** / Check whether the box lies entirely inside the
    /// sheet, **touching allowed**.
    /// </summary>
    /// <remarks>
    /// 贴板材边是合法放置；判成越界会让板材边缘一圈区域无法使用。因此下界用 `&gt;=`、上界用 `&lt;=`。
    /// A piece flush with the sheet edge is a legal placement; rejecting it would make a border strip of the sheet
    /// unusable. The lower bound is therefore `&gt;=` and the upper bound `&lt;=`.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="box">包围盒 / Bounding box.</param>
    /// <param name="sheetWidth">板材宽度 / Sheet width.</param>
    /// <param name="sheetHeight">板材高度 / Sheet height.</param>
    /// <returns>是否在范围内或量纲错误 / Whether inside the sheet, or a dimension error.</returns>
    public static Result<bool, ErrorCode, Error<ErrorCode>> InsideSheet<V>(
        this QuantityBox2<V> box,
        Quantity<V> sheetWidth,
        Quantity<V> sheetHeight)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(box);
        Quantity<V> zero = QuantityCompare.ZeroOf(box.X);
        bool inside =
            QuantityCompare.AtLeast(box.X, zero, "x") &&
            QuantityCompare.AtLeast(box.Y, zero, "y") &&
            QuantityCompare.AtMost(
                QuantityOps.PlusSafe(box.X, box.Width).Value, sheetWidth, "maxX") &&
            QuantityCompare.AtMost(
                QuantityOps.PlusSafe(box.Y, box.Height).Value, sheetHeight, "maxY");
        return Results.Ok(inside);
    }
}
