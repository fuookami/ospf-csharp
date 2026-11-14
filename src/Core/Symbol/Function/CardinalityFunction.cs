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
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>精确指示二元输入中至多指定数量为一 / Exactly indicate whether at most the specified number of binary inputs are one.</summary>
/// <remarks>重复输入按出现次数计数；输入、辅助变量范围和转换器变化会使模型失效。 / Repeated inputs count per occurrence; changes to input or helper ranges or the converter invalidate the model.</remarks>
/// <typeparam name="V">系数数值类型 / Coefficient numeric type.</typeparam>
public sealed class AtMostFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, ICardinalityFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly CardinalityEncoding<V> _encoding;

    /// <summary>二元输入快照 / Binary input snapshot.</summary>
    public IReadOnlyList<IVariableItem> Indicators => _encoding.Indicators;
    /// <summary>最大计数 / Maximum count.</summary>
    public int Limit => _encoding.Amount;
    /// <summary>满足指示变量 / Satisfaction indicator.</summary>
    public BinVar ResultVar => _encoding.ResultVar;
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _encoding.HelperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => _encoding.InputSymbols;
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _encoding.ResultPolynomial;

    private AtMostFunction(CardinalityEncoding<V> encoding, string name, string? displayName) {
        _encoding = encoding;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>校验并创建基数指示函数 / Validate and create a cardinality indicator.</summary>
    /// <param name="indicators">二元变量列表 / Binary variables.</param>
    /// <param name="limit">最大计数 / Maximum count.</param>
    /// <param name="converter">模型数值转换器 / Model value converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或校验失败 / Function or validation failure.</returns>
    public static Result<AtMostFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        IReadOnlyList<IVariableItem>? indicators,
        int limit,
        IIntoValue<V>? converter,
        string name = "at_most",
        string? displayName = null) => CardinalityEncoding<V>.Create(
            indicators: indicators,
            amount: limit,
            converter: converter,
            exactly: false,
            name: name
        ).Map(encoding => new AtMostFunction<V>(
            encoding: encoding,
            name: name,
            displayName: displayName
        ));

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;
    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) => _encoding.Evaluate(values);
    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => _encoding.RegisterAuxiliaryTokens(tokens);
    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => _encoding.RegisterConstraints(model, Name);
    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => _encoding.Prepare(
            inputs: inputs,
            modelValues: modelValues,
            evidence: out _
        );
    bool ICardinalityFunction<V>.IsCompatibleConverter(IFlt64ValueConverter<V> converter) => _encoding.Current(null, converter);
    bool ICardinalityFunction<V>.TryGetEvidence(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V> converter,
        out IReadOnlyDictionary<ISymbol, V> evidence) => _encoding.TryGetEvidence(
            result: result,
            inputs: inputs,
            modelValues: modelValues,
            converter: converter,
            evidence: out evidence
        );
}

