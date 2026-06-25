#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Quantities.Quantity;
/// <summary>
/// MinMax 扩展 / MinMax extensions on Quantity.
/// </summary>
public static class QuantityMinMax {
    /// <summary>取两个物理量的较小值 / Get the minimum of two quantities.</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Min<V>(Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, new DimensionMismatchException(lhs.Unit.Quantity, rhs.Unit.Quantity).Message));
        }

        if (lhs.Unit.Equals(rhs.Unit)) {
            return CompareValues(lhs.Value, rhs.Value) <= 0
                ? new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(lhs)
                : new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(rhs);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = rhs.To(lhs.Unit);
        if (converted.IsFailed) {
            return converted;
        }

        return CompareValues(lhs.Value, converted.Value.Value) <= 0
            ? new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(lhs)
            : new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(converted.Value with { Unit = lhs.Unit });
    }

    /// <summary>取两个物理量的较大值 / Get the maximum of two quantities.</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Max<V>(Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, new DimensionMismatchException(lhs.Unit.Quantity, rhs.Unit.Quantity).Message));
        }

        if (lhs.Unit.Equals(rhs.Unit)) {
            return CompareValues(lhs.Value, rhs.Value) >= 0
                ? new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(lhs)
                : new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(rhs);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = rhs.To(lhs.Unit);
        if (converted.IsFailed) {
            return converted;
        }

        return CompareValues(lhs.Value, converted.Value.Value) >= 0
            ? new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(lhs)
            : new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(converted.Value with { Unit = lhs.Unit });
    }

    private static int CompareValues<V>(V lhs, V rhs) {
        if (lhs is Flt64 f64l && rhs is Flt64 f64r) {
            return f64l.CompareTo(f64r);
        }

        if (lhs is FltX fxl && rhs is FltX fxr) {
            return fxl.CompareTo(fxr);
        }

        if (lhs is Int64 i64l && rhs is Int64 i64r) {
            return i64l.CompareTo(i64r);
        }

        if (lhs is IntX ixl && rhs is IntX ixr) {
            return ixl.CompareTo(ixr);
        }

        if (lhs is UInt64 u64l && rhs is UInt64 u64r) {
            return u64l.CompareTo(u64r);
        }

        throw new NotSupportedException($"Comparison not supported for type {typeof(V).Name}");
    }
}
