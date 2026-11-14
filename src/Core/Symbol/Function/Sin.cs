#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>基于采样点的正弦分段线性近似 / Sampled piecewise-linear sine approximation.</summary>
/// <remarks>默认仅定义在 [-pi, pi]，不作周期扩展；输入须有有限范围。FltX 的原生图与 Flt64 编码图分别校验；其他数值类型要求精确编码。
/// The default domain is [-pi, pi] without periodic extension; input bounds must be finite. Native and Flt64-encoded FltX graphs are checked separately; other numeric types require exact encoding.</remarks>
/// <typeparam name="V">输入及系数数值类型 / Input and coefficient numeric type.</typeparam>
public sealed class SinFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>, IUnivariatePiecewiseFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;
    UnivariateLinearPiecewiseFunction<V> IUnivariatePiecewiseFunction<V>.Piecewise => _piecewise;

    /// <summary>输入多项式快照 / Input polynomial snapshot.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;
    /// <summary>采样点快照 / Sampling-point snapshot.</summary>
    public IReadOnlyList<Point<Dim2, Flt64>> SamplingPoints { get; }
    /// <summary>结果变量 / Result variable.</summary>
    public RealVar ResultVar => _piecewise.ResultVar;
    /// <inheritdoc/>
    public string Name { get => _piecewise.Name; set => _piecewise.Name = value; }
    /// <inheritdoc/>
    public string? DisplayName { get => _piecewise.DisplayName; set => _piecewise.DisplayName = value; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _piecewise.HelperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => _piecewise.InputSymbols;
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _piecewise.ResultPolynomial;

    private SinFunction(UnivariateLinearPiecewiseFunction<V> piecewise, IReadOnlyList<Point<Dim2, Flt64>> points) {
        _piecewise = piecewise;
        SamplingPoints = points;
    }

    /// <summary>校验采样点并创建函数 / Validate the samples and create the function.</summary>
    /// <param name="input">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="samplingPoints">可选 Flt64 采样点，默认覆盖 [-pi, pi] / Optional Flt64 samples, defaulting to [-pi, pi].</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<SinFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        IIntoValue<V>? converter,
        IReadOnlyList<Point<Dim2, Flt64>>? samplingPoints = null,
        string name = "sin",
        string? displayName = null) => TrigonometricSupport.Create(
            input: input,
            converter: converter,
            samplingPoints: samplingPoints,
            cosine: false,
            name: name,
            displayName: displayName
        ).Map(data => new SinFunction<V>(data.Function, data.Points));

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _piecewise.Evaluate(values);
    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) => _piecewise.TryEvaluate(values);
    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => ((IModelAwareFunctionPreparation<V>)_piecewise).TryPrepare(inputs, modelValues);
    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => _piecewise.RegisterAuxiliaryTokens(tokens);
    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => _piecewise.RegisterConstraints(model);
}
