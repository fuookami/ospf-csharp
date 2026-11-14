#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>比较关系的二值阶跃函数，而非 Logistic 曲线 / Binary relation step function, not a logistic curve.</summary>
/// <remarks>
/// 使用与 If 相同的范围、格点证明、注册事务和展开值校验；显式范围会被强制执行。
/// Uses the same bounds, lattice proof, registration transaction and expanded-value checks as If; explicit bounds are enforced.
/// 直接求值不验证嵌套来源或求解器容差。 / Direct evaluation does not verify nested provenance or solver tolerances.
/// </remarks>
/// <typeparam name="V">输入和系数的数值类型 / Numeric type of inputs and coefficients.</typeparam>
public sealed class SigmoidFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IfFunction<V> _condition;
    private readonly IIntoValue<V> _converter;

    /// <summary>条件多项式的浅快照 / Shallow snapshot of the condition polynomial.</summary>
    public LinearPolynomial<V> Condition => _condition.Condition;
    /// <summary>条件关系 / Condition relation.</summary>
    public Comparison Relation => _condition.Relation;
    /// <summary>真假分支之间的间隔 / Gap between true and false branches.</summary>
    public V StrictBoundary => _condition.StrictBoundary;
    /// <summary>离散步长 / Discrete step.</summary>
    public V Delta => _condition.Delta;
    /// <summary>被强制的条件范围 / Enforced condition bounds.</summary>
    public LinearPolynomialBounds<V> Bounds => _condition.Bounds;
    /// <summary>二值结果变量 / Binary result variable.</summary>
    public BinVar IndicatorVar => _condition.ResultVar;
    /// <inheritdoc/>
    public string Name { get => _condition.Name; set => _condition.Name = value; }
    /// <inheritdoc/>
    public string? DisplayName { get => _condition.DisplayName; set => _condition.DisplayName = value; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _condition.HelperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => _condition.InputSymbols;
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _condition.ResultPolynomial;

    private SigmoidFunction(IfFunction<V> condition, IIntoValue<V> converter) {
        _condition = condition;
        _converter = converter;
    }

    /// <summary>校验并创建阶跃函数 / Validate and create a step function.</summary>
    /// <param name="condition">与零比较的线性多项式 / Linear polynomial compared with zero.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="bounds">可选范围；指定时约束条件取值 / Optional bounds, enforced on the condition when supplied.</param>
    /// <param name="bigM">仅验证的旧参数；不能替代范围 / Legacy parameter validated but not used in place of bounds.</param>
    /// <param name="tolerance">未指定间隔时的旧默认值 / Legacy default when no gap is specified.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<SigmoidFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? condition,
        IIntoValue<V>? converter,
        V? strictBoundary = null,
        Comparison relation = Comparison.GT,
        V? delta = null,
        LinearPolynomialBounds<V>? bounds = null,
        V? bigM = null,
        V? tolerance = null,
        string name = "sigmoid",
        string? displayName = null) {
        try {
            if (converter is null || bigM is { } value &&
                (!ExactBoundValue.TryRead(value, converter, out ExactBoundValue checkedM) || checkedM.Sign <= 0)) {
                return Invalid();
            }
            V gap = strictBoundary ?? tolerance ?? converter.IntoValue(IndicatorSupport.DefaultTolerance);
            return IfFunction<V>.Create(
                condition: condition,
                converter: converter,
                strictBoundary: gap,
                relation: relation,
                delta: delta,
                bounds: bounds,
                name: name,
                displayName: displayName
            ).Map(function => new SigmoidFunction<V>(function, converter));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid();
        }
    }

    /// <summary>按关系和严格间隔执行三值判定 / Classify the relation using the strict gap.</summary>
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
        return input.Value is { } difference
            ? ConditionClassification.ClassifyChecked(
                difference: difference,
                exact: exact,
                encoded: encoded,
                relation: Relation,
                delta: Delta,
                strictBoundary: StrictBoundary,
                converter: _converter
            )
            : FunctionEvaluation.Invalid<V>().Map(_ => TruthValue.Undefined);
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _condition.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _condition.TryEvaluate(values);

    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) =>
        ((IModelAwareFunctionPreparation<V>)_condition).TryPrepare(inputs, modelValues);

    internal bool ProvesResult(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => _condition.ProvesResult(
            result: result,
            inputs: inputs,
            modelValues: modelValues
        );

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => _condition.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => _condition.RegisterConstraints(model);

    private static Result<SigmoidFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() => Results.Failed<SigmoidFunction<V>>(
        new Err<ErrorCode>(ErrorCode.IllegalArgument,
            "阶跃条件或参数无效。 / Invalid step condition or parameter.")
    );
}