/// <summary>精确指示二元输入中恰有指定数量为一 / Exactly indicate whether exactly the specified number of binary inputs are one.</summary>
/// <remarks>重复输入按出现次数计数；输入、辅助变量范围和转换器变化会使模型失效。 / Repeated inputs count per occurrence; changes to input or helper ranges or the converter invalidate the model.</remarks>
/// <typeparam name="V">系数数值类型 / Coefficient numeric type.</typeparam>
public sealed class ExactlyFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, ICardinalityFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly CardinalityEncoding<V> _encoding;

    /// <summary>二元输入快照 / Binary input snapshot.</summary>
    public IReadOnlyList<IVariableItem> Indicators => _encoding.Indicators;
    /// <summary>精确计数 / Exact count.</summary>
    public int Amount => _encoding.Amount;
    /// <summary>满足指示变量 / Satisfaction indicator.</summary>
    public BinVar ResultVar => _encoding.ResultVar;
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _encoding.HelperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => _encoding.InputSymbols;
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _encoding.ResultPolynomial;

    private ExactlyFunction(CardinalityEncoding<V> encoding, string name, string? displayName) {
        _encoding = encoding;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>校验并创建基数指示函数 / Validate and create a cardinality indicator.</summary>
    /// <param name="indicators">二元变量列表 / Binary variables.</param>
    /// <param name="amount">精确计数 / Exact count.</param>
    /// <param name="converter">模型数值转换器 / Model value converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或校验失败 / Function or validation failure.</returns>
    public static Result<ExactlyFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        IReadOnlyList<IVariableItem>? indicators,
        int amount,
        IIntoValue<V>? converter,
        string name = "exactly",
        string? displayName = null) => CardinalityEncoding<V>.Create(
            indicators: indicators,
            amount: amount,
            converter: converter,
            exactly: true,
            name: name
        ).Map(encoding => new ExactlyFunction<V>(
            encoding: encoding,
            name: name,
            displayName: displayName
        ));

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;
    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) => _encoding.Evaluate(values);
    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => _encoding.RegisterAuxiliaryTokens(tokens);
    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => _encoding.RegisterConstraints(model, Name);
    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => _encoding.Prepare(
            inputs: inputs,
            modelValues: modelValues,
            evidence: out _
        );
    bool ICardinalityFunction<V>.IsCompatibleConverter(IFlt64ValueConverter<V> converter) => _encoding.Current(null, converter);
    bool ICardinalityFunction<V>.TryGetEvidence(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V> converter,
        out IReadOnlyDictionary<ISymbol, V> evidence) => _encoding.TryGetEvidence(
            result: result,
            inputs: inputs,
            modelValues: modelValues,
            converter: converter,
            evidence: out evidence
        );
}

internal interface ICardinalityFunction<V> : IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    bool IsCompatibleConverter(IFlt64ValueConverter<V> converter);
    bool TryGetEvidence(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V> converter,
        out IReadOnlyDictionary<ISymbol, V> evidence);
}

