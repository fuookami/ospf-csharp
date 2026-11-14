#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>函数符号注册生命周期基类 / V-generic base for function symbol registration lifecycle</summary>
public interface IMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens);
    Try RegisterConstraints(IAbstractLinearMechanismModel<V> model);
}

/// <summary>暴露结果多项式的可选接口</summary>
public interface IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    LinearPolynomial<V> ResultPolynomial { get; }
}

/// <summary>数学函数符号基础接口</summary>
public interface IMathFunctionSymbol<V> : IMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    string Name { get; set; }
    string? DisplayName { get; set; }
    IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <summary>求值可能读取的输入符号；null 表示未知 / Symbols evaluation may read; null means unknown.</summary>
    IReadOnlyCollection<ISymbol>? InputSymbols => null;
    V? Evaluate(IReadOnlyDictionary<ISymbol, V> values);

    /// <summary>返回函数求值结果；自定义实现负责检查中间运算 / Return the evaluation result; custom implementations must check intermediate arithmetic.</summary>
    /// <param name="values">输入符号值 / Input symbol values.</param>
    /// <returns>结果、缺失值或数值失败 / Value, missing value, or numeric failure.</returns>
    Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        FunctionEvaluation.Evaluate(() => Evaluate(values));
}

internal interface IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Result<V?, ErrorCode, Error<ErrorCode>> TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues);
}

internal static class FunctionInputs {
    public static IReadOnlyCollection<ISymbol> Of<V>(LinearPolynomial<V> polynomial)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        polynomial.Monomials.Select(monomial => monomial.Symbol).ToHashSet();

    /// <summary>二次多项式的输入符号 / A quadratic polynomial's input symbols.</summary>
    public static IReadOnlyCollection<ISymbol> Of<V>(QuadraticPolynomial<V> polynomial)
        where V : struct, IRing<V> {
        var symbols = new HashSet<ISymbol>();
        foreach (QuadraticMonomial<V> monomial in polynomial.Monomials) {
            symbols.Add(monomial.Symbol1);
            if (monomial.Symbol2 is { } second) {
                symbols.Add(second);
            }
        }

        return symbols;
    }

    public static IReadOnlyCollection<ISymbol> Of<V>(IEnumerable<LinearPolynomial<V>> polynomials)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        polynomials.SelectMany(polynomial => polynomial.Monomials)
            .Select(monomial => monomial.Symbol).ToHashSet();

    public static IReadOnlyCollection<ISymbol> Of<V>(IEnumerable<LinearInequality<V>> inequalities)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Of(inequalities.SelectMany(inequality => new[] { inequality.Lhs, inequality.Rhs }));

    public static IReadOnlyCollection<ISymbol> Of<V>(IEnumerable<LinearConstraintInput<V>> inputs)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        inputs.SelectMany(input => input.FlattenData.Monomials)
            .Select(monomial => monomial.Symbol).ToHashSet();
}

/// <summary>内部二次函数符号注册接口</summary>
public interface IQuadraticMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens);
    Try RegisterConstraints(IAbstractQuadraticMechanismModel<V> model);
}

/// <summary>具有受检求值的二次函数符号 / Quadratic function symbol with checked evaluation.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IQuadraticMathFunctionSymbol<V> : IQuadraticIntermediateSymbol<V>, IQuadraticMathFunctionSymbolBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>求值可能读取的输入符号；null 表示未知 / Symbols evaluation may read; null means unknown.</summary>
    IReadOnlyCollection<ISymbol>? InputSymbols { get; }

    /// <summary>注册辅助令牌 / Register auxiliary tokens.</summary>
    /// <param name="tokens">可添加令牌的集合 / Addable token collection.</param>
    /// <returns>注册结果 / Registration result.</returns>
    new Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens);

    /// <summary>从已准备的输入值求值 / Evaluate from prepared input values.</summary>
    /// <param name="values">输入符号值 / Input symbol values.</param>
    /// <returns>结果、缺失值或数值失败 / Value, missing value, or numeric failure.</returns>
    Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values);
}

