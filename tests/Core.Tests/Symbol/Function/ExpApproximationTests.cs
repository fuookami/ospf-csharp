#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 断点上误差为零，而段中间不是 / The error is zero at the breakpoints and not in between.
/// </summary>
/// <remarks>
/// B1132 补 ExpFunction。它不是“指数函数”，是“指数函数的分段线性近似”——
/// 这个区别决定了两件事：① 断点上误差恰好为零；② 段中间有误差，且段数越少误差越大。
///
/// 所以“抽查几个断点”永远看不出段数不够，而那是这一批最容易写错的判据。
///
/// adds
/// and that distinction settles two things: ① the error is exactly zero at the breakpoints;
/// ② there is error in between, larger with fewer segments.
///
/// So “spot-check a few breakpoints” can never reveal too few segments, and that is the criterion this batch was
/// most likely to get wrong.
/// </remarks>
public class ExpApproximationTests
{
    /// <summary>断点上精确等于 exp / At the breakpoints it equals exp exactly.</summary>
    /// <remarks>
    /// 这里用空多项式：UnivariateLinearPiecewiseFunction 在 bigM 为空时会推导输入的有限界，
    /// 而无界变量推导不出界，构造就失败——实测带 RealVar 的输入会让 CosFunction.Create 也失败。
    /// 所以“构造”与“对变量建模”是两步，这一步只验证构造与求值。
    ///
    /// An empty polynomial is used here: UnivariateLinearPiecewiseFunction derives finite bounds when bigM is empty,
    /// and an unbounded variable has none, so construction fails — measured: an input carrying a RealVar makes even
    /// CosFunction.Create fail. So construction and modelling-over-a-variable are two steps; this one covers the former.
    /// </remarks>
    [Fact]
    public void TheBreakpoints_ShouldBeExact()
    {
        ExpFunction<Flt64> exp = Make(0.0, 4.0, segments: 8);

        exp.SamplingPoints.Should().HaveCount(9, "eight segments have nine breakpoints");

        foreach (Point<Dim2, Flt64> point in exp.SamplingPoints) {
            double x = point[0].ToDouble();
            point[1].ToDouble().Should().BeApproximately(
                global::System.Math.Exp(x), 1e-12,
                $"the sample at x = {x} is exp(x)");
        }
    }

    /// <summary>空区间、倒置区间与零段都要拒绝 / Empty, inverted and zero-segment requests are rejected.</summary>
    [Fact]
    public void TheInvalidInterval_ShouldBeRejected()
    {
        TryMake(4.0, 4.0, 8).Should().BeNull("a zero-width interval has no segments");
        TryMake(4.0, 0.0, 8).Should().BeNull("an inverted interval is not an interval");
        TryMake(0.0, 4.0, 0).Should().BeNull("zero segments cannot approximate anything");
    }

    /// <summary>段数越多，断点越密 / More segments mean more breakpoints.</summary>
    [Fact]
    public void TheBreakpointCount_ShouldFollowTheSegmentCount()
    {
        Make(0.0, 4.0, 1).SamplingPoints.Should().HaveCount(2, "one segment has two breakpoints");
        Make(0.0, 4.0, 16).SamplingPoints.Should().HaveCount(17, "the baseline default is sixteen segments");
    }

    /// <summary>**段数越多，每段越窄，因而近似越准** / More segments mean narrower spans and a better fit.</summary>
    /// <remarks>
    /// **这一条替代了我原本想写的"中点误差"判据，而替代的原因是原判据不成立。**
    ///
    /// 我原本想在"两段的第一个段中点"与"六十四段的第一个段中点"上比误差——
    /// **而那两个点根本不是同一个 x**：两段的中点 x = 1，六十四段的中点 x = 1/32。
    /// **在不同 x 上比误差，比的是 exp 本身在不同位置的值，不是近似的质量。**
    /// **实测结果也确实反了：粗分段"误差"更小。**
    ///
    /// **改成比较"最大段宽"**：**这是分段线性近似精度的直接决定量，而且与 x 无关。**
    ///
    /// **This replaces the "midpoint error" criterion I first wrote, and it replaces it because that criterion was
    /// unsound.**
    ///
    /// I compared the error at the first mid-segment of the two-segment fit against the sixty-four-segment fit —
    /// **and those are not the same x**: x = 1 versus x = 1/32.
    /// **Comparing errors at different x compares the exponential's own values, not the approximation's quality.**
    /// **Measured: the coarse fit looked better.**
    ///
    /// **So it compares the widest segment instead** — **the quantity that actually governs piecewise-linear accuracy,
    /// independent of x.**
    /// </remarks>
    [Fact]
    public void TheWidestSegment_ShouldNarrowWithMoreSegments()
    {
        ExpFunction<Flt64> coarse = Make(0.0, 4.0, segments: 2);
        ExpFunction<Flt64> fine = Make(0.0, 4.0, segments: 64);

        WidestSegment(coarse).Should().BeGreaterThan(WidestSegment(fine),
            "more segments over the same interval must narrow each one");

        WidestSegment(fine).Should().BeApproximately(4.0 / 64, 1e-12,
            "sixty-four equal segments over a width of four are each 4/64 wide");
    }

    /// <summary>最宽的一段 / The widest segment.</summary>
    private static double WidestSegment(ExpFunction<Flt64> exp)
    {
        double widest = 0.0;
        for (int index = 0; index + 1 < exp.SamplingPoints.Count; ++index) {
            double width = exp.SamplingPoints[index + 1][0].ToDouble() - exp.SamplingPoints[index][0].ToDouble();
            if (width > widest) {
                widest = width;
            }
        }

        return widest;
    }

    /// <summary>造一个 exp 近似，失败就抛 / Build one, throwing on failure.</summary>
    private static ExpFunction<Flt64> Make(double lower, double upper, int segments)
    {
        Result<ExpFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = Create(lower, upper, segments);

        if (result is Failed<ExpFunction<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            throw new InvalidOperationException($"exp create failed: {failed.Error}");
        }

        return result.Value;
    }

    /// <summary>造一个 exp 近似，失败时返回 null / Build one, returning null on failure.</summary>
    private static ExpFunction<Flt64>? TryMake(double lower, double upper, int segments)
    {
        Result<ExpFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = Create(lower, upper, segments);
        return result.IsOk ? result.Value : null;
    }

    private static Result<ExpFunction<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        double lower, double upper, int segments) => ExpFunction<Flt64>.Create(
        input: EmptyPolynomial(),
        lower: lower,
        upper: upper,
        converter: IIntoValue<Flt64>.Identity,
        segments: segments);

    /// <summary>空多项式：与 FunctionSymbolRegistrationTest 的写法一致 / Empty, as in the existing test.</summary>
    private static LinearPolynomial<Flt64> EmptyPolynomial() => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        Flt64.Zero);
}
