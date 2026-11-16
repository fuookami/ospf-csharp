#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **输入界 → 模型约束：闭区间，不是开区间** / Bounds become constraints, closed rather than open.
/// </summary>
/// <remarks>
/// 里"选项的界 → 模型的界"这一步。**C# 侧变量的 `Range` 由类型常量给出、逐变量没有设置入口，**
/// **所以界只能表达为约束——而这不是退路：求解器最终看到的就是约束。**
///
/// Syncs the "options bounds → model bounds" step. **A C# variable's `Range` comes from the type's constants and has no per-variable setter, so bounds can only be
/// expressed as constraints — which is not a fallback: constraints are what the solver sees.**
/// </remarks>
public class OnnxBoundsConstraintsTests
{
    /// <summary>**一个区间生成两条约束，方向与开闭都正确** / One range yields two constraints with the right direction and closure.</summary>
    /// <remarks>
    /// **开闭写错在紧解上会给出不同的最优值：** **`x >= a` 与 `x > a` 在 `x = a` 处相差一个可行点，** **而模型照常求解、照常"成功"。**
    ///
    /// **Closure changes the optimum on tight solutions: `x >= a` and `x > a` differ by the feasible point at `a`, and the model still solves and still reports success.**
    /// </remarks>
    [Fact]
    public void OneRange_ShouldYieldTwoConstraints() {
        var x = new RealVar("x");
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.For(x, new OnnxBounds(-2.0, 5.0), "bound_x");

        generated.Failure.Should().BeNull();
        generated.Constraints.Should().HaveCount(2, "a closed interval needs both sides");

        generated.Constraints[0].Comparison.Should().Be(Comparison.GE, "the lower side includes its endpoint");
        generated.Constraints[0].Rhs.Constant.Should().Be(new Flt64(-2.0));
        generated.Constraints[0].Lhs.Monomials.Should().ContainSingle(
            monomial => ReferenceEquals(monomial.Symbol, x) && monomial.Coefficient.Eq(Flt64.One),
            "the left side is the variable itself with coefficient one");

        generated.Constraints[1].Comparison.Should().Be(Comparison.LE);
        generated.Constraints[1].Rhs.Constant.Should().Be(new Flt64(5.0));
    }

    /// <summary>**两个方向都用非严格比较** / Both directions use the non-strict comparison.</summary>
    [Fact]
    public void BothDirections_ShouldBeNonStrict() {
        var x = new RealVar("x");
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.For(x, new OnnxBounds(0.0, 1.0));

        foreach (LinearInequality<Flt64> constraint in generated.Constraints) {
            constraint.Comparison.Should().NotBe(Comparison.GT, "an open endpoint drops a feasible point");
            constraint.Comparison.Should().NotBe(Comparison.LT);
        }
    }

    /// <summary>**退化区间 `[a, a]` 生成两条约束而不是报错** / A degenerate range yields two constraints rather than an error.</summary>
    /// <remarks>
    /// **`[3, 3]` 是"这个变量固定为 3"，一条完全正当的模型事实：** **把它当错误会让一批固定权的模型打不开。**
    ///
    /// **`[3, 3]` means "this variable is the constant 3", a perfectly legitimate modelling fact, and treating it as an error would turn away a class of models with fixed
    /// weights.**
    /// </remarks>
    [Fact]
    public void ADegenerateRange_ShouldStillYieldBothSides() {
        var x = new RealVar("x");
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.For(x, new OnnxBounds(3.0, 3.0));

        generated.Failure.Should().BeNull();
        generated.Constraints.Should().HaveCount(2);
        generated.Constraints[0].Rhs.Constant.Should().Be(new Flt64(3.0));
        generated.Constraints[1].Rhs.Constant.Should().Be(new Flt64(3.0));
    }

    /// <summary>**倒挂或非有限的区间被拒绝** / An inverted or non-finite range is refused.</summary>
    [Fact]
    public void AnUnusableRange_ShouldBeRefused() {
        var x = new RealVar("x");
        OnnxBoundsConstraints.For(x, new OnnxBounds(5.0, 1.0)).Failure.Should().NotBeNull();
        OnnxBoundsConstraints.For(x, new OnnxBounds(double.NaN, 1.0)).Failure.Should().NotBeNull();
        OnnxBoundsConstraints.For(x, new OnnxBounds(0.0, double.PositiveInfinity)).Failure.Should().NotBeNull(
            "an infinite side is not a bound the model can hold");
    }

    /// <summary>**有变量没界时点名，而不是静默跳过** / A variable without a range is named, not skipped.</summary>
    /// <remarks>
    /// **跳过等于让那个变量无界：** **求解器会把它推到某个端点，解里出现一个没人预期的极值，** **却看不出是因为少了一条约束。**
    ///
    /// **Skipping leaves it unbounded: the solver pushes it to some extreme, the answer carries an unexpected value, and nothing shows a constraint went missing.**
    /// </remarks>
    [Fact]
    public void AVariableWithoutARange_ShouldBeNamed() {
        var x = new RealVar("x");
        var y = new RealVar("y");
        var variables = new Dictionary<string, ISymbol> { ["x"] = x, ["y"] = y };
        var bounds = new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(-1.0, 1.0) };

        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.ForAll(variables, bounds, out IReadOnlyList<string> missing);

        generated.Failure.Should().BeNull("the well-formed entry is still generated");
        generated.Constraints.Should().HaveCount(2, "only x has a range");
        missing.Should().Equal(new[] { "y" }, "and the one left out has to be named");
    }

    /// <summary>**全部有界时 `missing` 为空且约束数正确** / With every range present the count is exact.</summary>
    [Fact]
    public void WithEveryRange_TheCountShouldBeExact() {
        var x = new RealVar("x");
        var y = new RealVar("y");
        var variables = new Dictionary<string, ISymbol> { ["x"] = x, ["y"] = y };
        var bounds = new Dictionary<string, OnnxBounds> {
            ["x"] = new OnnxBounds(-1.0, 1.0),
            ["y"] = new OnnxBounds(0.0, 4.0),
        };

        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.ForAll(variables, bounds, out IReadOnlyList<string> missing);

        missing.Should().BeEmpty();
        generated.Constraints.Should().HaveCount(4, "two sides per variable");
    }
}
