#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **分段近似：切分策略是独立可验的数学** / Segmentation is independently verifiable mathematics.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation.kt` 里分段那一半。**判据钉三件容易静默出错的事：**
/// **① 断点均匀且端点精确落界；② 近似在每个断点上与实际函数相等；③ "形式有限、实际无界"的边界被拒绝。**
///
/// Syncs the piecewise half of `OnnxActivation.kt`. **Three things that fail silently are pinned: breakpoints are uniform and land exactly on the bounds,
/// the approximation equals the true function at every breakpoint, and a formally-finite-but-practically-unbounded range is refused.**
/// </remarks>
public class OnnxPiecewiseApproximationTests
{
    /// <summary>**断点均匀，端点精确落界** / Breakpoints are uniform and land exactly on the bounds.</summary>
    /// <remarks>
    /// **端点"差不多等于"边界是最常见的静默偏移：** **它会让模型在恰好等于上界的那一点上取到邻近段的斜率，**
    /// **而那是另一条直线——解会偏，但不会报错。**
    ///
    /// **"Almost equal to the bound" is the usual silent drift: the model then takes the neighbouring segment's slope exactly at the upper bound, which is a
    /// different line — the solution shifts without any error.**
    /// </remarks>
    [Fact]
    public void TheBreakpoints_ShouldBeUniformAndLandOnTheBounds() {
        OnnxPiecewiseApproximation.Result result = OnnxPiecewiseApproximation.Approximate(x => x * x, -2.0, 2.0, segments: 4);

        result.Failure.Should().BeNull();
        OnnxPiecewiseApproximation.Approximation approximation = result.Value!;
        approximation.Breakpoints.Should().Equal(new double[] { -2.0, -1.0, 0.0, 1.0, 2.0 },
            "the endpoints must be exact, not merely close");
        approximation.Slopes.Should().HaveCount(4);
        approximation.Intercepts.Should().HaveCount(4);
    }

    /// <summary>**近似在每个断点上与真实函数相等** / The approximation agrees with the function at every breakpoint.</summary>
    /// <remarks>
    /// **这就是分段线性近似的定义：** **断点处必须精确插值，段内才谈得上误差。** **若断点处都对不上，整条近似就是另一条曲线。**
    ///
    /// **This is the definition of piecewise-linear interpolation: it must be exact at the breakpoints, and only inside a segment does error enter. If it does not
    /// match at the breakpoints, the whole thing is a different curve.**
    /// </remarks>
    [Fact]
    public void TheApproximation_ShouldInterpolateAtEveryBreakpoint() {
        Func<double, double> sigmoid = x => 1.0 / (1.0 + System.Math.Exp(-x));
        OnnxPiecewiseApproximation.Result result = OnnxPiecewiseApproximation.Approximate(sigmoid, -4.0, 4.0, segments: 8);

        result.Failure.Should().BeNull();
        OnnxPiecewiseApproximation.Approximation approximation = result.Value!;
        foreach (double breakpoint in approximation.Breakpoints) {
            approximation.Evaluate(breakpoint).Should().BeApproximately(sigmoid(breakpoint), 1e-12,
                "at a breakpoint the line has to pass exactly through the function");
        }
    }

    /// <summary>**段中点的误差有限且随段数下降** / The error at midpoints is finite and falls as segments grow.</summary>
    [Fact]
    public void MoreSegments_ShouldMeanLessError() {
        Func<double, double> tanh = System.Math.Tanh;

        double CoarseError() => MidpointError(OnnxPiecewiseApproximation.Approximate(tanh, -3.0, 3.0, segments: 4).Value!, tanh, -3.0, 3.0, 4);
        double FineError() => MidpointError(OnnxPiecewiseApproximation.Approximate(tanh, -3.0, 3.0, segments: 32).Value!, tanh, -3.0, 3.0, 32);

        double coarse = CoarseError();
        double fine = FineError();
        coarse.Should().BeGreaterThan(0.0, "a piecewise-linear fit of a curve is not exact in between");
        fine.Should().BeLessThan(coarse, "more segments have to help, otherwise the segment count is decorative");
    }

