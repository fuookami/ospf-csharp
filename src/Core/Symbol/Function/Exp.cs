#nullable enable

using System;
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

/// <summary>
/// **指数函数的分段线性近似** / A piecewise-linear approximation of the exponential.
/// </summary>
/// <remarks>
/// **B1132 按基线 `NonlinearPiecewise.kt` 的 `ExpFunction` 补。**
///
/// **`exp` 不是线性的，所以这里给的不是"指数函数"，是"指数函数在给定区间上的分段线性近似"。**
/// **这个区别决定了两件事**：**① 断点上误差恰好为零；② 段中间有误差，且段数越少误差越大。**
/// **所以"抽查几个断点"永远看不出段数不够。**
///
/// **The `ExpFunction` from the baseline's `NonlinearPiecewise.kt`, added in B1132.**
///
/// **`exp` is not linear, so what this provides is not "the exponential" but "a piecewise-linear approximation of it
/// over a given interval".**
/// **That distinction settles two things**: **① the error is exactly zero at the breakpoints;
/// ② there is error in between, larger with fewer segments.**
/// **So "spot-check a few breakpoints" can never reveal too few segments.**
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ExpFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>, IUnivariatePiecewiseFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;
    private readonly double _domainLower;
    private readonly double _domainUpper;

    UnivariateLinearPiecewiseFunction<V> IUnivariatePiecewiseFunction<V>.Piecewise => _piecewise;

    private ExpFunction(
        UnivariateLinearPiecewiseFunction<V> piecewise,
        IReadOnlyList<Point<Dim2, V>> points,
        double domainLower,
        double domainUpper) {
        _piecewise = piecewise;
        SamplingPoints = points;
        _domainLower = domainLower;
        _domainUpper = domainUpper;
    }

    /// <summary>输入多项式 / Input polynomial.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;

    /// <summary>采样点 / Sampling points.</summary>
    public IReadOnlyList<Point<Dim2, V>> SamplingPoints { get; }
    /// <summary>
    /// 真实函数在该点的值 / The true function at a point.
    /// </summary>
    /// <remarks>
    /// **逼近值不是真实值**：**没有这个对照物就无法测出逼近误差，
    /// 而"误差在断点处为零"永远成立（线性插值在断点处与真实函数重合）。**
    ///
    /// **The approximation is not the true value**: **without this reference the error cannot be measured, and
    /// "the error is zero at the breakpoints" always holds (linear interpolation coincides there).**
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> OriginalValue(double input) =>
        NonlinearSourceValue.Of(global::System.Math.Exp, DomainLower, DomainUpper, input, "exp");

    /// <summary>
    /// 逼近误差 <c>|逼近 − 真实|</c> / The approximation error.
    /// </summary>
    public Result<double, ErrorCode, Error<ErrorCode>> ApproximationError(double input, double approximation) =>
        NonlinearSourceValue.ErrorAt(global::System.Math.Exp, DomainLower, DomainUpper, input, approximation, "exp");

    /// <summary>逼近定义域下界 / The lower end of the approximation domain.</summary>
    public double DomainLower => _domainLower;

    /// <summary>逼近定义域上界 / The upper end of the approximation domain.</summary>
    public double DomainUpper => _domainUpper;


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

    /// <summary>**在 `[lower, upper]` 上按等距断点创建指数近似** / Create an exponential approximation over `[lower, upper]`.</summary>
    /// <param name="input">输入多项式 / Input polynomial.</param>
    /// <param name="lower">区间下界 / Interval lower bound.</param>
    /// <param name="upper">区间上界 / Interval upper bound.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="segments">段数 / Number of segments.</param>
    /// <param name="bigM">可选 Big-M；**给了它就不再推导变量界** / Optional Big-M; supplying it skips bound derivation.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <remarks>
    /// **默认 16 段与基线一致。** **段数越少，区间中部的近似误差越大**——
    /// **而误差在断点上恰好为零，所以"抽查几个断点"看不出段数不够。**
    ///
    /// **`bigM` 为空时构造会去推导输入多项式的有限界**，**而无界变量推导不出界，构造就失败**——
    /// **这是 `UnivariateLinearPiecewiseFunction` 的既有约束，不是这里加的。**
    /// **要建模无界变量上的 `exp`，必须先给出界（把变量声明在有限区间上，或显式传 `bigM`）。**
    ///
    /// **The default of 16 segments matches the baseline.** **Fewer segments mean more error in the middle of each
    /// interval** — **and the error is exactly zero at the breakpoints, so "spot-check a few breakpoints" cannot
    /// reveal too few segments.**
    ///
    /// **With `bigM` empty, construction derives finite bounds for the input polynomial**, **and an unbounded variable
    /// has none, so construction fails** — **that is an existing constraint of `UnivariateLinearPiecewiseFunction`,
    /// not one added here.** **To model `exp` over an unbounded variable, supply bounds first.**
    /// </remarks>
    public static Result<ExpFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        double lower,
        double upper,
        IIntoValue<V>? converter,
        int segments = 16,
        V? bigM = null,
        string name = "exp",
        string? displayName = null) {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper || segments < 1 || converter is null) {
            return Invalid();
        }

        // **`FromPoints<V>` 要的是 `Point<Dim2, V>`，不是 `Flt64`**——
        // **所以采样点直接用 `converter.IntoValue` 转成 `V`**，与 `TrigonometricSupport` 的做法一致。
        //
        // **`FromPoints<V>` wants `Point<Dim2, V>`, not `Flt64`** —
        // **so the samples are converted with `converter.IntoValue`**, matching `TrigonometricSupport`.
        var points = new Point<Dim2, V>[segments + 1];
        for (int index = 0; index <= segments; ++index) {
            // **端点必须精确落在 `lower` 与 `upper` 上**，不能靠 `lower + i*step` 累加——
            // **累加会让最后一点差一个 `1e-16`，而断点必须严格递增，差一点点就可能被判成相等。**
            //
            // **The endpoints must land exactly on `lower` and `upper`** rather than accumulating `lower + i*step` —
            // **accumulation leaves the last point off by `1e-16`, and breakpoints must strictly increase, so
            // near-equal values can be rejected.**
            double x = index == segments
                ? upper
                : (index == 0 ? lower : lower + (upper - lower) * index / segments);
            points[index] = Point<Dim2, V>.Create(
                converter.IntoValue(new Flt64(x)),
                converter.IntoValue(new Flt64(global::System.Math.Exp(x))));
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: input,
            points: points,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName
        ).Map(function => new ExpFunction<V>(function, Array.AsReadOnly(points), lower, upper));
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _piecewise.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _piecewise.TryEvaluate(values);

    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) =>
        ((IModelAwareFunctionPreparation<V>)_piecewise).TryPrepare(inputs, modelValues);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _piecewise.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _piecewise.RegisterConstraints(model);

    private static Result<ExpFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        new Failed<ExpFunction<V>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            "exp approximation requires a finite increasing interval, at least one segment, and a converter");
}
