#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 逼近误差只能在断点<b>之间</b>测出来 / The approximation error is only visible between breakpoints.
/// </summary>
/// <remarks>
/// 对齐基线 `NonlinearPiecewise.kt` 基类的 `originalValue` / `originalValueFlt64`。
///
/// **线性插值在每个断点处与真实函数完全重合，所以"抽查断点"永远测不出段数不足。**
/// **这条判据就是那个陷阱的可执行版本。**
///
/// adds
/// breakpoint, so "spot-check the breakpoints" can never reveal too few segments.** **This criterion is the executable
/// form of that trap.**
/// </remarks>
public class NonlinearSourceValueTests
{
    /// <summary>**断点处误差为零，断点之间不为零** / Zero at breakpoints, non-zero between.</summary>
    /// <remarks>
    /// **这是本批最重要的一条**：**它同时证明两件事**——
    /// **① `OriginalValue` 真的在算真实函数（否则断点之间也谈不上误差）；**
    /// **② "断点处误差为零"不是逼近质量的证据。**
    ///
    /// **只有 2 段的 `exp` 在 `[0, 1]` 上，中点处的误差一定大于零。**
    /// **而三个断点（0、0.5、1）处的误差必须<b>恰好</b>为零。**
    ///
    /// **This is the batch's most important criterion**: **it proves two things at once** —
    /// **① `OriginalValue` really evaluates the true function (otherwise there would be no error between breakpoints
    /// either);** **② "zero error at breakpoints" is not evidence of approximation quality.**
    ///
    /// **A two-segment `exp` over `[0, 1]` must have a positive error at the midpoint, while the error at all three
    /// breakpoints must be <b>exactly</b> zero.**
    /// </remarks>
    [Fact]
    public void TheError_ShouldBeZeroAtBreakpointsAndPositiveBetween()
    {
        ExpFunction<Flt64> function = BuildExp(segments: 2);
        RealVar x = SharedVariable;

        // 断点处：误差必须恰好为零。
        // At the breakpoints: the error must be exactly zero.
        foreach (double breakpoint in new[] { 0.0, 0.5, 1.0 }) {
            double approximation = Approximate(function, x, breakpoint);
            function.ApproximationError(breakpoint, approximation).Value.Should().BeApproximately(
                0.0, 1e-12, $"linear interpolation coincides with exp at the breakpoint {breakpoint}");
        }

        // 断点之间：误差必须明显大于零。
        // Between the breakpoints: the error must be clearly positive.
        double midpointApproximation = Approximate(function, x, 0.25);
        double midpointError = function.ApproximationError(0.25, midpointApproximation).Value;
        midpointError.Should().BeGreaterThan(
            1e-4, "a two-segment linear interpolation of exp cannot be exact at a quarter point");
    }

    /// <summary>**段数增加，中点误差减小** / More segments, smaller error.</summary>
    /// <remarks>
    /// **"误差减小"是"逼近真的在逼近"的唯一证据**——
    /// **如果 `OriginalValue` 被错写成返回逼近值本身，误差会恒为 0，
    /// 于是"段数增加误差减小"这条<b>平凡成立</b>（0 不大于 0，但也不会小于）。**
    /// **所以这条判据必须同时断言"少段时误差非零"。**
    ///
    /// **"The error shrinks" is the only evidence that the approximation approximates** —
    /// **if `OriginalValue` were wrongly made to return the approximation itself, the error would be identically zero
    /// and "more segments, smaller error" would hold <b>trivially</b>.** **So this criterion must also assert that the
    /// coarse case has a non-zero error.**
    /// </remarks>
    [Fact]
    public void TheError_ShouldShrinkAsSegmentsGrow()
    {
        RealVar x = SharedVariable;

        double coarse = ErrorAt(BuildExp(segments: 2), x, 0.25);
        double fine = ErrorAt(BuildExp(segments: 64), x, 0.25);

        coarse.Should().BeGreaterThan(1e-4, "two segments cannot be exact");
        fine.Should().BeLessThan(coarse, "sixty-four segments must be closer");
    }

