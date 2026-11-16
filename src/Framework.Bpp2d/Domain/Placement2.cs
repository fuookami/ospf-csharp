// 二维装载放置 / 2D packing placement.
// 与固定 Kotlin 对应：一个装载形状落在板材坐标系中的位置。所有几何判定都返回结果
// 类型，不使用空断言。
// The pinned Kotlin: a packing shape positioned on a sheet. Every
// geometry check returns a result type rather than asserting.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维装载放置 / 2D packing placement.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Shape">装载形状 / Packing shape.</param>
/// <param name="Position">左下角坐标 / Lower-left position.</param>
/// <param name="Rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
/// <param name="ItemId">物品标识，层内件数统计依赖此字段 / Item identifier used for layer amount statistics.</param>
public sealed record Placement2<V>(
    PackingShape2<V> Shape,
    QuantityPoint2<V> Position,
    bool Rotated = false,
    string? ItemId = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>x 坐标 / x coordinate.</summary>
    public Quantity<V> X => Position.X;

    /// <summary>y 坐标 / y coordinate.</summary>
    public Quantity<V> Y => Position.Y;

    /// <summary>包围宽度 / Bounding width.</summary>
    public Quantity<V> Width => Shape.BoundingWidth;

    /// <summary>包围高度 / Bounding height.</summary>
    public Quantity<V> Height => Shape.BoundingHeight;

    /// <summary>实际占用面积 / Actual occupied area.</summary>
    public Quantity<V> Area => Shape.Area;

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
    /// 生成放置对应的包围盒 / Build the bounding box of this placement.
    /// </summary>
    /// <returns>包围盒 / Bounding box.</returns>
    public Box2Geometry<V> Box() => new(X, Y, Shape.Geometry());

    /// <summary>
    /// 判断与另一放置是否重叠 / Check overlap with another placement.
    /// </summary>
    /// <param name="rhs">另一放置 / Other placement.</param>
    /// <returns>是否重叠或量纲错误 / Whether they overlap, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Placement2<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        return Box().Overlapped(rhs.Box());
    }

    /// <summary>
    /// 判断本放置是否完全位于给定板材内 / Check whether this placement lies entirely inside the sheet.
    /// </summary>
    /// <param name="sheet">板材几何 / Sheet geometry.</param>
    /// <returns>是否位于内部或量纲错误 / Whether inside, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> InsideSheet(ISheet2Geometry<V> sheet) {
        ArgumentNullException.ThrowIfNull(sheet);
        return Box().Inside(sheet.OriginBox());
    }

    /// <summary>
    /// 平移放置 / Translate the placement.
    /// </summary>
    /// <param name="offset">偏移向量 / Offset vector.</param>
    /// <returns>平移后的放置或量纲错误 / The translated placement, or a dimension error.</returns>
    public Result<Placement2<V>, ErrorCode, Error<ErrorCode>> Translate(
        QuantityVector2<V> offset) {
        ArgumentNullException.ThrowIfNull(offset);
        Result<QuantityPoint2<V>, ErrorCode, Error<ErrorCode>> moved = Position.Plus(offset);
        if (!moved.IsOk) {
            return moved switch {
                Failed<QuantityPoint2<V>, ErrorCode, Error<ErrorCode>> failure =>
                    Results.Failed<Placement2<V>>(failure.Error),
                Fatal<QuantityPoint2<V>, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<Placement2<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<Placement2<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "平移失败 / Translation failed")),
            };
        }
        return Results.Ok(this with { Position = moved.Value });
    }
}
