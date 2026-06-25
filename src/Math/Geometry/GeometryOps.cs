#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 几何数量运算 / Geometry quantity operations (internal).
/// </summary>
internal static class GeometryOps {
    /// <summary>加法 / Addition.</summary>
    public static V Plus<V>(V lhs, V rhs) where V : struct, IFloatingNumber<V> => lhs.Plus(rhs);

    /// <summary>减法 / Subtraction.</summary>
    public static V Minus<V>(V lhs, V rhs) where V : struct, IFloatingNumber<V> => lhs.Minus(rhs);

    /// <summary>偏序比较（安全）/ Partial-order comparison (safe).</summary>
    public static Result<Order, ErrorCode, Error<ErrorCode>> OrdSafe<V>(V lhs, V rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => lhs.PartialOrd(rhs) is { } o
            ? Results.Ok(o)
            : Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Incomparable scalar: axis={axis}"));

    /// <summary>安全最大值 / Safe max.</summary>
    public static Result<V, ErrorCode, Error<ErrorCode>> MaxSafe<V>(V lhs, V rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => OrdSafe(lhs, rhs, axis).Map(o => o is Order.Greater or Order.Equal ? lhs : rhs);

    /// <summary>安全最小值 / Safe min.</summary>
    public static Result<V, ErrorCode, Error<ErrorCode>> MinSafe<V>(V lhs, V rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => OrdSafe(lhs, rhs, axis).Map(o => o is Order.Greater ? rhs : lhs);

    /// <summary>最大值（别名）/ Max (alias for MaxSafe).</summary>
    public static Result<V, ErrorCode, Error<ErrorCode>> Max<V>(V lhs, V rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => MaxSafe(lhs, rhs, axis);

    /// <summary>最小值（别名）/ Min (alias for MinSafe).</summary>
    public static Result<V, ErrorCode, Error<ErrorCode>> Min<V>(V lhs, V rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => MinSafe(lhs, rhs, axis);

    /// <summary>钳制 / Clamp.</summary>
    public static Result<V, ErrorCode, Error<ErrorCode>> Clamp<V>(V value, V lb, V ub, string axis)
        where V : struct, IFloatingNumber<V> {
        if (value.PartialOrd(lb) is Order.Less) {
            return Results.Ok(lb);
        }

        return MaxSafe(value, ub, axis + "-ub").Map(o => o is Order.Greater ? ub : value);
    }

    /// <summary>是否在范围内 / Whether in range.</summary>
    public static Result<bool, ErrorCode, Error<ErrorCode>> ContainsInRange<V>(
        V value, V lb, V ub, bool withLower, bool withUpper, string axis)
        where V : struct, IFloatingNumber<V> {
        Order? lo = value.PartialOrd(lb);
        Order? hi = value.PartialOrd(ub);
        if (lo is { } l && hi is { } h) {
            bool lowerOk = withLower ? l is Order.Equal or Order.Greater : l is Order.Greater;
            bool upperOk = withUpper ? h is Order.Equal or Order.Less : h is Order.Less;
            return Results.Ok(lowerOk && upperOk);
        }
        return Results.Failed<bool>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Incomparable scalar: axis={axis}"));
    }

    /// <summary>零值 / Zero of type.</summary>
    public static V ZeroOf<V>(V value) where V : struct, IFloatingNumber<V> => value.Constants.Zero;

    /// <summary>多值 min/max（math/ordinary.minMax 的本地替身）/ Local stand-in for ordinary.MinMax.</summary>
    public static (V Min, V Max) MinMax<V>(params V[] values) where V : struct, IFloatingNumber<V> {
        V min = values[0];
        V max = values[0];
        foreach (V v in values) {
            if (v.Ls(min)) {
                min = v;
            }

            if (v.Gr(max)) {
                max = v;
            }
        }
        return (min, max);
    }
}
