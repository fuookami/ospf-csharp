#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Quantities.Symbol;

/// <summary>
/// 符号多项式物理量运算扩展 / Symbol polynomial quantity operations extensions.
/// 为符号多项式物理量提供单位转换、加减运算和求值扩展函数。
/// Provides unit conversion, addition/subtraction, and evaluation extension functions
/// for symbol polynomial quantities.
/// </summary>
public static class SymbolQuantityOps {
    // ===== Unit conversion =====

    /// <summary>线性多项式物理量单位转换 (Flt64) / Unit conversion for linear polynomial quantity (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>>? ToLinearFlt64(
        this Quantity<LinearPolynomial<Flt64>> q, PhysicalUnit unit) {
        if (q.Unit == unit) {
            return q;
        }

        if (!q.Unit.Quantity.Equals(unit.Quantity)) {
            return null;
        }

        Scale? factor = q.Unit.To(unit);
        if (factor is not { } resolvedFactor || resolvedFactor.Value is not { } resolvedValue) {
            return null;
        }

        var f = resolvedValue.ToFlt64();

        var convertedMonomials = new List<LinearMonomial<Flt64>>();
        foreach (LinearMonomial<Flt64> m in q.Value.Monomials) {
            convertedMonomials.Add(new LinearMonomial<Flt64>(m.Coefficient.Times(f), m.Symbol));
        }

        return new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>((IReadOnlyList<LinearMonomial<Flt64>>)convertedMonomials, q.Value.Constant.Times(f)),
            unit);
    }

    /// <summary>线性多项式物理量单位转换 (FltX) / Unit conversion for linear polynomial quantity (FltX).</summary>
    public static Quantity<LinearPolynomial<FltX>>? ToLinearFltX(
        this Quantity<LinearPolynomial<FltX>> q, PhysicalUnit unit) {
        if (q.Unit == unit) {
            return q;
        }

        if (!q.Unit.Quantity.Equals(unit.Quantity)) {
            return null;
        }

        Scale? factor = q.Unit.To(unit);
        if (factor is not { } resolvedFactor || resolvedFactor.Value is not { } resolvedValue) {
            return null;
        }

        var f = resolvedValue.ToFltX();

        var convertedMonomials = new List<LinearMonomial<FltX>>();
        foreach (LinearMonomial<FltX> m in q.Value.Monomials) {
            convertedMonomials.Add(new LinearMonomial<FltX>(m.Coefficient.Times(f), m.Symbol));
        }

        return new Quantity<LinearPolynomial<FltX>>(
            new LinearPolynomial<FltX>((IReadOnlyList<LinearMonomial<FltX>>)convertedMonomials, q.Value.Constant.Times(f)),
            unit);
    }

    // ===== Addition / Subtraction (LinearPolynomial<Flt64>) =====

    /// <summary>线性多项式物理量安全加法 (Flt64) / Safe addition for linear polynomial quantities (Flt64).</summary>
    public static Result<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> PlusSafe(
        this Quantity<LinearPolynomial<Flt64>> lhs, Quantity<LinearPolynomial<Flt64>> rhs) {
        Result<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> convResult = ConvertOperandSafe(lhs, rhs, "addition", (q, u) => q.ToLinearFlt64(u));
        if (convResult is Failed<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> fc) {
            return Results.Failed<Quantity<LinearPolynomial<Flt64>>>(fc.Error);
        }

        return Results.Ok(new Quantity<LinearPolynomial<Flt64>>(lhs.Value + convResult.Value.Value, lhs.Unit));
    }

    /// <summary>线性多项式物理量安全减法 (Flt64) / Safe subtraction for linear polynomial quantities (Flt64).</summary>
    public static Result<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> MinusSafe(
        this Quantity<LinearPolynomial<Flt64>> lhs, Quantity<LinearPolynomial<Flt64>> rhs) {
        Result<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> convResult = ConvertOperandSafe(lhs, rhs, "subtraction", (q, u) => q.ToLinearFlt64(u));
        if (convResult is Failed<Quantity<LinearPolynomial<Flt64>>, ErrorCode, Error<ErrorCode>> fc) {
            return Results.Failed<Quantity<LinearPolynomial<Flt64>>>(fc.Error);
        }

        return Results.Ok(new Quantity<LinearPolynomial<Flt64>>(lhs.Value - convResult.Value.Value, lhs.Unit));
    }

    /// <summary>线性多项式物理量加法，失败返回 null (Flt64) / Addition, null on failure (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>>? PlusOrNull(
        this Quantity<LinearPolynomial<Flt64>> lhs, Quantity<LinearPolynomial<Flt64>> rhs)
        => lhs.PlusSafe(rhs).Value;

    /// <summary>线性多项式物理量减法，失败返回 null (Flt64) / Subtraction, null on failure (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>>? MinusOrNull(
        this Quantity<LinearPolynomial<Flt64>> lhs, Quantity<LinearPolynomial<Flt64>> rhs)
        => lhs.MinusSafe(rhs).Value;

    // ===== Addition / Subtraction (LinearPolynomial<FltX>) =====

    /// <summary>线性多项式物理量安全加法 (FltX) / Safe addition for linear polynomial quantities (FltX).</summary>
    public static Result<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> PlusSafe(
        this Quantity<LinearPolynomial<FltX>> lhs, Quantity<LinearPolynomial<FltX>> rhs) {
        Result<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> convResult = ConvertOperandSafe(lhs, rhs, "addition", (q, u) => q.ToLinearFltX(u));
        if (convResult is Failed<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> fc) {
            return Results.Failed<Quantity<LinearPolynomial<FltX>>>(fc.Error);
        }

        return Results.Ok(new Quantity<LinearPolynomial<FltX>>(lhs.Value + convResult.Value.Value, lhs.Unit));
    }

    /// <summary>线性多项式物理量安全减法 (FltX) / Safe subtraction for linear polynomial quantities (FltX).</summary>
    public static Result<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> MinusSafe(
        this Quantity<LinearPolynomial<FltX>> lhs, Quantity<LinearPolynomial<FltX>> rhs) {
        Result<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> convResult = ConvertOperandSafe(lhs, rhs, "subtraction", (q, u) => q.ToLinearFltX(u));
        if (convResult is Failed<Quantity<LinearPolynomial<FltX>>, ErrorCode, Error<ErrorCode>> fc) {
            return Results.Failed<Quantity<LinearPolynomial<FltX>>>(fc.Error);
        }

        return Results.Ok(new Quantity<LinearPolynomial<FltX>>(lhs.Value - convResult.Value.Value, lhs.Unit));
    }

    // ===== Scalar multiply / divide =====

    /// <summary>线性多项式物理量乘以标量 (Flt64) / Multiply linear polynomial quantity by scalar (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>> Times(
        this Quantity<LinearPolynomial<Flt64>> lhs, Flt64 scalar)
        => new(lhs.Value * scalar, lhs.Unit);

    /// <summary>标量乘以线性多项式物理量 (Flt64) / Scalar times linear polynomial quantity (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>> Times(
        this Flt64 scalar, Quantity<LinearPolynomial<Flt64>> rhs)
        => new(scalar * rhs.Value, rhs.Unit);

    /// <summary>线性多项式物理量除以标量 (Flt64) / Divide linear polynomial quantity by scalar (Flt64).</summary>
    public static Quantity<LinearPolynomial<Flt64>> Div(
        this Quantity<LinearPolynomial<Flt64>> lhs, Flt64 scalar)
        => new(lhs.Value * scalar.Reciprocal(), lhs.Unit);

    /// <summary>线性多项式物理量乘以标量 (FltX) / Multiply linear polynomial quantity by scalar (FltX).</summary>
    public static Quantity<LinearPolynomial<FltX>> Times(
        this Quantity<LinearPolynomial<FltX>> lhs, FltX scalar)
        => new(lhs.Value * scalar, lhs.Unit);

    /// <summary>线性多项式物理量除以标量 (FltX) / Divide linear polynomial quantity by scalar (FltX).</summary>
    public static Quantity<LinearPolynomial<FltX>> Div(
        this Quantity<LinearPolynomial<FltX>> lhs, FltX scalar)
        => new(lhs.Value * scalar.Reciprocal(), lhs.Unit);

    // ===== Evaluation =====

    /// <summary>求值线性多项式物理量 (Flt64) / Evaluate linear polynomial quantity (Flt64).</summary>
    public static Quantity<Flt64>? Evaluate(
        this Quantity<LinearPolynomial<Flt64>> q, IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64 evaluated = q.Value.Evaluate(values);
        return new Quantity<Flt64>(evaluated, q.Unit);
    }

    // ===== Internal helpers =====

    private static Result<Quantity<P>, ErrorCode, Error<ErrorCode>> ConvertOperandSafe<P>(
        Quantity<P> lhs, Quantity<P> rhs, string operation,
        Func<Quantity<P>, PhysicalUnit, Quantity<P>?> convert) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return Results.Failed<Quantity<P>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Quantity dimension mismatch for {operation}: expected {lhs.Unit.Quantity.DimensionSymbol()}, got {rhs.Unit.Quantity.DimensionSymbol()}"));
        }

        if (lhs.Unit == rhs.Unit) {
            return Results.Ok(rhs);
        }

        Quantity<P>? converted = convert(rhs, lhs.Unit);
        if (converted is null) {
            return Results.Failed<Quantity<P>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"Quantity unit conversion failed for {operation}."));
        }

        return Results.Ok(converted);
    }
}
