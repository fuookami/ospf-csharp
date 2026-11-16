// 二维量纲点与向量 / 2D quantity point and vector.
// 对应。票量纲几何层只提供矩形、圆与包围盒，没有点位类型；
// 二维装箱的落点计算需要点位与向量，因此单独补充。
// . The quantity geometry layer only ships
// rectangles, circles and boxes — no point type — so 2D packing placement needs points and vectors provided
// separately.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维量纲向量 / 2D quantity vector.
/// </summary>
/// <remarks>
/// **与一处刻意差异**：`plus`／`minus` 直接返回 <c>QuantityVector2</c>，维度不匹配时只能抛异常；
/// C# 这里返回结果类型，让调用方拿到结构化错误。<see cref="QuantityArithmetic"/> 的 `Plus`／`Minus` 会抛
/// <c>ArgumentException</c>，本类型**刻意不复用它**——几何计算里一个量纲写错不该让整个排样崩掉，而应作为可处理的
/// 失败返回。
///
/// **A deliberate difference from the code**: its `plus`/`minus` return <c>QuantityVector2</c> and can
/// only throw on a dimension mismatch; these return a result type so callers get a structured failure.
/// <see cref="QuantityArithmetic"/>'s `Plus`/`Minus` throw <c>ArgumentException</c> and are **deliberately not
/// reused** here: a single wrong dimension in a geometry computation should be a handleable failure, not a crash.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="X">x 分量 / x component.</param>
/// <param name="Y">y 分量 / y component.</param>
public sealed record QuantityVector2<V>(Quantity<V> X, Quantity<V> Y)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 向量加法 / Vector addition.
    /// </summary>
    /// <param name="rhs">右操作数 / Right-hand operand.</param>
    /// <returns>加法结果或量纲错误 / The sum, or a dimension error.</returns>
    public Result<QuantityVector2<V>, ErrorCode, Error<ErrorCode>> Add(QuantityVector2<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = X.Add(rhs.X);
        if (!x.IsOk) {
            return Fail<QuantityVector2<V>>(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = Y.Add(rhs.Y);
        if (!y.IsOk) {
            return Fail<QuantityVector2<V>>(y);
        }
        return Results.Ok(new QuantityVector2<V>(x.Value, y.Value));
    }

    /// <summary>
    /// 向量减法 / Vector subtraction.
    /// </summary>
    /// <param name="rhs">右操作数 / Right-hand operand.</param>
    /// <returns>减法结果或量纲错误 / The difference, or a dimension error.</returns>
    public Result<QuantityVector2<V>, ErrorCode, Error<ErrorCode>> Subtract(QuantityVector2<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = X.Subtract(rhs.X);
        if (!x.IsOk) {
            return Fail<QuantityVector2<V>>(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = Y.Subtract(rhs.Y);
        if (!y.IsOk) {
            return Fail<QuantityVector2<V>>(y);
        }
        return Results.Ok(new QuantityVector2<V>(x.Value, y.Value));
    }

    /// <summary>
    /// 把结果失败原样转成目标类型 / Re-type a failed result to the target type.
    /// </summary>
    /// <typeparam name="T">目标类型 / Target type.</typeparam>
    /// <param name="failed">失败的中间结果 / The failed intermediate result.</param>
    /// <returns>同因失败 / A failure carrying the same cause.</returns>
    internal static Result<T, ErrorCode, Error<ErrorCode>> Fail<T>(
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> failed) =>
        failed switch {
            Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failure =>
                Results.Failed<T>(failure.Error),
            Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "量纲运算失败 / Quantity operation failed")),
        };
}

/// <summary>
/// 二维量纲点 / 2D quantity point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="X">x 坐标 / x coordinate.</param>
/// <param name="Y">y 坐标 / y coordinate.</param>
public sealed record QuantityPoint2<V>(Quantity<V> X, Quantity<V> Y)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 加上向量偏移 / Add a vector offset.
    /// </summary>
    /// <param name="offset">偏移向量 / Offset vector.</param>
    /// <returns>新点位或量纲错误 / The new point, or a dimension error.</returns>
    public Result<QuantityPoint2<V>, ErrorCode, Error<ErrorCode>> Plus(
        QuantityVector2<V> offset) {
        ArgumentNullException.ThrowIfNull(offset);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = X.Add(offset.X);
        if (!x.IsOk) {
            return QuantityVector2<V>.Fail<QuantityPoint2<V>>(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = Y.Add(offset.Y);
        if (!y.IsOk) {
            return QuantityVector2<V>.Fail<QuantityPoint2<V>>(y);
        }
        return Results.Ok(new QuantityPoint2<V>(x.Value, y.Value));
    }

    /// <summary>
    /// 减去向量偏移 / Subtract a vector offset.
    /// </summary>
    /// <param name="offset">偏移向量 / Offset vector.</param>
    /// <returns>新点位或量纲错误 / The new point, or a dimension error.</returns>
    public Result<QuantityPoint2<V>, ErrorCode, Error<ErrorCode>> Minus(
        QuantityVector2<V> offset) {
        ArgumentNullException.ThrowIfNull(offset);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = X.Subtract(offset.X);
        if (!x.IsOk) {
            return QuantityVector2<V>.Fail<QuantityPoint2<V>>(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = Y.Subtract(offset.Y);
        if (!y.IsOk) {
            return QuantityVector2<V>.Fail<QuantityPoint2<V>>(y);
        }
        return Results.Ok(new QuantityPoint2<V>(x.Value, y.Value));
    }

    /// <summary>
    /// 比较两点排序：先比较 y，再比较 x；不可比较时返回 null / Compare two points by y then x, or null when
    /// incomparable.
    /// </summary>
    /// <remarks>
    /// 先 y 后 x 的顺序不是随意选的：BLA（左下对齐）排样按"更低的位置优先、同高时更左优先"选位，
    /// 因此 y 是主键。顺序反过来会得到不同（且更差）的放置序列。
    ///
    /// The y-then-x order is not arbitrary: BLA (bottom-left) packing prefers the lower position and, at equal
    /// height, the leftmost, so y is the primary key. Reversing it produces a different — and worse — placement
    /// sequence.
    /// </remarks>
    /// <param name="rhs">右侧点位 / Right-hand point.</param>
    /// <returns>比较结果或 null / The comparison order, or null.</returns>
    public Order? OrdOrNull(QuantityPoint2<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        (Order? Ord, bool Comparable) y = QuantityCompare.Compare(Y, rhs.Y, "y");
        if (!y.Comparable) {
            return null;
        }
        if (y.Ord is not Order.Equal) {
            return y.Ord;
        }
        (Order? Ord, bool Comparable) x = QuantityCompare.Compare(X, rhs.X, "x");
        return x.Comparable ? x.Ord : null;
    }

    /// <summary>
    /// 构造二维量纲点 / Build a 2D quantity point.
    /// </summary>
    /// <param name="x">x 坐标 / x coordinate.</param>
    /// <param name="y">y 坐标 / y coordinate.</param>
    /// <returns>二维量纲点 / 2D quantity point.</returns>
    public static QuantityPoint2<V> Of(Quantity<V> x, Quantity<V> y) => new(x, y);
}

/// <summary>
/// 二维量纲点与向量的构造辅助 / 2D quantity point and vector builders.
/// </summary>
public static class QuantityPoint2Factory {
    /// <summary>
    /// 构造二维量纲点 / Build a 2D quantity point.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="x">x 坐标 / x coordinate.</param>
    /// <param name="y">y 坐标 / y coordinate.</param>
    /// <returns>二维量纲点 / 2D quantity point.</returns>
    public static QuantityPoint2<V> Point2Of<V>(Quantity<V> x, Quantity<V> y)
        where V : struct, IFloatingNumber<V> => new(x, y);

    /// <summary>
    /// 构造二维量纲向量 / Build a 2D quantity vector.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="x">x 分量 / x component.</param>
    /// <param name="y">y 分量 / y component.</param>
    /// <returns>二维量纲向量 / 2D quantity vector.</returns>
    public static QuantityVector2<V> Vector2Of<V>(Quantity<V> x, Quantity<V> y)
        where V : struct, IFloatingNumber<V> => new(x, y);
}
