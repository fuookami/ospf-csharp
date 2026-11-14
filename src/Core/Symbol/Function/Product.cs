#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>两个线性表达式的乘积 / Product of two linear expressions.</summary>
/// <typeparam name="V">系数和求值的数值类型 / Numeric type of coefficients and values.</typeparam>
public sealed class ProductFunction<V> : IQuadraticMathFunctionSymbol<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly UInt64 _identifier = IdentifierGenerator.Gen();
    private readonly IFlt64ValueConverter<V> _converter;

    /// <summary>左侧输入快照 / Left input snapshot.</summary>
    public LinearPolynomial<V> Left { get; }
    /// <summary>右侧输入快照 / Right input snapshot.</summary>
    public LinearPolynomial<V> Right { get; }
    /// <inheritdoc/>
    public QuadraticPolynomial<V> Polynomial { get; }
    /// <inheritdoc/>
    public UInt64 Identifier => _identifier;
    /// <inheritdoc/>
    public int Index => 0;
    /// <inheritdoc/>
    public IIntermediateSymbol? Parent => null;
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public Category Category => QuadraticCategory.Instance;
    /// <inheritdoc/>
    public Category OperationCategory => QuadraticCategory.Instance;
    /// <inheritdoc/>
    public ExpressionRange<V> Range => ExpressionRange<V>.Create();
    /// <inheritdoc/>
    public bool Cached => false;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { Left, Right });
    /// <inheritdoc/>
    public IReadOnlySet<IIntermediateSymbol> Dependencies => Left.Monomials.Concat(Right.Monomials)
        .Select(monomial => monomial.Symbol).OfType<IIntermediateSymbol>().ToHashSet();

    private ProductFunction(
        LinearPolynomial<V> left,
        LinearPolynomial<V> right,
        QuadraticPolynomial<V> polynomial,
        IFlt64ValueConverter<V> converter,
        string name,
        string? displayName) {
        Left = left;
        Right = right;
        Polynomial = polynomial;
        _converter = converter;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>校验并创建乘积表达式 / Validate and create a product expression.</summary>
    /// <param name="left">左侧线性多项式 / Left linear polynomial.</param>
    /// <param name="right">右侧线性多项式 / Right linear polynomial.</param>
    /// <param name="converter">求解器数值转换器 / Solver value converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>乘积或校验失败 / Product or validation failure.</returns>
    public static Result<ProductFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? left,
        LinearPolynomial<V>? right,
        IFlt64ValueConverter<V>? converter,
        string name = "product",
        string? displayName = null) {
        if (left?.Monomials is null || right?.Monomials is null || converter is null) {
            return Invalid();
        }
        LinearMonomial<V>[] leftTerms = left.Monomials.ToArray();
        LinearMonomial<V>[] rightTerms = right.Monomials.ToArray();
        if (leftTerms.Concat(rightTerms).Any(term => term is null ||
            term.Symbol is not (IVariableItem or ILinearIntermediateSymbol<V>))) {
            return Invalid();
        }
        var terms = new List<QuadraticMonomial<V>>();
        foreach (LinearMonomial<V> lhs in leftTerms) {
            foreach (LinearMonomial<V> rhs in rightTerms) {
                Result<V?, ErrorCode, Error<ErrorCode>> coefficient = FunctionEvaluation.Product(lhs.Coefficient, rhs.Coefficient);
                if (coefficient.IsFailed || coefficient.Value is not { } value) {
                    return Invalid();
                }
                terms.Add(new QuadraticMonomial<V>(
                    Coefficient: value,
                    Symbol1: lhs.Symbol,
                    Symbol2: rhs.Symbol
                ));
            }
        }
        foreach (LinearMonomial<V> lhs in leftTerms) {
            Result<V?, ErrorCode, Error<ErrorCode>> coefficient = FunctionEvaluation.Product(lhs.Coefficient, right.Constant);
            if (coefficient.IsFailed || coefficient.Value is not { } value) {
                return Invalid();
            }
            terms.Add(QuadraticMonomial<V>.Linear(value, lhs.Symbol));
        }
        foreach (LinearMonomial<V> rhs in rightTerms) {
            Result<V?, ErrorCode, Error<ErrorCode>> coefficient = FunctionEvaluation.Product(rhs.Coefficient, left.Constant);
            if (coefficient.IsFailed || coefficient.Value is not { } value) {
                return Invalid();
            }
            terms.Add(QuadraticMonomial<V>.Linear(value, rhs.Symbol));
        }
        Result<V?, ErrorCode, Error<ErrorCode>> constant = FunctionEvaluation.Product(left.Constant, right.Constant);
        if (constant.IsFailed || constant.Value is not { } constantValue) {
            return Invalid();
        }
        return Results.Ok(new ProductFunction<V>(
            left: new LinearPolynomial<V>(Array.AsReadOnly(leftTerms), left.Constant),
            right: new LinearPolynomial<V>(Array.AsReadOnly(rightTerms), right.Constant),
            polynomial: new QuadraticPolynomial<V>(Array.AsReadOnly(terms.ToArray()), constantValue),
            converter: converter,
            name: name,
            displayName: displayName
        ));
    }

    /// <inheritdoc/>
    public MutableQuadraticPolynomial<V> AsMutable() => Polynomial.ToMutable();

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        values is null ? FunctionEvaluation.Invalid<V>() : EvaluateCore(
            resolve: symbol => values.TryGetValue(symbol, out V value) ? value : null,
            zero: _converter.Zero,
            zeroIfNone: false
        );

    /// <inheritdoc/>
    public V? Prepare(
        IReadOnlyDictionary<ISymbol, V>? values,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter) => values is null || values.Count == 0
            ? Evaluate(tokenTable, converter)
            : EvaluateValues(
                values: values,
                tokenTable: tokenTable,
                converter: converter,
                zeroIfNone: false
            ).Value;

    /// <inheritdoc/>
    public void PrepareAndCache(
        IReadOnlyDictionary<ISymbol, V>? values,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter) {
        if (Prepare(
                values: values,
                tokenTable: tokenTable,
                converter: converter
            ) is { } value) {
            if (values is null || values.Count == 0) {
                tokenTable.Cache(
                    cacheKey: this,
                    solution: (IReadOnlyList<V>?)null,
                    value: value
                );
            }
            else {
                tokenTable.Cache(
                    cacheKey: this,
                    fixedValues: values,
                    value: value
                );
            }
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false) =>
        EvaluateCore(
            resolve: symbol => symbol switch {
                IVariableItem variable => tokenTable.Find(variable)?.ResultWith(converter),
                IIntermediateSymbol<V> intermediate => intermediate.Evaluate(
                    tokenTable: tokenTable,
                    converter: converter,
                    zeroIfNone: zeroIfNone
                ),
                _ => null,
            },
            zero: converter.Zero,
            zeroIfNone: zeroIfNone
        ).Value;

    /// <inheritdoc/>
    public V? Evaluate(
        IReadOnlyList<V> results,
        IAbstractTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone = false) => EvaluateCore(
            resolve: symbol => symbol switch {
                IVariableItem variable when tokenTable.IndexOf(variable) is int index && index >= 0 && index < results.Count => results[index],
                IIntermediateSymbol<V> intermediate => intermediate.Evaluate(
                    results: results,
                    tokenTable: tokenTable,
                    converter: converter,
                    zeroIfNone: zeroIfNone
                ),
                _ => null,
            },
            zero: converter.Zero,
            zeroIfNone: zeroIfNone
        ).Value;

    /// <inheritdoc/>
    public V? Evaluate(
        IReadOnlyDictionary<ISymbol, V> values,
        IAbstractTokenTable<V>? tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone = false) => EvaluateValues(
            values: values,
            tokenTable: tokenTable,
            converter: converter,
            zeroIfNone: zeroIfNone
        ).Value;

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateValues(
        IReadOnlyDictionary<ISymbol, V> values,
        IAbstractTokenTable<V>? tokenTable,
        IFlt64ValueConverter<V> converter,
        bool zeroIfNone) => EvaluateCore(
            resolve: symbol => values.TryGetValue(symbol, out V value) ? value : symbol switch {
                IVariableItem variable => tokenTable?.Find(variable)?.ResultWith(converter),
                IIntermediateSymbol<V> intermediate => intermediate.Evaluate(
                    values: values,
                    tokenTable: tokenTable,
                    converter: converter,
                    zeroIfNone: zeroIfNone
                ),
                _ => null,
            },
            zero: converter.Zero,
            zeroIfNone: zeroIfNone
        );

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateCore(Func<ISymbol, V?> resolve, V zero, bool zeroIfNone) {
        try {
            var values = new Dictionary<ISymbol, V>();
            V? Resolve(ISymbol symbol) {
                if (values.TryGetValue(symbol, out V cached)) {
                    return cached;
                }
                V? value = resolve(symbol);
                if (value is null && !zeroIfNone) {
                    return null;
                }
                values.Add(symbol, value ?? zero);
                return value ?? zero;
            }
            Result<V?, ErrorCode, Error<ErrorCode>> left = FunctionEvaluation.Polynomial(
                polynomial: Left,
                resolve: Resolve,
                rejectUnderflow: true
            );
            if (left.IsFailed || left.Value is not { } leftValue) {
                return left;
            }
            Result<V?, ErrorCode, Error<ErrorCode>> right = FunctionEvaluation.Polynomial(
                polynomial: Right,
                resolve: Resolve,
                rejectUnderflow: true
            );
            if (right.IsFailed || right.Value is not { } rightValue) {
                return right;
            }
            return FunctionEvaluation.Product(leftValue, rightValue);
        }
        catch (ArithmeticException) {
            return FunctionEvaluation.Invalid<V>();
        }
        catch (InvalidOperationException) {
            return FunctionEvaluation.Invalid<V>();
        }
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.OkInstance;

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractQuadraticMechanismModel<V> model) => Results.OkInstance;

    /// <inheritdoc/>
    public void Flush(bool force = false) {
        foreach (IIntermediateSymbol<V> dependency in Dependencies.OfType<IIntermediateSymbol<V>>()) {
            dependency.Flush(force);
        }
    }

    /// <inheritdoc/>
    public string ToRawString(UInt64 unfold) => unfold == UInt64.Zero ? DisplayName ?? Name : $"product({Left}, {Right})";

    private static Result<ProductFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<ProductFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "乘积输入或展开系数无效 / Invalid product input or expanded coefficient"
        ));
}
