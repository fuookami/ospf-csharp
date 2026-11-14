#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using NInt8 = Fuookami.Ospf.Math.Algebra.Number.Int8;
using NInt16 = Fuookami.Ospf.Math.Algebra.Number.Int16;
using NInt32 = Fuookami.Ospf.Math.Algebra.Number.Int32;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using NUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
using NUInt16 = Fuookami.Ospf.Math.Algebra.Number.UInt16;
using NUInt32 = Fuookami.Ospf.Math.Algebra.Number.UInt32;
using NUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>有界整数与线性表达式的线性化乘积 / Linearized product of a bounded integer and a linear expression.</summary>
/// <typeparam name="V">系数与求值的数值类型 / Numeric type of coefficients and values.</typeparam>
public sealed class IntegerProductFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private const long MaximumExactInteger = 9_007_199_254_740_992L;
    private const long MaximumExactSingleInteger = 16_777_216L;
    private readonly IIntoValue<V> _converter;
    private readonly LinearPolynomial<V> _expandedInput;
    private readonly BoundDependencySnapshot<V> _dependencies;
    private readonly long _lower;
    private readonly long _upper;
    private readonly V _zero;
    private readonly V _one;
    private readonly V _lowerValue;
    private readonly V _width;
    private readonly V[] _weights;

    /// <summary>整数因子 / Integer factor.</summary>
    public IVariableItem Integer { get; }
    /// <summary>输入表达式的浅快照 / Shallow snapshot of the input expression.</summary>
    public LinearPolynomial<V> Input { get; }
    internal LinearPolynomial<V> ExpandedInput => _expandedInput;
    /// <summary>输入表达式允许范围 / Allowed bounds for the input expression.</summary>
    public LinearPolynomialBounds<V> InputBounds { get; }
    /// <summary>乘积结果变量 / Product result variable.</summary>
    public RealVar ResultVar { get; }
    /// <summary>输入表达式的辅助变量 / Auxiliary variable for the input expression.</summary>
    public RealVar InputVar { get; }
    /// <summary>整数偏移的二进制位 / Binary digits of the integer offset.</summary>
    public IReadOnlyList<BinVar> BitVars { get; }
    /// <summary>各二进制位对应的掩码变量 / Masked input variables for the binary digits.</summary>
    public IReadOnlyList<RealVar> MaskedVars { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input).Append(Integer).ToHashSet();
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_one, ResultVar) },
        _zero
    );

    private IntegerProductFunction(
        IVariableItem integer,
        LinearPolynomial<V> input,
        LinearPolynomial<V> expandedInput,
        LinearPolynomialBounds<V> inputBounds,
        BoundDependencySnapshot<V> dependencies,
        long lower,
        long upper,
        V zero,
        V one,
        V lowerValue,
        V width,
        V[] weights,
        IIntoValue<V> converter,
        ValueRange<Flt64> inputRange,
        ValueRange<Flt64> maskedRange,
        ValueRange<Flt64> resultRange,
        string name,
        string? displayName) {
        Integer = integer;
        Input = input;
        _expandedInput = expandedInput;
        InputBounds = inputBounds;
        _dependencies = dependencies;
        _lower = lower;
        _upper = upper;
        _zero = zero;
        _one = one;
        _lowerValue = lowerValue;
        _width = width;
        _weights = weights;
        _converter = converter;
        Name = name;
        DisplayName = displayName;
        ResultVar = new RealVar($"{name}_integer_product");
        InputVar = new RealVar($"{name}_integer_product_input");
        ResultVar.Range.SetRange(resultRange);
        InputVar.Range.SetRange(inputRange);
        var bits = new BinVar[weights.Length];
        var masked = new RealVar[weights.Length];
        for (int index = 0; index < weights.Length; ++index) {
            bits[index] = new BinVar($"{name}_integer_product_bit_{index}");
            masked[index] = new RealVar($"{name}_integer_product_mask_{index}");
            masked[index].Range.SetRange(maskedRange);
        }
        BitVars = Array.AsReadOnly(bits);
        MaskedVars = Array.AsReadOnly(masked);
        HelperVariables = Array.AsReadOnly(new IVariableItem[] { ResultVar, InputVar }
            .Concat(bits).Concat(masked).ToArray());
    }

    /// <summary>校验范围并创建整数乘积 / Validate bounds and create an integer product.</summary>
    /// <param name="integer">具有有限范围的整数变量 / Integer variable with finite bounds.</param>
    /// <param name="input">被乘的线性表达式 / Linear expression to multiply.</param>
    /// <param name="converter">求解器边界数值转换器 / Solver-boundary value converter.</param>
    /// <param name="inputBounds">可选输入范围；为 null 时推导 / Optional input bounds; inferred when null.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或校验失败 / Function or validation failure.</returns>
    public static Result<IntegerProductFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        IVariableItem? integer,
        LinearPolynomial<V>? input,
        IIntoValue<V>? converter,
        LinearPolynomialBounds<V>? inputBounds = null,
        string name = "integer_product",
        string? displayName = null) {
        try {
            if (integer is null || !integer.TypeKind.IsIntegerType || input?.Monomials is null || converter is null ||
                input.Monomials.Any(term => term is null || term.Symbol is not (IVariableItem or ILinearIntermediateSymbol<V>))) {
                return Invalid();
            }
            var integerInput = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(default, integer) }, default);
            var initialInteger = new BoundDependencySnapshot<V>(new[] { integerInput });
            if (!initialInteger.IsCurrent() || !TryIntegerDomain(
                    variable: integer,
                    lower: out long lower,
                    upper: out long upper
                )) {
                return Invalid();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(input.Monomials.ToArray()), input.Constant);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
                new SymbolFlatten.LinearFlattenData<V>(snapshot.Monomials, snapshot.Constant).ExpandIntermediateSymbols();
            if (!expanded.IsOk) {
                return Invalid();
            }
            var structural = new LinearPolynomial<V>(Array.AsReadOnly(expanded.Value.Monomials.ToArray()), expanded.Value.Constant);
            var dependencies = new BoundDependencySnapshot<V>(inputBounds is null
                ? new[] { integerInput, structural }
                : new[] { integerInput });
            if (!initialInteger.IsCurrent() || !dependencies.IsCurrent() || !TryIntegerValue(
                    value: 0,
                    converter: converter,
                    result: out V zero
                ) || !TryIntegerValue(
                    value: 1,
                    converter: converter,
                    result: out V one
                ) ||
                !StructuralValueEquality<V>.Same(zero, converter.Zero) ||
                !StructuralValueEquality<V>.Same(one, converter.One) ||
                !TryIntegerValue(
                    value: lower,
                    converter: converter,
                    result: out V lowerValue
                ) || !TryIntegerValue(
                    value: upper,
                    converter: converter,
                    result: out _
                ) || !TryIntegerValue(
                    value: upper - lower,
                    converter: converter,
                    result: out V width
                ) ||
                (typeof(V) == typeof(Flt32) && lower != upper &&
                    (lower < -MaximumExactSingleInteger || upper > MaximumExactSingleInteger)) ||
                !LinearFlattenExtensions.Arithmetic<V>.TryNegate(one, out _) ||
                !LinearFlattenExtensions.Arithmetic<V>.TryNegate(lowerValue, out _) ||
                !Negatable(snapshot, converter) || !Negatable(structural, converter)) {
                return Invalid();
            }
            LinearPolynomialBounds<V>? bounds = inputBounds ?? BigMValues.FiniteBounds(structural, converter);
            if (bounds is null || !TryRanges(
                    bounds: bounds,
                    lower: lower,
                    upper: upper,
                    converter: converter,
                    inputRange: out ValueRange<Flt64>? inputRange,
                    maskedRange: out ValueRange<Flt64>? maskedRange,
                    resultRange: out ValueRange<Flt64>? resultRange
                )) {
                return Invalid();
            }
            long difference = upper - lower;
            var weights = new List<V>();
            for (long bit = 1; bit > 0 && bit <= difference; bit <<= 1) {
                if (!TryIntegerValue(
                        value: bit,
                        converter: converter,
                        result: out V weight
                    ) ||
                    !LinearFlattenExtensions.Arithmetic<V>.TryNegate(weight, out _)) {
                    return Invalid();
                }
                weights.Add(weight);
            }
            if (!initialInteger.IsCurrent() || !dependencies.IsCurrent()) {
                return Invalid();
            }
            return Results.Ok(new IntegerProductFunction<V>(
                integer: integer,
                input: snapshot,
                expandedInput: structural,
                inputBounds: bounds,
                dependencies: dependencies,
                lower: lower,
                upper: upper,
                zero: zero,
                one: one,
                lowerValue: lowerValue,
                width: width,
                weights: weights.ToArray(),
                converter: converter,
                inputRange: inputRange,
                maskedRange: maskedRange,
                resultRange: resultRange,
                name: name,
                displayName: displayName
            ));
        }
        catch (ArithmeticException) {
            return Invalid();
        }
        catch (InvalidOperationException) {
            return Invalid();
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        try {
            if (values is null) {
                return FunctionEvaluation.Invalid<V>();
            }
            if (!values.TryGetValue(Integer, out V integer)) {
                return Results.Ok<V?>(null);
            }
            if (!FunctionEvaluation.IsUsable(integer) || !ExactBoundValue.TryRead(integer, out ExactBoundValue integerExact) ||
                integerExact.Numerator % integerExact.Denominator != 0 ||
                integerExact.CompareTo(new ExactBoundValue(_lower, BigInteger.One)) < 0 ||
                integerExact.CompareTo(new ExactBoundValue(_upper, BigInteger.One)) > 0) {
                return FunctionEvaluation.Invalid<V>();
            }
            Result<V?, ErrorCode, Error<ErrorCode>> evaluated = FunctionEvaluation.Polynomial(
                polynomial: Input,
                values: values,
                rejectUnderflow: true
            );
            if (evaluated.IsFailed || evaluated.Value is not { } value) {
                return evaluated;
            }
            if (!ExactBoundValue.TryRead(value, out ExactBoundValue actual) ||
                !ExactBoundValue.TryRead(InputBounds.LowerBound, out ExactBoundValue lower) ||
                !ExactBoundValue.TryRead(InputBounds.UpperBound, out ExactBoundValue upper) ||
                actual.CompareTo(lower) < 0 || actual.CompareTo(upper) > 0) {
                return FunctionEvaluation.Invalid<V>();
            }
            return FunctionEvaluation.Product(integer, value);
        }
        catch (ArithmeticException) {
            return FunctionEvaluation.Invalid<V>();
        }
        catch (InvalidOperationException) {
            return FunctionEvaluation.Invalid<V>();
        }
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        if (tokens is null) {
            return InvalidRegistration();
        }
        Try current = ValidateInput(null);
        return current.IsOk ? tokens.Add(HelperVariables) : current;
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            return InvalidRegistration();
        }
        Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> prepared = PrepareConstraints(model);
        if (!prepared.IsOk) {
            return prepared.Map(_ => Results.SuccessInstance);
        }
        try {
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: prepared.Value,
                afterWrite: () => {
                    if (!_dependencies.IsCurrent()) {
                        return InvalidRegistration();
                    }
                    TrackDependencies(model);
                    return Results.OkInstance;
                }
            );
        }
        catch (ArithmeticException) {
            return InvalidRegistration();
        }
        catch (InvalidOperationException) {
            return InvalidRegistration();
        }
    }

    internal Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> PrepareConstraints(
        IAbstractLinearMechanismModel<V> model) {
        Try current = ValidateInput(model.Tokens);
        if (current.IsFailed) {
            return current.Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
        }
        try {
            IReadOnlyList<LinearInequality<V>> rows = Constraints();
            var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
            foreach (LinearInequality<V> row in rows) {
                Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                    row.ToLinearFlattenData(model.Tokens);
                if (!flattened.IsOk) {
                    return flattened.Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
                }
                if (!MatchesRow(row, flattened.Value) || flattened.Value.Monomials.Any(term =>
                        term.Symbol is not IVariableItem variable || !registered.Contains(variable.Key) ||
                        !ExactBoundValue.TryRead(
                            value: term.Coefficient,
                            converter: _converter,
                            exact: out _
                        )) ||
                    !ExactBoundValue.TryRead(
                        value: flattened.Value.Constant,
                        converter: _converter,
                        exact: out _
                    )) {
                    return InvalidRows();
                }
            }
            if (!_dependencies.IsCurrent()) {
                return InvalidRows();
            }
            return Results.Ok(rows);
        }
        catch (ArithmeticException) {
            return InvalidRows();
        }
        catch (InvalidOperationException) {
            return InvalidRows();
        }
    }

    internal void TrackDependencies(IAbstractLinearMechanismModel<V> model) {
        if (model is BasicMechanismModel<V> mechanism) {
            mechanism.TrackBoundDependencies(_dependencies);
        }
    }

    internal Try ValidateInput(IAbstractTokenTable<V>? tokens) {
        if (!_dependencies.IsCurrent()) {
            return InvalidRegistration();
        }
        Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
            new SymbolFlatten.LinearFlattenData<V>(Input.Monomials, Input.Constant).ExpandIntermediateSymbols(tokens);
        if (!expanded.IsOk || expanded.Value.Monomials.Count != _expandedInput.Monomials.Count ||
            !StructuralValueEquality<V>.Same(expanded.Value.Constant, _expandedInput.Constant)) {
            return InvalidRegistration();
        }
        for (int index = 0; index < _expandedInput.Monomials.Count; ++index) {
            LinearMonomial<V> original = _expandedInput.Monomials[index];
            LinearMonomial<V> current = expanded.Value.Monomials[index];
            if (original.Symbol is not IVariableItem first || current.Symbol is not IVariableItem second ||
                first.Key != second.Key || !StructuralValueEquality<V>.Same(original.Coefficient, current.Coefficient)) {
                return InvalidRegistration();
            }
        }
        return Results.OkInstance;
    }

    private IReadOnlyList<LinearInequality<V>> Constraints() {
        V zero = _zero;
        V one = _one;
        V negativeOne = one.Negate();
        V negativeLower = InputBounds.LowerBound.Negate();
        V negativeUpper = InputBounds.UpperBound.Negate();
        var rows = new List<LinearInequality<V>>();
        var inputTerms = new List<LinearMonomial<V>> { new(one, InputVar) };
        inputTerms.AddRange(_expandedInput.Monomials.Select(term =>
            new LinearMonomial<V>(term.Coefficient.Negate(), term.Symbol)));
        rows.Add(IndicatorSupport.Row(
            monomials: inputTerms,
            constant: _expandedInput.Constant.Negate(),
            comparison: Comparison.EQ,
            rhs: zero,
            name: $"{Name}_integer_product_input"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: new[] { new LinearMonomial<V>(one, InputVar) },
            constant: zero,
            comparison: Comparison.GE,
            rhs: InputBounds.LowerBound,
            name: $"{Name}_integer_product_input_lower"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: new[] { new LinearMonomial<V>(one, InputVar) },
            constant: zero,
            comparison: Comparison.LE,
            rhs: InputBounds.UpperBound,
            name: $"{Name}_integer_product_input_upper"
        ));
        var integerTerms = new List<LinearMonomial<V>> { new(one, Integer) };
        var widthTerms = new List<LinearMonomial<V>>();
        var resultTerms = new List<LinearMonomial<V>> { new(one, ResultVar), new(_lowerValue.Negate(), InputVar) };
        for (int index = 0; index < _weights.Length; ++index) {
            BinVar bit = BitVars[index];
            RealVar masked = MaskedVars[index];
            V weight = _weights[index];
            integerTerms.Add(new LinearMonomial<V>(weight.Negate(), bit));
            widthTerms.Add(new LinearMonomial<V>(weight, bit));
            resultTerms.Add(new LinearMonomial<V>(weight.Negate(), masked));
            string prefix = $"{Name}_integer_product_mask_{index}";
            rows.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(one, masked), new LinearMonomial<V>(negativeUpper, bit) },
                constant: zero,
                comparison: Comparison.LE,
                rhs: zero,
                name: $"{prefix}_zero_upper"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(one, masked), new LinearMonomial<V>(negativeLower, bit) },
                constant: zero,
                comparison: Comparison.GE,
                rhs: zero,
                name: $"{prefix}_zero_lower"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, masked),
                    new LinearMonomial<V>(negativeOne, InputVar),
                    new LinearMonomial<V>(negativeLower, bit),
                },
                constant: zero,
                comparison: Comparison.LE,
                rhs: negativeLower,
                name: $"{prefix}_one_upper"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, masked),
                    new LinearMonomial<V>(negativeOne, InputVar),
                    new LinearMonomial<V>(negativeUpper, bit),
                },
                constant: zero,
                comparison: Comparison.GE,
                rhs: negativeUpper,
                name: $"{prefix}_one_lower"
            ));
        }
        rows.Add(IndicatorSupport.Row(
            monomials: integerTerms,
            constant: _lowerValue.Negate(),
            comparison: Comparison.EQ,
            rhs: zero,
            name: $"{Name}_integer_product_offset"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: widthTerms,
            constant: zero,
            comparison: Comparison.LE,
            rhs: _width,
            name: $"{Name}_integer_product_width"
        ));
        if (_upper - _lower == MaximumExactInteger) {
            // 最高位加一可能舍入回 2^53 / The high bit plus one can round back to 2^53.
            V count = _weights[^1].Constants.Zero;
            for (int index = 0; index < _weights.Length - 1; ++index) {
                count = count.Plus(one);
            }
            var highBitTerms = BitVars.Take(BitVars.Count - 1)
                .Select(bit => new LinearMonomial<V>(one, bit)).ToList();
            highBitTerms.Add(new LinearMonomial<V>(count, BitVars[^1]));
            rows.Add(IndicatorSupport.Row(
                monomials: highBitTerms,
                constant: zero,
                comparison: Comparison.LE,
                rhs: count,
                name: $"{Name}_integer_product_high_bit"
            ));
        }
        rows.Add(IndicatorSupport.Row(
            monomials: resultTerms,
            constant: zero,
            comparison: Comparison.EQ,
            rhs: zero,
            name: $"{Name}_integer_product_result"
        ));
        return rows;
    }

    private bool MatchesRow(LinearInequality<V> row, SymbolFlatten.LinearFlattenData<V> flattened) {
        var coefficients = new Dictionary<VariableItemKey, ExactBoundValue>();
        foreach ((LinearPolynomial<V> polynomial, bool subtract) in new[] { (row.Lhs, false), (row.Rhs, true) }) {
            foreach (LinearMonomial<V> term in polynomial.Monomials) {
                if (term.Symbol is not IVariableItem variable ||
                    !ExactBoundValue.TryRead(
                        value: term.Coefficient,
                        converter: _converter,
                        exact: out ExactBoundValue value
                    )) {
                    return false;
                }
                coefficients[variable.Key] = (coefficients.TryGetValue(variable.Key, out ExactBoundValue current)
                    ? current : ExactBoundValue.Zero).Add(subtract ? value.Negate() : value);
            }
        }
        foreach (LinearMonomial<V> term in flattened.Monomials) {
            if (term.Symbol is not IVariableItem variable ||
                !ExactBoundValue.TryRead(
                    value: term.Coefficient,
                    converter: _converter,
                    exact: out ExactBoundValue value
                )) {
                return false;
            }
            coefficients[variable.Key] = (coefficients.TryGetValue(variable.Key, out ExactBoundValue current)
                ? current : ExactBoundValue.Zero).Add(value.Negate());
        }
        return coefficients.Values.All(value => value.Sign == 0) &&
            ExactBoundValue.TryRead(
                value: row.Lhs.Constant,
                converter: _converter,
                exact: out ExactBoundValue lhs
            ) && ExactBoundValue.TryRead(
                value: row.Rhs.Constant,
                converter: _converter,
                exact: out ExactBoundValue rhs
            ) && ExactBoundValue.TryRead(
                value: flattened.Constant,
                converter: _converter,
                exact: out ExactBoundValue actual
            ) &&
            lhs.Add(rhs.Negate()).CompareTo(actual) == 0;
    }

    private static bool Negatable(LinearPolynomial<V> polynomial, IIntoValue<V> converter) =>
        ExactBoundValue.TryRead(
            value: polynomial.Constant,
            converter: converter,
            exact: out _
        ) &&
        LinearFlattenExtensions.Arithmetic<V>.TryNegate(polynomial.Constant, out _) &&
        polynomial.Monomials.All(term => ExactBoundValue.TryRead(
            value: term.Coefficient,
            converter: converter,
            exact: out _
        ) &&
            LinearFlattenExtensions.Arithmetic<V>.TryNegate(term.Coefficient, out _));

    private static bool TryIntegerValue(long value, IIntoValue<V> converter, out V result) {
        result = converter.IntoValue(new Flt64(value));
        return ExactBoundValue.TryRead(
                value: result,
                converter: converter,
                exact: out ExactBoundValue exact
            ) &&
            exact.Numerator == (BigInteger)value * exact.Denominator &&
            converter.FromValue(result).ToDouble() == value;
    }

    private static bool TryIntegerDomain(IVariableItem variable, out long lower, out long upper) {
        object? range = (variable as IVariableRangeSnapshot)?.CurrentRange;
        (bool valid, long first, long last) = range switch {
            ValueRange<NInt64> signed => ReadDomain(signed),
            ValueRange<NUInt64> unsigned => ReadDomain(unsigned),
            ValueRange<NUInt8> binary => ReadDomain(binary),
            ValueRange<NInt8> ternary => ReadDomain(ternary),
            ValueRange<NInt16> signed16 => ReadDomain(signed16),
            ValueRange<NInt32> signed32 => ReadDomain(signed32),
            ValueRange<NUInt16> unsigned16 => ReadDomain(unsigned16),
            ValueRange<NUInt32> unsigned32 => ReadDomain(unsigned32),
            ValueRange<IntX> signedBig => ReadDomain(signedBig),
            ValueRange<UIntX> unsignedBig => ReadDomain(unsignedBig),
            ValueRange<Flt32> single => ReadDomain(single),
            ValueRange<Flt64> real => ReadDomain(real),
            ValueRange<FltX> decimalRange => ReadDomain(decimalRange),
            ValueRange<RtnX> rational => ReadDomain(rational),
            ValueRange<V> typed => ReadDomain(typed),
            _ => (false, 0, 0),
        };
        lower = first;
        upper = last;
        return valid;
    }

    private static (bool Valid, long Lower, long Upper) ReadDomain<T>(ValueRange<T> range)
        where T : struct, IRealNumber<T>, INumberField<T> {
        T? first = range.LowerBound.Value.UnwrapOrNull();
        T? last = range.UpperBound.Value.UnwrapOrNull();
        if (first is null || last is null || !ExactBoundValue.TryRead(first.Value, out ExactBoundValue left) ||
            !ExactBoundValue.TryRead(last.Value, out ExactBoundValue right)) {
            return (false, 0, 0);
        }
        var min = BigInteger.DivRem(
            dividend: left.Numerator,
            divisor: left.Denominator,
            remainder: out BigInteger leftRemainder
        );
        var max = BigInteger.DivRem(
            dividend: right.Numerator,
            divisor: right.Denominator,
            remainder: out BigInteger rightRemainder
        );
        if (leftRemainder.Sign > 0 || leftRemainder.IsZero && range.LowerBound.Interval is Interval.Open) {
            ++min;
        }
        if (rightRemainder.Sign < 0 || rightRemainder.IsZero && range.UpperBound.Interval is Interval.Open) {
            --max;
        }
        if (min < -MaximumExactInteger || max > MaximumExactInteger || min > max || max - min > MaximumExactInteger) {
            return (false, 0, 0);
        }
        return (true, (long)min, (long)max);
    }

    private static bool TryRanges(
        LinearPolynomialBounds<V> bounds,
        long lower,
        long upper,
        IIntoValue<V> converter,
        out ValueRange<Flt64> inputRange,
        out ValueRange<Flt64> maskedRange,
        out ValueRange<Flt64> resultRange) {
        inputRange = ValueRange<Flt64>.Full();
        maskedRange = ValueRange<Flt64>.Full();
        resultRange = ValueRange<Flt64>.Full();
        if (!ExactBoundValue.TryRead(
                value: bounds.LowerBound,
                converter: converter,
                exact: out ExactBoundValue nativeLower
            ) || !ExactBoundValue.TryRead(
                value: bounds.UpperBound,
                converter: converter,
                exact: out ExactBoundValue nativeUpper
            ) ||
            nativeLower.CompareTo(nativeUpper) > 0 ||
            !LinearFlattenExtensions.Arithmetic<V>.TryNegate(bounds.LowerBound, out _) ||
            !LinearFlattenExtensions.Arithmetic<V>.TryNegate(bounds.UpperBound, out _) ||
            !ExactBoundValue.TryRead(converter.FromValue(bounds.LowerBound), out ExactBoundValue encodedLower) ||
            !ExactBoundValue.TryRead(converter.FromValue(bounds.UpperBound), out ExactBoundValue encodedUpper)) {
            return false;
        }
        ExactBoundValue min = nativeLower.CompareTo(encodedLower) < 0 ? nativeLower : encodedLower;
        ExactBoundValue max = nativeUpper.CompareTo(encodedUpper) > 0 ? nativeUpper : encodedUpper;
        var first = new ExactBoundValue(lower, BigInteger.One);
        var last = new ExactBoundValue(upper, BigInteger.One);
        ExactBoundValue[] products = { first.Multiply(min), first.Multiply(max), last.Multiply(min), last.Multiply(max) };
        ExactBoundValue productLower = products.Aggregate((lhs, rhs) => lhs.CompareTo(rhs) <= 0 ? lhs : rhs);
        ExactBoundValue productUpper = products.Aggregate((lhs, rhs) => lhs.CompareTo(rhs) >= 0 ? lhs : rhs);
        ExactBoundValue maskLower = min.Sign < 0 ? min : ExactBoundValue.Zero;
        ExactBoundValue maskUpper = max.Sign > 0 ? max : ExactBoundValue.Zero;
        return productLower.TryRound(
                converter: converter,
                upper: false,
                result: out V _
            ) && productUpper.TryRound(
                converter: converter,
                upper: true,
                result: out V _
            ) && TryRange(
                lower: min,
                upper: max,
                range: out inputRange
            ) && TryRange(
                lower: maskLower,
                upper: maskUpper,
                range: out maskedRange
            ) && TryRange(
                lower: productLower,
                upper: productUpper,
                range: out resultRange
            );
    }

    private static bool TryRange(ExactBoundValue lower, ExactBoundValue upper, out ValueRange<Flt64> range) {
        range = ValueRange<Flt64>.Full();
        if (!lower.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: false,
                result: out Flt64 first
            ) || !upper.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: true,
                result: out Flt64 last
            )) {
            return false;
        }
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueRange<Flt64>.Of(first, last);
        if (!result.IsOk) {
            return false;
        }
        range = result.Value;
        return true;
    }

    private static Result<IntegerProductFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<IntegerProductFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "整数乘积的变量、表达式或范围无法安全编码。 / Integer-product variable, expression, or bounds cannot be encoded safely."
        ));

    private static Try InvalidRegistration() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "整数乘积输入、范围或注册状态无效。 / Invalid integer-product input, bounds, or registration state."
    ));

    private static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> InvalidRows() =>
        InvalidRegistration().Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
}
