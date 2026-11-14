#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using MathInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Model.Intermediate;

internal static class MechanismModelDumpVariables {
    internal static Result<MechanismModelSourceSnapshot<V>.VariableSnapshot[], ErrorCode, Error<ErrorCode>> Capture<V>(
        IReadOnlyList<Token<V>> tokens)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            return Results.Ok(MechanismModelSourceSnapshot<V>.CaptureVariables(tokens));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<MechanismModelSourceSnapshot<V>.VariableSnapshot[]>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查变量：{error.Message} / Cannot inspect variables: {error.Message}"));
        }
    }

    internal static Result<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> Build<V>(
        IReadOnlyList<Token<V>> tokens,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        IFlt64ValueConverter<V> converter,
        List<(V Native, double Encoded)> fixedConversions,
        CancellationToken cancellationToken)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var fixedValues = new Dictionary<VariableItemKey, double>();
        if (fixedVariables is not null) {
            foreach ((IVariableItem variable, V value) in fixedVariables) {
                if (cancellationToken.IsCancellationRequested) {
                    return Failure(ErrorCode.ApplicationStopped, "模型转换已取消 / Model conversion was cancelled");
                }
                if (variable is null || !TryConvertFixed(value, converter, out double converted) || !fixedValues.TryAdd(variable.Key, converted)) {
                    return Failure(ErrorCode.IllegalArgument, "固定变量或固定值无效 / Invalid fixed variable or fixed value");
                }
                fixedConversions.Add((value, converted));
            }
        }
        var indexes = new Dictionary<VariableItemKey, int>(tokens.Count);
        var variables = new List<ModelViewVariable>(tokens.Count);
        var seen = new HashSet<VariableItemKey>();
        int matchedFixed = 0;
        for (int i = 0; i < tokens.Count; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Failure(ErrorCode.ApplicationStopped, "模型转换已取消 / Model conversion was cancelled");
            }
            Token<V> token = tokens[i];
            if (!seen.Add(token.Key)) {
                return Failure(ErrorCode.IllegalArgument, $"变量键重复：{token.Name} / Duplicate variable key: {token.Name}");
            }
            Bound<Flt64>? lowerBound = token.LowerBound;
            Bound<Flt64>? upperBound = token.UpperBound;
            if (lowerBound is null || upperBound is null) {
                return Failure(ErrorCode.IllegalArgument, $"变量值域缺失或为空：{token.Name} / Variable range is missing or empty: {token.Name}");
            }
            var lower = lowerBound.Value.ToFlt64();
            var upper = upperBound.Value.ToFlt64();
            double lb = lower.ToDouble();
            double ub = upper.ToDouble();
            if (double.IsNaN(lb) || double.IsNaN(ub) || double.IsPositiveInfinity(lb)
                || double.IsNegativeInfinity(ub) || lb > ub) {
                return Failure(ErrorCode.IllegalArgument, $"变量边界无效：{token.Name} / Invalid variable bounds: {token.Name}");
            }
            if (fixedValues.TryGetValue(token.Key, out double fixedValue)) {
                matchedFixed++;
                if (!ContainsFixedValue(
                    token: token,
                    value: fixedValue,
                    lowerBound: lowerBound,
                    upperBound: upperBound
                )) {
                    return Failure(ErrorCode.IllegalArgument, $"固定值不在变量域中：{token.Name} / Fixed value is outside the variable domain: {token.Name}");
                }
                continue;
            }
            if ((double.IsFinite(lb) && lowerBound.Interval is Interval.Open)
                || (double.IsFinite(ub) && upperBound.Interval is Interval.Open)) {
                return Failure(ErrorCode.IllegalArgument, $"不支持有限开区间边界：{token.Name} / Finite open bounds are unsupported: {token.Name}");
            }
            Flt64? initial = token.ResultFlt64;
            if (initial is { } start && !double.IsFinite(start.ToDouble())) {
                return Failure(ErrorCode.IllegalArgument, $"变量初始值无效：{token.Name} / Invalid variable initial value: {token.Name}");
            }
            int index = variables.Count;
            indexes.Add(token.Key, index);
            variables.Add(new ModelViewVariable(
                index: index,
                lowerBound: lower,
                upperBound: upper,
                type: token.Type,
                name: token.Name,
                initialResult: initial,
                origin: token.Variable
            ));
        }
        if (matchedFixed != fixedValues.Count) {
            return Failure(ErrorCode.IllegalArgument, "固定变量未注册 / Fixed variable is not registered");
        }
        return Results.Ok((indexes, variables, fixedValues));
    }

    private static bool ContainsFixedValue<V>(
        Token<V> token,
        double value,
        Bound<Flt64> lowerBound,
        Bound<Flt64> upperBound)
        where V : struct, IRealNumber<V> {
        IVariableTypeKind type = token.Type;
        if (((type.IsIntegerType || type.IsBinaryType) && value != System.Math.Truncate(value))
            || (type.IsUnsignedType && value < 0)
            || (type.IsBinaryType && value != 0 && value != 1)
            || (type is Ternary && (value < 0 || value > 2))
            || (type is BalancedTernary && (value < -1 || value > 1))) {
            return false;
        }

        // 避免 Int64 边界转为 double 时舍入 / Avoid rounding Int64 bounds to double.
        if (token.Variable is AbstractVariableItem<MathInt64, Integer> signed) {
            return value >= -9223372036854775808d && value < 9223372036854775808d
                && signed.Range.Range?.Contains(new MathInt64((long)value)) == true;
        }
        if (token.Variable is AbstractVariableItem<MathUInt64, UInteger> unsigned) {
            return value >= 0 && value < 18446744073709551616d
                && unsigned.Range.Range?.Contains(new MathUInt64((ulong)value)) == true;
        }
        // shortcut: 自定义整数边界仅有 Flt64 视图，提供精确边界后放宽 / Custom integer bounds expose only Flt64; relax when exact bounds are available.
        if (type.IsIntegerType && System.Math.Abs(value) >= 9007199254740992d) {
            return false;
        }
        double lower = lowerBound.Value.ToFlt64().ToDouble();
        double upper = upperBound.Value.ToFlt64().ToDouble();
        return value >= lower && value <= upper
            && (value != lower || lowerBound.Interval is not Interval.Open)
            && (value != upper || upperBound.Interval is not Interval.Open);
    }

    internal static bool TryConvert<V>(V value, out double result)
        where V : struct, IRealNumber<V>, INumberField<V> {
        result = default;
        try {
            if (!NumericValueValidation.IsValid(value)) {
                return false;
            }
            result = value.ToFlt64().ToDouble();
            return double.IsFinite(result) && (result != 0 || value.Eq(NumericConstantsRegistry.For<V>().Zero));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return false;
        }
    }

    internal static bool TryConvertFixed<V>(V value, IFlt64ValueConverter<V> converter, out double result)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (!TryConvert(value, out result)) {
            return false;
        }
        try {
            if (BitConverter.DoubleToInt64Bits(converter.FromValue(value).ToDouble()) != BitConverter.DoubleToInt64Bits(result)) {
                return false;
            }
            V decoded = converter.IntoValue(new Flt64(result));
            if (!NumericValueValidation.IsValid(decoded)) {
                return false;
            }
            return ExactBoundValue.TryRead(value, out ExactBoundValue original) && ExactBoundValue.TryRead(decoded, out ExactBoundValue restored)
                ? original.CompareTo(restored) == 0
                : value.Eq(decoded);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return false;
        }
    }

    internal static bool FixedConversionsCurrent<V>(
        IFlt64ValueConverter<V> converter,
        IReadOnlyList<(V Native, double Encoded)> conversions)
        where V : struct, IRealNumber<V>, INumberField<V> {
        foreach ((V native, double encoded) in conversions) {
            if (!TryConvertFixed(native, converter, out double current) ||
                BitConverter.DoubleToInt64Bits(encoded) != BitConverter.DoubleToInt64Bits(current)) {
                return false;
            }
        }
        return true;
    }

    internal static bool TryCaptureConverter<V>(IFlt64ValueConverter<V> converter, out (V Zero, V One) snapshot)
        where V : struct, IRealNumber<V>, INumberField<V> {
        snapshot = default;
        try {
            snapshot = (converter.Zero, converter.One);
            return ConverterCurrent(converter, snapshot);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return false;
        }
    }

    internal static bool ConverterCurrent<V>(IFlt64ValueConverter<V> converter, (V Zero, V One) snapshot)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            return StructuralValueEquality<V>.Same(converter.Zero, snapshot.Zero) &&
                StructuralValueEquality<V>.Same(converter.One, snapshot.One) &&
                TryConvertFixed(snapshot.Zero, converter, out double zero) && zero == 0 &&
                TryConvertFixed(snapshot.One, converter, out double one) && one == 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return false;
        }
    }

    private static Result<(Dictionary<VariableItemKey, int> Indexes, List<ModelViewVariable> Variables, Dictionary<VariableItemKey, double> FixedValues), ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<(Dictionary<VariableItemKey, int>, List<ModelViewVariable>, Dictionary<VariableItemKey, double>)>(new Err<ErrorCode>(code, message));
}
