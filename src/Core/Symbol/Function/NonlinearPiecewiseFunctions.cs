#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 非线性函数的分段线性采样 / Piecewise-linear sampling of a nonlinear function.
/// </summary>
/// <remarks>
/// `LogFunction` / `PowerFunction` / `ReciprocalFunction` 三者只差一个源函数，
/// **所以采样逻辑共享一份，而不是复制三遍。**
///
/// **采样里最重要的一件事是"检查每一个采样点是否有限"**：
/// **`ln(0)`、`1/0`、`(-1)^0.5` 都会给出非有限值，而那些值会一路变成 NaN 塞进分段函数里**——
/// **分段函数本身不会拒绝它们，于是整条约束悄悄地全部无效。**
///
/// The three functions differ only by their source, **so the sampling is shared rather than tripled.**
///
/// **The important part of sampling is checking that every sample is finite**.
/// **`ln(0)`, `1/0` and `(-1)^0.5` all give non-finite values, and those become NaN inside the piecewise function** —
/// **the piecewise function accepts them, and the whole constraint quietly becomes meaningless.**
/// </remarks>
internal static class NonlinearSampling {
    /// <summary>在 [lower, upper] 上等距采样 / Sample equally over [lower, upper].</summary>
    /// <remarks>
    /// **端点精确落在 `lower` 与 `upper` 上**，不靠累加——
    /// **累加会让最后一点差一个 `1e-16`，而断点必须严格递增。**
    ///
    /// **The endpoints land exactly on `lower` and `upper`** rather than accumulating —
    /// **accumulation leaves the last point off by `1e-16`, and breakpoints must strictly increase.**
    /// </remarks>
    public static Result<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> Sample<V>(
        Func<double, double> source,
        IIntoValue<V> converter,
        double lower,
        double upper,
        int segments,
        string what) where V : struct, IFloatingNumber<V> {
        var points = new Point<Dim2, V>[segments + 1];

        for (int index = 0; index <= segments; ++index) {
            double x = index == segments
                ? upper
                : (index == 0 ? lower : lower + (upper - lower) * index / segments);
            double y = source(x);

            // **一个非有限的采样点就让整次构造失败**，而不是把它留在分段函数里。
            // **A single non-finite sample fails the whole construction** rather than being left in the piecewise
            // function.
            if (!double.IsFinite(y)) {
                return new Failed<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"{what} is not finite at x={x}, so the approximation would be built from a NaN");
            }

            points[index] = Point<Dim2, V>.Create(
                converter.IntoValue(new Flt64(x)),
                converter.IntoValue(new Flt64(y)));
        }

        return new Ok<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>>(points);
    }
}

/// <summary>
/// 自然对数的分段线性近似 / Piecewise-linear approximation of the natural logarithm.
/// </summary>
/// <remarks>
/// 对齐的 `LogFunction`。
///
/// **`ln` 在 0 处没有定义、在负值处是复数**——**所以区间必须严格在 0 的右侧。**
/// **这条护栏不是"更严格"，而是"把 NaN 变成一次明确的拒绝"。**
///
/// The natural logarithm.
///
/// **`ln` is undefined at zero and complex for negatives** — **so the interval has to lie strictly right of zero.**
/// **That guard is not extra strictness but a way to turn a NaN into an explicit rejection.**
/// </remarks>
public sealed class LogFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;
    private readonly double _domainLower;
    private readonly double _domainUpper;

    private LogFunction(
        UnivariateLinearPiecewiseFunction<V> piecewise,
        IReadOnlyList<Point<Dim2, V>> points,
        double domainLower,
        double domainUpper) {
        _piecewise = piecewise;
        SamplingPoints = points;
        _domainLower = domainLower;
        _domainUpper = domainUpper;
    }

    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;

    /// <summary>采样点 / The sampling points.</summary>
    public IReadOnlyList<Point<Dim2, V>> SamplingPoints { get; }
    /// <summary>真实函数在该点的值 / The true function at a point.</summary>
    /// <remarks>
    /// **逼近值不是真实值**：**没有这个对照物就无法测出逼近误差。**
    ///
    /// **The approximation is not the true value**: **without this reference the error cannot be measured.**
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> OriginalValue(double input) =>
        NonlinearSourceValue.Of(global::System.Math.Log, DomainLower, DomainUpper, input, "ln");

    /// <summary>逼近误差 <c>|逼近 − 真实|</c> / The approximation error.</summary>
    public Result<double, ErrorCode, Error<ErrorCode>> ApproximationError(double input, double approximation) =>
        NonlinearSourceValue.ErrorAt(global::System.Math.Log, DomainLower, DomainUpper, input, approximation, "ln");

    /// <summary>逼近定义域下界 / The lower end of the approximation domain.</summary>
    public double DomainLower => _domainLower;

    /// <summary>逼近定义域上界 / The upper end of the approximation domain.</summary>
    public double DomainUpper => _domainUpper;

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

    /// <summary>构造一个 ln 近似 / Build one.</summary>
    public static Result<LogFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        double lower,
        double upper,
        IIntoValue<V>? converter,
        int segments = 16,
        V? bigM = null,
        string name = "ln",
        string? displayName = null) {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper || segments < 1 || converter is null) {
            return new Failed<LogFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "ln approximation requires a finite increasing interval, at least one segment, and a converter");
        }

        // 区间必须严格在 0 右侧：ln 在 0 处发散、在负值处无定义。
        // The interval must lie strictly right of zero: ln diverges at zero and is undefined for negatives.
        if (lower <= 0.0) {
            return new Failed<LogFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"ln approximation needs a lower bound above zero (lower={lower})");
        }

        Result<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> sampled =
            NonlinearSampling.Sample<V>(global::System.Math.Log, converter, lower, upper, segments, "ln");
        if (sampled is Failed<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<LogFunction<V>, ErrorCode, Error<ErrorCode>>(failed.Error);
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: input,
            points: sampled.Value,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName
        ).Map(function => new LogFunction<V>(function, Array.AsReadOnly(sampled.Value), lower, upper));
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

