#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 固定变量等式：一行，但方向与元都容易反 / One row, but direction and identities are easy to flip.
/// </summary>
/// <remarks>
/// 对齐基线 `DiscreteCondition.kt` 的 `fixedVariableEquality`。
///
/// **`EQ` 是对称的**，**所以"左右写反"在数值上完全测不出来**——
/// **只有系数与常数项的位置能分开。**
///
/// **`EQ` is symmetric**, **so "sides swapped" is numerically undetectable** — **only the positions of the coefficient
/// and the constant tell them apart.**
/// </remarks>
public class FixedVariableEqualityTests
{
    /// <summary>**等式把变量钉在值上** / The row pins the variable to the value.</summary>
    [Fact]
    public void TheRow_ShouldPinTheVariableToTheValue()
    {
        LinearInequality<Flt64> row = FixedVariableEquality.Of(
            SharedVariable, new Flt64(7.0), Flt64.Zero, Flt64.One, "pin");

        row.Comparison.Should().Be(Comparison.EQ);
        row.Name.Should().Be("pin");

        // 左端：系数 1 的变量，常数 0。
        // Left: the variable with coefficient one and constant zero.
        row.Lhs.Monomials.Should().HaveCount(1);
        row.Lhs.Monomials[0].Symbol.Should().BeSameAs(SharedVariable);
        row.Lhs.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12);
        row.Lhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12);

        // 右端：没有变量，常数就是那个值。
        // Right: no variable, the constant is the value.
        row.Rhs.Monomials.Should().BeEmpty();
        row.Rhs.Constant.ToDouble().Should().BeApproximately(7.0, 1e-12);
    }

    /// <summary>
    /// **系数与常数不能互换** / The coefficient and the constant must not swap.
    /// </summary>
    /// <remarks>
    /// **这是本函数唯一能测出来的错误。**
    /// **如果实现把 `one` 放到常数位、`zero` 放到系数位，左端会变成 `zero·x + one`，
    /// 右端仍是 `value`——那是一个完全不同的等式，而 `EQ` 的对称性掩盖不了它。**
    ///
    /// **This is the only error this function can expose.** **Swapping `one` into the constant slot and `zero` into the
    /// coefficient slot makes the left-hand side `zero·x + one` while the right stays `value` — a completely different
    /// equation, and `EQ`'s symmetry does not hide it.**
    /// </remarks>
    [Fact]
    public void TheCoefficientAndConstant_ShouldNotSwap()
    {
        LinearInequality<Flt64> row = FixedVariableEquality.Of(
            SharedVariable, new Flt64(7.0), Flt64.Zero, Flt64.One, "pin");

        row.Lhs.Constant.ToDouble().Should().NotBe(
            1.0, "the constant slot holds the zero, not the one");
        row.Lhs.Monomials[0].Coefficient.ToDouble().Should().NotBe(
            0.0, "the coefficient slot holds the one, not the zero");
    }

    /// <summary>**调用方给的单位元被真的用上** / The caller's identities are actually used.</summary>
    /// <remarks>
    /// **这条用非标准的零元与单位元（`0` 与 `2`）来测**：
    /// **如果实现忽略入参、自己现造 `0` 和 `1`，这里会读到 `1` 而不是 `2`。**
    /// **而"现造"在定点数或高精度数上不一定等于调用方的单位元。**
    ///
    /// **This uses non-standard identities (`0` and `2`)**: **an implementation that ignores the parameters and builds
    /// `0`/`1` itself would read `1` here instead of `2`.** **And "built itself" need not equal the caller's identity on
    /// fixed-point or high-precision types.**
    /// </remarks>
    [Fact]
    public void TheCallersIdentities_ShouldBeUsedRatherThanRebuilt()
    {
        LinearInequality<Flt64> row = FixedVariableEquality.Of(
            SharedVariable, new Flt64(7.0), Flt64.Zero, new Flt64(2.0), "scaled");

        row.Lhs.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "the caller passed two as the identity");
    }

    /// <summary>**变量为 null 时抛出** / A null variable throws.</summary>
    [Fact]
    public void TheRow_ShouldRejectANullVariable()
    {
        Action nullVariable = () => FixedVariableEquality.Of(
            null!, new Flt64(7.0), Flt64.Zero, Flt64.One, "null");

        nullVariable.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// **不检查值的可用性** / The value's usability is not checked.
    /// </summary>
    /// <remarks>
    /// **基线也不检查。** **这里把这条写成一个判据，是为了让"没有检查"成为<b>已记录的行为</b>，
    /// 而不是"可能被漏掉的地方"。**
    ///
    /// **`NaN` 会被原样放进右端**——**这是有意为之，检查在调用方。**
    ///
    /// **The baseline does not either.** **Recording it as a criterion makes "no check" a <b>documented behaviour</b>
    /// rather than a possible omission.**
    ///
    /// **`NaN` goes into the right-hand side verbatim** — **deliberately; the checking is the caller's.**
    /// </remarks>
    [Fact]
    public void TheRow_ShouldPassANonFiniteValueThrough()
    {
        LinearInequality<Flt64> row = FixedVariableEquality.Of(
            SharedVariable, new Flt64(double.NaN), Flt64.Zero, Flt64.One, "nan");

        double.IsNaN(row.Rhs.Constant.ToDouble()).Should().BeTrue(
            "this function builds a row and does not gate the value");
    }

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("fve-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }
}