/// <summary>
/// 将线性函数符号适配为可加入元模型的中间符号。
/// Adapts a linear function symbol into an intermediate symbol that can be added to a meta-model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class LinearFunctionSymbolAdapter<V> :
    ILinearIntermediateSymbol<V>,
    IMathFunctionSymbol<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly LinearPolynomial<V> _resultPolynomial;
    private readonly UInt64 _identifier = IdentifierGenerator.Gen();

    /// <summary>被包装的函数符号 / Wrapped function symbol.</summary>
    public IMathFunctionSymbol<V> Function { get; }

    /// <summary>适配后的符号标识 / Adapter symbol identifier.</summary>
    public UInt64 Identifier => _identifier;

    /// <inheritdoc/>
    public int Index => 0;

    /// <inheritdoc/>
    public IIntermediateSymbol? Parent => null;

    /// <inheritdoc/>
    public string Name {
        get => Function.Name;
        set => Function.Name = value;
    }

    /// <inheritdoc/>
    public string? DisplayName {
        get => Function.DisplayName;
        set => Function.DisplayName = value;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => Function.HelperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol>? InputSymbols => Function.InputSymbols;

    /// <inheritdoc/>
    public LinearPolynomial<V> Polynomial => _resultPolynomial;

    /// <inheritdoc/>
    public MutableLinearPolynomial<V> AsMutable() =>
        new(_resultPolynomial.Monomials, _resultPolynomial.Constant);

    /// <inheritdoc/>
    public ExpressionRange<V> Range => ExpressionRange<V>.Create();

    /// <inheritdoc/>
    public IReadOnlySet<IIntermediateSymbol> Dependencies =>
        (InputSymbols ?? Array.Empty<ISymbol>())
            .Concat(_resultPolynomial.Monomials.Select(term => term.Symbol))
            .OfType<IIntermediateSymbol>()
            .ToHashSet();

    /// <inheritdoc/>
    public Category Category => LinearCategory.Instance;

    /// <inheritdoc/>
    public bool Cached => false;

    /// <inheritdoc/>
    public bool Discrete => false;

    /// <summary>创建函数适配器 / Create a function adapter.</summary>
    /// <param name="function">线性函数符号 / Linear function symbol.</param>
    public LinearFunctionSymbolAdapter(IMathFunctionSymbol<V> function) {
        Function = function ?? throw new ArgumentNullException(nameof(function));
        _resultPolynomial = function is IHasResultPolynomial<V> result
            ? result.ResultPolynomial
            : new LinearPolynomial<V>(
                Array.Empty<LinearMonomial<V>>(),
                NumericConstantsRegistry.For<V>().Zero);
    }

    /// <inheritdoc/>
    public V? Prepare(
        IReadOnlyDictionary<ISymbol, V>? values,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter) {
        if (values is null || values.Count == 0) {
            var tokenValues = new Dictionary<ISymbol, V>();
            foreach (Token<V> token in tokenTable.Tokens) {
                if (token.ResultWith(converter) is { } value) {
                    tokenValues[token.Variable] = value;
                }
            }
            values = tokenValues;
        }
        return Function is IModelAwareFunctionPreparation<V>
            ? TryPrepare(
                inputs: values,
                modelValues: values,
                converter: converter
            ).Value
            : Function.Evaluate(values);
    }

    /// <inheritdoc/>
    public void PrepareAndCache(
        IReadOnlyDictionary<ISymbol, V>? values,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter) {
        if (Prepare(values, tokenTable, converter) is { } value) {
            tokenTable.Cache(this, (IReadOnlyList<V>?)null, value);
        }
        else if (Function is IModelAwareFunctionPreparation<V>) {
            tokenTable.ClearCachedValue(this, (IReadOnlyList<V>?)null);
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone = false) =>
        EvaluatePolynomial(
            symbol => symbol is IVariableItem variable
                ? tokenTable.Find(variable)?.ResultWith(converter)
                : null,
            zeroIfNone);

    /// <inheritdoc/>
    public V? Evaluate(
        IReadOnlyList<V> results,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone = false) =>
        EvaluatePolynomial(
            symbol => symbol is IVariableItem variable &&
                tokenTable.IndexOf(variable) is int index &&
                index < results.Count
                    ? results[index]
                    : null,
            zeroIfNone);

    /// <inheritdoc/>
    public V? Evaluate(
        IReadOnlyDictionary<ISymbol, V> values,
        IAbstractTokenTable<V>? tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone = false) {
        if (values.TryGetValue(this, out V selfValue)) {
            return selfValue;
        }
        return EvaluatePolynomial(
            symbol => values.TryGetValue(symbol, out V value) ? value : null,
            zeroIfNone);
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => Function.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        Function.TryEvaluate(values);

    internal Result<V?, ErrorCode, Error<ErrorCode>> TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V>? converter = null) =>
        Function is IUnivariatePiecewiseFunction<V> piecewise && converter is not null &&
            !piecewise.Piecewise.IsCompatibleConverter(converter)
            ? Results.Ok<V?>(null)
            : Function is ICardinalityFunction<V> cardinality && converter is not null &&
                !cardinality.IsCompatibleConverter(converter)
            ? Results.Ok<V?>(null)
            : Function is IModelAwareFunctionPreparation<V> aware
            ? aware.TryPrepare(inputs, modelValues)
            : Function.TryEvaluate(inputs);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        Function.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        Function.RegisterConstraints(model);

    /// <inheritdoc/>
    public void Flush(bool force = false) { }

    /// <inheritdoc/>
    public string ToRawString(UInt64 unfold) => DisplayName ?? Name;

    private V? EvaluatePolynomial(
        Func<ISymbol, V?> valueOf,
        bool zeroIfNone) {
        V result = _resultPolynomial.Constant;
        foreach (LinearMonomial<V> term in _resultPolynomial.Monomials) {
            V? value = valueOf(term.Symbol);
            if (value is null && !zeroIfNone) {
                return null;
            }
            result = result.Plus(term.Coefficient.Times(value ?? NumericConstantsRegistry.For<V>().Zero));
        }
        return result;
    }
}
