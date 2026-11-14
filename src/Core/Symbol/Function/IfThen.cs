#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>条件成立时取表达式，否则取零 / Take an expression when the condition holds, otherwise zero.</summary>
/// <remarks>
/// 显式范围会作为约束强制执行；求值不验证嵌套来源或求解器容差。
/// Explicit bounds are enforced as constraints; evaluation does not verify nested provenance or solver tolerances.
/// </remarks>
/// <typeparam name="V">输入和系数的数值类型 / Numeric type of inputs and coefficients.</typeparam>
public sealed class IfThenFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly V _zero;
    private readonly V _one;

    internal IfFunction<V> ConditionEncoding { get; }
    internal IntegerProductFunction<V> ProductEncoding { get; }
    /// <summary>条件多项式的浅快照 / Shallow snapshot of the condition polynomial.</summary>
    public LinearPolynomial<V> Condition => ConditionEncoding.Condition;
    /// <summary>真分支多项式的浅快照 / Shallow snapshot of the true-branch polynomial.</summary>
    public LinearPolynomial<V> Then => ProductEncoding.Input;
    /// <summary>条件比较关系 / Condition relation.</summary>
    public Comparison Relation => ConditionEncoding.Relation;
    /// <summary>真假分支间隔 / Gap between true and false branches.</summary>
    public V StrictBoundary => ConditionEncoding.StrictBoundary;
    /// <summary>离散步长 / Discrete step.</summary>
    public V Delta => ConditionEncoding.Delta;
    /// <summary>被强制的条件范围 / Enforced condition bounds.</summary>
    public LinearPolynomialBounds<V> ConditionBounds => ConditionEncoding.Bounds;
    /// <summary>被强制的真分支表达式范围 / Enforced bounds of the true-branch expression.</summary>
    public LinearPolynomialBounds<V> ThenBounds => ProductEncoding.InputBounds;
    /// <summary>条件真分支指示变量 / True-branch indicator variable.</summary>
    public BinVar IndicatorVar => ConditionEncoding.ResultVar;
    /// <summary>条件结果变量 / Conditional result variable.</summary>
    public RealVar ResultVar => ProductEncoding.ResultVar;
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { Condition, Then });
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );

    private IfThenFunction(
        IfFunction<V> condition,
        IntegerProductFunction<V> product,
        IIntoValue<V> converter,
        string name,
        string? displayName) {
        ConditionEncoding = condition;
        ProductEncoding = product;
        _converter = converter;
        _zero = condition.ResultPolynomial.Constant;
        _one = condition.ResultPolynomial.Monomials[0].Coefficient;
        Name = name;
        DisplayName = displayName;
        HelperVariables = Array.AsReadOnly(condition.HelperVariables.Concat(product.HelperVariables).ToArray());
    }

    /// <summary>校验并创建条件结果函数 / Validate and create a conditional-value function.</summary>
    /// <param name="condition">与零比较的线性多项式 / Linear polynomial compared with zero.</param>
    /// <param name="then">真分支线性多项式 / True-branch linear polynomial.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="conditionBounds">可选条件范围；指定时强制执行 / Optional condition bounds, enforced when supplied.</param>
    /// <param name="thenBounds">可选表达式范围；指定时强制执行 / Optional expression bounds, enforced when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<IfThenFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? condition,
        LinearPolynomial<V>? then,
        IIntoValue<V>? converter,
        V strictBoundary,
        Comparison relation = Comparison.GT,
        V? delta = null,
        LinearPolynomialBounds<V>? conditionBounds = null,
        LinearPolynomialBounds<V>? thenBounds = null,
        string name = "ifThen",
        string? displayName = null) {
        try {
            if (then?.Monomials is null || converter is null ||
                then.Monomials.Any(term => term is null || term.Symbol is null)) {
                return Invalid<IfThenFunction<V>>();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(then.Monomials.ToArray()), then.Constant);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> initial =
                new SymbolFlatten.LinearFlattenData<V>(snapshot.Monomials, snapshot.Constant).ExpandIntermediateSymbols();
            if (!initial.IsOk) {
                return initial.Bind(_ => Invalid<IfThenFunction<V>>());
            }
            var expanded = new LinearPolynomial<V>(Array.AsReadOnly(initial.Value.Monomials.ToArray()), initial.Value.Constant);
            BoundDependencySnapshot<V>? captured = thenBounds is null ? new BoundDependencySnapshot<V>(new[] { expanded }) : null;
            if (captured is not null && !captured.IsCurrent()) {
                return Invalid<IfThenFunction<V>>();
            }
            Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> conditionResult = IfFunction<V>.Create(
                condition: condition,
                converter: converter,
                strictBoundary: strictBoundary,
                relation: relation,
                delta: delta,
                bounds: conditionBounds,
                name: $"{name}_condition"
            );
            if (!conditionResult.IsOk) {
                return conditionResult.Bind(_ => Invalid<IfThenFunction<V>>());
            }
            Result<IntegerProductFunction<V>, ErrorCode, Error<ErrorCode>> productResult = IntegerProductFunction<V>.Create(
                integer: conditionResult.Value.ResultVar,
                input: snapshot,
                converter: converter,
                inputBounds: thenBounds,
                name: name
            );
            if (!productResult.IsOk) {
                return productResult.Bind(_ => Invalid<IfThenFunction<V>>());
            }
            IntegerProductFunction<V> product = productResult.Value;
            if (captured is not null && !captured.IsCurrent() ||
                !SameExpansion(expanded, product.ExpandedInput) ||
                !ExactLinearExpansion.Matches(
                    input: snapshot,
                    expanded: product.ExpandedInput,
                    converter: converter
                ) || !Exact(product.InputBounds.LowerBound, converter) || !Exact(product.InputBounds.UpperBound, converter) ||
                !conditionResult.Value.Current(null) || product.ValidateInput(null).IsFailed ||
                captured is not null && !captured.IsCurrent()) {
                return Invalid<IfThenFunction<V>>();
            }
            return Results.Ok(new IfThenFunction<V>(
                condition: conditionResult.Value,
                product: product,
                converter: converter,
                name: name,
                displayName: displayName
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<IfThenFunction<V>>();
        }
    }

    /// <summary>从归一化约束输入创建条件结果适配器 / Create a conditional-value adapter from a normalized constraint input.</summary>
    /// <param name="inequality">已归一化的约束输入 / Normalized constraint input.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="then">真分支多项式，默认取一 / True-branch polynomial, defaulting to one.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="conditionBounds">可选条件范围；指定时强制执行 / Optional condition bounds, enforced when supplied.</param>
    /// <param name="thenBounds">可选表达式范围；指定时强制执行 / Optional expression bounds, enforced when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数适配器或结构化失败 / Function adapter or structured failure.</returns>
    public static Result<LinearFunctionSymbolAdapter<V>, ErrorCode, Error<ErrorCode>> From(
        LinearConstraintInput<V>? inequality,
        IIntoValue<V>? converter,
        V strictBoundary,
        LinearPolynomial<V>? then = null,
        V? delta = null,
        LinearPolynomialBounds<V>? conditionBounds = null,
        LinearPolynomialBounds<V>? thenBounds = null,
        string name = "ifThen",
        string? displayName = null) {
        try {
            if (inequality?.FlattenData is not { } flattened || converter is null) {
                return Invalid<LinearFunctionSymbolAdapter<V>>();
            }
            return Create(
                condition: new LinearPolynomial<V>(flattened.Monomials, flattened.Constant),
                then: then ?? new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), strictBoundary.Constants.One),
                converter: converter,
                strictBoundary: strictBoundary,
                relation: inequality.Sign,
                delta: delta,
                conditionBounds: conditionBounds,
                thenBounds: thenBounds,
                name: name,
                displayName: displayName
            ).Map(function => new LinearFunctionSymbolAdapter<V>(function));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<LinearFunctionSymbolAdapter<V>>();
        }
    }

    /// <summary>按条件关系执行三值判定 / Classify the condition using three-valued semantics.</summary>
    /// <param name="values">输入符号值 / Input symbol values.</param>
    /// <returns>三值结果；缺失或无效输入返回失败 / Three-valued result, or failure for missing or invalid input.</returns>
    public Result<TruthValue, ErrorCode, Error<ErrorCode>> Classify(IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.ConditionalPolynomial(
            polynomial: Condition,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!input.IsOk) {
            return input.Map(_ => TruthValue.Undefined);
        }
        return input.Value is { } difference ? ConditionClassification.ClassifyChecked(
            difference: difference,
            exact: exact,
            encoded: encoded,
            relation: Relation,
            delta: Delta,
            strictBoundary: StrictBoundary,
            converter: _converter
        ) : Invalid<TruthValue>();
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        EvaluateCore(values, null);

    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) =>
        Current(null) ? EvaluateCore(inputs, modelValues) : FunctionEvaluation.Invalid<V>();

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateCore(
        IReadOnlyDictionary<ISymbol, V> values,
        IReadOnlyDictionary<ISymbol, V>? modelValues) {
        Result<V?, ErrorCode, Error<ErrorCode>> condition = modelValues is null
            ? ConditionEncoding.TryEvaluate(values)
            : ((IModelAwareFunctionPreparation<V>)ConditionEncoding).TryPrepare(values, modelValues);
        if (!condition.IsOk || condition.Value is not { } indicator) {
            return condition;
        }
        if (indicator.Eq(_zero)) {
            return Results.Ok<V?>(_zero);
        }
        Result<V?, ErrorCode, Error<ErrorCode>> result = FunctionEvaluation.ConditionalPolynomial(
            polynomial: Then,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!result.IsOk || result.Value is not { } value) {
            return result;
        }
        if (!FunctionEvaluation.TryReadConditionValue(
                value: value,
                converter: _converter,
                exact: out ExactBoundValue actual,
                encoded: out ExactBoundValue encodedActual
            ) || actual.CompareTo(exact) != 0 || encodedActual.CompareTo(encoded) != 0) {
            return FunctionEvaluation.Invalid<V>();
        }
        if (modelValues is not null) {
            Result<bool, ErrorCode, Error<ErrorCode>> proof = FunctionEvaluation.ExactPolynomial(
                polynomial: ProductEncoding.ExpandedInput,
                values: modelValues,
                converter: _converter,
                exact: out ExactBoundValue modelExact,
                encoded: out ExactBoundValue modelEncoded
            );
            if (!proof.IsOk || !proof.Value) {
                return proof.Map<V?>(_ => null);
            }
            if (actual.CompareTo(modelExact) != 0 || encodedActual.CompareTo(modelEncoded) != 0) {
                return FunctionEvaluation.Invalid<V>();
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        try {
            return tokens is not null && Current(null) ? tokens.Add(HelperVariables) : Invalid<Success>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            return Invalid<Success>();
        }
        try {
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> condition = ConditionEncoding.PrepareConstraints(model);
            if (!condition.IsOk) {
                return condition.Map(_ => Results.SuccessInstance);
            }
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> product = ProductEncoding.PrepareConstraints(model);
            if (!product.IsOk) {
                return product.Map(_ => Results.SuccessInstance);
            }
            if (!Current(model.Tokens)) {
                return Invalid<Success>();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: condition.Value.Concat(product.Value),
                afterWrite: () => {
                    if (!Current(model.Tokens)) {
                        return Invalid<Success>();
                    }
                    ConditionEncoding.TrackDependencies(model);
                    ProductEncoding.TrackDependencies(model);
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    private bool Current(IAbstractTokenTable<V>? tokens) {
        try {
            return ConditionEncoding.Current(tokens) && ProductEncoding.ValidateInput(tokens).IsOk &&
                ExactLinearExpansion.Matches(
                    input: Then,
                    expanded: ProductEncoding.ExpandedInput,
                    converter: _converter
                ) && ConditionEncoding.Current(tokens) && ProductEncoding.ValidateInput(tokens).IsOk;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private static bool SameExpansion(LinearPolynomial<V> first, LinearPolynomial<V> second) {
        if (first.Monomials.Count != second.Monomials.Count ||
            !StructuralValueEquality<V>.Same(first.Constant, second.Constant)) {
            return false;
        }
        for (int index = 0; index < first.Monomials.Count; ++index) {
            LinearMonomial<V> left = first.Monomials[index];
            LinearMonomial<V> right = second.Monomials[index];
            if (left.Symbol is not IVariableItem lhs || right.Symbol is not IVariableItem rhs || lhs.Key != rhs.Key ||
                !StructuralValueEquality<V>.Same(left.Coefficient, right.Coefficient)) {
                return false;
            }
        }
        return true;
    }

    private static bool Exact(V value, IIntoValue<V> converter) =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out ExactBoundValue exact
        ) && ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>() => Results.Failed<T>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "条件结果输入、范围或注册状态无效。 / Invalid conditional-value input, bounds or registration state."
    ));
}
