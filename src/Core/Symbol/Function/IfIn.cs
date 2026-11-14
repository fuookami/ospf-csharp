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
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>闭区间条件的二值结果 / Binary result of a closed-interval condition.</summary>
/// <remarks>
/// 区间外存在严格间隔；显式输入范围会作为约束强制执行。
/// Strict gaps lie outside the interval; explicit input bounds are enforced as constraints.
/// 准备缓存时核对可验证的展开值；直接求值不验证嵌套来源，且不校验求解器容差。
/// Cache preparation checks provable expanded values; direct evaluation does not verify nested provenance or solver tolerances.
/// </remarks>
/// <typeparam name="V">输入和系数的数值类型 / Numeric type of inputs and coefficients.</typeparam>
public sealed class IfInFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly V _zero;
    private readonly V _one;
    private readonly V _negativeOne;

    /// <summary>输入多项式的浅快照 / Shallow snapshot of the input polynomial.</summary>
    public LinearPolynomial<V> X { get; }
    /// <summary>包含在区间内的下界 / Inclusive lower endpoint.</summary>
    public V Lower { get; }
    /// <summary>包含在区间内的上界 / Inclusive upper endpoint.</summary>
    public V Upper { get; }
    /// <summary>真假分支之间的间隔 / Gap between true and false branches.</summary>
    public V StrictBoundary => LowerCondition.StrictBoundary;
    /// <summary>离散步长 / Discrete step.</summary>
    public V Delta => LowerCondition.Delta;
    /// <summary>下界条件及其辅助变量 / Lower condition and its helper variables.</summary>
    public IfFunction<V> LowerCondition { get; }
    /// <summary>上界条件及其辅助变量 / Upper condition and its helper variables.</summary>
    public IfFunction<V> UpperCondition { get; }
    /// <summary>区间条件的结果变量 / Result variable of the interval condition.</summary>
    public BinVar ResultVar { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(X);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );

    private IfInFunction(
        LinearPolynomial<V> x,
        V lower,
        V upper,
        IIntoValue<V> converter,
        IfFunction<V> lowerCondition,
        IfFunction<V> upperCondition,
        string name,
        string? displayName) {
        X = x;
        Lower = lower;
        Upper = upper;
        _converter = converter;
        LowerCondition = lowerCondition;
        UpperCondition = upperCondition;
        _zero = lowerCondition.ResultPolynomial.Constant;
        _one = lowerCondition.ResultPolynomial.Monomials[0].Coefficient;
        _negativeOne = _one.Negate();
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_ifin");
        HelperVariables = Array.AsReadOnly(new IVariableItem[] { ResultVar }
            .Concat(lowerCondition.HelperVariables).Concat(upperCondition.HelperVariables).ToArray());
    }

    /// <summary>校验并创建区间条件 / Validate and create an interval condition.</summary>
    /// <param name="x">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="lower">闭区间下界 / Inclusive lower endpoint.</param>
    /// <param name="upper">闭区间上界 / Inclusive upper endpoint.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="bounds">可选输入范围；指定时强制执行 / Optional input bounds, enforced when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<IfInFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? x,
        V lower,
        V upper,
        IIntoValue<V>? converter,
        V strictBoundary,
        V? delta = null,
        LinearPolynomialBounds<V>? bounds = null,
        string name = "ifIn",
        string? displayName = null) {
        try {
            if (x?.Monomials is null || converter is null ||
                x.Monomials.Any(term => term is null || term.Symbol is null) ||
                !Exact(
                    value: lower,
                    converter: converter,
                    exact: out ExactBoundValue lowerValue
                ) || !Exact(
                    value: upper,
                    converter: converter,
                    exact: out ExactBoundValue upperValue
                ) ||
                lowerValue.CompareTo(upperValue) > 0 ||
                !Subtract(
                    lhs: x.Constant,
                    rhs: lower,
                    converter: converter,
                    result: out V lowerConstant
                ) || !Subtract(
                    lhs: x.Constant,
                    rhs: upper,
                    converter: converter,
                    result: out V upperConstant
                )) {
                return Invalid<IfInFunction<V>>();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(x.Monomials.ToArray()), x.Constant);
            LinearPolynomialBounds<V>? lowerBounds = null;
            LinearPolynomialBounds<V>? upperBounds = null;
            if (bounds is not null) {
                if (!Exact(
                        value: bounds.LowerBound,
                        converter: converter,
                        exact: out ExactBoundValue boundsLower
                    ) || !Exact(
                        value: bounds.UpperBound,
                        converter: converter,
                        exact: out ExactBoundValue boundsUpper
                    ) ||
                    boundsLower.CompareTo(boundsUpper) > 0 ||
                    !Subtract(
                        lhs: bounds.LowerBound,
                        rhs: lower,
                        converter: converter,
                        result: out V lowerMin
                    ) || !Subtract(
                        lhs: bounds.UpperBound,
                        rhs: lower,
                        converter: converter,
                        result: out V lowerMax
                    ) || !Subtract(
                        lhs: bounds.LowerBound,
                        rhs: upper,
                        converter: converter,
                        result: out V upperMin
                    ) || !Subtract(
                        lhs: bounds.UpperBound,
                        rhs: upper,
                        converter: converter,
                        result: out V upperMax
                    )) {
                    return Invalid<IfInFunction<V>>();
                }
                lowerBounds = new LinearPolynomialBounds<V>(lowerMin, lowerMax);
                upperBounds = new LinearPolynomialBounds<V>(upperMin, upperMax);
            }
            return IfFunction<V>.Create(
                condition: new LinearPolynomial<V>(snapshot.Monomials, lowerConstant),
                converter: converter,
                strictBoundary: strictBoundary,
                relation: Comparison.GE,
                delta: delta,
                bounds: lowerBounds,
                name: $"{name}_lower"
            ).Bind(lowerCondition => IfFunction<V>.Create(
                condition: new LinearPolynomial<V>(snapshot.Monomials, upperConstant),
                converter: converter,
                strictBoundary: strictBoundary,
                relation: Comparison.LE,
                delta: delta,
                bounds: upperBounds,
                name: $"{name}_upper"
            ).Bind(upperCondition => lowerCondition.Current(null) && upperCondition.Current(null)
                ? Results.Ok(new IfInFunction<V>(
                    x: snapshot,
                    lower: lower,
                    upper: upper,
                    converter: converter,
                    lowerCondition: lowerCondition,
                    upperCondition: upperCondition,
                    name: name,
                    displayName: displayName
                ))
                : Invalid<IfInFunction<V>>()));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<IfInFunction<V>>();
        }
    }

    /// <summary>按闭区间和严格间隔执行三值判定 / Classify against the closed interval and strict gaps.</summary>
    /// <param name="values">输入符号值 / Input symbol values.</param>
    /// <returns>三值结果；缺失或无效输入返回失败 / Three-valued result, or failure for missing or invalid input.</returns>
    public Result<TruthValue, ErrorCode, Error<ErrorCode>> Classify(IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.ConditionalPolynomial(
            polynomial: X,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!input.IsOk) {
            return input.Map(_ => TruthValue.Undefined);
        }
        return input.Value is { } value
            ? ClassifyValue(
                value: value,
                exact: exact,
                encoded: encoded
            )
            : Invalid<TruthValue>();
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        EvaluateCore(values, null);

    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) =>
        LowerCondition.Current(null) && UpperCondition.Current(null)
            ? EvaluateCore(inputs, modelValues) : FunctionEvaluation.Invalid<V>();

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateCore(
        IReadOnlyDictionary<ISymbol, V> values,
        IReadOnlyDictionary<ISymbol, V>? modelValues) {
        Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.ConditionalPolynomial(
            polynomial: X,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!input.IsOk || input.Value is not { } value) {
            return input;
        }
        Result<(TruthValue Lower, TruthValue Upper), ErrorCode, Error<ErrorCode>> sides = ClassifySides(
            value: value,
            exact: exact,
            encoded: encoded
        );
        if (!sides.IsOk) {
            return sides.Map<V?>(_ => null);
        }
        if (modelValues is not null) {
            Result<bool, ErrorCode, Error<ErrorCode>> lower = LowerCondition.VerifyExpanded(sides.Value.Lower, modelValues);
            if (!lower.IsOk || !lower.Value) {
                return lower.Map<V?>(_ => null);
            }
            Result<bool, ErrorCode, Error<ErrorCode>> upper = UpperCondition.VerifyExpanded(sides.Value.Upper, modelValues);
            if (!upper.IsOk || !upper.Value) {
                return upper.Map<V?>(_ => null);
            }
        }
        return sides.Map<V?>(classification => Combine(classification) switch {
            TruthValue.True => _one,
            TruthValue.False => _zero,
            _ => null,
        });
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        if (tokens is null || !LowerCondition.Current(null) || !UpperCondition.Current(null)) {
            return Invalid<Success>();
        }
        return tokens.Add(HelperVariables);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            return Invalid<Success>();
        }
        try {
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> lower = LowerCondition.PrepareConstraints(model);
            if (!lower.IsOk) {
                return lower.Map(_ => Results.SuccessInstance);
            }
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> upper = UpperCondition.PrepareConstraints(model);
            if (!upper.IsOk) {
                return upper.Map(_ => Results.SuccessInstance);
            }
            IReadOnlyList<LinearInequality<V>> links = LinkConstraints();
            if (!LowerCondition.ValidRows(model, links) || !LowerCondition.Current(model.Tokens) ||
                !UpperCondition.Current(model.Tokens)) {
                return Invalid<Success>();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: lower.Value.Concat(upper.Value).Concat(links),
                afterWrite: () => {
                    if (!LowerCondition.Current(model.Tokens) || !UpperCondition.Current(model.Tokens)) {
                        return Invalid<Success>();
                    }
                    LowerCondition.TrackDependencies(model);
                    UpperCondition.TrackDependencies(model);
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    private Result<TruthValue, ErrorCode, Error<ErrorCode>> ClassifyValue(
        V value,
        ExactBoundValue exact,
        ExactBoundValue encoded) => ClassifySides(
            value: value,
            exact: exact,
            encoded: encoded
        ).Map(Combine);

    private Result<(TruthValue Lower, TruthValue Upper), ErrorCode, Error<ErrorCode>> ClassifySides(
        V value,
        ExactBoundValue exact,
        ExactBoundValue encoded) {
        Result<V?, ErrorCode, Error<ErrorCode>> lower = FunctionEvaluation.Difference(value, Lower);
        Result<V?, ErrorCode, Error<ErrorCode>> upper = FunctionEvaluation.Difference(Upper, value);
        if (!lower.IsOk) {
            return lower.Map(_ => (TruthValue.Undefined, TruthValue.Undefined));
        }
        if (!upper.IsOk) {
            return upper.Map(_ => (TruthValue.Undefined, TruthValue.Undefined));
        }
        if (lower.Value is not { } lowerDifference || upper.Value is not { } upperDifference) {
            return Invalid<(TruthValue, TruthValue)>();
        }
        if (!FunctionEvaluation.TryReadConditionValue(
                value: Lower,
                converter: _converter,
                exact: out ExactBoundValue lowerValue,
                encoded: out ExactBoundValue encodedLower
            ) || lowerValue.CompareTo(encodedLower) != 0 || !FunctionEvaluation.TryReadConditionValue(
                value: Upper,
                converter: _converter,
                exact: out ExactBoundValue upperValue,
                encoded: out ExactBoundValue encodedUpper
            ) || upperValue.CompareTo(encodedUpper) != 0) {
            return Invalid<(TruthValue, TruthValue)>();
        }
        return ConditionClassification.ClassifyChecked(
            difference: lowerDifference,
            exact: exact.Add(lowerValue.Negate()),
            encoded: encoded.Add(encodedLower.Negate()),
            relation: Comparison.GE,
            delta: Delta,
            strictBoundary: StrictBoundary,
            converter: _converter
        ).Bind(first => ConditionClassification.ClassifyChecked(
            difference: upperDifference,
            exact: upperValue.Add(exact.Negate()),
            encoded: encodedUpper.Add(encoded.Negate()),
            relation: Comparison.GE,
            delta: Delta,
            strictBoundary: StrictBoundary,
            converter: _converter
        ).Map(second => (first, second)));
    }

    private static TruthValue Combine((TruthValue Lower, TruthValue Upper) sides) =>
        sides.Lower == TruthValue.False || sides.Upper == TruthValue.False
            ? TruthValue.False
            : sides.Lower == TruthValue.True && sides.Upper == TruthValue.True ? TruthValue.True : TruthValue.Undefined;

    private IReadOnlyList<LinearInequality<V>> LinkConstraints() => new[] {
        IndicatorSupport.Row(
            monomials: new[] { new LinearMonomial<V>(_one, ResultVar), new LinearMonomial<V>(_negativeOne, LowerCondition.ResultVar) },
            constant: _zero,
            comparison: Comparison.LE,
            rhs: _zero,
            name: $"{Name}_link_lower"
        ),
        IndicatorSupport.Row(
            monomials: new[] { new LinearMonomial<V>(_one, ResultVar), new LinearMonomial<V>(_negativeOne, UpperCondition.ResultVar) },
            constant: _zero,
            comparison: Comparison.LE,
            rhs: _zero,
            name: $"{Name}_link_upper"
        ),
        IndicatorSupport.Row(
            monomials: new[] {
                new LinearMonomial<V>(_one, ResultVar),
                new LinearMonomial<V>(_negativeOne, LowerCondition.ResultVar),
                new LinearMonomial<V>(_negativeOne, UpperCondition.ResultVar),
            },
            constant: _zero,
            comparison: Comparison.GE,
            rhs: _negativeOne,
            name: $"{Name}_link_both"
        ),
    };

    private static bool Exact(V value, IIntoValue<V> converter, out ExactBoundValue exact) =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out exact
        ) &&
        ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;

    private static bool Subtract(V lhs, V rhs, IIntoValue<V> converter, out V result) {
        result = default;
        return Exact(
                value: lhs,
                converter: converter,
                exact: out ExactBoundValue first
            ) && Exact(
                value: rhs,
                converter: converter,
                exact: out ExactBoundValue second
            ) &&
            LinearFlattenExtensions.Arithmetic<V>.TrySubtract(
                lhs: lhs,
                rhs: rhs,
                result: out result
            ) && Exact(
                value: result,
                converter: converter,
                exact: out ExactBoundValue difference
            ) &&
            first.Add(second.Negate()).CompareTo(difference) == 0;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>() => Results.Failed<T>(
        new Err<ErrorCode>(ErrorCode.IllegalArgument,
            "区间条件输入、范围或注册状态无效。 / Invalid interval condition, bounds or registration state.")
    );
}