internal sealed class CardinalityEncoding<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly bool _exactly;
    private readonly V _zero;
    private readonly V _one;
    private readonly BinVar? _below;
    private readonly BinVar? _above;
    private readonly VariableItemKey[] _keys;
    private readonly BoundDependencySnapshot<V> _dependencies;

    public IReadOnlyList<IVariableItem> Indicators { get; }
    public int Amount { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    public IReadOnlyCollection<ISymbol> InputSymbols { get; }
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );

    private CardinalityEncoding(IReadOnlyList<IVariableItem> indicators, int amount, IIntoValue<V> converter, bool exactly, string name) {
        Indicators = indicators;
        Amount = amount;
        _converter = converter;
        _exactly = exactly;
        _zero = converter.Zero;
        _one = converter.One;
        ResultVar = new BinVar($"{name}_satisfied");
        _below = exactly && amount > 0 ? new BinVar($"{name}_below") : null;
        _above = exactly && amount < indicators.Count ? new BinVar($"{name}_above") : null;
        HelperVariables = Array.AsReadOnly(new IVariableItem?[] { ResultVar, _below, _above }.OfType<IVariableItem>().ToArray());
        InputSymbols = Array.AsReadOnly(indicators.Cast<ISymbol>().Distinct().ToArray());
        _keys = indicators.Concat(HelperVariables).Select(variable => variable.Key).ToArray();
        _dependencies = new BoundDependencySnapshot<V>(new[] {
            new LinearPolynomial<V>(indicators.Concat(HelperVariables).Select(variable => new LinearMonomial<V>(_one, variable)).ToArray(), _zero),
        });
    }

    public static Result<CardinalityEncoding<V>, ErrorCode, Error<ErrorCode>> Create(
        IReadOnlyList<IVariableItem>? indicators,
        int amount,
        IIntoValue<V>? converter,
        bool exactly,
        string name) {
        try {
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> checkedInputs = CardinalityConstraints.Exactly(
                indicators: indicators,
                amount: amount,
                converter: converter,
                name: name
            );
            if (!checkedInputs.IsOk || converter is null) {
                return Invalid<CardinalityEncoding<V>>();
            }
            IVariableItem[] snapshot = checkedInputs.Value[0].Lhs.Monomials.Select(term => (IVariableItem)term.Symbol).ToArray();
            if (snapshot.GroupBy(variable => variable.Key).Any(group => group.Any(variable => !ReferenceEquals(variable, group.First())))) {
                return Invalid<CardinalityEncoding<V>>();
            }
            var encoding = new CardinalityEncoding<V>(
                indicators: Array.AsReadOnly(snapshot),
                amount: amount,
                converter: converter,
                exactly: exactly,
                name: name
            );
            return encoding.Current(null, null) ? Results.Ok(encoding) : Invalid<CardinalityEncoding<V>>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<CardinalityEncoding<V>>();
        }
    }

    public Result<V?, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        try {
            if (!Current(null, null) || values is null) {
                return Invalid<V?>();
            }
            if (!TryCount(values, out int count)) {
                return Results.Ok<V?>(null);
            }
            return Results.Ok<V?>(Satisfied(count) ? _one : _zero);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V?>();
        }
    }

    public Result<V?, ErrorCode, Error<ErrorCode>> Prepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        out IReadOnlyDictionary<ISymbol, V> evidence) {
        evidence = new Dictionary<ISymbol, V>();
        try {
            if (!Current(null, null)) {
                return Invalid<V?>();
            }
            if (!TryCount(inputs, out int count) || !TryCount(modelValues, out int modelCount) || count != modelCount) {
                return Results.Ok<V?>(null);
            }
            foreach (IVariableItem input in Indicators) {
                if (!inputs.TryGetValue(input, out V direct) || !modelValues.TryGetValue(input, out V model) ||
                    !StructuralValueEquality<V>.Same(direct, model) || !InRange(input, model)) {
                    return Results.Ok<V?>(null);
                }
            }
            V result = Satisfied(count) ? _one : _zero;
            var inferred = new Dictionary<ISymbol, V> { [ResultVar] = result };
            if (_below is not null) {
                inferred[_below] = count < Amount ? _one : _zero;
            }
            if (_above is not null) {
                inferred[_above] = count > Amount ? _one : _zero;
            }
            if (inferred.Any(pair => pair.Key is not IVariableItem variable || !InRange(variable, pair.Value) ||
                    modelValues.TryGetValue(pair.Key, out V known) && !StructuralValueEquality<V>.Same(known, pair.Value)) ||
                !Current(null, null)) {
                return Results.Ok<V?>(null);
            }
            evidence = inferred;
            return Results.Ok<V?>(result);

            bool InRange(IVariableItem variable, V value) => variable.LowerBound is { } lower && variable.UpperBound is { } upper &&
                new ValueRange<Flt64>(lower, upper).Contains(_converter.FromValue(value));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Results.Ok<V?>(null);
        }
    }

    public bool TryGetEvidence(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V> converter,
        out IReadOnlyDictionary<ISymbol, V> evidence) {
        evidence = new Dictionary<ISymbol, V>();
        if (!Current(null, converter)) {
            return false;
        }
        Result<V?, ErrorCode, Error<ErrorCode>> prepared = Prepare(
            inputs: inputs,
            modelValues: modelValues,
            evidence: out evidence
        );
        return prepared.IsOk && prepared.Value is { } value && StructuralValueEquality<V>.Same(value, result) && Current(null, converter);
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        try {
            return tokens is not null && Current(null, null) ? tokens.Add(HelperVariables) : Invalid<Success>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model, string name) {
        try {
            if (model is null || !CurrentForModel(model)) {
                return Invalid<Success>();
            }
            IReadOnlyList<LinearInequality<V>> rows = Rows(name, out bool valid);
            if (!valid || !ValidateRows(
                    rows: rows,
                    tokens: model.Tokens,
                    modelConverter: ModelConverter(model)
                ) || !CurrentForModel(model)) {
                return Invalid<Success>();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: rows,
                afterWrite: () => {
                    if (!ValidateRows(
                            rows: rows,
                            tokens: model.Tokens,
                            modelConverter: ModelConverter(model)
                        ) || !CurrentForModel(model)) {
                        return Invalid<Success>();
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackModelDependency(() => CurrentForModel(model));
                    }
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    private bool CurrentForModel(IAbstractLinearMechanismModel<V> model) => Current(
        tokens: model.Tokens,
        modelConverter: ModelConverter(model)
    );

    private static IFlt64ValueConverter<V>? ModelConverter(IAbstractLinearMechanismModel<V> model) => model switch {
        LinearMechanismModel<V> linear => linear.Parent.Converter,
        QuadraticMechanismModel<V> quadratic => quadratic.Parent.Converter,
        // shortcut: 自定义模型未暴露转换器；统一契约可用后校验 / Custom models do not expose converters; validate when a common contract exists.
        _ => null,
    };

    public bool Current(IAbstractTokenTable<V>? tokens, IFlt64ValueConverter<V>? modelConverter) {
        try {
            if (!_dependencies.IsCurrent() || !StructuralValueEquality<V>.Same(_converter.Zero, _zero) ||
                !StructuralValueEquality<V>.Same(_converter.One, _one) || modelConverter is not null &&
                (!StructuralValueEquality<V>.Same(modelConverter.Zero, _zero) || !StructuralValueEquality<V>.Same(modelConverter.One, _one))) {
                return false;
            }
            IVariableItem[] variables = Indicators.Concat(HelperVariables).ToArray();
            var occupied = Indicators.Select(variable => variable.Key).ToHashSet();
            if (HelperVariables.Any(variable => !occupied.Add(variable.Key))) {
                return false;
            }
            for (int i = 0; i < variables.Length; ++i) {
                if (variables[i].Key != _keys[i] || !variables[i].TypeKind.IsBinaryType ||
                    tokens is not null && !ReferenceEquals(tokens.Find(variables[i])?.Variable, variables[i])) {
                    return false;
                }
            }
            IReadOnlyList<LinearInequality<V>> rows = Rows("cardinality", out bool valid);
            return valid && ValidateRows(
                rows: rows,
                tokens: tokens,
                modelConverter: modelConverter
            ) && _dependencies.IsCurrent();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private bool ValidateRows(
        IReadOnlyList<LinearInequality<V>> rows,
        IAbstractTokenTable<V>? tokens,
        IFlt64ValueConverter<V>? modelConverter) {
        foreach (LinearInequality<V> row in rows) {
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = row.ToLinearFlattenData(tokens);
            if (!flattened.IsOk || !ExactLinearExpansion.MatchesDifference(row, flattened.Value) ||
                !ValidInteger(row.Lhs.Constant, modelConverter) || !ValidInteger(row.Rhs.Constant, modelConverter) ||
                !ValidInteger(flattened.Value.Constant, modelConverter) ||
                row.Lhs.Monomials.Concat(row.Rhs.Monomials).Concat(flattened.Value.Monomials)
                    .Any(term => !ValidInteger(term.Coefficient, modelConverter))) {
                return false;
            }
        }
        return true;
    }

    private bool ValidInteger(V value, IFlt64ValueConverter<V>? modelConverter) {
        if (!ExactBoundValue.TryRead(
                value: value,
                converter: _converter,
                exact: out ExactBoundValue exact
            ) || exact.Denominator != BigInteger.One ||
            !ExactBoundValue.TryRead(_converter.FromValue(value), out ExactBoundValue encoded) || exact.CompareTo(encoded) != 0 ||
            !LinearFlattenExtensions.Arithmetic<V>.TryNegate(value, out V negative) ||
            !ExactBoundValue.TryRead(
                value: negative,
                converter: _converter,
                exact: out ExactBoundValue negated
            ) || negated.CompareTo(exact.Negate()) != 0 ||
            !ExactBoundValue.TryRead(_converter.FromValue(negative), out ExactBoundValue encodedNegative) || negated.CompareTo(encodedNegative) != 0) {
            return false;
        }
        double magnitude = (double)BigInteger.Abs(exact.Numerator);
        if (!double.IsFinite(magnitude) || !ExactBoundValue.TryRead(_converter.IntoValue(new Flt64(magnitude)), out ExactBoundValue converted) ||
            converted.CompareTo(exact.Abs()) != 0) {
            return false;
        }
        return modelConverter is null ||
            ExactBoundValue.TryRead(modelConverter.FromValue(value), out ExactBoundValue modelValue) && modelValue.CompareTo(exact) == 0 &&
            ExactBoundValue.TryRead(modelConverter.FromValue(negative), out ExactBoundValue modelNegative) && modelNegative.CompareTo(negated) == 0 &&
            ExactBoundValue.TryRead(modelConverter.IntoValue(new Flt64(magnitude)), out ExactBoundValue modelConverted) && modelConverted.CompareTo(exact.Abs()) == 0;
    }

    private bool TryCount(IReadOnlyDictionary<ISymbol, V> values, out int count) {
        count = 0;
        foreach (IVariableItem indicator in Indicators) {
            if (!values.TryGetValue(indicator, out V value) ||
                !ExactBoundValue.TryRead(
                    value: value,
                    converter: _converter,
                    exact: out ExactBoundValue exact
                ) ||
                !ExactBoundValue.TryRead(_converter.FromValue(value), out ExactBoundValue encoded) || exact.CompareTo(encoded) != 0) {
                return false;
            }
            if (exact.Sign == 0) {
                continue;
            }
            if (exact.Numerator != exact.Denominator) {
                return false;
            }
            ++count;
        }
        return true;
    }

    private bool Satisfied(int count) => _exactly ? count == Amount : count <= Amount;

    private IReadOnlyList<LinearInequality<V>> Rows(string name, out bool validNumbers) {
        int n = Indicators.Count;
        LinearMonomial<V>[] count = Indicators.Select(variable => new LinearMonomial<V>(_one, variable)).ToArray();
        var rows = new List<LinearInequality<V>>();
        bool valid = true;
        V Number(int value) {
            V result = _converter.IntoValue(new Flt64(value));
            valid &= ExactBoundValue.TryRead(
                value: result,
                converter: _converter,
                exact: out ExactBoundValue actual
            ) &&
                actual.Numerator == (BigInteger)value * actual.Denominator && ValidInteger(result, null);
            return result;
        }
        void Add(IEnumerable<LinearMonomial<V>> terms, Comparison comparison, V rhs, string suffix) => rows.Add(IndicatorSupport.Row(
            monomials: terms.ToArray(),
            constant: _zero,
            comparison: comparison,
            rhs: rhs,
            name: $"{name}_{suffix}"
        ));
        if (!_exactly) {
            if (Amount == n) {
                Add(
                    terms: new[] { new LinearMonomial<V>(_one, ResultVar) },
                    comparison: Comparison.EQ,
                    rhs: _one,
                    suffix: "always_true"
                );
            }
            else {
                Add(
                    terms: count.Append(new LinearMonomial<V>(Number(n - Amount), ResultVar)),
                    comparison: Comparison.LE,
                    rhs: Number(n),
                    suffix: "upper"
                );
                Add(
                    terms: count.Append(new LinearMonomial<V>(Number(Amount + 1), ResultVar)),
                    comparison: Comparison.GE,
                    rhs: Number(Amount + 1),
                    suffix: "lower"
                );
            }
        }
        else {
            Add(
                terms: HelperVariables.Select(variable => new LinearMonomial<V>(_one, variable)),
                comparison: Comparison.EQ,
                rhs: _one,
                suffix: "one_branch"
            );
            Add(
                terms: count.Append(new LinearMonomial<V>(Number(n - Amount), ResultVar)),
                comparison: Comparison.LE,
                rhs: Number(n),
                suffix: "exact_upper"
            );
            Add(
                terms: count.Append(new LinearMonomial<V>(Number(Amount).Negate(), ResultVar)),
                comparison: Comparison.GE,
                rhs: _zero,
                suffix: "exact_lower"
            );
            if (_below is not null) {
                Add(
                    terms: count.Append(new LinearMonomial<V>(Number(n - Amount + 1), _below)),
                    comparison: Comparison.LE,
                    rhs: Number(n),
                    suffix: "below_branch"
                );
            }
            if (_above is not null) {
                Add(
                    terms: count.Append(new LinearMonomial<V>(Number(Amount + 1).Negate(), _above)),
                    comparison: Comparison.GE,
                    rhs: _zero,
                    suffix: "above_branch"
                );
            }
        }
        validNumbers = valid;
        return rows;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>() => Results.Failed<T>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "基数指示函数输入、转换器或注册状态无效。 / Invalid cardinality-indicator input, converter or registration state."
    ));
}
