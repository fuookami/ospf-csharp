#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
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

/// <summary>有限线性表达式的正部 / Positive part of a bounded linear expression.</summary>
/// <remarks>建模要求可精确展开且数值可精确编码；直接求值不证明可行性。 / Modeling requires exact expansion and encoding; direct evaluation is not a feasibility proof.</remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class PositivePartFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly V _zero;
    private readonly V _one;
    private readonly LinearPolynomial<V> _expanded;
    private readonly IReadOnlyList<LinearPolynomial<V>> _candidates;
    private readonly IReadOnlyList<V> _bigMs;
    private readonly BoundDependencySnapshot<V> _dependencies;
    private readonly IVariableItem[] _variables;
    private readonly VariableItemKey[] _keys;
    private readonly (Flt64 Bound, V Converted, Flt64 Encoded)[] _boundConversions;

    /// <summary>输入多项式浅快照 / Shallow input-polynomial snapshot.</summary>
    public LinearPolynomial<V> Input { get; }
    /// <summary>正部结果变量 / Positive-part result variable.</summary>
    public RealVar ResultVar { get; }
    /// <summary>极值分支选择变量 / Extremum-branch selectors.</summary>
    public IReadOnlyList<BinVar> SelectorVars { get; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    private PositivePartFunction(
        LinearPolynomial<V> input,
        LinearPolynomial<V> expanded,
        IIntoValue<V> converter,
        string name,
        string? displayName) {
        Input = input;
        _expanded = expanded;
        _converter = converter;
        _zero = converter.Zero;
        _one = converter.One;
        Name = name;
        DisplayName = displayName;
        _candidates = Array.AsReadOnly(new[] {
            expanded,
            new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), _zero),
        });
        var maximum = new MaxFunction<V>(
            polynomials: _candidates,
            converter: converter,
            name: name,
            displayName: displayName
        );
        ResultVar = maximum.ResultVar;
        SelectorVars = Array.AsReadOnly(maximum.SelectorVars.ToArray());
        HelperVariables = Array.AsReadOnly(new IVariableItem[] { ResultVar }.Concat(SelectorVars).ToArray());
        _bigMs = Array.AsReadOnly((ExtremumValues.BigMs(
            polynomials: _candidates,
            explicitBigM: null,
            converter: converter,
            minimum: false
        ) ?? Array.Empty<V>()).ToArray());
        _variables = expanded.Monomials.Select(term => (IVariableItem)term.Symbol).Concat(HelperVariables).ToArray();
        _keys = _variables.Select(variable => variable.Key).ToArray();
        _dependencies = new BoundDependencySnapshot<V>(new[] {
            new LinearPolynomial<V>(_variables.Select(variable => new LinearMonomial<V>(_one, variable)).ToArray(), _zero),
        });
        _boundConversions = _variables.SelectMany(variable => new[] { variable.LowerBound, variable.UpperBound })
            .Where(bound => bound is not null && double.IsFinite(bound.Value.ToFlt64().ToDouble()))
            .Select(bound => bound!.Value.ToFlt64())
            .Select(bound => (Bound: bound, Converted: converter.IntoValue(bound)))
            .Select(pair => (pair.Bound, pair.Converted, converter.FromValue(pair.Converted)))
            .ToArray();
    }

    /// <summary>校验并创建正部函数 / Validate and create a positive-part function.</summary>
    /// <param name="input">有界线性多项式 / Bounded linear polynomial.</param>
    /// <param name="converter">模型数值转换器 / Model value converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或校验失败 / Function or validation failure.</returns>
    public static Result<PositivePartFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        IIntoValue<V>? converter,
        string name = "positive_part",
        string? displayName = null) {
        try {
            if (input?.Monomials is null || converter is null || name is null ||
                input.Monomials.Any(term => term is null || term.Symbol is null)) {
                return Invalid<PositivePartFunction<V>>();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(input.Monomials.ToArray()), input.Constant);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                new SymbolFlatten.LinearFlattenData<V>(snapshot.Monomials, snapshot.Constant).ExpandIntermediateSymbols();
            if (!flattened.IsOk || flattened.Value.Monomials.Any(term => term.Symbol is not IVariableItem)) {
                return Invalid<PositivePartFunction<V>>();
            }
            var expanded = new LinearPolynomial<V>(Array.AsReadOnly(flattened.Value.Monomials.ToArray()), flattened.Value.Constant);
            if (!ExactLinearExpansion.Matches(
                    input: snapshot,
                    expanded: expanded,
                    converter: converter
                ) || BigMValues.FiniteBounds(expanded, converter) is null) {
                return Invalid<PositivePartFunction<V>>();
            }
            var function = new PositivePartFunction<V>(
                input: snapshot,
                expanded: expanded,
                converter: converter,
                name: name,
                displayName: displayName
            );
            return function.Current(null, null) ? Results.Ok(function) : Invalid<PositivePartFunction<V>>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<PositivePartFunction<V>>();
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> evaluated = FunctionEvaluation.Polynomial(Input, values);
        if (!evaluated.IsOk || evaluated.Value is not { } value) {
            return evaluated;
        }
        return FunctionEvaluation.Comparable(value, _zero)
            ? FunctionEvaluation.Evaluate<V>(() => value.Gr(_zero) ? value : _zero)
            : FunctionEvaluation.Invalid<V>();
    }

    // shortcut: 未证明辅助变量一致性时不缓存；实现极值准备证据后放宽 / Do not cache unproven helper values; relax after extremum evidence is implemented.
    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => Results.Ok<V?>(null);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        try {
            return tokens is not null && Current(null, null) ? tokens.Add(HelperVariables) : Invalid<Success>();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<Success>();
        }
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        try {
            if (model is null || !CurrentForModel(model)) {
                return Invalid<Success>();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: Rows(),
                afterWrite: () => {
                    if (!CurrentForModel(model)) {
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
        modelConverter: model switch {
            LinearMechanismModel<V> linear => linear.Parent.Converter,
            QuadraticMechanismModel<V> quadratic => quadratic.Parent.Converter,
            // shortcut: 自定义模型未暴露转换器；提供统一契约后校验 / Custom models lack a converter contract; validate when one is available.
            _ => null,
        }
    );

    private bool Current(IAbstractTokenTable<V>? tokens, IFlt64ValueConverter<V>? modelConverter) {
        try {
            if (!_dependencies.IsCurrent() || _bigMs.Count != 2 ||
                !StructuralValueEquality<V>.Same(_converter.Zero, _zero) ||
                !StructuralValueEquality<V>.Same(_converter.One, _one) ||
                modelConverter is not null && (!StructuralValueEquality<V>.Same(modelConverter.Zero, _zero) ||
                    !StructuralValueEquality<V>.Same(modelConverter.One, _one)) ||
                !Exact(
                    value: _zero,
                    modelConverter: modelConverter,
                    exact: out ExactBoundValue zero
                ) || zero.Sign != 0 ||
                !Exact(
                    value: _one,
                    modelConverter: modelConverter,
                    exact: out ExactBoundValue one
                ) || one.Numerator != BigInteger.One || one.Denominator != BigInteger.One) {
                return false;
            }
            if (_variables.GroupBy(variable => variable.Key).Any(group => group.Any(variable => !ReferenceEquals(variable, group.First()))) ||
                HelperVariables.Select(helper => helper.Key).Distinct().Count() != HelperVariables.Count ||
                _expanded.Monomials.Any(term => HelperVariables.Any(helper => helper.Key == ((IVariableItem)term.Symbol).Key))) {
                return false;
            }
            for (int i = 0; i < _variables.Length; ++i) {
                if (_variables[i].Key != _keys[i] || tokens is not null && !ReferenceEquals(tokens.Find(_variables[i])?.Variable, _variables[i])) {
                    return false;
                }
            }
            foreach ((Flt64 bound, V converted, Flt64 encoded) in _boundConversions) {
                if (!ExactBoundValue.TryRead(bound, out ExactBoundValue originalBound) ||
                    !Exact(
                        value: converted,
                        modelConverter: modelConverter,
                        exact: out ExactBoundValue convertedBound
                    ) ||
                    originalBound.CompareTo(convertedBound) != 0 ||
                    !StructuralValueEquality<V>.Same(_converter.IntoValue(bound), converted) ||
                    _converter.FromValue(converted).ToDouble() != encoded.ToDouble() ||
                    modelConverter is not null && (!StructuralValueEquality<V>.Same(modelConverter.IntoValue(bound), converted) ||
                        modelConverter.FromValue(converted).ToDouble() != encoded.ToDouble())) {
                    return false;
                }
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> current =
                new SymbolFlatten.LinearFlattenData<V>(Input.Monomials, Input.Constant).ExpandIntermediateSymbols(tokens);
            if (!current.IsOk || current.Value.Monomials.Count != _expanded.Monomials.Count ||
                !StructuralValueEquality<V>.Same(current.Value.Constant, _expanded.Constant) ||
                !ExactLinearExpansion.Matches(
                    input: Input,
                    expanded: _expanded,
                    converter: _converter
                )) {
                return false;
            }
            for (int i = 0; i < _expanded.Monomials.Count; ++i) {
                if (!ReferenceEquals(current.Value.Monomials[i].Symbol, _expanded.Monomials[i].Symbol) ||
                    !StructuralValueEquality<V>.Same(current.Value.Monomials[i].Coefficient, _expanded.Monomials[i].Coefficient)) {
                    return false;
                }
            }
            IReadOnlyList<V>? bigMs = ExtremumValues.BigMs(
                polynomials: _candidates,
                explicitBigM: null,
                converter: _converter,
                minimum: false
            );
            if (bigMs is null || bigMs.Count != _bigMs.Count ||
                bigMs.Where((value, index) => !StructuralValueEquality<V>.Same(value, _bigMs[index])).Any()) {
                return false;
            }
            foreach (LinearInequality<V> row in Rows()) {
                Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = row.ToLinearFlattenData(tokens);
                if (!flattened.IsOk || !ExactLinearExpansion.MatchesDifference(row, flattened.Value) ||
                    !Check(row.Lhs.Constant) || !Check(row.Rhs.Constant) || !Check(flattened.Value.Constant) ||
                    row.Lhs.Monomials.Concat(row.Rhs.Monomials).Concat(flattened.Value.Monomials).Any(term => !Check(term.Coefficient))) {
                    return false;
                }
            }
            return _dependencies.IsCurrent();

            bool Check(V value) => Exact(
                    value: value,
                    modelConverter: modelConverter,
                    exact: out ExactBoundValue original
                ) &&
                LinearFlattenExtensions.Arithmetic<V>.TryNegate(value, out V negative) &&
                Exact(
                    value: negative,
                    modelConverter: modelConverter,
                    exact: out ExactBoundValue negated
                ) && original.Negate().CompareTo(negated) == 0;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private bool Exact(V value, IFlt64ValueConverter<V>? modelConverter, out ExactBoundValue exact) {
        exact = default;
        return FunctionEvaluation.IsUsable(value) && BigMValues.IsUsable(value, _converter) &&
            ExactBoundValue.TryRead(
                value: value,
                converter: _converter,
                exact: out exact
            ) &&
            ExactBoundValue.TryRead(_converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0 &&
            ExactBoundValue.TryRead(_converter.IntoValue(_converter.FromValue(value)), out ExactBoundValue decoded) &&
            exact.CompareTo(decoded) == 0 &&
            (modelConverter is null || ExactBoundValue.TryRead(modelConverter.FromValue(value), out ExactBoundValue modelValue) &&
                exact.CompareTo(modelValue) == 0 &&
                ExactBoundValue.TryRead(modelConverter.IntoValue(_converter.FromValue(value)), out ExactBoundValue modelDecoded) &&
                exact.CompareTo(modelDecoded) == 0);
    }

    private IReadOnlyList<LinearInequality<V>> Rows() => ExtremumValues.CreateConstraints(
        inputs: _candidates,
        result: ResultVar,
        selectors: SelectorVars,
        bigMs: _bigMs,
        name: Name,
        minimum: false,
        converter: _converter
    );

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>() => Results.Failed<T>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "正部函数输入、转换器或注册状态无效。 / Invalid positive-part input, converter or registration state."
    ));
}
