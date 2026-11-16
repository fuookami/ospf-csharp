#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **四个近似算子的降阶：三条契约** / Lowering the four approximated operators: three contracts.
/// </summary>
/// <remarks>
/// `OnnxActivation` 近似那一半，把切分策略接到 C# 的 `UnivariateLinearPiecewiseFunction`。
/// **三条契约每条漏掉都只会在求解阶段表现为"结果不对"**——**断点严格递增、段数等于断点数减一、边界真的有限。**
///
/// Syncs the approximated half of `OnnxActivation`, wiring the segmentation into `UnivariateLinearPiecewiseFunction`. **Each contract, when missed, shows up
/// only as "the answer is wrong" after solving: breakpoints strictly increasing, segments equal to breakpoints minus one, bounds actually finite.**
/// </remarks>
public class OnnxApproximationLoweringTests
{
    /// <summary>**四个近似算子都切出正确的分段** / All four produce the right segmentation.</summary>
    /// <remarks>
    /// **这一条只看切分结果，不看函数符号：** **符号构造要求输入已注册有限界（模型层前提），** **而切分是否正确与那件事无关——**
    /// **把两者混在一起会让"构造失败"掩盖"切分错了"。**
    ///
    /// **This one looks only at the segmentation, not the symbol: construction requires finite bounds on the inputs (a model-layer precondition), and whether the
    /// segmentation is right has nothing to do with that — mixing the two lets "construction failed" hide "the split was wrong".**
    /// </remarks>
    [Fact]
    public void TheFourApproximatedOperators_ShouldSegmentCorrectly() {
        foreach (string opType in new[] { "Sigmoid", "Tanh", "Softplus", "Elu" }) {
            OnnxApproximationLowering.Lowering result = OnnxApproximationLowering.Lower(opType, Affine(), -2.0, 2.0, segments: 8);

            result.Pieces.Should().NotBeNull(opType + " has to produce a segmentation");
            result.Pieces!.Breakpoints.Should().HaveCount(9, opType + " keeps every breakpoint the segmentation produced");
            result.Pieces.Slopes.Should().HaveCount(8, opType + " has one slope per segment");
            result.Pieces.Intercepts.Should().HaveCount(8);
            result.Pieces.Breakpoints[0].Should().Be(-2.0);
            result.Pieces.Breakpoints[^1].Should().Be(2.0);
        }
    }

    /// <summary>**缺界时函数符号不构造，但切分结果仍然带出来** / Without bounds no symbol is built, yet the pieces come back.</summary>
    /// <remarks>
    /// **这是分层的结果而不是缺陷：** **切分只需要区间，符号还需要输入变量上已注册的界。** **判据把两件事分开钉，**
    /// **于是"构造失败"不会被误读成"切分错了"。**
    ///
    /// **This is the layering showing, not a defect: the segmentation needs only the range, the symbol needs bounds registered on the input variable. The criteria pin
    /// the two separately, so "construction failed" is not misread as "the split was wrong".**
    /// </remarks>
    [Fact]
    public void WithoutRegisteredBounds_ThePiecesStillComeBack() {
        OnnxApproximationLowering.Lowering result = OnnxApproximationLowering.Lower("Tanh", Affine(), -1.0, 1.0);

        result.Pieces.Should().NotBeNull("the segmentation itself does not depend on registered bounds");
        result.Function.Should().BeNull("a bare RealVar carries ±double.MaxValue, which is not a usable bound");
        result.Failure.Should().NotBeNull("and that has to be reported rather than papered over");
    }

    /// <summary>**非近似算子被拒绝，不静默当恒等** / A non-approximated operator is refused rather than treated as identity.</summary>
    /// <remarks>
    /// **`Relu` 走到这条路上意味着派发写错了：** **把它当恒等返回会让激活完全消失，** **而模型照常求解、照常给出一个"最优解"。**
    ///
    /// **Relu reaching this path means the dispatch is wrong: returning the identity would erase the activation entirely, and the model still solves and still returns
    /// an "optimum".**
    /// </remarks>
    [Fact]
    public void ANonApproximatedOperator_ShouldBeRefused() {
        OnnxApproximationLowering.Lower("Relu", Affine(), -1.0, 1.0).Failure.Should().NotBeNull();
        OnnxApproximationLowering.Lower("Add", Affine(), -1.0, 1.0).Failure.Should().NotBeNull();
        OnnxApproximationLowering.Lower("Gelu", Affine(), -1.0, 1.0).Failure.Should().NotBeNull();
    }

