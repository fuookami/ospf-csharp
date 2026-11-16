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
/// 逻辑斯谛（sigmoid）的分段线性近似 / Piecewise-linear approximation of the logistic curve.
/// </summary>
/// <remarks>
/// 对齐基线 `NonlinearPiecewise.kt` 的 `LogisticApproximationFunction`。
///
/// <c>σ(z) = 1 / (1 + e^(−z))</c>，其中 <c>z = k(x − m)</c>（`k` 是陡度、`m` 是中点）。
///
/// **基线把源函数写成两支**：
/// ```
/// z >= 0 ? 1 / (1 + exp(-z)) : exp(z) / (1 + exp(z))
/// ```
///
/// **两支在数学上恒等，所以这里照写，但把它为什么存在说清楚**：
/// **单支写法 <c>1/(1+e^(−z))</c> 在 <c>z</c> 很负时会让 <c>e^(−z)</c> 溢出成无穷，
/// 于是中间量是「无穷」而不是一个数**——**最终结果仍然是 0（正确），但中间量已经不可信，
/// 而任何在中间量上做判断的代码（例如"是否有限"）会在这里做出错误决定。**
///
/// The logistic curve from the baseline's `NonlinearPiecewise.kt`, added in B1151.
///
/// <c>σ(z) = 1 / (1 + e^(−z))</c> with <c>z = k(x − m)</c> (`k` the steepness, `m` the midpoint).
///
/// **The baseline writes the source in two branches**:
/// ```
/// z >= 0 ? 1 / (1 + exp(-z)) : exp(z) / (1 + exp(z))
/// ```
///
/// **The branches are mathematically identical, so they are copied here, but why they exist is worth stating**:
/// **the single-branch form `1/(1+e^(−z))` overflows `e^(−z)` to infinity for very negative `z`, so an intermediate
/// is "infinity" rather than a number** — **the final result is still 0 (correct), but the intermediate is no longer
/// trustworthy, and anything judging that intermediate (a finiteness check, say) makes a wrong decision there.**
/// </remarks>
public sealed class LogisticApproximationFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;

    private LogisticApproximationFunction(
        UnivariateLinearPiecewiseFunction<V> piecewise,
        IReadOnlyList<Point<Dim2, V>> points,
        double steepness,
        double midpoint) {
        _piecewise = piecewise;
        SamplingPoints = points;
        Steepness = steepness;
        Midpoint = midpoint;
    }

    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;

    /// <summary>采样点 / The sampling points.</summary>
    public IReadOnlyList<Point<Dim2, V>> SamplingPoints { get; }

    /// <summary>陡度 / The steepness.</summary>
    public double Steepness { get; }

    /// <summary>中点 / The midpoint.</summary>
    public double Midpoint { get; }

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

    /// <summary>
    /// 数值稳定的 sigmoid / A numerically stable sigmoid.
    /// </summary>
    /// <remarks>
    /// **公开出来是为了让判据能直接对着它算期望值**——
    /// **否则判据只能写成"采样点的值在 0 与 1 之间"，而那在任何单调实现下都成立。**
    ///
    /// **Exposed so a criterion can compute the expected values against it directly** —
    /// **otherwise the criterion could only say "the samples lie between 0 and 1", which any monotone
    /// implementation satisfies.**
    /// </remarks>
    public static double Source(double z) =>
        z >= 0.0
            ? 1.0 / (1.0 + global::System.Math.Exp(-z))
            : global::System.Math.Exp(z) / (1.0 + global::System.Math.Exp(z));

    /// <summary>构造一个 sigmoid 近似 / Build one.</summary>
    public static Result<LogisticApproximationFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        double lower,
        double upper,
        IIntoValue<V>? converter,
        double steepness = 1.0,
        double midpoint = 0.0,
        int segments = 16,
        V? bigM = null,
        string name = "logistic",
        string? displayName = null) {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper || segments < 1 ||
            converter is null || !double.IsFinite(steepness) || !double.IsFinite(midpoint)) {
            return new Failed<LogisticApproximationFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "logistic approximation requires a finite steepness and midpoint, a finite increasing interval, " +
                "at least one segment, and a converter");
        }

        // 陡度为 0 时 σ 恒为 0.5，近似是一条水平线——那是合法的，但值得把它与"忘了设陡度"分开，
        // 所以不拒绝，只是靠判据钉住"陡度真的被用了"。
        //
        // A zero steepness makes sigma identically 0.5, a flat line — legal, but worth separating from "forgot to set
        // the steepness", so it is not rejected; a criterion pins that the steepness is really used instead.
        Result<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> sampled =
            NonlinearSampling.Sample<V>(
                x => Source(steepness * (x - midpoint)),
                converter,
                lower,
                upper,
                segments,
                "logistic");
        if (sampled is Failed<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<LogisticApproximationFunction<V>, ErrorCode, Error<ErrorCode>>(failed.Error);
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: input,
            points: sampled.Value,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName
        ).Map(function => new LogisticApproximationFunction<V>(
            function, Array.AsReadOnly(sampled.Value), steepness, midpoint));
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _piecewise.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _piecewise.TryEvaluate(values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _piecewise.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _piecewise.RegisterConstraints(model);
}