/// <summary>
/// 倒数的分段线性近似 / Piecewise-linear approximation of the reciprocal.
/// </summary>
/// <remarks>
/// 对齐的 `ReciprocalFunction`。
///
/// **`1/x` 在 0 处发散**——**分段线性在奇点附近会给出越来越大的斜率，而跨过奇点的采样会得到一正一负两个巨大值，
/// 于是分段函数"看起来构造成功"但完全错误。**
/// **所以区间绝不能跨过 0，也不能以 0 为端点。**
///
/// The reciprocal.
///
/// **`1/x` diverges at zero** — **piecewise-linear slopes grow near the singularity, and sampling across it yields one
/// huge positive and one huge negative value, so the piecewise function "builds fine" while being completely wrong.**
/// **So the interval must not cross zero, nor end on it.**
/// </remarks>
public sealed class ReciprocalFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;
    private readonly double _domainLower;
    private readonly double _domainUpper;

    private ReciprocalFunction(
        UnivariateLinearPiecewiseFunction<V> piecewise,
        IReadOnlyList<Point<Dim2, V>> points,
        double domainLower,
        double domainUpper) {
        _piecewise = piecewise;
        SamplingPoints = points;
        _domainLower = domainLower;
        _domainUpper = domainUpper;
    }

    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;

    /// <summary>采样点 / The sampling points.</summary>
    public IReadOnlyList<Point<Dim2, V>> SamplingPoints { get; }
    /// <summary>真实函数在该点的值 / The true function at a point.</summary>
    /// <remarks>
    /// **逼近值不是真实值**：**没有这个对照物就无法测出逼近误差。**
    ///
    /// **The approximation is not the true value**: **without this reference the error cannot be measured.**
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> OriginalValue(double input) =>
        NonlinearSourceValue.Of(value => 1.0 / value, DomainLower, DomainUpper, input, "1/x");

    /// <summary>逼近误差 <c>|逼近 − 真实|</c> / The approximation error.</summary>
    public Result<double, ErrorCode, Error<ErrorCode>> ApproximationError(double input, double approximation) =>
        NonlinearSourceValue.ErrorAt(value => 1.0 / value, DomainLower, DomainUpper, input, approximation, "1/x");

    /// <summary>逼近定义域下界 / The lower end of the approximation domain.</summary>
    public double DomainLower => _domainLower;

    /// <summary>逼近定义域上界 / The upper end of the approximation domain.</summary>
    public double DomainUpper => _domainUpper;

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

    /// <summary>构造一个 1/x 近似 / Build one.</summary>
    public static Result<ReciprocalFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        double lower,
        double upper,
        IIntoValue<V>? converter,
        int segments = 16,
        V? bigM = null,
        string name = "reciprocal",
        string? displayName = null) {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper || segments < 1 || converter is null) {
            return new Failed<ReciprocalFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "reciprocal approximation requires a finite increasing interval, at least one segment, and a converter");
        }

        // 区间不能跨过 0，也不能以 0 为端点。
        // The interval must not cross zero, nor end on it.
        if (lower <= 0.0 && upper >= 0.0) {
            return new Failed<ReciprocalFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"reciprocal approximation cannot span zero (interval [{lower}, {upper}])");
        }

        Result<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> sampled =
            NonlinearSampling.Sample<V>(x => 1.0 / x, converter, lower, upper, segments, "1/x");
        if (sampled is Failed<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<ReciprocalFunction<V>, ErrorCode, Error<ErrorCode>>(failed.Error);
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: input,
            points: sampled.Value,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName
        ).Map(function => new ReciprocalFunction<V>(function, Array.AsReadOnly(sampled.Value), lower, upper));
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

