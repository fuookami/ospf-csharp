#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **分段近似：与基线对齐的六处行为** / The piecewise approximation and the six behaviours aligned with the baseline.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation.approximated`。**与基线一致的四处：** **超过 1e6 的界按"缺少有效范围"处理；'
/// **`lower == upper` 直接塌缩成常数；断点均匀且在断点上<b>精确插值**（不是拟合）；分段数区间 `2..1024`、缺省 `8`。**
///
/// Syncs `approximated`. **Four things follow the baseline: a bound beyond 1e6 counts as missing; lower == upper collapses to a constant; breakpoints are uniform with exact
/// interpolation; and the segment range is 2..1024 with a default of 8.**
/// </remarks>
public class OnnxSymbolicActivationTests
{
    /// <summary>**可近似与可正部是两份名单** / Approximable and positive-part are two different lists.</summary>
    [Fact]
    public void TheTwoLists_ShouldBeSeparate() {
        OnnxSymbolicActivation.CanExpress("Relu").Should().BeTrue();
        OnnxSymbolicActivation.CanApproximate("Relu").Should().BeFalse("Relu is exact, it needs no approximation");

        OnnxSymbolicActivation.CanApproximate("Sigmoid").Should().BeTrue();
        OnnxSymbolicActivation.CanApproximate("Tanh").Should().BeTrue();
        OnnxSymbolicActivation.CanApproximate("Softplus").Should().BeTrue();
        OnnxSymbolicActivation.CanApproximate("Elu").Should().BeTrue();
        OnnxSymbolicActivation.CanExpress("Sigmoid").Should().BeFalse("a sigmoid is not a positive part");
    }

    /// <summary>**分段数超出 `2..1024` 被拒绝** / A segment count outside 2..1024 is refused.</summary>
    /// <remarks>
    /// **与基线同一个区间、同一个缺省：** **缺省变成 32 会让"同一份模型在两边得到不同的近似"，** **而两边的答案都能求解——只是差在没人会对的第三位小数上。**
    ///
    /// **Same range and same default as the baseline: a default of 32 would let the same model be approximated differently on the two sides, and both answers solve — they just
    /// differ in a decimal place nobody checks.**
    /// </remarks>
    [Fact]
    public void TheSegmentCount_ShouldRespectTheBaselineRange() {
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0, 1.0), segments: 1).IsOk.Should().BeFalse();
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0, 1.0), segments: 1025).IsOk.Should().BeFalse();
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0, 1.0), segments: 2).Symbol.Should().NotBeNull(
            "the inclusive lower end is usable");
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0, 1.0), segments: 1024).Symbol.Should().NotBeNull();
        OnnxPiecewiseApproximation.DefaultSegments.Should().Be(8, "the baseline's default is 8, and a different default is a silent divergence");
    }

    /// <summary>**超过 `1e6` 的界按"缺少有效范围"处理** / A bound beyond 1e6 is treated as missing.</summary>
    /// <remarks>
    /// **形式有限但实际无界的取值域不做近似：** **`RealVar` 的默认界是 ±`double.MaxValue`，那是"有限"的，** '
    /// **若照字面接受它，断点会稀疏到每一段跨过一个天文数字——** **而模型照常求解。**
    ///
    /// **A formally finite but practically unbounded domain gets no approximation: a RealVar defaults to ±double.MaxValue, which is "finite", and taking that literally spreads
    /// the breakpoints so thin that each segment spans an astronomical range — and the model still solves.**
    /// </remarks>
    [Fact]
    public void ABoundBeyondTheLimit_ShouldBeRefused() {
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0e7, 1.0e7)).IsOk.Should().BeFalse();
        OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(-1.0e6, 1.0e6)).Symbol.Should().NotBeNull(
            "exactly at the limit is still usable");
    }

    /// <summary>**界相等时塌缩成常数，不建函数** / Equal bounds collapse to a constant rather than a function.</summary>
    /// <remarks>
    /// **建一个零斜率的函数只是多一层没有意义的结构：** **它在模型里占一个结果变量、一条等式约束，** **而那个变量恒等于常数。**
    ///
    /// **Building a zero-slope function only adds a pointless layer: it occupies a result variable and an equality constraint in the model, and that variable is always the
    /// constant.**
    /// </remarks>
    [Fact]
    public void EqualBounds_ShouldCollapseToAConstant() {
        OnnxSymbolicActivation.Result collapsed = OnnxSymbolicActivation.Piecewise("Sigmoid", Shifted(), Range(0.0, 0.0));

        collapsed.Symbol.Should().BeNull(collapsed.Failure);
        collapsed.Constant.Should().NotBeNull();
        collapsed.Constant!.Value.ToDouble().Should().BeApproximately(0.5, 1e-12, "sigmoid(0) is exactly one half");
    }

    /// <summary>**正常区间给出函数符号，而不是常数** / A real interval yields a function symbol, not a constant.</summary>
    [Fact]
    public void ARealInterval_ShouldGiveASymbol() {
        OnnxSymbolicActivation.Result built = OnnxSymbolicActivation.Piecewise("Tanh", Shifted(), Range(-2.0, 2.0));

        built.Symbol.Should().NotBeNull(built.Failure);
        built.Constant.Should().BeNull("a non-degenerate interval cannot collapse");
    }

    /// <summary>**不在名单里的算子被拒绝** / An operator outside the list is refused.</summary>
    [Fact]
    public void AnOperatorOutsideTheList_ShouldBeRefused() {
        OnnxSymbolicActivation.Piecewise("MatMul", Shifted(), Range(-1.0, 1.0)).IsOk.Should().BeFalse();
        OnnxSymbolicActivation.Piecewise("Relu", Shifted(), Range(-1.0, 1.0)).IsOk.Should().BeFalse(
            "Relu is exact; sending it through an approximation would add error where none is needed");
    }

    private static OnnxBounds Range(double lower, double upper) => new(lower, upper);

    private static LinearPolynomial<Flt64> Shifted() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar("x")) }, Flt64.Zero);
}