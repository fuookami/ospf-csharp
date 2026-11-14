#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 上取整函数 / Ceiling function
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class CeilingFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<IVariableItem> _helperVariables;
    private readonly IIntoValue<V> _converter;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<V> Polynomial { get; }
    public V? BigM { get; }
    public IntVar KVar { get; }
    public IntVar ResultVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero
    );

    /// <summary>创建上取整函数 / Create a ceiling function.</summary>
    /// <param name="polynomial">输入多项式 / Input polynomial.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">保留参数，编码不使用 / Reserved parameter, unused in the encoding.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public CeilingFunction(
        LinearPolynomial<V> polynomial,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "ceil",
        string? displayName = null) {
        Polynomial = polynomial ?? throw new ArgumentNullException(nameof(polynomial));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        BigM = bigM;
        Name = name;
        DisplayName = displayName;
        KVar = new IntVar($"{name}_k");
        ResultVar = new IntVar($"{name}_ceil");
        _helperVariables = new IVariableItem[] { KVar, ResultVar };
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        FunctionEvaluation.TransformRounding(
            input: FunctionEvaluation.Polynomial(Polynomial, values),
            converter: _converter,
            rounding: false
        );

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (!ExtremumValues.IsUsable(Polynomial, _converter)) {
            return RoundingSupport.InvalidValue("CEILING");
        }
        if (!RoundingSupport.TryConstants(
            converter: _converter,
            constant: Polynomial.Constant,
            ceiling: true,
            rounding: false,
            epsilon: out V epsilon,
            half: out _
        )) {
            return RoundingSupport.InvalidResolution("CEILING");
        }
        return RoundingSupport.AddConstraints(
            model,
            RoundingSupport.CreateIntegerRows(
                polynomial: Polynomial,
                integerVar: KVar,
                resultVar: ResultVar,
                name: Name,
                ceiling: true,
                converter: _converter,
                epsilon: epsilon
            )
        );
    }
}
