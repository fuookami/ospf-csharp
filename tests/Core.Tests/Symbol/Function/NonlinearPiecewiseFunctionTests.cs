#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 把 NaN 变成一次明确的拒绝 / Turning a NaN into an explicit rejection.
/// </summary>
/// <remarks>
/// 补 `LogFunction` / `PowerFunction` / `ReciprocalFunction`（基线 `NonlinearPiecewise.kt`）。
///
/// **三者的源函数各在什么地方失效**：
/// | 函数 | 失效条件 |
/// | --- | --- |
/// | `ln` | `x ≤ 0` |
/// | `1/x` | `x = 0`（区间跨过或端点落在 0） |
/// | `x^e` | **`e &lt; 0` 且区间含 0**；**`x &lt; 0` 且 `e` 非整数** |
///
/// **如果不拦，`ln(0)` 给出 `-∞`、`1/0` 给出 `±∞`、`(-1)^0.5` 给出 `NaN`，
/// 而这些值会一路塞进分段线性函数里——分段函数不拒绝它们，于是整条约束悄悄失效。**
///
/// **Where each source fails**:
/// | Function | Fails when |
/// | --- | --- |
/// | `ln` | `x ≤ 0` |
/// | `1/x` | `x = 0` (interval crosses or ends on it) |
/// | `x^e` | **`e &lt; 0` with an interval containing 0**; **`x &lt; 0` with a non-integer `e`** |
///
/// **Without a guard, `ln(0)` gives `-∞`, `1/0` gives `±∞` and `(-1)^0.5` gives `NaN`, and those land inside the
/// piecewise function, which accepts them — so the whole constraint quietly stops meaning anything.**
/// </remarks>
public class NonlinearPiecewiseFunctionTests
{
    /// <summary>**`ln` 拒绝下界为 0 的区间** / `ln` rejects a lower bound at zero.</summary>
    /// <remarks>
    /// **`ln(0)` 是 `-∞`，不是错误。** **它会被 `converter.IntoValue` 变成一个"数"，然后分段函数照收。**
    ///
    /// **`ln(0)` is `-∞`, not an error.** **It gets converted into a "number" and the piecewise function accepts it.**
    /// </remarks>
    [Fact]
    public void TheLog_ShouldRejectAnIntervalStartingAtZero()
    {
        LogFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: 0.0,
                upper: 1.0,
                converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("ln diverges at zero, so no finite sample exists there");
    }