    /// <summary>**"形式有限、实际无界"的边界被拒绝，不返回近似** / A formally-finite-but-unbounded range is refused.</summary>
    /// <remarks>
    /// **`±double.MaxValue` 是变量的默认范围：** **对它切分得到的曲线依然能求解、依然返回"最优解"，**
    /// **而那条曲线与真实 Sigmoid 毫无关系——** **这是本插件最危险的一种"成功"。**
    ///
    /// **`±double.MaxValue` is the default variable range: splitting it yields a curve that still solves and still returns an "optimum" while having nothing to do
    /// with the real sigmoid — the most dangerous kind of success this plugin could report.**
    /// </remarks>
    [Fact]
    public void AnUnboundedRange_ShouldBeRefused() {
        OnnxPiecewiseApproximation.Result huge = OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -double.MaxValue, double.MaxValue);
        huge.Value.Should().BeNull("an approximation over the whole double range is meaningless, not merely imprecise");
        huge.Failure.Should().Contain("缺少有效范围", "the message has to say what the caller should supply: bounds");

        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -1e7, 1e7).Value.Should().BeNull("one step past the limit is already unbounded for our purposes");
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -1e6, 1e6).Value.Should().NotBeNull("exactly at the limit is still usable");

        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, double.NaN, 1.0).Value.Should().BeNull();
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, 1.0, -1.0).Value.Should().BeNull("an inverted range is a caller error, not something to normalise silently");
    }

    /// <summary>**段数越界被拒绝，不夹紧到区间** / An out-of-range segment count is refused, not clamped.</summary>
    /// <remarks>
    /// **夹紧到 1024 会让调用方以为自己要求的东西生效了：** **而结果是另一条曲线。** 报错才能让调用方改。
    ///
    /// **Clamping to 1024 would let the caller believe its request took effect while the result is another curve. Refusing is what lets it change the request.**
    /// </remarks>
    [Fact]
    public void AnIllegalSegmentCount_ShouldBeRefused() {
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -2, 2, segments: 1).Value.Should().BeNull();
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -2, 2, segments: 1025).Value.Should().BeNull();
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -2, 2, segments: 2).Value.Should().NotBeNull("the inclusive lower bound is legal");
        OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, -2, 2, segments: 1024).Value.Should().NotBeNull("and so is the inclusive upper bound");
    }

    /// <summary>**常量输入塌缩成一个常数，而不是零斜率加零截距** / A constant input collapses to the constant itself.</summary>
    /// <remarks>
    /// **塌缩时若把截距也写成 0，整段就变成了恒等于零**——**而调用方拿到的是一个"合法的近似式"，于是永远不会发现激活输出被清零了。**
    ///
    /// **Writing the intercept as zero while collapsing would make the piece identically zero — and the caller receives a "valid approximation", so it never finds
    /// out the activation output was wiped.**
    /// </remarks>
    [Fact]
    public void AConstantInput_ShouldCollapseToTheConstantValue() {
        OnnxPiecewiseApproximation.Result result = OnnxPiecewiseApproximation.Approximate(System.Math.Tanh, 1.5, 1.5);

        result.Failure.Should().BeNull();
        OnnxPiecewiseApproximation.Approximation approximation = result.Value!;
        approximation.Slopes.Should().Equal(new double[] { 0.0 });
        approximation.Intercepts.Should().Equal(new double[] { System.Math.Tanh(1.5) }, "the constant has to survive, or the collapsed form is silently zero");
        approximation.Evaluate(1.5).Should().BeApproximately(System.Math.Tanh(1.5), 1e-12);
        approximation.Evaluate(99.0).Should().BeApproximately(System.Math.Tanh(1.5), 1e-12, "a collapsed piece stays constant everywhere");
    }

    /// <summary>**算子表：需要近似的恰好是那四个** / The approximated set is exactly those four.</summary>
    [Fact]
    public void TheOperatorSets_ShouldAgreeWithTheBaseline() {
        OnnxPiecewiseApproximation.Approximated.Should().BeSubsetOf(OnnxPiecewiseApproximation.Operators,
            "an operator cannot need approximation without being supported at all");
        OnnxPiecewiseApproximation.Approximated.Should().BeEquivalentTo(
            new[] { "Sigmoid", "Tanh", "Softplus", "Elu" },
            "these are the four the baseline approximates; the other four lower exactly");
        OnnxPiecewiseApproximation.Operators.Should().HaveCount(8);
    }

    private static double MidpointError(
        OnnxPiecewiseApproximation.Approximation approximation,
        Func<double, double> function,
        double lower,
        double upper,
        int segments) {
        double worst = 0.0;
        for (int index = 0; index < segments; ++index) {
            double midpoint = lower + (upper - lower) * (index + 0.5) / segments;
            worst = System.Math.Max(worst, System.Math.Abs(approximation.Evaluate(midpoint) - function(midpoint)));
        }
        return worst;
    }
}