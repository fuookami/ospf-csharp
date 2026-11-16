#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **仿射多项式 → 线性模型：变量与目标是两份独立的真相** / Assembling a polynomial into a linear model.
/// </summary>
/// <remarks>
/// `OnnxFunctionalModel` 里"仿射目标 → 线性模型"那一小段。**覆盖范围明确有限：只有仿射链。**
///
/// Syncs the "affine objective → linear model" fragment. **Coverage is deliberately narrow: affine chains only.**
/// </remarks>
public class OnnxAffineModelBuilderTests
{
    /// <summary>**变量按出现顺序编号，界原样落到变量上** / Variables are numbered in order and carry their bounds.</summary>
    [Fact]
    public void TheVariables_ShouldCarryTheirBoundsInOrder() {
        var x = new RealVar("x");
        var y = new RealVar("y");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), x),
                new LinearMonomial<Flt64>(new Flt64(-1.0), y),
            },
            new Flt64(5.0));

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(
            polynomial,
            new Dictionary<string, OnnxBounds> { ["x"] = new(0.0, 10.0), ["y"] = new(-3.0, 4.0) });

        built.Failure.Should().BeNull(built.Failure);
        built.Model!.Variables.Should().HaveCount(2);
        built.Model.Variables[0].Name.Should().Be("x", "the order follows the polynomial, so a coefficient and its column agree");
        built.Model.Variables[0].LowerBound.ToDouble().Should().Be(0.0);
        built.Model.Variables[0].UpperBound.ToDouble().Should().Be(10.0);
        built.Model.Variables[1].Name.Should().Be("y");
    }

    /// <summary>**目标系数与多项式系数一一对应** / Objective coefficients follow the polynomial exactly.</summary>
    /// <remarks>
    /// **系数与列号错位不会有任何报错：** **模型照常求解，解却是另一个问题的解。**
    ///
    /// **A coefficient and its column drifting apart throws nothing: the model solves and the answer belongs to another problem.**
    /// </remarks>
    [Fact]
    public void TheObjective_ShouldFollowThePolynomialCoefficients() {
        var x = new RealVar("x");
        var y = new RealVar("y");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), x),
                new LinearMonomial<Flt64>(new Flt64(-1.5), y),
            },
            Flt64.Zero);

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(
            polynomial,
            new Dictionary<string, OnnxBounds> { ["x"] = new(0.0, 1.0), ["y"] = new(0.0, 1.0) });

        built.Model!.Objective.Cells.Should().HaveCount(2);
        built.Model.Objective.Cells[0].Coefficient.ToDouble().Should().Be(2.0);
        built.Model.Objective.Cells[1].Coefficient.ToDouble().Should().Be(-1.5);
        built.Model.Objective.Category.Should().Be(ObjectCategory.Minimum);
    }

    /// <summary>**缺区间的变量让组装失败** / A variable without a range fails the assembly.</summary>
    /// <remarks>
    /// **给缺失的符号补一个 ±∞ 的界会让模型"可解"，而那个变量会跑到无界方向，解里出现一个没人预期的极值。**
    ///
    /// **Filling a missing symbol with ±infinity makes the model "solvable" while that variable runs to infinity and puts an unexpected extreme in the answer.**
    /// </remarks>
    [Fact]
    public void AMissingRange_ShouldFail() {
        var x = new RealVar("x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero);

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(polynomial, new Dictionary<string, OnnxBounds>());

        built.Model.Should().BeNull();
        built.Failure.Should().Contain("x", "the missing variable has to be named");
    }

    /// <summary>**没有变量项的目标被拒绝** / An objective with no variable term is refused.</summary>
    /// <remarks>
    /// **常数目标对任何可行解都成立：** **求解器会返回"最优"，而那个"最优"与模型里的任何选择都无关——**
    /// **它看起来像一次成功的求解，实际上没有回答任何问题。**
    ///
    /// **A constant objective holds for every feasible solution: the solver returns "optimal", that optimum is unrelated to any choice in the model, and it looks like a
    /// successful solve while answering nothing.**
    /// </remarks>
    [Fact]
    public void AConstantObjective_ShouldBeRefused() {
        var polynomial = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(7.0));

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(polynomial, new Dictionary<string, OnnxBounds>());

        built.Model.Should().BeNull();
        built.Failure.Should().NotBeNull();
    }

    /// <summary>**倒挂的区间在组装时被拒绝** / An inverted range is refused during assembly.</summary>
    [Fact]
    public void AnInvertedRange_ShouldBeRefused() {
        var x = new RealVar("x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero);

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(
            polynomial,
            new Dictionary<string, OnnxBounds> { ["x"] = new(5.0, 1.0) });

        built.Model.Should().BeNull();
        built.Failure.Should().NotBeNull();
    }
}
