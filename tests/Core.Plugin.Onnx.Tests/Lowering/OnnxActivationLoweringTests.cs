#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **精确激活算子的代数化简** / Algebraic simplification of the exact activations.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation` 精确那一半。**判据钉的是"哪些情形可以化简成纯仿射"，而这关系到正确性：**
/// **化简判错会让模型多加约束（对偶值不可解释），或不化简而引入一个从不触发的分段结构。**
///
/// Syncs the exact half of `OnnxActivation`. **The criteria pin which cases collapse to pure affine form, and that is about correctness: a wrong judgement adds
/// constraints that make the duals uninterpretable, or fails to collapse and builds a piecewise structure that never triggers.**
/// </remarks>
public class OnnxActivationLoweringTests
{
    /// <summary>**`alpha = 1` 的 `LeakyRelu` 化简成输入本身** / LeakyRelu with alpha = 1 collapses to the input.</summary>
    /// <remarks>
    /// **"加一个恒等函数符号"也能算出正确答案，但每节点多一个变量、多一条等式，** **几百个节点就让模型凭空大一倍。**
    ///
    /// **"An identity function symbol" also computes the right answer, but costs a variable and an equality per node — a few hundred nodes double the model for
    /// free.**
    /// </remarks>
    [Fact]
    public void LeakyReluWithUnitAlpha_ShouldCollapseToTheInput() {
        LinearPolynomial<Flt64> input = Affine("x", 2.0, 1.0);
        OnnxActivationLowering.Result result = OnnxActivationLowering.SimplifyLeakyRelu(input, 1.0);

        result.Failure.Should().BeNull();
        result.NeedsFunctionSymbol.Should().BeFalse("an identity needs no symbol");
        result.Polynomial.Should().BeSameAs(input, "the input object itself is the answer; a copy would be needless allocation per node");
    }

    /// <summary>**负的 `alpha` 被拒绝** / A negative alpha is refused.</summary>
    [Fact]
    public void ANegativeAlpha_ShouldBeRefused() {
        OnnxActivationLowering.SimplifyLeakyRelu(Affine("x", 1.0, 0.0), -0.1).Failure.Should().NotBeNull();
        OnnxActivationLowering.SimplifyLeakyRelu(Affine("x", 1.0, 0.0), 0.1).NeedsFunctionSymbol.Should()
            .BeTrue("at 0.1 the function actually bends and needs a symbol");
    }

    /// <summary>**两侧都缺省的 `Clip` 化简成输入本身** / Clip with neither bound collapses to the input.</summary>
    /// <remarks>
    /// **钳到一个"形式无界的区间"也给出正确答案，但会引入两条永远不紧的约束：** **它们让"这个模型有多少条约束"这个诊断数字失去意义。**
    ///
    /// **Clamping to a "formally unbounded interval" also gives the right answer while adding two constraints that never bind, and their presence makes "how many
    /// constraints does this model have" meaningless as a diagnostic.**
    /// </remarks>
    [Fact]
    public void ClipWithoutBounds_ShouldCollapseToTheInput() {
        LinearPolynomial<Flt64> input = Affine("x", 1.0, 0.0);
        OnnxActivationLowering.Result result = OnnxActivationLowering.SimplifyClip(input, null, null);

        result.NeedsFunctionSymbol.Should().BeFalse();
        result.Polynomial.Should().BeSameAs(input);
    }

    /// <summary>**倒挂的 `Clip` 区间被拒绝，单边缺省则保留** / An inverted Clip range is refused; a one-sided range stays.</summary>
    [Fact]
    public void Clip_ShouldRefuseOnlyAnInvertedRange() {
        OnnxActivationLowering.SimplifyClip(Affine("x", 1.0, 0.0), 5.0, 1.0).Failure.Should().NotBeNull();
        OnnxActivationLowering.SimplifyClip(Affine("x", 1.0, 0.0), null, 1.0).NeedsFunctionSymbol.Should()
            .BeTrue("one bound still clamps");
        OnnxActivationLowering.SimplifyClip(Affine("x", 1.0, 0.0), -1.0, 1.0).NeedsFunctionSymbol.Should().BeTrue();
    }

    /// <summary>**输入区间整段非负时 `Relu` 化简成输入** / Relu collapses to the input when the range is non-negative.</summary>
    /// <remarks>
    /// **这是正确性问题不是优化：** **对一段已经非负的输入硬建正部函数，会多一个变量与两条约束，** **它们不改变解，但让对偶值不可解释。**
    ///
    /// **This is correctness rather than optimisation: a positive-part function over an already non-negative input adds a variable and two constraints that do not
    /// change the answer while making the duals uninterpretable.**
    /// </remarks>
    [Fact]
    public void Relu_ShouldCollapseAtTheEdgesOfItsRange() {
        LinearPolynomial<Flt64> input = Affine("x", 1.0, 0.0);

        OnnxActivationLowering.SimplifyRelu(input, 0.0, 5.0).Polynomial.Should().BeSameAs(input, "zero is already on the positive side");
        OnnxActivationLowering.SimplifyRelu(input, 2.0, 5.0).Polynomial.Should().BeSameAs(input);

        OnnxActivationLowering.Result negative = OnnxActivationLowering.SimplifyRelu(input, -5.0, -1.0);
        negative.NeedsFunctionSymbol.Should().BeFalse();
        negative.Polynomial!.Monomials.Should().BeEmpty("the whole range is negative, so the positive part is the constant zero");
        negative.Polynomial.Constant.Should().Be(Flt64.Zero);

        OnnxActivationLowering.SimplifyRelu(input, -1.0, 1.0).NeedsFunctionSymbol.Should()
            .BeTrue("a range crossing zero is where the positive part actually bends");
    }

