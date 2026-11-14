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
/// 四舍五入函数 / Rounding function
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RoundingFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<IVariableItem> _helperVariables;
    private readonly V? _explicitBigM;
    private readonly IIntoValue<V> _converter;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<V> Polynomial { get; }
    public IntVar KVar { get; }
    public BinVar RoundUpVar { get; }
    public URealVar FractionVar { get; }
    public IntVar ResultVar { get; }
    public V BigM { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero
    );

    /// <summary>创建取整函数 / Create a rounding function.</summary>
    /// <param name="polynomial">输入多项式 / Input polynomial.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">小数指示 Big-M / Big-M for the fractional indicator.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public RoundingFunction(
        LinearPolynomial<V> polynomial,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "round",
        string? displayName = null) {
        Polynomial = polynomial ?? throw new ArgumentNullException(nameof(polynomial));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _explicitBigM = bigM;
        BigM = bigM is { } value && value.Geq(_converter.One) ? value : _converter.One;
        Name = name;
        DisplayName = displayName;
        KVar = new IntVar($"{name}_k");
        RoundUpVar = new BinVar($"{name}_r");
        FractionVar = new URealVar($"{name}_b");
        ResultVar = new IntVar($"{name}_round");
        _helperVariables = new IVariableItem[] {
            KVar,
            RoundUpVar,
            FractionVar,
            ResultVar,
        };
    }

    /// <summary>按中点取偶求值；模型约束在中点向上取整 / Evaluate ties to even; model rows round ties up.</summary>
    /// <param name="values">符号值 / Symbol values.</param>
    /// <returns>结果或缺失值 / Result or null when a value is missing.</returns>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        FunctionEvaluation.TransformRounding(
            input: FunctionEvaluation.Polynomial(Polynomial, values),
            converter: _converter,
            rounding: true
        );

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if ((_explicitBigM is { } explicitBigM &&
                !BigMValues.IsUsable(explicitBigM, _converter)) ||
            !ExtremumValues.IsUsable(Polynomial, _converter) ||
            !BigMValues.IsUsable(BigM, _converter)) {
            return RoundingSupport.InvalidValue("ROUND");
        }
        if (!RoundingSupport.TryConstants(
            converter: _converter,
            constant: Polynomial.Constant,
            ceiling: false,
            rounding: true,
            epsilon: out V epsilon,
            half: out V half
        )) {
            return RoundingSupport.InvalidResolution("ROUND");
        }
        return RoundingSupport.AddConstraints(
            model,
            RoundingSupport.CreateRoundingRows(
                polynomial: Polynomial,
                integerVar: KVar,
                roundUpVar: RoundUpVar,
                fractionVar: FractionVar,
                resultVar: ResultVar,
                bigM: BigM,
                name: Name,
                converter: _converter,
                epsilon: epsilon,
                half: half
            )
        );
    }
}
