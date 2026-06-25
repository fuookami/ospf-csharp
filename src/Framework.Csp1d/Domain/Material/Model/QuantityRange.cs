#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 物理量区间辅助方法 / Quantity range helper methods
/// </summary>
internal static class QuantityRangeHelpers {
    /// <summary>
    /// 运行时安全比较两个物理量（不要求 IFloatingNumber 约束）/
    /// Runtime safe comparison of two quantities (no IFloatingNumber constraint required)
    /// </summary>
    public static Result<Order, ErrorCode, Error<ErrorCode>> CompareSafe<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct {
        if (lhs.Unit.Equals(rhs.Unit)) {
            Order ord = CompareValues(lhs.Value, rhs.Value);
            return Results.Ok(ord);
        }

        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Quantity dimension mismatch for comparison: axis={axis}"));
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = lhs.To(rhs.Unit);
        if (converted is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Unit conversion failed for comparison: axis={axis}"));
        }

        return Results.Ok(CompareValues(converted.Value.Value, rhs.Value));
    }

    private static Order CompareValues<V>(V lhs, V rhs) {
        if (lhs is Flt64 f64l && rhs is Flt64 f64r) {
            return f64l.CompareTo(f64r) < 0 ? new Order.Less() : f64l.CompareTo(f64r) > 0 ? new Order.Greater() : new Order.Equal();
        }

        if (lhs is FltX fxl && rhs is FltX fxr) { int c = fxl.CompareTo(fxr); return c < 0 ? new Order.Less() : c > 0 ? new Order.Greater() : new Order.Equal(); }
        if (lhs is Int64 i64l && rhs is Int64 i64r) { int c = i64l.CompareTo(i64r); return c < 0 ? new Order.Less() : c > 0 ? new Order.Greater() : new Order.Equal(); }
        if (lhs is IntX ixl && rhs is IntX ixr) { int c = ixl.CompareTo(ixr); return c < 0 ? new Order.Less() : c > 0 ? new Order.Greater() : new Order.Equal(); }
        if (lhs is UInt64 u64l && rhs is UInt64 u64r) { int c = u64l.CompareTo(u64r); return c < 0 ? new Order.Less() : c > 0 ? new Order.Greater() : new Order.Equal(); }
        throw new NotSupportedException($"Comparison not supported for type {typeof(V).Name}");
    }
}

/// <summary>
/// 物理量区间，使用显式上下界和开闭区间语义 / Quantity range with explicit bounds and interval semantics
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="LowerBound">下界 / Lower bound.</param>
/// <param name="UpperBound">上界 / Upper bound.</param>
/// <param name="LowerInclusive">下界是否包含 / Whether lower bound is inclusive.</param>
/// <param name="UpperInclusive">上界是否包含 / Whether upper bound is inclusive.</param>
public sealed record QuantityRange<V>(
    Quantity<V> LowerBound,
    Quantity<V> UpperBound,
    bool LowerInclusive = true,
    bool UpperInclusive = true
) where V : struct {
    /// <summary>
    /// 判断值是否落在区间内 / Check whether the value is inside the range
    /// </summary>
    /// <param name="value">待判断值 / Value to check.</param>
    /// <returns>是否在区间内 / Whether inside the range.</returns>
    public bool Contains(Quantity<V> value) {
        Result<Order, ErrorCode, Error<ErrorCode>> leftOrdResult = QuantityRangeHelpers.CompareSafe(value, LowerBound, "quantity-range-lower");
        if (leftOrdResult.IsFailed) {
            return false;
        }

        Order leftOrd = leftOrdResult.Value;

        Result<Order, ErrorCode, Error<ErrorCode>> rightOrdResult = QuantityRangeHelpers.CompareSafe(value, UpperBound, "quantity-range-upper");
        if (rightOrdResult.IsFailed) {
            return false;
        }

        Order rightOrd = rightOrdResult.Value;

        bool lowerOk = leftOrd switch {
            Order.Greater => true,
            Order.Equal => LowerInclusive,
            _ => false
        };
        bool upperOk = rightOrd switch {
            Order.Less => true,
            Order.Equal => UpperInclusive,
            _ => false
        };
        return lowerOk && upperOk;
    }
}
