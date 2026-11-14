#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>最小中的最大值函数 / Max-of-values function.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MinMaxFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MaxFunction<V> _inner;

    public IReadOnlyList<LinearPolynomial<V>> Polynomials => _inner.Polynomials;
    public RealVar ResultVar => _inner.ResultVar;
    public IReadOnlyList<BinVar> SelectorVars => _inner.SelectorVars;
    public IReadOnlyList<IVariableItem> HelperVariables => _inner.HelperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => _inner.InputSymbols;
    public LinearPolynomial<V> ResultPolynomial => _inner.ResultPolynomial;
    public string Name { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>创建最大值包装函数 / Create a maximum wrapper function.</summary>
    /// <param name="polynomials">输入多项式 / Input polynomials.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MinMaxFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "minMax",
        string? displayName = null) {
        Name = name;
        DisplayName = displayName;
        _inner = new MaxFunction<V>(
            polynomials: polynomials,
            converter: converter,
            bigM: bigM,
            name: name
        );
    }

    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _inner.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _inner.TryEvaluate(values);

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _inner.RegisterAuxiliaryTokens(tokens);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _inner.RegisterConstraints(model);
}

/// <summary>最大中的最小值函数 / Min-of-values function.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MaxMinFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MinFunction<V> _inner;

    public IReadOnlyList<LinearPolynomial<V>> Polynomials => _inner.Polynomials;
    public RealVar ResultVar => _inner.ResultVar;
    public IReadOnlyList<BinVar> SelectorVars => _inner.SelectorVars;
    public IReadOnlyList<IVariableItem> HelperVariables => _inner.HelperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => _inner.InputSymbols;
    public LinearPolynomial<V> ResultPolynomial => _inner.ResultPolynomial;
    public string Name { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>创建最小值包装函数 / Create a minimum wrapper function.</summary>
    /// <param name="polynomials">输入多项式 / Input polynomials.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MaxMinFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "maxMin",
        string? displayName = null) {
        Name = name;
        DisplayName = displayName;
        _inner = new MinFunction<V>(
            polynomials: polynomials,
            converter: converter,
            bigM: bigM,
            name: name
        );
    }

    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _inner.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _inner.TryEvaluate(values);

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _inner.RegisterAuxiliaryTokens(tokens);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _inner.RegisterConstraints(model);
}