    /// <summary>**`ln` 接受严格在 0 右侧的区间** / `ln` accepts an interval strictly right of zero.</summary>
    [Fact]
    public void TheLog_ShouldAcceptAPositiveInterval()
    {
        LogFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: 0.5,
                upper: 4.0,
                converter: IIntoValue<Flt64>.Identity,
                segments: 8)
            .Value.SamplingPoints.Should().HaveCount(9,
                "8 segments over a legal interval give 9 sample points");
    }

    /// <summary>**`1/x` 拒绝跨过 0 的区间** / `1/x` rejects an interval spanning zero.</summary>
    /// <remarks>
    /// **跨过 0 的采样会得到一正一负两个巨大值，分段线性函数"构造成功"而完全错误**——
    /// **这是三处里唯一一个不会产生非有限值、因此只能靠显式护栏拦下的情况。**
    ///
    /// **Sampling across zero yields one huge positive and one huge negative, and the piecewise function "builds
    /// fine" while being completely wrong** — **the only one of the three that produces no non-finite value and so
    /// has to be caught by an explicit guard.**
    /// </remarks>
    [Fact]
    public void TheReciprocal_ShouldRejectAnIntervalSpanningZero()
    {
        ReciprocalFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: -1.0,
                upper: 1.0,
                converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("1/x has no finite value at zero");

        // **上面这条其实 `FromPoints` 自己就能拦下**：跨过 0 的采样里必有一个 ∞，
        // 而 `FromPoints` 用 `FunctionEvaluation.IsUsable` 拒绝非有限值。
        // **所以要证明本类的护栏有价值，必须用一个"跨过 0 但采样点都不在 0 上"的区间。**
        //
        // The criterion above is already covered by FromPoints, which rejects non-finite samples through
        // FunctionEvaluation.IsUsable. So the guard's value only shows with an interval that crosses zero while no
        // sample lands on it.
        ReciprocalFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: -1.0,
                upper: 2.0,
                converter: IIntoValue<Flt64>.Identity,
                segments: 3)
            .IsOk.Should().BeFalse(
                "with segments=3 the samples are -1, 0, 1, 2 -- still hitting zero; use segments=2 to miss it");

        // segments=2 时采样点是 -1、0.5、2 —— **没有一个落在 0 上，于是没有任何采样值是 ∞**，
        // 而分段线性会在这条断崖上"构造成功"。
        // With segments=2 the samples are -1, 0.5 and 2 -- none on zero, so no sample is infinite, and the piecewise
        // function builds fine across the cliff.
        ReciprocalFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: -1.0,
                upper: 2.0,
                converter: IIntoValue<Flt64>.Identity,
                segments: 2)
            .IsOk.Should().BeFalse(
                "no sample lands on zero here, so only an explicit span guard catches it");
    }

    /// <summary>**`1/x` 拒绝以 0 为端点的区间** / `1/x` rejects an interval ending on zero.</summary>
    /// <remarks>
    /// **这条与上一条分开写**：**`[0, 1]` 的端点采样正好是 `1/0`，会给出 `∞`；
    /// 而"跨过 0"的判据写的是 `lower &lt; 0 &amp;&amp; upper &gt; 0`，端点落在 0 时它不会红。**
    ///
    /// **Separate from the previous one**: **`[0, 1]` samples `1/0` at its endpoint and yields `∞`, while a
    /// "spans zero" check written as `lower &lt; 0 &amp;&amp; upper &gt; 0` stays green when an endpoint sits on zero.**
    /// </remarks>
    [Fact]
    public void TheReciprocal_ShouldRejectAnIntervalEndingAtZero()
    {
        ReciprocalFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: 0.0,
                upper: 1.0,
                converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("the sample at x=0 would be 1/0");
    }

    /// <summary>**`x^e` 允许负底数配整数指数** / `x^e` allows a negative base with an integer exponent.</summary>
    /// <remarks>
    /// **把护栏写成"区间必须在 0 右侧"会把 `x²` 在 `[-1, 1]` 上这种完全合法的情况拒掉**——
    /// **而平方与立方项在优化模型里很常见。**
    ///
    /// **A blanket "interval right of zero" guard would reject the perfectly legal `x²` over `[-1, 1]`** —
    /// **and squares and cubes are common in optimisation models.**
    /// </remarks>
    [Fact]
    public void ThePower_ShouldAllowANegativeBaseWithAnIntegerExponent()
    {
        PowerFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                exponent: 2.0,
                lower: -1.0,
                upper: 1.0,
                converter: IIntoValue<Flt64>.Identity,
                segments: 8)
            .Value.SamplingPoints.Should().HaveCount(9,
                "8 segments over a legal interval give 9 sample points");
    }

    /// <summary>**`x^e` 拒绝负底数配分数指数** / `x^e` rejects a negative base with a fractional exponent.</summary>
    [Fact]
    public void ThePower_ShouldRejectANegativeBaseWithAFractionalExponent()
    {
        PowerFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                exponent: 0.5,
                lower: -1.0,
                upper: 4.0,
                converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("(-1)^0.5 is not real");
    }

    /// <summary>**`x^e` 拒绝含 0 的负指数区间** / `x^e` rejects a negative exponent over an interval containing zero.</summary>
    [Fact]
    public void ThePower_ShouldRejectANegativeExponentAcrossZero()
    {
        PowerFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                exponent: -1.0,
                lower: -1.0,
                upper: 1.0,
                converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("x^-1 diverges at zero");

        // **与 `1/x` 同一个道理**：跨过 0 的采样里必有一个 ∞，而 `FromPoints` 会拒绝它。
        // **所以用 segments=2 让采样点为 -1、0.5、2——没有一个落在 0 上。**
        //
        // Same as with 1/x: a sample across zero is infinite and FromPoints rejects it. So use segments=2 so the
        // samples are -1, 0.5 and 2, none of them on zero.
        PowerFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                exponent: -1.0,
                lower: -1.0,
                upper: 2.0,
                converter: IIntoValue<Flt64>.Identity,
                segments: 2)
            .IsOk.Should().BeFalse(
                "no sample lands on zero here, so only the explicit span guard catches the divergence");
    }

    /// <summary>**三条护栏都不误伤正常区间** / None of the three guards rejects an ordinary interval.</summary>
    /// <remarks>
    /// **上面五条都在测"该拒的拒了"，而没有一条测"该收的收了"**——
    /// **一个 `return Invalid` 无条件执行的实现能让它们全部通过。**
    ///
    /// **The five criteria above test "the bad ones are rejected", and none tests "the good ones are accepted"** —
    /// **an implementation that returns a failure unconditionally passes all of them.**
    /// </remarks>
    [Fact]
    public void TheThreeFunctions_ShouldAcceptOrdinaryIntervals()
    {
        LogFunction<Flt64>.Create(
                input: EmptyPolynomial(), lower: 1.0, upper: 10.0,
                converter: IIntoValue<Flt64>.Identity, segments: 4)
            .Value.SamplingPoints.Should().HaveCount(5, "4 segments give 5 sample points");

        ReciprocalFunction<Flt64>.Create(
                input: EmptyPolynomial(), lower: 1.0, upper: 10.0,
                converter: IIntoValue<Flt64>.Identity, segments: 4)
            .Value.SamplingPoints.Should().HaveCount(5, "4 segments give 5 sample points");

        PowerFunction<Flt64>.Create(
                input: EmptyPolynomial(), exponent: 3.0, lower: 0.0, upper: 2.0,
                converter: IIntoValue<Flt64>.Identity, segments: 4)
            .Value.SamplingPoints.Should().HaveCount(5, "4 segments give 5 sample points");
    }

    /// <summary>**采样端点精确落在区间端点、且值等于源函数** / Endpoints are exact and match the source.</summary>
    /// <remarks>
    /// **靠 `lower + i*step` 累加会让最后一点差一点点，而断点必须严格递增。**
    /// **这条钉住"第一个采样点就是 `(lower, f(lower))`、最后一个就是 `(upper, f(upper))`"。**
    ///
    /// **Accumulating `lower + i*step` leaves the last point slightly off, and breakpoints must strictly increase.**
    /// **This pins that the first sample is `(lower, f(lower))` and the last is `(upper, f(upper))`.**
    /// </remarks>
    [Fact]
    public void TheSampling_ShouldLandExactlyOnTheEndpoints()
    {
        LogFunction<Flt64> log = LogFunction<Flt64>.Create(
            input: EmptyPolynomial(), lower: 0.5, upper: 4.0,
            converter: IIntoValue<Flt64>.Identity, segments: 8).Value;

        log.SamplingPoints.Should().HaveCount(9);
        log.SamplingPoints[0][0].ToDouble().Should().Be(0.5);
        log.SamplingPoints[0][1].ToDouble().Should().BeApproximately(
            global::System.Math.Log(0.5), 1e-15);
        log.SamplingPoints[8][0].ToDouble().Should().Be(4.0);
        log.SamplingPoints[8][1].ToDouble().Should().BeApproximately(
            global::System.Math.Log(4.0), 1e-15);
    }

    /// <summary>**空多项式：与既有测试的写法一致** / Empty, as in the existing test.</summary>
    private static LinearPolynomial<Flt64> EmptyPolynomial() => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        Flt64.Zero);
}
