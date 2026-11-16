// 二维包围盒几何 / 2D bounding box geometry.
// 与固定 Kotlin 对应：在 `QuantityBox2` 之上提供装箱所需的包含与相交能力。
// The pinned Kotlin: containment and intersection on top of
// `QuantityBox2`.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维包围盒，用于装载形状之间的重叠与包含判定 / 2D bounding box for overlap and containment tests.
/// </summary>
/// <remarks>
/// **重叠语义统一为「内部重叠」：相切不算重叠**。矩形贴边、圆与圆相切、圆与矩形边界相切都视为不重叠。
/// 这与网格化排样的前提一致——相切是排样中最常见的相邻关系，若把相切判为重叠，任何规则网格都无法成立。
/// 该语义已由 <see cref="QuantityBox2{V}.Overlapped"/> 完整实现，本类型**直接委托**而不是重写一套相交算法；
/// Kotlin 文件头也声明了这一委托意图，只是其代码另写了一份。
///
/// **Overlap is uniformly "interior overlap": tangency is not an overlap.** Edge contact between rectangles and
/// tangent circles are all disjoint, matching grid packing: tangency is the most common adjacency and treating it
/// as overlap would invalidate every regular grid. <see cref="QuantityBox2{V}.Overlapped"/> already implements
/// exactly this, so this type **delegates** rather than re-deriving the intersection maths.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="X">左下角 x 坐标 / Lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / Lower-left y coordinate.</param>
/// <param name="Shape">形状投影 / Shape projection.</param>
public sealed record Box2Geometry<V>(Quantity<V> X, Quantity<V> Y, IQuantityProjection2<V> Shape)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 由角点与尺寸构造矩形包围盒 / Build a rectangle box from an origin and a size.
    /// </summary>
    /// <param name="x">左下角 x 坐标 / Lower-left x coordinate.</param>
    /// <param name="y">左下角 y 坐标 / Lower-left y coordinate.</param>
    /// <param name="width">宽度 / Width.</param>
    /// <param name="height">高度 / Height.</param>
    /// <returns>矩形包围盒 / Rectangle box.</returns>
    public static Box2Geometry<V> Of(
        Quantity<V> x,
        Quantity<V> y,
        Quantity<V> width,
        Quantity<V> height) =>
        new(x, y, new QuantityRectangle2<V>(width, height));

    /// <summary>宽度 / Width.</summary>
    public Quantity<V> Width => Shape switch {
        QuantityRectangle2<V> rectangle => rectangle.Width,
        QuantityCircle2<V> circle => circle.Diameter,
        _ => throw new NotSupportedException("未知二维投影形状 / Unknown 2D projection shape"),
    };

    /// <summary>高度 / Height.</summary>
    public Quantity<V> Height => Shape switch {
        QuantityRectangle2<V> rectangle => rectangle.Height,
        QuantityCircle2<V> circle => circle.Diameter,
        _ => throw new NotSupportedException("未知二维投影形状 / Unknown 2D projection shape"),
    };

    /// <summary>
    /// 中心 x 坐标 / Center x coordinate.
    /// </summary>
    /// <remarks>
    /// **与一处刻意差异**：`centerX`／`centerY` 对矩形返回**左下角坐标**而不是中心，与注释所述不符。
    /// 这里按注释语义计算真实中心。照搬一个明显与自身文档矛盾的实现不算"同步能力"。
    ///
    /// **A deliberate difference**: its `centerX`/`centerY` return the **lower-left corner** for
    /// rectangles, contradicting its own documentation. The real centre is computed here; faithfully copying an
    /// implementation that contradicts its own doc comment is not "capability sync".
    /// </remarks>
    public Quantity<V> CenterX => Shape switch {
        QuantityRectangle2<V> rectangle => QuantityOps.PlusSafe(X, Half(rectangle.Width)).Value,
        QuantityCircle2<V> circle => QuantityOps.PlusSafe(X, circle.Radius).Value,
        _ => X,
    };

    /// <summary>中心 y 坐标 / Center y coordinate.</summary>
    public Quantity<V> CenterY => Shape switch {
        QuantityRectangle2<V> rectangle => QuantityOps.PlusSafe(Y, Half(rectangle.Height)).Value,
        QuantityCircle2<V> circle => QuantityOps.PlusSafe(Y, circle.Radius).Value,
        _ => Y,
    };

    /// <summary>
    /// 右上角 x 坐标 / Top-right x coordinate.
    /// </summary>
    /// <returns>x 坐标或量纲错误 / The coordinate, or a dimension error.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() =>
        QuantityOps.PlusSafe(X, Width);

    /// <summary>
    /// 右上角 y 坐标 / Top-right y coordinate.
    /// </summary>
    /// <returns>y 坐标或量纲错误 / The coordinate, or a dimension error.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() =>
        QuantityOps.PlusSafe(Y, Height);

    /// <summary>
    /// 判断与另一包围盒是否（内部）重叠 / Check interior overlap with another box.
    /// </summary>
    /// <param name="rhs">另一包围盒 / Other box.</param>
    /// <returns>是否重叠或量纲错误 / Whether they overlap, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Box2Geometry<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        return ToQuantityBox().Overlapped(rhs.ToQuantityBox());
    }

    /// <summary>
    /// 判断本包围盒是否完全位于另一包围盒内部，**允许贴边** / Check whether this box lies entirely inside
    /// another, **touching allowed**.
    /// </summary>
    /// <param name="outer">外部包围盒 / Outer box.</param>
    /// <returns>是否位于内部或量纲错误 / Whether inside, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Inside(Box2Geometry<V> outer) {
        ArgumentNullException.ThrowIfNull(outer);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownMaxX = MaxX();
        if (!ownMaxX.IsOk) {
            return ReType(ownMaxX);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownMaxY = MaxY();
        if (!ownMaxY.IsOk) {
            return ReType(ownMaxY);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> outerMaxX = outer.MaxX();
        if (!outerMaxX.IsOk) {
            return ReType(outerMaxX);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> outerMaxY = outer.MaxY();
        if (!outerMaxY.IsOk) {
            return ReType(outerMaxY);
        }
        bool inside =
            AtLeast(X, outer.X, "x") &&
            AtLeast(Y, outer.Y, "y") &&
            AtMost(ownMaxX.Value, outerMaxX.Value, "maxX") &&
            AtMost(ownMaxY.Value, outerMaxY.Value, "maxY");
        return Results.Ok(inside);
    }

    /// <summary>
    /// 计算与另一矩形包围盒的交集面积 / Compute the intersection area with another rectangle box.
    /// </summary>
    /// <param name="rhs">另一矩形包围盒 / Other rectangle box.</param>
    /// <returns>交集面积，无交集时为零 / Intersection area, zero when disjoint.</returns>
    public Quantity<V> OverlapArea(Box2Geometry<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        Quantity<V> zero = QuantityOps.QuantityZeroOf(X);
        Result<QuantityBox2<V>?, ErrorCode, Error<ErrorCode>> intersection =
            ToQuantityBox().Intersect(rhs.ToQuantityBox());
        if (!intersection.IsOk || intersection.Value is not { } box) {
            return QuantityOps.QuantityProduct(zero, zero);
        }
        return QuantityOps.QuantityProduct(box.Width, box.Height);
    }

    /// <summary>
    /// 判断包围盒是否退化为零尺寸 / Check whether the box has a degenerate zero extent.
    /// </summary>
    /// <returns>是否退化 / Whether degenerate.</returns>
    public bool IsDegenerate() {
        Quantity<V> zero = QuantityOps.QuantityZeroOf(X);
        return AtMost(Width, zero, "width") || AtMost(Height, zero, "height");
    }

    /// <summary>
    /// 构造对应的 <see cref="QuantityBox2{V}"/> / Build the underlying box.
    /// </summary>
    /// <returns>底层包围盒 / Underlying box.</returns>
    private QuantityBox2<V> ToQuantityBox() => new(X, Y, Shape);

    /// <summary>取半 / Halve a quantity.</summary>
    /// <param name="value">量值 / Quantity.</param>
    /// <returns>一半 / Half of it.</returns>
    private static Quantity<V> Half(Quantity<V> value) =>
        QuantityOps.QuantityProduct(value, value.Value.Constants.Half);

    /// <summary>大于等于 / Greater than or equal.</summary>
    private static bool AtLeast(Quantity<V> lhs, Quantity<V> rhs, string axis) =>
        QuantityCompare.AtLeast(lhs, rhs, axis);

    /// <summary>小于等于 / Less than or equal.</summary>
    private static bool AtMost(Quantity<V> lhs, Quantity<V> rhs, string axis) =>
        QuantityCompare.AtMost(lhs, rhs, axis);

    /// <summary>把量值失败转成布尔失败 / Re-type a quantity failure into a boolean failure.</summary>
    private static Result<bool, ErrorCode, Error<ErrorCode>> ReType(
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> failed) =>
        failed switch {
            Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failure =>
                Results.Failed<bool>(failure.Error),
            Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<bool, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<bool>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "包围盒比较失败 / Box comparison failed")),
        };
}
