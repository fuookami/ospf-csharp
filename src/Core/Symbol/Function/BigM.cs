#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using NInt8 = Fuookami.Ospf.Math.Algebra.Number.Int8;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using NUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
using NUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 线性多项式上下界记录 / Linear polynomial bounds record for Big-M method
/// </summary>
public sealed record LinearPolynomialBounds<V>(
    V LowerBound,
    V UpperBound
) where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// Big-M 边界推导 / Big-M bound inference.
/// </summary>
internal static class BigMValues {
    public const double Default = 1_000_000.0;
    public const double Minimum = 1.0;

    /// <summary>验证值可作为求解器边界 / Check that the value is usable as a solver bound.</summary>
    public static bool IsUsable(Flt64 value) {
        double number = value.ToDouble();
        return double.IsFinite(number) &&
            number > -double.MaxValue &&
            number < double.MaxValue;
    }

    /// <summary>验证数值可编码为求解器边界 / Check whether a value can be encoded as a solver bound.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">待检查数值 / Value to check.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <returns>数值可编码时为 true / True when the value can be encoded.</returns>
    public static bool IsUsable<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            return IsUsable(converter.FromValue(value));
        }
        catch (OverflowException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    /// <summary>推导线性多项式的有限上下界 / Infer finite bounds for a linear polynomial.</summary>
    public static LinearPolynomialBounds<V>? FiniteBounds<V>(
        LinearPolynomial<V> polynomial,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (!ValueBounds(
                    value: polynomial.Constant,
                    converter: converter,
                    lower: out ExactBoundValue lower,
                    upper: out ExactBoundValue upper
                )) {
                return null;
            }
            foreach (LinearMonomial<V> monomial in polynomial.Monomials) {
                if (!ValueBounds(
                        value: monomial.Coefficient,
                        converter: converter,
                        lower: out ExactBoundValue coefficientLower,
                        upper: out ExactBoundValue coefficientUpper
                    ) ||
                    !SymbolBounds(
                        symbol: monomial.Symbol,
                        converter: converter,
                        lower: out ExactBoundValue symbolLower,
                        upper: out ExactBoundValue symbolUpper
                    )) {
                    return null;
                }
                ExactBoundValue first = coefficientLower.Multiply(symbolLower);
                ExactBoundValue second = coefficientLower.Multiply(symbolUpper);
                ExactBoundValue third = coefficientUpper.Multiply(symbolLower);
                ExactBoundValue fourth = coefficientUpper.Multiply(symbolUpper);
                ExactBoundValue lowerTerm = first;
                ExactBoundValue upperTerm = first;
                foreach (ExactBoundValue product in new[] { second, third, fourth }) {
                    lowerTerm = product.CompareTo(lowerTerm) < 0 ? product : lowerTerm;
                    upperTerm = product.CompareTo(upperTerm) > 0 ? product : upperTerm;
                }
                if (!RoundBound(
                        value: lowerTerm,
                        converter: converter,
                        upper: false,
                        result: out lowerTerm
                    ) || !RoundBound(
                        value: upperTerm,
                        converter: converter,
                        upper: true,
                        result: out upperTerm
                    ) || !RoundBound(
                        value: lower.Add(lowerTerm),
                        converter: converter,
                        upper: false,
                        result: out lower
                    ) || !RoundBound(
                        value: upper.Add(upperTerm),
                        converter: converter,
                        upper: true,
                        result: out upper
                    ) || lower.CompareTo(upper) > 0) {
                    return null;
                }
            }
            if (!lower.TryRound(
                    converter: converter,
                    upper: false,
                    result: out V lowerValue
                ) || !upper.TryRound(
                    converter: converter,
                    upper: true,
                    result: out V upperValue
                )) {
                return null;
            }
            return new LinearPolynomialBounds<V>(
                lowerValue,
                upperValue
            );
        }
        catch (ArithmeticException) {
            return null;
        }
        catch (InvalidOperationException) {
            return null;
        }
    }

    /// <summary>将候选值提升到最小可用 Big-M / Promote a candidate to the minimum usable Big-M.</summary>
    public static V EnsurePositiveBigM<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V minimum = converter.IntoValue(new Flt64(Minimum));
        return value.Geq(minimum) ? value : minimum;
    }

    /// <summary>推导可表示的 Big-M；无法证明时返回空 / Infer a representable Big-M, or null when unproven.</summary>
    public static V? InferredBigM<V>(
        LinearPolynomial<V> polynomial,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            LinearPolynomialBounds<V>? bounds = FiniteBounds(polynomial, converter);
            if (bounds is null || !TryMagnitude(
                    bounds: bounds,
                    converter: converter,
                    magnitude: out ExactBoundValue magnitude
                ) || !magnitude.TryRoundSolverUpper(converter, out V candidate)) {
                return null;
            }
            V positive = EnsurePositiveBigM(candidate, converter);
            return IsUsable(positive, converter) && positive.Gr(converter.Zero) ? positive : null;
        }
        catch (ArithmeticException) {
            return null;
        }
        catch (InvalidOperationException) {
            return null;
        }
    }

    private static bool TryMagnitude<V>(
        LinearPolynomialBounds<V> bounds,
        IIntoValue<V> converter,
        out ExactBoundValue magnitude)
        where V : struct, IRealNumber<V>, INumberField<V> {
        magnitude = default;
        if (!ExactBoundValue.TryRead(
                value: bounds.LowerBound,
                converter: converter,
                exact: out ExactBoundValue lower
            ) || !ExactBoundValue.TryRead(
                value: bounds.UpperBound,
                converter: converter,
                exact: out ExactBoundValue upper
            )) {
            return false;
        }
        lower = lower.Abs();
        upper = upper.Abs();
        magnitude = lower.CompareTo(upper) >= 0 ? lower : upper;
        return true;
    }

    private static bool ValueBounds<V>(
        V value,
        IIntoValue<V> converter,
        out ExactBoundValue lower,
        out ExactBoundValue upper)
        where V : struct, IRealNumber<V>, INumberField<V> {
        lower = default;
        upper = default;
        if (!ExactBoundValue.TryRead(
                value: value,
                converter: converter,
                exact: out ExactBoundValue native
            ) ||
            !ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded)) {
            return false;
        }
        lower = native.CompareTo(encoded) <= 0 ? native : encoded;
        upper = native.CompareTo(encoded) >= 0 ? native : encoded;
        return RoundBound(
                value: lower,
                converter: converter,
                upper: false,
                result: out lower
            ) && RoundBound(
                value: upper,
                converter: converter,
                upper: true,
                result: out upper
            );
    }

    private static bool SymbolBounds<V>(
        ISymbol symbol,
        IIntoValue<V> converter,
        out ExactBoundValue lower,
        out ExactBoundValue upper)
        where V : struct, IRealNumber<V>, INumberField<V> {
        lower = default;
        upper = default;
        if (symbol is IVariableItem) {
            object? range = (symbol as IVariableRangeSnapshot)?.CurrentRange;
            return range switch {
                ValueRange<V> typed => ReadRange(
                    range: typed,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                ValueRange<Flt64> real => ReadRange(
                    range: real,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                ValueRange<NInt64> integer => ReadRange(
                    range: integer,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                ValueRange<NUInt64> unsigned => ReadRange(
                    range: unsigned,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                ValueRange<NUInt8> binary => ReadRange(
                    range: binary,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                ValueRange<NInt8> ternary => ReadRange(
                    range: ternary,
                    converter: converter,
                    lower: out lower,
                    upper: out upper
                ),
                _ => false,
            };
        }
        if (symbol is IIntermediateSymbol<V> intermediate) {
            return intermediate.Range.Range is { } range && ReadRange(
                range: range,
                converter: converter,
                lower: out lower,
                upper: out upper
            );
        }
        return false;
    }

    private static bool ReadRange<T, V>(
        ValueRange<T> range,
        IIntoValue<V> converter,
        out ExactBoundValue lower,
        out ExactBoundValue upper)
        where T : struct, IRealNumber<T>, INumberField<T>
        where V : struct, IRealNumber<V>, INumberField<V> {
        lower = default;
        upper = default;
        T? first = range.LowerBound.Value.UnwrapOrNull();
        T? last = range.UpperBound.Value.UnwrapOrNull();
        if (first is null || last is null || !FunctionEvaluation.IsUsable(first.Value) ||
            !FunctionEvaluation.IsUsable(last.Value) || !ExactBoundValue.TryRead(first.Value, out lower) ||
            !ExactBoundValue.TryRead(last.Value, out upper) || lower.CompareTo(upper) > 0 ||
            !ExactBoundValue.TryRead(
                value: converter.IntoValue(first.Value.ToFlt64()),
                converter: converter,
                exact: out ExactBoundValue convertedLower
            ) || !ExactBoundValue.TryRead(
                value: converter.IntoValue(last.Value.ToFlt64()),
                converter: converter,
                exact: out ExactBoundValue convertedUpper
            )) {
            return false;
        }
        if (convertedLower.CompareTo(lower) < 0) {
            lower = convertedLower;
        }
        if (convertedUpper.CompareTo(upper) > 0) {
            upper = convertedUpper;
        }
        return RoundBound(
                value: lower,
                converter: converter,
                upper: false,
                result: out lower
            ) && RoundBound(
                value: upper,
                converter: converter,
                upper: true,
                result: out upper
            );
    }

    private static bool RoundBound<V>(
        ExactBoundValue value,
        IIntoValue<V> converter,
        bool upper,
        out ExactBoundValue result)
        where V : struct, IRealNumber<V>, INumberField<V> {
        result = default;
        return value.TryRound(
                converter: converter,
                upper: upper,
                result: out V rounded
            ) && ExactBoundValue.TryRead(
                value: rounded,
                converter: converter,
                exact: out result
            );
    }
}

/// <summary>
/// Big-M 约束编码函数 / Big-M constraint encoding function
/// </summary>
public sealed class BigMFunction : IMathFunctionSymbol<Flt64> {
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();

    public BigMFunction(string name = "bigM", string? displayName = null) {
        Name = name;
        DisplayName = displayName;
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        Results.Ok<Success>(Results.SuccessInstance);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