/// <summary>
/// 幂函数的分段线性近似 / Piecewise-linear approximation of a power.
/// </summary>
/// <remarks>
/// 对齐的 `PowerFunction`。
///
/// **`x^e` 有两处会非有限，而两处条件不同**：
/// ① **`e &lt; 0` 且区间含 0** → `1/0`；
/// ② **`x &lt; 0` 且 `e` 非整数** → 负数没有一般的实数分数次幂。
///
/// **所以护栏分两条，而不是简单地"区间必须在 0 右侧"**——
/// **那会把 `x²` 在 `[-1, 1]` 上这种完全合法的情况也拒掉，而立方与平方项在优化里很常见。**
///
/// The power function.
///
/// **`x^e` goes non-finite in two places, under different conditions**.
/// ① **`e &lt; 0` with an interval containing zero** → `1/0`;
/// ② **`x &lt; 0` with a non-integer `e`** → a negative base has no general real fractional power.
///
/// **So there are two guards rather than a blanket "interval right of zero"** —
/// **which would reject the perfectly legal `x²` over `[-1, 1]`, and squares and cubes are common in optimisation.**
/// </remarks>
public sealed class PowerFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V>, IFloatingNumber<V> {
    private readonly UnivariateLinearPiecewiseFunction<V> _piecewise;
    private readonly double _domainLower;
    private readonly double _domainUpper;

    private PowerFunction(
        UnivariateLinearPiecewiseFunction<V> piecewise,
        IReadOnlyList<Point<Dim2, V>> points,
        double exponent,
        double domainLower,
        double domainUpper) {
        _piecewise = piecewise;
        SamplingPoints = points;
        _domainLower = domainLower;
        _domainUpper = domainUpper;
        Exponent = exponent;
    }

    /// <summary>输入多项式 / The input polynomial.</summary>
    public LinearPolynomial<V> Input => _piecewise.Input;

    /// <summary>采样点 / The sampling points.</summary>
    public IReadOnlyList<Point<Dim2, V>> SamplingPoints { get; }
    /// <summary>真实函数在该点的值 / The true function at a point.</summary>
    /// <remarks>
    /// **逼近值不是真实值**：**没有这个对照物就无法测出逼近误差。**
    ///
    /// **The approximation is not the true value**: **without this reference the error cannot be measured.**
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> OriginalValue(double input) =>
        NonlinearSourceValue.Of(value => global::System.Math.Pow(value, Exponent), DomainLower, DomainUpper, input, "x^p");

    /// <summary>逼近误差 <c>|逼近 − 真实|</c> / The approximation error.</summary>
    public Result<double, ErrorCode, Error<ErrorCode>> ApproximationError(double input, double approximation) =>
        NonlinearSourceValue.ErrorAt(value => global::System.Math.Pow(value, Exponent), DomainLower, DomainUpper, input, approximation, "x^p");

    /// <summary>逼近定义域下界 / The lower end of the approximation domain.</summary>
    public double DomainLower => _domainLower;

    /// <summary>逼近定义域上界 / The upper end of the approximation domain.</summary>
    public double DomainUpper => _domainUpper;

    /// <summary>指数 / The exponent.</summary>
    public double Exponent { get; }

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

    /// <summary>构造一个 x^e 近似 / Build one.</summary>
    public static Result<PowerFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        double exponent,
        double lower,
        double upper,
        IIntoValue<V>? converter,
        int segments = 16,
        V? bigM = null,
        string name = "power",
        string? displayName = null) {
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper || segments < 1 ||
            converter is null || !double.IsFinite(exponent)) {
            return new Failed<PowerFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "power approximation requires a finite exponent, a finite increasing interval, at least one segment, " +
                "and a converter");
        }

        // 第一条：负指数不能碰到 0。
        if (exponent < 0.0 && lower <= 0.0 && upper >= 0.0) {
            return new Failed<PowerFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"x^{exponent} diverges at zero, and the interval [{lower}, {upper}] contains it");
        }

        // 第二条：负底数只接受整数指数（负数没有一般的实数分数次幂）。
        bool integralExponent = global::System.Math.Abs(exponent - global::System.Math.Round(exponent)) < 1e-12;
        if (lower < 0.0 && !integralExponent) {
            return new Failed<PowerFunction<V>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"x^{exponent} is not real for negative x, and the interval starts at {lower}");
        }

        Result<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> sampled =
            NonlinearSampling.Sample<V>(
                x => global::System.Math.Pow(x, exponent), converter, lower, upper, segments, $"x^{exponent}");
        if (sampled is Failed<Point<Dim2, V>[], ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<PowerFunction<V>, ErrorCode, Error<ErrorCode>>(failed.Error);
        }

        return UnivariateLinearPiecewiseFunction.FromPoints<V>(
            input: input,
            points: sampled.Value,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName
        ).Map(function => new PowerFunction<V>(function, Array.AsReadOnly(sampled.Value), exponent, lower, upper));
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