    /// <summary>**缺区间被拒绝，函数不构造** / A missing range is refused and no function is built.</summary>
    /// <remarks>
    /// **这条与切分策略那一层是同一条规则，在这里再钉一次：** **分层之后任何一层漏掉它，另一层都可能被绕过——**
    /// **而"用 ±double.MaxValue 切出来的 Sigmoid"是一个能求解、能返回最优解、却不描述任何东西的模型。**
    ///
    /// **This is the same rule as in the segmentation layer, pinned again here: with layers involved, missing it in one can be bypassed by the other, and "a sigmoid
    /// split over ±double.MaxValue" is a model that solves, returns an optimum, and describes nothing.**
    /// </remarks>
    [Fact]
    public void AnUnboundedRange_ShouldBeRefused() {
        OnnxApproximationLowering.Lowering result = OnnxApproximationLowering.Lower(
            "Tanh", Affine(), -double.MaxValue, double.MaxValue);

        result.Pieces.Should().BeNull("an approximation over the whole double range is meaningless, so nothing is even segmented");
        result.Function.Should().BeNull();
        result.Failure.Should().NotBeNull();
    }

    /// <summary>**段数越界被拒绝** / An out-of-range segment count is refused.</summary>
    [Fact]
    public void AnIllegalSegmentCount_ShouldBeRefused() {
        OnnxApproximationLowering.Lower("Tanh", Affine(), -1.0, 1.0, segments: 1).Pieces.Should().BeNull();
        OnnxApproximationLowering.Lower("Tanh", Affine(), -1.0, 1.0, segments: 1025).Pieces.Should().BeNull();
        OnnxApproximationLowering.Lower("Tanh", Affine(), -1.0, 1.0, segments: 2).Pieces.Should().NotBeNull();
    }

    /// <summary>**退化区间：断点相等必须被拒绝，而不是构造出零宽段** / A degenerate range must be refused, not turned into zero-width segments.</summary>
    /// <remarks>
    /// **切分策略在 `lower == upper` 时塌缩成"一个常数 + 零斜率"的单段，** **而那个形状在分段线性函数符号里不是合法输入——**
    /// **硬塞进去会得到一条"每段都零宽"的结构，** **而它的解是任意的。**
    ///
    /// **The segmentation collapses a degenerate range into one constant piece with zero slope, and that shape is not a legal piecewise input: forcing it through yields a
    /// structure whose every segment has zero width, and whose value is therefore arbitrary.**
    /// </remarks>
    [Fact]
    public void ADegenerateRange_ShouldNotProduceZeroWidthSegments() {
        OnnxApproximationLowering.Lowering result = OnnxApproximationLowering.Lower("Tanh", Affine(), 1.0, 1.0);

        if (result.Pieces is not null) {
            for (int index = 1; index < result.Pieces.Breakpoints.Count; ++index) {
                result.Pieces.Breakpoints[index].Should().BeGreaterThan(
                    result.Pieces.Breakpoints[index - 1],
                    "a segmentation must never contain a zero-width segment");
            }
        }
        else {
            result.Failure.Should().NotBeNull("or it is refused, which is the other acceptable outcome");
        }
    }

    /// <summary>**`Elu` 的 `alpha` 真的传进函数里** / Elu's alpha reaches the function.</summary>
    /// <remarks>
    /// **`Elu` 是四个里唯一带参数的：** **漏传会退回 `alpha = 1`，** **而 `alpha = 1` 恰好是默认值——**
    /// **于是"没传"在默认情形下完全看不出来，只在别人显式指定 `alpha = 0.5` 时才错。**
    ///
    /// **Elu is the only one of the four with a parameter: dropping it falls back to alpha = 1, which is also the default — so "not passed" is invisible in the default
    /// case and only wrong when someone explicitly asks for 0.5.**
    /// </remarks>
    [Fact]
    public void Elu_ShouldCarryItsAlpha() {
        OnnxApproximationLowering.Lowering defaultAlpha = OnnxApproximationLowering.Lower("Elu", Affine(), -2.0, 2.0);
        OnnxApproximationLowering.Lowering halfAlpha = OnnxApproximationLowering.Lower("Elu", Affine(), -2.0, 2.0, alpha: 0.5);

        defaultAlpha.Pieces.Should().NotBeNull();
        halfAlpha.Pieces.Should().NotBeNull();
        halfAlpha.Pieces!.Slopes[0].Should().NotBe(
            defaultAlpha.Pieces!.Slopes[0],
            "halving alpha has to change the negative-side slope, otherwise the parameter is decorative");
    }

    private static LinearPolynomial<Flt64> Affine() {
        var variable = new RealVar("x");
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);
    }
}