    /// <summary>**域外被拒绝，而不是外推** / Outside the domain is rejected, not extrapolated.</summary>
    /// <remarks>
    /// **分段函数在域外仍会"给出一个数"，而那个数与真实函数无关。**
    /// **`ln` 在负值处是 `NaN`、`1/x` 在零处是 `inf`——两者都会让误差判据静默通过。**
    ///
    /// **The piecewise function still "gives a number" outside its domain, one unrelated to the true function.**
    /// **`ln` at negatives is `NaN` and `1/x` at zero is `inf` — both make an error criterion pass silently.**
    /// </remarks>
    [Fact]
    public void TheOriginalValue_ShouldRejectInputsOutsideTheDomain()
    {
        LogFunction<Flt64> function = BuildLog();

        // **断言错误消息里点出原因，而不只是"失败了"**：**预算判据挡的是裸 `IsOk`，
        // 而"为什么失败"比"失败了"有信息得多。**
        //
        // **Assert on the message, not merely on failure**: **the budget criterion blocks bare `IsOk`, and "why it
        // failed" carries far more than "it failed".**
        function.OriginalValue(-1.0).Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
            "minus one lies outside [1, 2]");
        function.OriginalValue(3.0).Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
            "three lies outside [1, 2]");
        function.OriginalValue(double.NaN).Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
            "a NaN input is not inside anything");
        function.OriginalValue(1.5).Value.ToDouble().Should().BeApproximately(
            global::System.Math.Log(1.5), 1e-12, "one and a half lies inside [1, 2]");
    }

    /// <summary>**真实值确实是真实函数** / The true value really is the true function.</summary>
    /// <remarks>
    /// **`ln(2) = 0.693147...`、`exp(1) = 2.718281...`。**
    /// **如果 `OriginalValue` 返回的是逼近值，这两个数会差出逼近误差的量级（`1e-3` 左右），
    /// 而 `1e-12` 的容差会把它们分开。**
    ///
    /// **`ln(2) = 0.693147...` and `exp(1) = 2.718281...`.** **If `OriginalValue` returned the approximation these would
    /// be off by the approximation error (around `1e-3`), which a `1e-12` tolerance separates.**
    /// </remarks>
    [Fact]
    public void TheOriginalValue_ShouldBeTheTrueFunction()
    {
        BuildLog().OriginalValue(2.0).Value.ToDouble().Should().BeApproximately(
            global::System.Math.Log(2.0), 1e-12, "the true value of ln at two");
        BuildExp(segments: 2).OriginalValue(1.0).Value.ToDouble().Should().BeApproximately(
            global::System.Math.Exp(1.0), 1e-12, "the true value of exp at one");
    }

    /// <summary>**非有限的逼近值被拒绝** / A non-finite approximation is rejected.</summary>
    /// <remarks>
    /// **`NaN > 容差` 是 `false`，所以非有限的误差会让"误差过大"的判据静默通过。**
    ///
    /// **`NaN > tolerance` is `false`, so a non-finite error makes an "error too large" criterion pass silently.**
    /// </remarks>
    [Fact]
    public void TheApproximationError_ShouldRejectNonFiniteApproximations()
    {
        ExpFunction<Flt64> function = BuildExp(segments: 4);

        function.ApproximationError(0.5, double.NaN).Should()
            .BeOfType<Failed<double, ErrorCode, Error<ErrorCode>>>("a NaN approximation has no error");
        function.ApproximationError(0.5, double.PositiveInfinity).Should()
            .BeOfType<Failed<double, ErrorCode, Error<ErrorCode>>>("an infinite approximation has no finite error");
    }

    /// <summary>**定义域端点与实际采样一致** / The domain matches the actual sampling.</summary>
    [Fact]
    public void TheDomain_ShouldMatchTheSamplingInterval()
    {
        ExpFunction<Flt64> function = BuildExp(segments: 4);

        function.DomainLower.Should().Be(0.0);
        function.DomainUpper.Should().Be(1.0);
        function.SamplingPoints.Count.Should().Be(5, "four segments need five points");
    }

    private static ExpFunction<Flt64> BuildExp(int segments) =>
        ExpFunction<Flt64>.Create(
            input: Input(),
            lower: 0.0,
            upper: 1.0,
            converter: IIntoValue<Flt64>.Identity,
            segments: segments,
            name: "nlsv-exp").Value;

    private static LogFunction<Flt64> BuildLog() =>
        LogFunction<Flt64>.Create(
            input: Input(),
            lower: 1.0,
            upper: 2.0,
            converter: IIntoValue<Flt64>.Identity,
            segments: 4,
            name: "nlsv-ln").Value;

    private static LinearPolynomial<Flt64> Input() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, SharedVariable) }, Flt64.Zero);

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("nlsv-input");
        variable.Range.Geq(Flt64.Zero);
        variable.Range.Leq(Flt64.One);
        return variable;
    }

    /// <summary>在给定点求逼近值 / Evaluate the approximation at a point.</summary>
    private static double Approximate(
        ExpFunction<Flt64> function,
        RealVar variable,
        double at) =>
        function.Evaluate(new System.Collections.Generic.Dictionary<Fuookami.Ospf.Math.Symbol.ISymbol, Flt64> {
            [variable] = new Flt64(at),
        })!.Value.ToDouble();

    /// <summary>在给定点求误差 / The error at a point.</summary>
    private static double ErrorAt(ExpFunction<Flt64> function, RealVar variable, double at) =>
        function.ApproximationError(at, Approximate(function, variable, at)).Value;
}
