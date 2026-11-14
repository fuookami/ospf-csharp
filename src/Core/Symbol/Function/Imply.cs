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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>前件成立时强制后件成立 / Enforce the consequent when the antecedent holds.</summary>
/// <remarks>
/// 可行模型中的结果恒为一；独立求值仍可返回假或未定义。显式范围在前件为假时也会被强制执行。
/// The result is one in every feasible model; standalone evaluation can be false or undefined.
/// Explicit bounds are enforced even when the antecedent is false.
/// </remarks>
/// <typeparam name="V">输入和系数的数值类型 / Numeric type of inputs and coefficients.</typeparam>
public sealed class ImplyFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly V _zero;
    private readonly V _one;

    internal IfFunction<V> AntecedentEncoding { get; }
    internal IfFunction<V> ConsequentEncoding { get; }
    /// <summary>前件多项式的浅快照 / Shallow snapshot of the antecedent polynomial.</summary>
    public LinearPolynomial<V> Antecedent => AntecedentEncoding.Condition;
    /// <summary>后件多项式的浅快照 / Shallow snapshot of the consequent polynomial.</summary>
    public LinearPolynomial<V> Consequent => ConsequentEncoding.Condition;
    /// <summary>前后件与零的比较关系 / Relation comparing both operands to zero.</summary>
    public Comparison Relation => AntecedentEncoding.Relation;
    /// <summary>真假分支之间的间隔 / Gap between true and false branches.</summary>
    public V StrictBoundary => AntecedentEncoding.StrictBoundary;
    /// <summary>离散步长 / Discrete step.</summary>
    public V Delta => AntecedentEncoding.Delta;
    /// <summary>被强制的前件范围 / Enforced antecedent bounds.</summary>
    public LinearPolynomialBounds<V> AntecedentBounds => AntecedentEncoding.Bounds;
    /// <summary>被强制的后件范围 / Enforced consequent bounds.</summary>
    public LinearPolynomialBounds<V> ConsequentBounds => ConsequentEncoding.Bounds;
    /// <summary>前件真分支指示变量 / Antecedent true-branch indicator.</summary>
    public BinVar AntecedentIndicatorVar => AntecedentEncoding.ResultVar;
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { Antecedent, Consequent });
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(Array.Empty<LinearMonomial<V>>(), _one);

    private ImplyFunction(
        IfFunction<V> antecedent,
        IfFunction<V> consequent,
        IIntoValue<V> converter,
        string name,
        string? displayName) {
        AntecedentEncoding = antecedent;
        ConsequentEncoding = consequent;
        _converter = converter;
        _zero = antecedent.ResultPolynomial.Constant;
        _one = antecedent.ResultPolynomial.Monomials[0].Coefficient;
        Name = name;
        DisplayName = displayName;
        HelperVariables = Array.AsReadOnly(antecedent.HelperVariables.Concat(new[] { consequent.ConditionVar }).ToArray());
    }

    /// <summary>校验并创建蕴含约束函数 / Validate and create an implication constraint function.</summary>
    /// <param name="antecedent">前件线性多项式 / Antecedent linear polynomial.</param>
    /// <param name="consequent">后件线性多项式 / Consequent linear polynomial.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="antecedentBounds">可选前件范围；指定时强制执行 / Optional antecedent bounds, enforced when supplied.</param>
    /// <param name="consequentBounds">可选后件范围；指定时强制执行 / Optional consequent bounds, enforced when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<ImplyFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? antecedent,
        LinearPolynomial<V>? consequent,
        IIntoValue<V>? converter,
        V strictBoundary,
        Comparison relation = Comparison.GT,
        V? delta = null,
        LinearPolynomialBounds<V>? antecedentBounds = null,
        LinearPolynomialBounds<V>? consequentBounds = null,
        string name = "imply",
        string? displayName = null) {
        try {
            if (converter is null) {
                return Invalid<ImplyFunction<V>>();
            }
            Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> first = IfFunction<V>.Create(
                condition: antecedent,
                converter: converter,
                strictBoundary: strictBoundary,
                relation: relation,
                delta: delta,
                bounds: antecedentBounds,
                name: $"{name}_antecedent"
            );
            if (!first.IsOk) {
                return first.Bind(_ => Invalid<ImplyFunction<V>>());
            }
            Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> second = IfFunction<V>.Create(
                condition: consequent,
                converter: converter,
                strictBoundary: strictBoundary,
                relation: relation,
                delta: delta,
                bounds: consequentBounds,
                name: $"{name}_consequent"
            );
            if (!second.IsOk) {
                return second.Bind(_ => Invalid<ImplyFunction<V>>());
            }
            return first.Value.Current(null) && second.Value.Current(null) && first.Value.Current(null)
                ? Results.Ok(new ImplyFunction<V>(
                    antecedent: first.Value,
                    consequent: second.Value,
                    converter: converter,
                    name: name,
                    displayName: displayName
                ))
                : Invalid<ImplyFunction<V>>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<ImplyFunction<V>>();
        }
    }

    /// <summary>按短路三值逻辑判定蕴含 / Classify implication with short-circuit three-valued logic.</summary>
    /// <param name="values">输入符号值 / Input symbol values.</param>
    /// <returns>三值结果；必要输入缺失或无效时返回失败 / Three-valued result, or failure for missing or invalid required inputs.</returns>
    public Result<TruthValue, ErrorCode, Error<ErrorCode>> Classify(IReadOnlyDictionary<ISymbol, V> values) {
        Result<TruthValue, ErrorCode, Error<ErrorCode>> first = ClassifyCondition(Antecedent, values);
        if (!first.IsOk || first.Value == TruthValue.Undefined) {
            return first;
        }
        return first.Value == TruthValue.False ? Results.Ok(TruthValue.True) : ClassifyCondition(Consequent, values);
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

    private Result<TruthValue, ErrorCode, Error<ErrorCode>> ClassifyCondition(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.ConditionalPolynomial(
            polynomial: polynomial,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!input.IsOk) {
            return input.Map(_ => TruthValue.Undefined);
        }
        return input.Value is { } value ? ConditionClassification.ClassifyChecked(
            difference: value,
            exact: exact,
            encoded: encoded,
            relation: Relation,
            delta: Delta,
            strictBoundary: StrictBoundary,
            converter: _converter
        ) : Invalid<TruthValue>();
    }

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateCore(
        IReadOnlyDictionary<ISymbol, V> values,
        IReadOnlyDictionary<ISymbol, V>? modelValues) {
        Result<V?, ErrorCode, Error<ErrorCode>> first = modelValues is null
            ? AntecedentEncoding.TryEvaluate(values)
            : ((IModelAwareFunctionPreparation<V>)AntecedentEncoding).TryPrepare(values, modelValues);
        if (!first.IsOk || first.Value is not { } antecedent) {
            return first;
        }
        if (antecedent.Eq(_zero)) {
            return Results.Ok<V?>(_one);
        }
        Result<V?, ErrorCode, Error<ErrorCode>> consequent = modelValues is null
            ? ConsequentEncoding.TryEvaluate(values)
            : ((IModelAwareFunctionPreparation<V>)ConsequentEncoding).TryPrepare(values, modelValues);
        return modelValues is not null && consequent.IsOk && consequent.Value is { } value && value.Eq(_zero)
            ? Results.Ok<V?>(null) : consequent;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        try {
            // shortcut: 任意令牌集合不保证批量原子；实现通用事务后移除 / Arbitrary token batches may not be atomic; remove after transactional registration.
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
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> first = AntecedentEncoding.PrepareConstraints(model);
            if (!first.IsOk) {
                return first.Map(_ => Results.SuccessInstance);
            }
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> second = ConsequentEncoding.PrepareGuardedTrueConstraints(
                model: model,
                inactive: AntecedentEncoding.FalseVar
            );
            if (!second.IsOk) {
                return second.Map(_ => Results.SuccessInstance);
            }
            if (!Current(model.Tokens)) {
                return Invalid<Success>();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: first.Value.Concat(second.Value),
                afterWrite: () => {
                    if (!Current(model.Tokens)) {
                        return Invalid<Success>();
                    }
                    AntecedentEncoding.TrackDependencies(model);
                    ConsequentEncoding.TrackDependencies(model);
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    private bool Current(IAbstractTokenTable<V>? tokens) => AntecedentEncoding.Current(tokens) &&
        ConsequentEncoding.Current(tokens) && AntecedentEncoding.Current(tokens);

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>() => Results.Failed<T>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "蕴含输入、范围或注册状态无效。 / Invalid implication input, bounds or registration state."
    ));
}
