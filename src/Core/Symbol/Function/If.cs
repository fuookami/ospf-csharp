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
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>有界条件的二值结果 / Binary result of a bounded condition.</summary>
/// <remarks>
/// 支持可精确展开的线性表达式。显式范围会作为约束强制执行。
/// Supports exactly expandable linear expressions. Explicit bounds are enforced as constraints.
/// 准备缓存时核对可验证的展开值；直接求值不验证嵌套来源，且不校验求解器容差。
/// Cache preparation checks provable expanded values; direct evaluation does not verify nested provenance or solver tolerances.
/// </remarks>
/// <typeparam name="V">输入和系数的数值类型 / Numeric type of inputs and coefficients.</typeparam>
public sealed class IfFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly LinearPolynomial<V> _expanded;
    private readonly BoundDependencySnapshot<V>? _dependencies;
    private readonly V _zero;
    private readonly V _one;
    private readonly V _negativeOne;
    private readonly V _negativeGap;
    private readonly V _lowerRelaxation;
    private readonly V _upperRelaxation;

    /// <summary>条件多项式的浅快照 / Shallow snapshot of the condition polynomial.</summary>
    public LinearPolynomial<V> Condition { get; }
    /// <summary>条件关系 / Condition relation.</summary>
    public Comparison Relation { get; }
    /// <summary>真假分支之间的间隔 / Gap between true and false branches.</summary>
    public V StrictBoundary { get; }
    /// <summary>离散步长 / Discrete step.</summary>
    public V Delta { get; }
    /// <summary>被强制的条件范围 / Enforced condition bounds.</summary>
    public LinearPolynomialBounds<V> Bounds { get; }
    /// <summary>真分支标志及函数结果 / True-branch flag and function result.</summary>
    public BinVar ResultVar { get; }
    /// <summary>假分支标志 / False-branch flag.</summary>
    public BinVar FalseVar { get; }
    /// <summary>条件差值辅助变量 / Auxiliary condition-value variable.</summary>
    public RealVar ConditionVar { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Condition);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );

    private IfFunction(
        LinearPolynomial<V> condition,
        LinearPolynomial<V> expanded,
        IIntoValue<V> converter,
        Comparison relation,
        V strictBoundary,
        V delta,
        LinearPolynomialBounds<V> bounds,
        BoundDependencySnapshot<V>? dependencies,
        V zero,
        V one,
        V negativeOne,
        V negativeGap,
        V lowerRelaxation,
        V upperRelaxation,
        string name,
        string? displayName) {
        Condition = condition;
        _expanded = expanded;
        _converter = converter;
        Relation = relation;
        StrictBoundary = strictBoundary;
        Delta = delta;
        Bounds = bounds;
        _dependencies = dependencies;
        _zero = zero;
        _one = one;
        _negativeOne = negativeOne;
        _negativeGap = negativeGap;
        _lowerRelaxation = lowerRelaxation;
        _upperRelaxation = upperRelaxation;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_if");
        FalseVar = new BinVar($"{name}_if_false");
        ConditionVar = new RealVar($"{name}_condition");
        ConditionVar.Range.SetRange(ValueRange<Flt64>.Full());
        HelperVariables = Array.AsReadOnly<IVariableItem>(new IVariableItem[] { ResultVar, FalseVar, ConditionVar });
    }

    /// <summary>校验并创建条件函数 / Validate and create a condition function.</summary>
    /// <param name="condition">与零比较的线性多项式 / Linear polynomial compared with zero.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="bounds">可选范围；指定时约束条件取值 / Optional bounds, enforced on the condition when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? condition,
        IIntoValue<V>? converter,
        V strictBoundary,
        Comparison relation = Comparison.GT,
        V? delta = null,
        LinearPolynomialBounds<V>? bounds = null,
        string name = "if",
        string? displayName = null) {
        try {
            if (condition?.Monomials is null || converter is null ||
                condition.Monomials.Any(term => term is null || term.Symbol is null) ||
                relation is not (Comparison.GT or Comparison.GE or Comparison.LT or Comparison.LE)) {
                return Invalid();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(condition.Monomials.ToArray()), condition.Constant);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expandedResult =
                new SymbolFlatten.LinearFlattenData<V>(snapshot.Monomials, snapshot.Constant).ExpandIntermediateSymbols();
            if (!expandedResult.IsOk) {
                return Invalid();
            }
            var expanded = new LinearPolynomial<V>(
                Array.AsReadOnly(expandedResult.Value.Monomials.ToArray()), expandedResult.Value.Constant
            );
            BoundDependencySnapshot<V>? captured = bounds is null ? new BoundDependencySnapshot<V>(new[] { expanded }) : null;
            V zero = converter.Zero;
            V one = converter.One;
            V step = delta ?? strictBoundary;
            if (captured is not null && !captured.IsCurrent() ||
                !Exact(
                    value: zero,
                    converter: converter,
                    exact: out ExactBoundValue zeroExact
                ) || zeroExact.Sign != 0 ||
                !Exact(
                    value: one,
                    converter: converter,
                    exact: out ExactBoundValue oneExact
                ) ||
                oneExact.CompareTo(new ExactBoundValue(BigInteger.One, BigInteger.One)) != 0 ||
                !Exact(
                    value: strictBoundary,
                    converter: converter,
                    exact: out ExactBoundValue gap
                ) || gap.Sign <= 0 ||
                !Exact(
                    value: step,
                    converter: converter,
                    exact: out ExactBoundValue stepExact
                ) || stepExact.CompareTo(gap) < 0 ||
                !TryNegate(
                    value: one,
                    converter: converter,
                    result: out V negativeOne
                ) ||
                !TryNegate(
                    value: strictBoundary,
                    converter: converter,
                    result: out V negativeGap
                ) ||
                !ExactLinearExpansion.Matches(
                    input: snapshot,
                    expanded: expanded,
                    converter: converter
                ) || !CanMerge(expanded, converter)) {
                return Invalid();
            }
            LinearPolynomialBounds<V>? resolved = bounds ?? BigMValues.FiniteBounds(expanded, converter);
            if (resolved is null ||
                !Exact(
                    value: resolved.LowerBound,
                    converter: converter,
                    exact: out ExactBoundValue lower
                ) ||
                !Exact(
                    value: resolved.UpperBound,
                    converter: converter,
                    exact: out ExactBoundValue upper
                ) ||
                lower.CompareTo(upper) > 0 ||
                !TryNegate(
                    value: resolved.LowerBound,
                    converter: converter,
                    result: out _
                ) ||
                !TryNegate(
                    value: resolved.UpperBound,
                    converter: converter,
                    result: out _
                )) {
                return Invalid();
            }
            bool reverse = relation is Comparison.LT or Comparison.LE;
            ExactBoundValue orientedLower = reverse ? upper.Negate() : lower;
            ExactBoundValue orientedUpper = reverse ? lower.Negate() : upper;
            bool strict = relation is Comparison.GT or Comparison.LT;
            ExactBoundValue trueLower = strict ? gap : ExactBoundValue.Zero;
            ExactBoundValue falseUpper = strict ? ExactBoundValue.Zero : gap.Negate();
            if (orientedUpper.CompareTo(trueLower) < 0 && orientedLower.CompareTo(falseUpper) > 0) {
                return Invalid();
            }
            bool fixedBranch = orientedLower.CompareTo(trueLower) >= 0 || orientedUpper.CompareTo(falseUpper) <= 0;
            if (!fixedBranch && !SafeLattice(
                    expanded: expanded,
                    relation: relation,
                    gap: gap,
                    step: stepExact,
                    converter: converter
                )) {
                return Invalid();
            }
            ExactBoundValue lowerRelaxation = trueLower.Add(orientedLower.Negate());
            ExactBoundValue upperRelaxation = orientedUpper.Add(falseUpper.Negate());
            lowerRelaxation = lowerRelaxation.Sign < 0 ? ExactBoundValue.Zero : lowerRelaxation;
            upperRelaxation = upperRelaxation.Sign < 0 ? ExactBoundValue.Zero : upperRelaxation;
            if (!lowerRelaxation.TryRoundSolverUpper(converter, out V lowerM) ||
                !upperRelaxation.TryRoundSolverUpper(converter, out V upperM) ||
                !Exact(
                    value: lowerM,
                    converter: converter,
                    exact: out _
                ) || !Exact(
                    value: upperM,
                    converter: converter,
                    exact: out _
                ) ||
                !TryNegate(
                    value: upperM,
                    converter: converter,
                    result: out _
                ) ||
                captured is not null && !captured.IsCurrent()) {
                return Invalid();
            }
            return Results.Ok(new IfFunction<V>(
                condition: snapshot,
                expanded: expanded,
                converter: converter,
                relation: relation,
                strictBoundary: strictBoundary,
                delta: step,
                bounds: resolved,
                dependencies: captured,
                zero: zero,
                one: one,
                negativeOne: negativeOne,
                negativeGap: negativeGap,
                lowerRelaxation: lowerM,
                upperRelaxation: upperM,
                name: name,
                displayName: displayName
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid();
        }
    }

    /// <summary>从归一化约束输入创建条件适配器 / Create a condition adapter from a normalized constraint input.</summary>
    /// <remarks>
    /// 使用输入的扁平表达式和比较关系；不会使用左侧范围或右侧常量元数据，也无法还原扁平化前的舍入损失。
    /// Uses the flattened expression and relation; left-hand range and right-hand constant metadata are ignored,
    /// and rounding lost during prior flattening cannot be recovered.
    /// </remarks>
    /// <param name="inequality">已归一化的约束输入 / Normalized constraint input.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="strictBoundary">正的真假分支间隔 / Positive gap between true and false branches.</param>
    /// <param name="delta">离散步长，默认等于间隔 / Discrete step, defaulting to the gap.</param>
    /// <param name="bounds">可选范围；指定时约束条件取值 / Optional bounds, enforced on the condition when supplied.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数适配器或结构化失败 / Function adapter or structured failure.</returns>
    public static Result<LinearFunctionSymbolAdapter<V>, ErrorCode, Error<ErrorCode>> From(
        LinearConstraintInput<V>? inequality,
        IIntoValue<V>? converter,
        V strictBoundary,
        V? delta = null,
        LinearPolynomialBounds<V>? bounds = null,
        string name = "if",
        string? displayName = null) {
        LinearPolynomial<V>? condition = inequality?.FlattenData is { } flattened
            ? new LinearPolynomial<V>(flattened.Monomials, flattened.Constant)
            : null;
        return Create(
            condition: condition,
            converter: converter,
            strictBoundary: strictBoundary,
            relation: inequality?.Sign ?? Comparison.GT,
            delta: delta,
            bounds: bounds,
            name: name,
            displayName: displayName
        ).Map(function => new LinearFunctionSymbolAdapter<V>(function));
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
        Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.ConditionalPolynomial(
            polynomial: Condition,
            values: values,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!input.IsOk || input.Value is not { } difference) {
            return input;
        }
        Result<TruthValue, ErrorCode, Error<ErrorCode>> classification = ConditionClassification.ClassifyChecked(
            difference: difference,
            exact: exact,
            encoded: encoded,
            relation: Relation,
            delta: Delta,
            strictBoundary: StrictBoundary,
            converter: _converter
        );
        if (!classification.IsOk) {
            return classification.Map<V?>(_ => null);
        }
        if (modelValues is not null) {
            Result<bool, ErrorCode, Error<ErrorCode>> proof = VerifyExpanded(classification.Value, modelValues);
            if (!proof.IsOk || !proof.Value) {
                return proof.Map<V?>(_ => null);
            }
        }
        return classification.Map<V?>(value => value switch {
            TruthValue.True => _one,
            TruthValue.False => _zero,
            _ => null,
        });
    }

    internal Result<bool, ErrorCode, Error<ErrorCode>> VerifyExpanded(
        TruthValue expected,
        IReadOnlyDictionary<ISymbol, V> modelValues) {
        Result<bool, ErrorCode, Error<ErrorCode>> read = FunctionEvaluation.ExactPolynomial(
            polynomial: _expanded,
            values: modelValues,
            converter: _converter,
            exact: out ExactBoundValue exact,
            encoded: out ExactBoundValue encoded
        );
        if (!read.IsOk || !read.Value) {
            return read;
        }
        return ConditionClassification.VerifyExpanded(
            expected: expected,
            exact: exact,
            encoded: encoded,
            relation: Relation,
            strictBoundary: StrictBoundary,
            converter: _converter
        ).Map(_ => true);
    }

    internal bool ProvesResult(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) {
        try {
            if (!Current(null) || _expanded.Monomials.Any(term => term.Symbol is IVariableItem variable &&
                    HelperVariables.Any(helper => helper.Key == variable.Key)) ||
                !Exact(
                    value: _zero,
                    converter: _converter,
                    exact: out _
                ) || !Exact(
                    value: _one,
                    converter: _converter,
                    exact: out _
                ) || !Exact(
                    value: _negativeOne,
                    converter: _converter,
                    exact: out _
                ) || !Exact(
                    value: _negativeGap,
                    converter: _converter,
                    exact: out _
                ) || !Exact(
                    value: _lowerRelaxation,
                    converter: _converter,
                    exact: out _
                ) || !TryNegate(
                    value: _upperRelaxation,
                    converter: _converter,
                    result: out _
                ) ||
                !Exact(
                    value: result,
                    converter: _converter,
                    exact: out ExactBoundValue actual
                ) || !Exact(
                    value: Bounds.LowerBound,
                    converter: _converter,
                    exact: out ExactBoundValue lower
                ) || !Exact(
                    value: Bounds.UpperBound,
                    converter: _converter,
                    exact: out ExactBoundValue upper
                )) {
                return false;
            }
            Result<V?, ErrorCode, Error<ErrorCode>> direct = FunctionEvaluation.ConditionalPolynomial(
                polynomial: Condition,
                values: inputs,
                converter: _converter,
                exact: out ExactBoundValue directExact,
                encoded: out ExactBoundValue directEncoded
            );
            if (!direct.IsOk || direct.Value is not { } difference) {
                return false;
            }
            Result<TruthValue, ErrorCode, Error<ErrorCode>> classified = ConditionClassification.ClassifyChecked(
                difference: difference,
                exact: directExact,
                encoded: directEncoded,
                relation: Relation,
                delta: Delta,
                strictBoundary: StrictBoundary,
                converter: _converter
            );
            if (!classified.IsOk || classified.Value == TruthValue.Undefined) {
                return false;
            }
            ExactBoundValue expected = classified.Value == TruthValue.True
                ? new ExactBoundValue(BigInteger.One, BigInteger.One)
                : ExactBoundValue.Zero;
            Result<bool, ErrorCode, Error<ErrorCode>> expanded = FunctionEvaluation.ExactPolynomial(
                polynomial: _expanded,
                values: modelValues,
                converter: _converter,
                exact: out ExactBoundValue exact,
                encoded: out ExactBoundValue encoded
            );
            if (actual.CompareTo(expected) != 0 || !expanded.IsOk || !expanded.Value ||
                !ConditionClassification.VerifyExpanded(
                    expected: classified.Value,
                    exact: exact,
                    encoded: encoded,
                    relation: Relation,
                    strictBoundary: StrictBoundary,
                    converter: _converter
                ).IsOk ||
                directExact.CompareTo(lower) < 0 || directExact.CompareTo(upper) > 0 ||
                directEncoded.CompareTo(lower) < 0 || directEncoded.CompareTo(upper) > 0 ||
                exact.CompareTo(lower) < 0 || exact.CompareTo(upper) > 0 ||
                encoded.CompareTo(lower) < 0 || encoded.CompareTo(upper) > 0) {
                return false;
            }
            ExactBoundValue complement = classified.Value == TruthValue.True
                ? ExactBoundValue.Zero
                : new ExactBoundValue(BigInteger.One, BigInteger.One);
            return MatchesKnownHelper(
                    helper: ResultVar,
                    natural: expected,
                    solver: expected
                ) && MatchesKnownHelper(
                    helper: FalseVar,
                    natural: complement,
                    solver: complement
                ) && MatchesKnownHelper(
                    helper: ConditionVar,
                    natural: exact,
                    solver: encoded
                ) && Current(null);

            bool MatchesKnownHelper(IVariableItem helper, ExactBoundValue natural, ExactBoundValue solver) =>
                !modelValues.TryGetValue(helper, out V value) ||
                FunctionEvaluation.TryReadConditionValue(
                    value: value,
                    converter: _converter,
                    exact: out ExactBoundValue known,
                    encoded: out ExactBoundValue knownEncoded
                ) && known.CompareTo(natural) == 0 && knownEncoded.CompareTo(solver) == 0;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        if (tokens is null || !Current(null)) {
            return InvalidRegistration();
        }
        return tokens.Add(HelperVariables);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (model is null) {
            return InvalidRegistration();
        }
        try {
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> prepared = PrepareConstraints(model);
            if (!prepared.IsOk) {
                return prepared.Map(_ => Results.SuccessInstance);
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: prepared.Value,
                afterWrite: () => {
                    if (!Current(model.Tokens)) {
                        return InvalidRegistration();
                    }
                    TrackDependencies(model);
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return InvalidRegistration();
        }
    }

    internal Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> PrepareConstraints(
        IAbstractLinearMechanismModel<V> model) {
        try {
            if (!Current(model.Tokens)) {
                return InvalidRegistration().Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
            }
            IReadOnlyList<LinearInequality<V>> rows = Constraints();
            if (ValidRows(model, rows) && Current(model.Tokens)) {
                return Results.Ok(rows);
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        return InvalidRegistration().Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
    }

    internal Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> PrepareGuardedTrueConstraints(
        IAbstractLinearMechanismModel<V> model,
        BinVar inactive) {
        try {
            if (Current(model.Tokens)) {
                IReadOnlyList<LinearInequality<V>> all = Constraints();
                // 仅复用定义和范围，后件真假分类不能无条件启用 / Keep definition and bounds; do not classify an inactive consequent.
                IReadOnlyList<LinearInequality<V>> rows = new[] {
                    all[0], all[1], all[2], TrueConstraint(inactive),
                };
                if (ValidRows(model, rows) && Current(model.Tokens)) {
                    return Results.Ok(rows);
                }
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        return InvalidRegistration().Map<IReadOnlyList<LinearInequality<V>>>(_ => Array.Empty<LinearInequality<V>>());
    }

    internal bool ValidRows(IAbstractLinearMechanismModel<V> model, IReadOnlyList<LinearInequality<V>> rows) {
        var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
        foreach (LinearInequality<V> row in rows) {
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                row.ToLinearFlattenData(model.Tokens);
            if (!flattened.IsOk || !MatchesRow(row, flattened.Value) ||
                flattened.Value.Monomials.Any(term => term.Symbol is not IVariableItem variable ||
                    !registered.Contains(variable.Key) || !Exact(
                        value: term.Coefficient,
                        converter: _converter,
                        exact: out _
                    )) ||
                !Exact(
                    value: flattened.Value.Constant,
                    converter: _converter,
                    exact: out _
                )) {
                return false;
            }
        }
        return true;
    }

    internal void TrackDependencies(IAbstractLinearMechanismModel<V> model) {
        if (_dependencies is not null && model is BasicMechanismModel<V> mechanism) {
            mechanism.TrackBoundDependencies(_dependencies);
        }
    }

    internal bool Current(IAbstractTokenTable<V>? tokens) {
        try {
            if (_dependencies is not null && !_dependencies.IsCurrent()) {
                return false;
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
                new SymbolFlatten.LinearFlattenData<V>(Condition.Monomials, Condition.Constant).ExpandIntermediateSymbols(tokens);
            if (!expanded.IsOk || expanded.Value.Monomials.Count != _expanded.Monomials.Count ||
                !ExactLinearExpansion.Matches(
                    input: Condition,
                    expanded: new LinearPolynomial<V>(expanded.Value.Monomials, expanded.Value.Constant),
                    converter: _converter
                ) ||
                !StructuralValueEquality<V>.Same(expanded.Value.Constant, _expanded.Constant)) {
                return false;
            }
            for (int index = 0; index < _expanded.Monomials.Count; ++index) {
                LinearMonomial<V> original = _expanded.Monomials[index];
                LinearMonomial<V> current = expanded.Value.Monomials[index];
                if (original.Symbol is not IVariableItem first || current.Symbol is not IVariableItem second ||
                    first.Key != second.Key || !StructuralValueEquality<V>.Same(original.Coefficient, current.Coefficient)) {
                    return false;
                }
            }
            return _dependencies is null || _dependencies.IsCurrent();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private IReadOnlyList<LinearInequality<V>> Constraints() {
        bool reverse = Relation is Comparison.LT or Comparison.LE;
        bool strict = Relation is Comparison.GT or Comparison.LT;
        var definition = new List<LinearMonomial<V>> { new(_one, ConditionVar) };
        definition.AddRange(_expanded.Monomials.Select(term =>
            new LinearMonomial<V>(term.Coefficient.Negate(), term.Symbol)));
        V direction = reverse ? _negativeOne : _one;
        return new[] {
            IndicatorSupport.Row(
                monomials: definition,
                constant: _expanded.Constant.Negate(),
                comparison: Comparison.EQ,
                rhs: _zero,
                name: $"{Name}_condition"
            ),
            IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, ConditionVar) },
                constant: _zero,
                comparison: Comparison.GE,
                rhs: Bounds.LowerBound,
                name: $"{Name}_bound_lower"
            ),
            IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, ConditionVar) },
                constant: _zero,
                comparison: Comparison.LE,
                rhs: Bounds.UpperBound,
                name: $"{Name}_bound_upper"
            ),
            IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, ResultVar), new LinearMonomial<V>(_one, FalseVar) },
                constant: _zero,
                comparison: Comparison.EQ,
                rhs: _one,
                name: $"{Name}_one_hot"
            ),
            TrueConstraint(FalseVar),
            IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(direction, ConditionVar), new LinearMonomial<V>(_upperRelaxation.Negate(), ResultVar) },
                constant: _zero,
                comparison: Comparison.LE,
                rhs: strict ? _zero : _negativeGap,
                name: $"{Name}_false"
            ),
        };
    }

    private LinearInequality<V> TrueConstraint(BinVar inactive) => IndicatorSupport.Row(
        monomials: new[] {
            new LinearMonomial<V>(Relation is Comparison.LT or Comparison.LE ? _negativeOne : _one, ConditionVar),
            new LinearMonomial<V>(_lowerRelaxation, inactive),
        },
        constant: _zero,
        comparison: Comparison.GE,
        rhs: Relation is Comparison.GT or Comparison.LT ? StrictBoundary : _zero,
        name: $"{Name}_true"
    );

    private static bool SafeLattice(
        LinearPolynomial<V> expanded,
        Comparison relation,
        ExactBoundValue gap,
        ExactBoundValue step,
        IIntoValue<V> converter) {
        var coefficients = new Dictionary<VariableItemKey, (IVariableItem Variable, ExactBoundValue Coefficient)>();
        foreach (LinearMonomial<V> term in expanded.Monomials) {
            if (term.Symbol is not IVariableItem variable || !Exact(
                value: term.Coefficient,
                converter: converter,
                exact: out ExactBoundValue coefficient
            )) {
                return false;
            }
            ExactBoundValue previous = coefficients.TryGetValue(variable.Key, out (IVariableItem Variable, ExactBoundValue Coefficient) item)
                ? item.Coefficient : ExactBoundValue.Zero;
            coefficients[variable.Key] = (variable, previous.Add(coefficient));
        }
        (IVariableItem Variable, ExactBoundValue Coefficient)[] effective = coefficients.Values.Where(item => item.Coefficient.Sign != 0).ToArray();
        if (effective.Any(item => !item.Variable.TypeKind.IsIntegerType)) {
            return step.CompareTo(gap) == 0;
        }
        if (effective.Any(item => item.Coefficient.Numerator % item.Coefficient.Denominator != 0) ||
            !Exact(
                value: expanded.Constant,
                converter: converter,
                exact: out ExactBoundValue constant
            )) {
            return false;
        }
        BigInteger spacing = BigInteger.Zero;
        foreach ((IVariableItem Variable, ExactBoundValue Coefficient) item in effective) {
            spacing = BigInteger.GreatestCommonDivisor(spacing, BigInteger.Abs(item.Coefficient.Numerator / item.Coefficient.Denominator));
        }
        if (spacing.IsZero) {
            return relation switch {
                Comparison.GT => constant.Sign <= 0 || constant.CompareTo(gap) >= 0,
                Comparison.GE => constant.Sign >= 0 || constant.CompareTo(step.Negate()) <= 0,
                Comparison.LT => constant.Sign >= 0 || constant.CompareTo(gap.Negate()) <= 0,
                _ => constant.Sign <= 0 || constant.CompareTo(step) >= 0,
            };
        }
        if (relation is Comparison.LT) {
            constant = constant.Negate();
        }
        // 按整数系数的最大公约数找最近格点 / Find the nearest lattice point using the integer-coefficient GCD.
        BigInteger divisor = constant.Denominator * spacing;
        BigInteger remainder = constant.Numerator % divisor;
        if (remainder.Sign < 0) {
            remainder += divisor;
        }
        var distance = new ExactBoundValue(remainder.IsZero ? divisor : remainder, constant.Denominator);
        if (relation == Comparison.GE && !remainder.IsZero) {
            distance = new ExactBoundValue(divisor - remainder, constant.Denominator);
        }
        return distance.CompareTo(relation is Comparison.GT or Comparison.LT ? gap : step) >= 0;
    }

    private static bool CanMerge(LinearPolynomial<V> polynomial, IIntoValue<V> converter) {
        var coefficients = new Dictionary<VariableItemKey, (V Value, ExactBoundValue Exact)>();
        foreach (LinearMonomial<V> term in polynomial.Monomials) {
            if (term.Symbol is not IVariableItem variable || !Exact(
                value: term.Coefficient,
                converter: converter,
                exact: out ExactBoundValue coefficient
            )) {
                return false;
            }
            if (!coefficients.TryGetValue(variable.Key, out (V Value, ExactBoundValue Exact) previous)) {
                coefficients.Add(variable.Key, (term.Coefficient, coefficient));
                continue;
            }
            if (!LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                    lhs: previous.Value,
                    rhs: term.Coefficient,
                    result: out V sum
                ) || !Exact(
                    value: sum,
                    converter: converter,
                    exact: out ExactBoundValue actual
                ) || previous.Exact.Add(coefficient).CompareTo(actual) != 0) {
                return false;
            }
            coefficients[variable.Key] = (sum, actual);
        }
        return true;
    }

    private bool MatchesRow(LinearInequality<V> row, SymbolFlatten.LinearFlattenData<V> flattened) {
        var coefficients = new Dictionary<VariableItemKey, ExactBoundValue>();
        foreach ((LinearPolynomial<V> polynomial, bool subtract) in new[] { (row.Lhs, false), (row.Rhs, true) }) {
            foreach (LinearMonomial<V> term in polynomial.Monomials) {
                if (term.Symbol is not IVariableItem variable || !Exact(
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
            if (term.Symbol is not IVariableItem variable || !Exact(
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
            Exact(
                value: row.Lhs.Constant,
                converter: _converter,
                exact: out ExactBoundValue lhs
            ) &&
            Exact(
                value: row.Rhs.Constant,
                converter: _converter,
                exact: out ExactBoundValue rhs
            ) &&
            Exact(
                value: flattened.Constant,
                converter: _converter,
                exact: out ExactBoundValue actual
            ) &&
            lhs.Add(rhs.Negate()).CompareTo(actual) == 0;
    }

    private static bool Exact(V value, IIntoValue<V> converter, out ExactBoundValue exact) =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out exact
        ) &&
        ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;

    private static bool TryNegate(V value, IIntoValue<V> converter, out V result) {
        result = default;
        return Exact(
            value: value,
            converter: converter,
            exact: out ExactBoundValue original
        ) &&
            LinearFlattenExtensions.Arithmetic<V>.TryNegate(value, out result) &&
            Exact(
                value: result,
                converter: converter,
                exact: out ExactBoundValue negated
            ) && original.Negate().CompareTo(negated) == 0;
    }

    private static Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() => Results.Failed<IfFunction<V>>(
        new Err<ErrorCode>(ErrorCode.IllegalArgument,
            "条件输入、范围或离散证明无效。 / Invalid condition input, bounds or discrete proof.")
    );

    private static Try InvalidRegistration() => Results.Failed<Success>(
        new Err<ErrorCode>(ErrorCode.IllegalArgument,
            "条件输入、令牌或范围已失效。 / Condition input, tokens or bounds are invalid or stale.")
    );
}
