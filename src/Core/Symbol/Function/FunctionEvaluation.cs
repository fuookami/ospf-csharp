#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Symbol.Function;

internal static class FunctionEvaluation {
    public static Result<V?, ErrorCode, Error<ErrorCode>> Polynomial<V>(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V> values,
        bool rejectUnderflow = false)
        where V : struct, IRealNumber<V>, INumberField<V> => values is null ? Invalid<V>() : PolynomialCore(
            polynomial: polynomial,
            values: values,
            resolve: null,
            rejectUnderflow: rejectUnderflow,
            exactConverter: null,
            exact: out _,
            encoded: out _
        );

    public static Result<V?, ErrorCode, Error<ErrorCode>> Polynomial<V>(
        LinearPolynomial<V> polynomial,
        Func<ISymbol, V?> resolve,
        bool rejectUnderflow = false)
        where V : struct, IRealNumber<V>, INumberField<V> => resolve is null ? Invalid<V>() : PolynomialCore(
            polynomial: polynomial,
            values: null,
            resolve: resolve,
            rejectUnderflow: rejectUnderflow,
            exactConverter: null,
            exact: out _,
            encoded: out _
        );

    public static Result<V?, ErrorCode, Error<ErrorCode>> ConditionalPolynomial<V>(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V> values,
        IIntoValue<V> converter,
        out ExactBoundValue exact,
        out ExactBoundValue encoded)
        where V : struct, IRealNumber<V>, INumberField<V> {
        exact = default;
        encoded = default;
        if (values is null || converter is null) {
            return Invalid<V>();
        }
        try {
            return PolynomialCore(
                polynomial: polynomial,
                values: values,
                resolve: null,
                rejectUnderflow: true,
                exactConverter: converter,
                exact: out exact,
                encoded: out encoded
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V>();
        }
    }

    public static Result<bool, ErrorCode, Error<ErrorCode>> ExactPolynomial<V>(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V> values,
        IIntoValue<V> converter,
        out ExactBoundValue exact,
        out ExactBoundValue encoded)
        where V : struct, IRealNumber<V>, INumberField<V> {
        exact = default;
        encoded = default;
        try {
            if (!TryReadConditionValue(
                    value: polynomial.Constant,
                    converter: converter,
                    exact: out exact,
                    encoded: out encoded
                ) || exact.CompareTo(encoded) != 0) {
                return Invalid<V>().Map(_ => false);
            }
            foreach (LinearMonomial<V> term in polynomial.Monomials) {
                if (term.Symbol is not IVariableItem || !TryReadConditionValue(
                        value: term.Coefficient,
                        converter: converter,
                        exact: out ExactBoundValue coefficient,
                        encoded: out ExactBoundValue encodedCoefficient
                    ) || coefficient.CompareTo(encodedCoefficient) != 0) {
                    return Invalid<V>().Map(_ => false);
                }
                if (coefficient.Sign == 0) {
                    continue;
                }
                if (!values.TryGetValue(term.Symbol, out V value)) {
                    return Results.Ok(false);
                }
                if (!TryReadConditionValue(
                        value: value,
                        converter: converter,
                        exact: out ExactBoundValue input,
                        encoded: out ExactBoundValue encodedInput
                    )) {
                    return Invalid<V>().Map(_ => false);
                }
                exact = exact.Add(coefficient.Multiply(input));
                encoded = encoded.Add(encodedCoefficient.Multiply(encodedInput));
            }
            return Results.Ok(true);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V>().Map(_ => false);
        }
    }

    private static Result<V?, ErrorCode, Error<ErrorCode>> PolynomialCore<V>(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V>? values,
        Func<ISymbol, V?>? resolve,
        bool rejectUnderflow,
        IIntoValue<V>? exactConverter,
        out ExactBoundValue exact,
        out ExactBoundValue encoded)
        where V : struct, IRealNumber<V>, INumberField<V> {
        exact = default;
        encoded = default;
        try {
            V result = polynomial.Constant;
            if (!IsUsable(result)) {
                return Invalid<V>();
            }
            if (exactConverter is not null && !TryReadConditionValue(
                    value: result,
                    converter: exactConverter,
                    exact: out exact,
                    encoded: out encoded
                )) {
                return Invalid<V>();
            }
            if (exactConverter is not null && exact.CompareTo(encoded) != 0) {
                return Invalid<V>();
            }
            foreach (LinearMonomial<V> monomial in polynomial.Monomials) {
                V value;
                if (values is not null) {
                    if (!values.TryGetValue(monomial.Symbol, out value)) {
                        return Results.Ok<V?>(null);
                    }
                }
                else if (resolve?.Invoke(monomial.Symbol) is { } resolved) {
                    value = resolved;
                }
                else {
                    return Results.Ok<V?>(null);
                }
                if (!IsUsable(value) || !IsUsable(monomial.Coefficient)) {
                    return Invalid<V>();
                }
                if (exactConverter is not null) {
                    if (!TryReadConditionValue(
                            value: monomial.Coefficient,
                            converter: exactConverter,
                            exact: out ExactBoundValue coefficient,
                            encoded: out ExactBoundValue encodedCoefficient
                        ) || coefficient.CompareTo(encodedCoefficient) != 0 || !TryReadConditionValue(
                            value: value,
                            converter: exactConverter,
                            exact: out ExactBoundValue naturalValue,
                            encoded: out ExactBoundValue encodedValue
                        )) {
                        return Invalid<V>();
                    }
                    exact = exact.Add(coefficient.Multiply(naturalValue));
                    encoded = encoded.Add(encodedCoefficient.Multiply(encodedValue));
                }
                if (IntegerArithmetic<V>.Enabled) {
                    if (!LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
                            lhs: monomial.Coefficient,
                            rhs: value,
                            result: out V term
                        ) || !LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                            lhs: result,
                            rhs: term,
                            result: out result
                        )) {
                        return Invalid<V>();
                    }
                }
                else {
                    if (!SafeRationalProduct(monomial.Coefficient, value)) {
                        return Invalid<V>();
                    }
                    V term = monomial.Coefficient.Times(value);
                    if (!IsUsable(term) || !SafeRationalSum(result, term)) {
                        return Invalid<V>();
                    }
                    if (rejectUnderflow) {
                        V zero = monomial.Coefficient.Constants.Zero;
                        if (!monomial.Coefficient.Eq(zero) && !value.Eq(zero) && term.Eq(zero)) {
                            return Invalid<V>();
                        }
                    }
                    result = result.Plus(term);
                    if (!IsUsable(result)) {
                        return Invalid<V>();
                    }
                }
            }
            return Results.Ok<V?>(result);
        }
        catch (ArithmeticException) {
            return Invalid<V>();
        }
        catch (InvalidOperationException) {
            return Invalid<V>();
        }
    }

    public static bool TryReadConditionValue<V>(
        V value,
        IIntoValue<V> converter,
        out ExactBoundValue exact,
        out ExactBoundValue encoded)
        where V : struct, IRealNumber<V> {
        exact = default;
        encoded = default;
        try {
            if (!ExactBoundValue.TryRead(value, out exact) || !IsUsable(value)) {
                return false;
            }
            Flt64 converted = converter.FromValue(value);
            return BigMValues.IsUsable(converted) && value.ToFlt64().ToDouble() == converted.ToDouble() &&
                (exact.Sign == 0 || converted.ToDouble() != 0.0) && ExactBoundValue.TryRead(converted, out encoded);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> Transform<V>(
        Result<V?, ErrorCode, Error<ErrorCode>> input,
        Func<V, V> transform)
        where V : struct, IRealNumber<V> {
        if (input.IsFailed || input.Value is not { } value) {
            return input;
        }
        return Evaluate<V>(() => transform(value));
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> Difference<V>(V lhs, V rhs)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (!IsUsable(lhs) || !IsUsable(rhs)) {
                return Invalid<V>();
            }
            V result;
            if (IntegerArithmetic<V>.Enabled) {
                if (!LinearFlattenExtensions.Arithmetic<V>.TrySubtract(
                    lhs: lhs,
                    rhs: rhs,
                    result: out result
                )) {
                    return Invalid<V>();
                }
            }
            else {
                V negative = rhs.Negate();
                if (!IsUsable(negative) || !SafeRationalSum(lhs, negative)) {
                    return Invalid<V>();
                }
                result = lhs.Minus(rhs);
            }
            return IsUsable(result) ? Results.Ok<V?>(result) : Invalid<V>();
        }
        catch (ArithmeticException) {
            return Invalid<V>();
        }
        catch (InvalidOperationException) {
            return Invalid<V>();
        }
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> Product<V>(V lhs, V rhs)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (!IsUsable(lhs) || !IsUsable(rhs) || !SafeRationalProduct(lhs, rhs) ||
                !LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
                    lhs: lhs,
                    rhs: rhs,
                    result: out V result
                ) || !IsUsable(result)) {
                return Invalid<V>();
            }
            return Results.Ok<V?>(result);
        }
        catch (ArithmeticException) {
            return Invalid<V>();
        }
        catch (InvalidOperationException) {
            return Invalid<V>();
        }
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> TransformRounding<V>(
        Result<V?, ErrorCode, Error<ErrorCode>> input,
        IIntoValue<V> converter,
        bool rounding)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (input.IsFailed || input.Value is not { } value) {
            return input;
        }
        try {
            if (!SafeRationalRounding(
                value: value,
                converter: converter,
                rounding: rounding
            )) {
                return Invalid<V>();
            }
            return Evaluate<V>(() => rounding
                ? RoundingSupport.Round(value, converter)
                : RoundingSupport.Ceiling(value, converter));
        }
        catch (ArithmeticException) {
            return Invalid<V>();
        }
        catch (InvalidOperationException) {
            return Invalid<V>();
        }
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> Evaluate<V>(Func<V?> evaluate)
        where V : struct, IRealNumber<V> {
        try {
            V? value = evaluate();
            return value is { } numeric && !IsUsable(numeric)
                ? Invalid<V>()
                : Results.Ok(value);
        }
        catch (ArithmeticException) {
            return Invalid<V>();
        }
        catch (InvalidOperationException) {
            return Invalid<V>();
        }
    }

    public static Result<V?, ErrorCode, Error<ErrorCode>> Invalid<V>() where V : struct =>
        Results.Failed<V?>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "函数求值无效或溢出 / Invalid or overflowing function evaluation"
        ));

    public static bool Comparable<V>(V lhs, V rhs) where V : struct {
        if (typeof(V) != typeof(RtnX)) {
            return true;
        }
        ReadOnlySpan<double> left = RationalParts(ref lhs);
        ReadOnlySpan<double> right = RationalParts(ref rhs);
        double first = left[0] * right[1];
        double second = right[0] * left[1];
        return SafeProduct(left[0], right[1]) && SafeProduct(right[0], left[1]) &&
            (first != second || ExactProductEqual(
                leftNumerator: left[0],
                rightDenominator: right[1],
                rightNumerator: right[0],
                leftDenominator: left[1]
            ));
    }

    private static bool ExactProductEqual(
        double leftNumerator,
        double rightDenominator,
        double rightNumerator,
        double leftDenominator) {
        (BigInteger leftMantissa, int leftExponent) = BinaryParts(leftNumerator);
        (BigInteger rightDenominatorMantissa, int rightDenominatorExponent) = BinaryParts(rightDenominator);
        (BigInteger rightMantissa, int rightExponent) = BinaryParts(rightNumerator);
        (BigInteger leftDenominatorMantissa, int leftDenominatorExponent) = BinaryParts(leftDenominator);
        BigInteger first = leftMantissa * rightDenominatorMantissa;
        BigInteger second = rightMantissa * leftDenominatorMantissa;
        int difference = leftExponent + rightDenominatorExponent - rightExponent - leftDenominatorExponent;
        return difference >= 0
            ? first << difference == second
            : first == second << -difference;
    }

    private static (BigInteger Mantissa, int Exponent) BinaryParts(double value) {
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        int exponent = (int)((bits >> 52) & 0x7ff);
        ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1UL << 52);
        if ((bits >> 63) != 0) {
            mantissa = -mantissa;
        }
        return (mantissa, exponent == 0 ? -1074 : exponent - 1075);
    }

    public static bool IsUsable<V>(V value) where V : struct, IRealNumber<V> {
        if (!NumericValueValidation.IsValid(value)) {
            return false;
        }
        if (typeof(V) != typeof(RtnX)) {
            return true;
        }
        ReadOnlySpan<double> parts = RationalParts(ref value);
        return parts[1] > 0 && (parts[0] == 0 || parts[0] / parts[1] != 0);
    }

    private static bool SafeRationalProduct<V>(V lhs, V rhs) where V : struct {
        if (typeof(V) != typeof(RtnX)) {
            return true;
        }
        ReadOnlySpan<double> left = RationalParts(ref lhs);
        ReadOnlySpan<double> right = RationalParts(ref rhs);
        return SafeProduct(left[0], right[0]) && SafeProduct(left[1], right[1]);
    }

    private static bool SafeRationalSum<V>(V lhs, V rhs) where V : struct {
        if (typeof(V) != typeof(RtnX)) {
            return true;
        }
        ReadOnlySpan<double> left = RationalParts(ref lhs);
        ReadOnlySpan<double> right = RationalParts(ref rhs);
        double first = left[0] * right[1];
        double second = right[0] * left[1];
        double sum = first + second;
        return SafeProduct(left[0], right[1]) && SafeProduct(right[0], left[1]) &&
            SafeProduct(left[1], right[1]) &&
            double.IsFinite(sum) && (sum != 0 || ExactProductEqual(
                leftNumerator: left[0],
                rightDenominator: right[1],
                rightNumerator: -right[0],
                leftDenominator: left[1]
            ));
    }

    private static bool SafeRationalRounding<V>(V value, IIntoValue<V> converter, bool rounding)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (typeof(V) != typeof(RtnX)) {
            return true;
        }
        V one = converter.One;
        V floor = RoundingSupport.Floor(value, converter);
        if (!IsUsable(one) || !IsUsable(floor) || !Comparable(value, floor)) {
            return false;
        }
        if (value.Eq(floor)) {
            return true;
        }
        if (!SafeRationalSum(floor, one) || !IsUsable(floor.Plus(one))) {
            return false;
        }
        if (!rounding) {
            return true;
        }
        if (!SafeRationalSum(value, floor.Negate())) {
            return false;
        }
        V fraction = value.Minus(floor);
        if (!IsUsable(fraction) || !SafeRationalSum(fraction, fraction)) {
            return false;
        }
        V twiceFraction = fraction.Plus(fraction);
        return IsUsable(twiceFraction) && Comparable(twiceFraction, one) &&
            SafeRationalSum(one, one) && IsUsable(one.Plus(one));
    }

    private static bool SafeProduct(double lhs, double rhs) {
        double product = lhs * rhs;
        return double.IsFinite(product) && (lhs == 0 || rhs == 0 || product != 0);
    }

    // shortcut: RtnX 使用两个 double 存储字段；布局变化时改用显式表示 API / RtnX has two double fields; use an explicit representation API if its layout changes.
    private static ReadOnlySpan<double> RationalParts<V>(ref V value) where V : struct =>
        MemoryMarshal.Cast<V, double>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

    private static class IntegerArithmetic<V> where V : struct, IRealNumber<V> {
        public static readonly bool Enabled = typeof(V).GetInterfaces().Any(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IInteger<>));
    }
}
