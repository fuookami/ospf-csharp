#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 分段输出范围：只在端点取值 / Only the endpoints are sampled.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `calculatePwlOutputBounds` 与 `PwlOutputBounds`。
///
/// **端点足够，只因为每段是线性的。**
///
/// **Endpoints suffice only because each piece is linear.**
/// </remarks>
public class PwlOutputBoundsCalculatorTests
{
    /// <summary>
    /// **两段的端点极值** / The extremes across two segments.
    /// </summary>
    /// <remarks>
    /// **`f(x) = x` 在 `[0, 5]`，`f(x) = 5` 在 `[5, 10]`：**
    /// **端点值是 `0, 5, 5, 5`，范围 `[0, 5]`。**
    ///
    /// **判据同时断言下界与上界，因为"只算下界"与"只算上界"是两种不同的错法。**
    ///
    /// **`f(x) = x` over `[0, 5]` then `f(x) = 5` over `[5, 10]`: the endpoint values are `0, 5, 5, 5`, so the range is
    /// `[0, 5]`.**
    ///
    /// **Both bounds are asserted, because "only the lower" and "only the upper" are two different mistakes.**
    /// </remarks>
    [Fact]
    public void TheTwoSegments_ShouldGiveTheEndpointExtremes()
    {
        Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> result = Calculate(
            breakpoints: new[] { 0.0, 5.0, 10.0 },
            slopes: new[] { 1.0, 0.0 },
            intercepts: new[] { 0.0, 5.0 });

        result.Should().BeOfType<Ok<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
        result.Value.Lower.ToDouble().Should().Be(0.0);
        result.Value.Upper.ToDouble().Should().Be(5.0);
    }

