#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **两个一次式相乘：合并同类项必须做对** / Two first-degree forms multiplied: merging like terms has to be right.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `Mul`／`MatMul` 在 `Quadratic` 模式下的那一半。**一次域里 `Mul` 只允许"变量乘常数"，'
/// **而两个含变量的式子相乘不再是多项式一次——硬塞进一次域只能得到一个错误的结果，而它照常可解。**
///
/// Syncs the `Quadratic`-mode half of `Mul` and `MatMul`. **In the first-degree domain Mul only allows a variable times a constant, while two forms that both carry variables
/// are no longer first degree — forcing them into the first-degree domain yields a wrong result that still solves.**
/// </remarks>
public class OnnxQuadraticProductTests
{
    /// <summary>**`(x+y)(x−y)` 的交叉项互相抵消** / The cross terms of (x+y)(x−y) cancel.</summary>
    /// <remarks>
    /// **这条存在的理由：** **展开出 `x²`、`−y²` 与 `xy`、`−xy` 四项，后两项必须是同一格才能抵消。** '
    /// **按有序对做键，结果里会留下一项 `xy`——** **而求解器会用它去凑一个"更优"的点，那个点根本不满足原来的问题。**
    ///
    /// **This is why the batch exists: the expansion gives x², −y² and xy, −xy, and the last two have to land in the same cell to cancel.** **Keyed by an ordered pair, an xy
    /// term survives — and the solver uses it to reach a "better" point that does not satisfy the original problem at all.**
    /// </remarks>
    [Fact]
    public void TheCrossTerms_ShouldCancel() {
        LinearPolynomial<Flt64> left = Sum("x", "y", firstCoefficient: 1.0, secondCoefficient: 1.0);
        LinearPolynomial<Flt64> right = Sum("x", "y", firstCoefficient: 1.0, secondCoefficient: -1.0);

        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(left, right);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().HaveCount(2, "x² and −y² survive; the two xy terms cancel");
        CoefficientOf(result.Polynomial, "x", "x").Should().Be(1.0);
        CoefficientOf(result.Polynomial, "y", "y").Should().Be(-1.0);
        CoefficientOf(result.Polynomial, "x", "y").Should().Be(0.0, "an xy term here would be the mistake this criterion exists for");
    }

    /// <summary>**`(x+1)(x+1) = x² + 2x + 1`** / The square expands with its cross terms doubled.</summary>
    [Fact]
    public void ASquare_ShouldDoubleTheCrossTerm() {
        LinearPolynomial<Flt64> x = Sum("x", null, firstCoefficient: 1.0, constant: 1.0);

        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(x, x);

        result.IsOk.Should().BeTrue(result.Failure);
        CoefficientOf(result.Polynomial!, "x", "x").Should().Be(1.0);
        CoefficientOf(result.Polynomial!, "x", null).Should().Be(2.0, "each factor contributes one x, and both are added");
        result.Polynomial!.Constant.ToDouble().Should().Be(1.0);
    }

    /// <summary>**`x · y` 是一对交叉项，不是零** / x times y is one cross term, not zero.</summary>
    [Fact]
    public void TwoDifferentVariables_ShouldGiveOneCrossTerm() {
        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(Sum("x", null, firstCoefficient: 1.0), Sum("y", null, firstCoefficient: 1.0));

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().HaveCount(1);
        CoefficientOf(result.Polynomial, "x", "y").Should().Be(1.0);
    }

    /// <summary>**常数乘变量仍是一次项** / A constant times a variable stays a first-degree term.</summary>
    [Fact]
    public void AConstantFactor_ShouldStayFirstDegree() {
        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(Sum("x", null, firstCoefficient: 1.0), Constant(3.0));

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().HaveCount(1);
        CoefficientOf(result.Polynomial, "x", null).Should().Be(3.0, "a constancy term is first degree, written with the second symbol absent");
        OnnxQuadraticProduct.IsConstant(Constant(3.0)).Should().BeTrue();
        OnnxQuadraticProduct.IsConstant(Sum("x", null, firstCoefficient: 1.0)).Should().BeFalse();
    }

    /// <summary>**两个常数相乘只给常数** / Two constants give a constant.</summary>
    [Fact]
    public void TwoConstants_ShouldGiveAConstant() {
        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(Constant(2.0), Constant(5.0));

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().BeEmpty();
        result.Polynomial.Constant.ToDouble().Should().Be(10.0);
    }

    /// <summary>**系数为零的项不会留下来** / Terms with zero coefficient are dropped.</summary>
    /// <remarks>
    /// **留着一个系数为零的项不会改变数学，却会改变展平后的列数：** **模型里多一列，** **而报告"变量数"或做敏感性分析时那一列是噪声。**
    ///
    /// **Keeping a zero-coefficient term does not change the mathematics but changes the column count after flattening: the model gains a column that is pure noise in any
    /// report about "how many variables" or in a sensitivity analysis.**
    /// </remarks>
    [Fact]
    public void AZeroCoefficientTerm_ShouldBeDropped() {
        LinearPolynomial<Flt64> zeroX = new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        OnnxQuadraticProduct.Result result = OnnxQuadraticProduct.Multiply(Sum("x", null, firstCoefficient: 1.0), zeroX);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Polynomial!.Monomials.Should().BeEmpty("a product with a zero form is zero, not a pile of zero-coefficient terms");
    }

    /// <summary>**空的一方被拒绝，而不是给出零** / A null factor is refused rather than treated as zero.</summary>
    /// <remarks>
    /// **把空当作零会让"读错了一个输入"表现成"结果恒为零"——** **而恒为零的模型永远可解，也永远没有意义。**
    ///
    /// **Treating null as zero turns "an input was misread" into "the result is always zero" — and a model that is always zero always solves and never means anything.**
    /// </remarks>
    [Fact]
    public void ANullFactor_ShouldBeRefused() =>
        OnnxQuadraticProduct.Multiply(null!, Sum("x", null, firstCoefficient: 1.0)).IsOk.Should().BeFalse();

    // 系数逐个写清楚：上一版把"第二个系数"绑到第一个系数上，于是 `(x+y)(x−y)` 变成了 `(x+y)(−x−y)`。
    // Each coefficient is explicit: the earlier version tied the second coefficient to the first, so (x+y)(x−y) became (x+y)(−x−y).
    private static LinearPolynomial<Flt64> Sum(string first, string? second, double firstCoefficient, double secondCoefficient = 0.0, double constant = 0.0) {
        var monomials = new List<LinearMonomial<Flt64>> {
            new(new Flt64(firstCoefficient), new RealVar(first)),
        };
        if (second is not null) {
            monomials.Add(new LinearMonomial<Flt64>(new Flt64(secondCoefficient), new RealVar(second)));
        }
        return new LinearPolynomial<Flt64>(monomials, new Flt64(constant));
    }

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static double CoefficientOf(QuadraticPolynomial<Flt64> polynomial, string? first, string? second) {
        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            bool firstMatches = monomial.Symbol1.Name == first;
            bool secondMatches = second is null
                ? monomial.Symbol2 is null
                : monomial.Symbol2?.Name == second;
            if (firstMatches && secondMatches) {
                return monomial.Coefficient.ToDouble();
            }
        }
        return 0.0;
    }
}