    /// <summary>**`Relu` 拒绝非有限与倒挂的区间** / Relu refuses non-finite and inverted ranges.</summary>
    [Fact]
    public void Relu_ShouldRefuseUnusableRanges() {
        LinearPolynomial<Flt64> input = Affine("x", 1.0, 0.0);

        OnnxActivationLowering.SimplifyRelu(input, double.NaN, 1.0).Failure.Should().NotBeNull();
        OnnxActivationLowering.SimplifyRelu(input, -1.0, double.PositiveInfinity).Failure.Should().NotBeNull();
        OnnxActivationLowering.SimplifyRelu(input, 5.0, 1.0).Failure.Should().NotBeNull();
    }

    /// <summary>**整段落在 [0,1] 内的 `HardSigmoid` 化简成仿射式** / A HardSigmoid that never clamps becomes affine.</summary>
    /// <remarks>
    /// **这是把"带钳位的节点"变成"一条线性表达式"的唯一机会：** **漏掉它，模型里就多出一整个分段结构，只为表示一个从不触发的钳位。**
    ///
    /// **This is the only chance to turn a clamping node into one linear expression: miss it and a whole piecewise structure represents a clamp that never fires.**
    /// </remarks>
    [Fact]
    public void HardSigmoid_ShouldCollapseWhenTheClampCannotFire() {
        LinearPolynomial<Flt64> input = Affine("x", 1.0, 0.0);

        // 0.2·x + 0.5 在 [-1, 1] 上的范围是 [0.3, 0.7]，整段在 [0, 1] 内。
        OnnxActivationLowering.Result inside = OnnxActivationLowering.SimplifyHardSigmoid(input, 0.2, 0.5, -1.0, 1.0);
        inside.NeedsFunctionSymbol.Should().BeFalse("the clamp never fires, so the result is affine");
        inside.Polynomial!.Monomials.Should().HaveCount(1);
        inside.Polynomial.Monomials[0].Coefficient.Should().Be(new Flt64(0.2), "the slope carries through");
        inside.Polynomial.Constant.Should().Be(new Flt64(0.5), "and so does the offset");

        // 同一个算子在 [-5, 5] 上会触到上界，必须交给函数符号。
        OnnxActivationLowering.SimplifyHardSigmoid(input, 0.2, 0.5, -5.0, 5.0).NeedsFunctionSymbol.Should()
            .BeTrue("this range reaches past 1, so the clamp does fire");
    }

    /// <summary>**`HardSigmoid` 的单调性让端点检查足够** / Monotonicity makes the endpoint check sufficient.</summary>
    /// <remarks>
    /// **仿射函数在区间上单调，所以"两端都在 `[0,1]` 内"等价于"整段都在"：** **若哪天有人改成"检查中点"或"检查若干采样点"，**
    /// **判据里这个用例会失败——** **而那正是"看起来近似正确、实际漏掉一个端点"的地方。**
    ///
    /// **An affine function is monotone over an interval, so "both ends inside [0,1]" is equivalent to "the whole segment is": a rewrite to check the midpoint or a few
    /// samples fails this case, which is exactly where a plausible-looking check misses an endpoint.**
    /// </remarks>
    [Fact]
    public void HardSigmoid_ShouldCheckBothEndpoints() {
        LinearPolynomial<Flt64> input = Affine("x", 1.0, 0.0);

        // 上端恰好等于 1，仍算"不触发"。 / The upper end is exactly 1, which still counts as "does not fire".
        OnnxActivationLowering.SimplifyHardSigmoid(input, 0.1, 0.5, -1.0, 5.0).NeedsFunctionSymbol.Should().BeFalse();

        // 再放宽一点点就越界了。 / One step wider and it leaves the interval.
        OnnxActivationLowering.SimplifyHardSigmoid(input, 0.1, 0.5, -1.0, 5.001).NeedsFunctionSymbol.Should().BeTrue();

        // 负斜率也要被端点检查覆盖。 / A negative slope has to be covered by the same check.
        OnnxActivationLowering.SimplifyHardSigmoid(input, -0.1, 0.5, -1.0, 1.0).NeedsFunctionSymbol.Should().BeFalse();
        OnnxActivationLowering.SimplifyHardSigmoid(input, -0.1, 0.5, -1.0, 6.0).NeedsFunctionSymbol.Should().BeTrue();
    }

    private static LinearPolynomial<Flt64> Affine(string name, double coefficient, double constant) {
        var variable = new RealVar(name);
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), variable) },
            new Flt64(constant));
    }
}