    /// <summary>
    /// **中段低于两端时，极值在中间段的端点** / A middle segment's endpoints win when it dips.
    /// </summary>
    /// <remarks>
    /// **`f(x) = 0` 在 `[0, 1]`，`f(x) = −10` 在 `[1, 2]`，`f(x) = 0` 在 `[2, 3]`：**
    /// **端点值是 `0, 0, −10, −10, 0, 0`，范围 `[−10, 0]`。**
    ///
    /// **这一例专门对准"只看第一段与最后一段"的实现——
    /// 那种实现会给出 `[0, 0]`，而模型照常可行。**
    ///
    /// **`0` on `[0, 1]`, `−10` on `[1, 2]`, `0` on `[2, 3]`: the endpoint values are `0, 0, −10, −10, 0, 0`, giving
    /// `[−10, 0]`.**
    ///
    /// **This case targets an implementation that looks only at the first and last segment — it would report `[0, 0]`,
    /// and the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheMiddleSegment_ShouldBeConsidered()
    {
        Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> result = Calculate(
            breakpoints: new[] { 0.0, 1.0, 2.0, 3.0 },
            slopes: new[] { 0.0, 0.0, 0.0 },
            intercepts: new[] { 0.0, -10.0, 0.0 });

        result.Value.Lower.ToDouble().Should().Be(-10.0, "the dip is interior, not at either end");
        result.Value.Upper.ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **单调递增时，范围就是首末两个端点** / For a monotone case the range is the two outer endpoints.
    /// </summary>
    [Fact]
    public void TheMonotoneCase_ShouldMatchItsOuterEndpoints()
    {
        Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> result = Calculate(
            breakpoints: new[] { 0.0, 1.0, 2.0 },
            slopes: new[] { 2.0, 3.0 },
            intercepts: new[] { 1.0, -2.0 });

        // **`f(0) = 1`、`f(1) = 3`、`f(1) = 1`、`f(2) = 4`；范围 `[1, 4]`。**
        // **`f(0) = 1`, `f(1) = 3` then `f(1) = 1`, `f(2) = 4`; the range is `[1, 4]`.**
        result.Value.Lower.ToDouble().Should().Be(1.0);
        result.Value.Upper.ToDouble().Should().Be(4.0);
    }

    /// <summary>
    /// **数量不匹配与"没有段"给不同的消息** / A count mismatch and "no segment" say different things.
    /// </summary>
    /// <remarks>
    /// **`Breakpoints.Count &lt; 2` 那一支不是冗余：** **少了它，`Count == 1` 配空斜率表会先通过尺寸检查，
    /// 然后在端点求值那一步以"输出非有限或无界"失败——那条消息指向数值，
    /// 而真正的原因是"没有段可算"。**
    ///
    /// **The `Count &lt; 2` branch is not redundant**: **without it, `Count == 1` with empty slope tables passes the size
    /// checks and then fails at evaluation with "non-finite or unbounded output" — a message pointing at numerics while
    /// the cause is "there is no segment".**
    /// </remarks>
    [Fact]
    public void TheCountProblems_ShouldSayWhichOne()
    {
        // **一个断点：没有段。**
        // **A single breakpoint: no segment.**
        Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> single = Calculate(
            breakpoints: new[] { 0.0 }, slopes: Array.Empty<double>(),
            intercepts: Array.Empty<double>());
        single.Should().BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
        ((Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>)single).Error.Message.Should().Contain(
            "inconsistent counts",
            "a missing segment is a structural problem, not a numeric one");

        // **斜率少一个：数量不匹配。**
        // **One slope short: an inconsistent count.**
        Calculate(
            breakpoints: new[] { 0.0, 1.0, 2.0 }, slopes: new[] { 1.0 },
            intercepts: new[] { 0.0, 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();

        // **截距少一个：同样。**
        // **One intercept short: likewise.**
        Calculate(
            breakpoints: new[] { 0.0, 1.0, 2.0 }, slopes: new[] { 1.0, 1.0 },
            intercepts: new[] { 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **断点必须严格递增，等于也不行** / Breakpoints must strictly increase; equality is not enough.
    /// </summary>
    /// <remarks>
    /// **只比较相邻对，而这对全局严格递增是充分的：相邻都严格小于，则整列严格递增。**
    ///
    /// **相等与递减各测一例**：**相等是最容易漏的——"递增"在口语里常常包含"不递减"。**
    ///
    /// **Only neighbours are compared, which is sufficient for global strict increase.**
    ///
    /// **Equality and decrease are tested separately**: **equality is the one most easily missed — colloquial "increasing"
    /// often includes "non-decreasing".**
    /// </remarks>
    [Fact]
    public void TheBreakpoints_ShouldStrictlyIncrease()
    {
        // **相等。**
        // **Equality.**
        Calculate(
            breakpoints: new[] { 0.0, 1.0, 1.0 }, slopes: new[] { 1.0, 1.0 },
            intercepts: new[] { 0.0, 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>(
                "a repeated breakpoint has zero-width segment");

        // **递减。**
        // **A decrease.**
        Calculate(
            breakpoints: new[] { 0.0, 2.0, 1.0 }, slopes: new[] { 1.0, 1.0 },
            intercepts: new[] { 0.0, 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();

        // **顺序正确但跨度大，必须通过。**
        // **Correctly ordered with a wide span must pass.**
        Calculate(
            breakpoints: new[] { -100.0, 0.0, 100.0 }, slopes: new[] { 1.0, 1.0 },
            intercepts: new[] { 0.0, 0.0 }).Should()
            .BeOfType<Ok<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **任何一处不可用值都被拒** / Any unusable value is rejected.
    /// </summary>
    /// <remarks>
    /// **三个列表各测一例**：**只测其中一个的话，"只检查断点"的实现也能通过。**
    ///
    /// **Each of the three lists gets its own case**: **testing only one would let an implementation that checks only the
    /// breakpoints pass.**
    /// </remarks>
    [Fact]
    public void TheUnusableValues_ShouldBeRejectedEverywhere()
    {
        Calculate(
            breakpoints: new[] { 0.0, double.NaN }, slopes: new[] { 1.0 },
            intercepts: new[] { 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();

        Calculate(
            breakpoints: new[] { 0.0, 1.0 }, slopes: new[] { double.PositiveInfinity },
            intercepts: new[] { 0.0 }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();

        Calculate(
            breakpoints: new[] { 0.0, 1.0 }, slopes: new[] { 1.0 },
            intercepts: new[] { double.NegativeInfinity }).Should()
            .BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **端点乘积溢出时被拒** / An overflowing endpoint product is rejected.
    /// </summary>
    /// <remarks>
    /// **输入值都可用、但它们的乘积不可用**——**这是"逐项检查通过、组合起来失败"的情形，
    /// 而它只被最后那道输出检查挡住。**
    ///
    /// **The inputs are all usable while their product is not** — **a case where every individual check passes and the
    /// combination fails, caught only by the final output check.**
    /// </remarks>
    [Fact]
    public void TheOverflowingProduct_ShouldBeRejected()
    {
        Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> result = Calculate(
            breakpoints: new[] { 0.0, 1e300 }, slopes: new[] { 1e300 },
            intercepts: new[] { 0.0 });

        result.Should().BeOfType<Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>>();
        ((Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain(
            "non-finite or unbounded output",
            "the inputs were usable; the product was not");
    }

    private static Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> Calculate(
        double[] breakpoints, double[] slopes, double[] intercepts) {
        var bps = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            bps[index] = new Flt64(breakpoints[index]);
        }

        var sls = new Flt64[slopes.Length];
        for (int index = 0; index < slopes.Length; ++index) {
            sls[index] = new Flt64(slopes[index]);
        }

        var ints = new Flt64[intercepts.Length];
        for (int index = 0; index < intercepts.Length; ++index) {
            ints[index] = new Flt64(intercepts[index]);
        }

        return PwlOutputBoundsCalculator.Calculate(bps, sls, ints, IIntoValue<Flt64>.Identity);
    }
}
