#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 物理量几何运算辅助函数 / Quantity geometry operation helpers.
/// 提供物理量的乘积、加减、比较、取极值、限制范围和获取零值等辅助函数。
/// Provides helpers for quantity product, addition/subtraction, comparison, clamping, and zero value retrieval.
/// </summary>
public static class QuantityOps {
    // ===== Infallible operations =====

    /// <summary>两个物理量相乘（不可失败）/ Multiply two quantities (infallible).</summary>
    public static Quantity<V> QuantityProduct<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V>
        => lhs.Multiply(rhs);

    /// <summary>物理量乘以标量（不可失败）/ Multiply quantity by scalar (infallible).</summary>
    public static Quantity<V> QuantityProduct<V>(Quantity<V> lhs, V rhs)
        where V : struct, IRealNumber<V>, INumberField<V>
        => lhs.MultiplyScalar(rhs);

    /// <summary>获取与给定物理量同单位的零值（不可失败）/ Get zero-valued quantity with same unit (infallible).</summary>
    public static Quantity<V> QuantityZeroOf<V>(Quantity<V> quantity)
        where V : struct, IFloatingNumber<V>
        => new(quantity.Value.Constants.Zero, quantity.Unit);

    // ===== Safe arithmetic =====

    /// <summary>两个物理量安全相加 / Safely add two quantities (dimension mismatch -> Failed).</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> PlusSafe<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V>
        => lhs.Add(rhs);

    /// <summary>两个物理量安全相减 / Safely subtract two quantities (dimension mismatch -> Failed).</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MinusSafe<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IRealNumber<V>, INumberField<V>
        => lhs.Subtract(rhs);

    // ===== Comparison =====

    /// <summary>安全比较两个物理量 / Safe partial-order comparison of two quantities.</summary>
    public static Result<Order, ErrorCode, Error<ErrorCode>> OrdSafe<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> {
        if (lhs.Unit == rhs.Unit) {
            Order? ord = lhs.Value.PartialOrd(rhs.Value);
            return ord is not null
                ? Results.Ok(ord)
                : Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"Incomparable value: axis={axis}"));
        }

        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Quantity dimension mismatch for comparison: axis={axis}, {lhs.Unit.Quantity.DimensionSymbol()} vs {rhs.Unit.Quantity.DimensionSymbol()}"));
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = lhs.To(rhs.Unit);
        if (converted is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> fc) {
            return Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Unit conversion failed for comparison: axis={axis}, {lhs.Unit} -> {rhs.Unit}"));
        }

        Order? ordConv = converted.Value.Value.PartialOrd(rhs.Value);
        return ordConv is not null
            ? Results.Ok(ordConv)
            : Results.Failed<Order>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Incomparable value after conversion: axis={axis}"));
    }

    // ===== Min / Max =====

    /// <summary>安全取较大值 / Safe max of two quantities.</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxSafe<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => OrdSafe(lhs, rhs, axis).Map(o => o is Order.Greater or Order.Equal ? lhs : rhs);

    /// <summary>安全取较小值 / Safe min of two quantities.</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MinSafe<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V>
        => OrdSafe(lhs, rhs, axis).Map(o => o is Order.Greater ? rhs : lhs);

    // ===== Clamp =====

    /// <summary>将物理量限制在指定范围内 / Clamp a quantity to a range.</summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ClampSafe<V>(
        Quantity<V> value, Quantity<V> lb, Quantity<V> ub, string axis)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> lowerOrd = OrdSafe(value, lb, $"{axis}-lb");
        if (lowerOrd is Failed<Order, ErrorCode, Error<ErrorCode>> fl) {
            return Results.Failed<Quantity<V>>(fl.Error);
        }

        if (lowerOrd.Value is Order.Less) {
            return Results.Ok(lb);
        }

        return OrdSafe(value, ub, $"{axis}-ub").Map(o => o is Order.Greater ? ub : value);
    }

    // ===== Range check =====

    /// <summary>判断物理量是否在指定范围内 / Check if a quantity is within a range.</summary>
    public static Result<bool, ErrorCode, Error<ErrorCode>> ContainsInRangeSafe<V>(
        Quantity<V> value, Quantity<V> lb, Quantity<V> ub,
        bool withLowerBound, bool withUpperBound, string axis)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> lowerOrd = OrdSafe(value, lb, axis);
        if (lowerOrd is Failed<Order, ErrorCode, Error<ErrorCode>> fl) {
            return Results.Failed<bool>(fl.Error);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> upperOrd = OrdSafe(value, ub, axis);
        if (upperOrd is Failed<Order, ErrorCode, Error<ErrorCode>> fu) {
            return Results.Failed<bool>(fu.Error);
        }

        bool lowerOk = withLowerBound
            ? lowerOrd.Value is Order.Equal or Order.Greater
            : lowerOrd.Value is Order.Greater;
        bool upperOk = withUpperBound
            ? upperOrd.Value is Order.Equal or Order.Less
            : upperOrd.Value is Order.Less;

        return Results.Ok(lowerOk && upperOk);
    }
}
