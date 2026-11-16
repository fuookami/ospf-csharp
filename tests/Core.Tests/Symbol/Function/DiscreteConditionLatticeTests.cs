#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// `spacing ≥ δ` 不够，余数检查才挡得住歪斜格点 / Spacing alone is not enough.
/// </summary>
/// <remarks>
/// 对齐基线 `DiscreteCondition.kt` 的四个格点辅助函数。
///
/// **补它的原因是一个已发现的缺口**：**B1168 只检查了"步长 ≥ δ"。**
///
/// adds
///
/// **The reason is a gap already found**: **B1168 only checked "spacing ≥ δ".**
/// </remarks>
public class DiscreteConditionLatticeTests
{
    /// <summary>
    /// **歪斜格点：步长够大但位置不对，仍然不安全** / A misaligned lattice is unsafe even with a large spacing.
    /// </summary>
    /// <remarks>
    /// **这是本批唯一真正新增的判据，也是 B1168 缺的那一块。**
    ///
    /// **`p = 3x + 2`、`δ = 2`。取值是 `2 + 3ℤ = …, −1, 2, 5, …`。**
    ///
    /// - **步长 `3 ≥ δ = 2`，所以"步长检查"通过**
    /// - **但 `p` 能取到 `−1`，而 `−1 > −2` <b>成立</b>、`−1 ≥ 0` <b>不成立</b>**
    /// - **于是 `p + δ > 0` 与 `p ≥ 0` 在 `p = −1` 处给出不同答案**
    ///
    /// **只有余数距离检查能挡住它。**
    ///
    /// **This is the batch's only genuinely new criterion, and exactly what B1168 lacked.**
    ///
    /// **`p = 3x + 2`, `δ = 2`. The values are `2 + 3ℤ = …, −1, 2, 5, …`.**
    ///
    /// - **The spacing `3 ≥ δ = 2`, so the spacing check passes**
    /// - **But `p` reaches `−1`, where `−1 > −2` <b>holds</b> and `−1 ≥ 0` <b>does not</b>**
    /// - **So `p + δ > 0` and `p ≥ 0` disagree at `p = −1`**
    ///
    /// **Only the remainder-distance check blocks it.**
    /// </remarks>
    [Fact]
    public void TheMisalignedLattice_ShouldBeRejected()
    {
        DiscreteConditionLattice.CoefficientGcd(Shifted(3.0, 2.0)).Should().BeApproximately(
            3.0, 1e-12, "the spacing alone looks fine");

        DiscreteConditionLattice.HasSafeShiftProof(
            Shifted(3.0, 2.0), Comparison.GE, new Flt64(2.0)).Should().BeFalse(
                "the lattice is offset so that minus one is reachable, which breaks the equivalence");
    }

    /// <summary>**对齐的格点通过** / An aligned lattice passes.</summary>
    /// <remarks>
    /// **`p = 3x + 3`、`δ = 2`：取值 `3 + 3ℤ = …, 0, 3, 6, …`。**
    /// **`GE` 平移后要求 `p > −2`；`p` 的最小非负一侧取值是 `0`，而下方最近的格点是 `−3`，
    /// 距离 3 ≥ 2。** **所以安全。**
    ///
    /// **`p = 3x + 3`, `δ = 2`: the values are `3 + 3ℤ = …, 0, 3, 6, …`.**
    /// **After the `GE` shift the requirement is `p > −2`; the nearest value below the constant is `−3`, three away,
    /// so it is safe.**
    /// </remarks>
    [Fact]
    public void TheAlignedLattice_ShouldPass()
    {
        DiscreteConditionLattice.HasSafeShiftProof(
            Shifted(3.0, 3.0), Comparison.GE, new Flt64(2.0)).Should().BeTrue(
                "minus three is far enough below zero");
    }

    /// <summary>**步长太小被拒** / A spacing below delta is rejected.</summary>
    [Fact]
    public void TheTooDenseLattice_ShouldBeRejected()
    {
        DiscreteConditionLattice.HasSafeShiftProof(
            Shifted(1.0, 0.5), Comparison.GE, new Flt64(2.0)).Should().BeFalse(
                "a spacing of one is finer than a delta of two");
    }

    /// <summary>**`GT`/`LT` 不走这条路** / `GT` and `LT` never take this path.</summary>
    [Fact]
    public void TheStrictRelations_ShouldNotUseTheShiftProof()
    {
        DiscreteConditionLattice.HasSafeShiftProof(
            Shifted(3.0, 3.0), Comparison.GT, new Flt64(2.0)).Should().BeFalse(
                "GT does not shift, so this proof does not apply");
        DiscreteConditionLattice.HasSafeShiftProof(
            Shifted(3.0, 3.0), Comparison.LT, new Flt64(2.0)).Should().BeFalse();
    }

    /// <summary>**同变量系数先合并再求步长** / Coefficients merge before the spacing is taken.</summary>
    /// <remarks>
    /// **`2x` 与 `−2x` 分两个 monomial 写：逐个看系数是 2 与 −2，
    /// 而合并后是 0——那个变量根本不参与格点。**
    /// **不合并会算出步长 2，而正确答案是"没有非零系数"。**
    ///
    /// **`2x` and `−2x` as two monomials: individually the coefficients are two and minus two, but merged they cancel —
    /// that variable does not constrain the lattice.**
    /// **Without merging the spacing comes out as two, while the correct answer is "no non-zero coefficients".**
    /// </remarks>
    [Fact]
    public void TheEffectiveMonomials_ShouldCancelOppositeCoefficients()
    {
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), SharedVariable),
                new LinearMonomial<Flt64>(new Flt64(-2.0), SharedVariable),
            },
            new Flt64(5.0));

        DiscreteConditionLattice.EffectiveMonomials(polynomial).Should().BeEmpty(
            "the two terms cancel and nothing else is left");
        DiscreteConditionLattice.CoefficientGcd(polynomial).Should().BeNull(
            "no non-zero coefficients means a degenerate lattice");
    }

    /// <summary>**无变量时退回常数检查** / With no variables the constant decides.</summary>
    [Fact]
    public void TheConstantOnlyPolynomial_ShouldFallBackToTheConstant()
    {
        var positive = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0));
        DiscreteConditionLattice.CoefficientGcd(positive).Should().BeNull();
        DiscreteConditionLattice.HasSafeShiftProof(positive, Comparison.GE, Flt64.One).Should().BeTrue(
            "a constant of five already satisfies both forms");

        var insideWindow = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(), new Flt64(-0.5));
        DiscreteConditionLattice.HasSafeShiftProof(insideWindow, Comparison.GE, Flt64.One).Should().BeFalse(
            "a constant inside the shift window breaks the equivalence");
    }

    /// <summary>**连续变量不是整数格点** / A continuous variable is not an integer lattice.</summary>
    [Fact]
    public void TheContinuousVariable_ShouldNotFormALattice()
    {
        var continuous = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, ContinuousVariable) }, Flt64.Zero);

        DiscreteConditionLattice.IsIntegerLatticePolynomial(continuous).Should().BeFalse(
            "a continuous variable takes non-integer values");
        DiscreteConditionLattice.HasSafeShiftProof(continuous, Comparison.GE, Flt64.One).Should().BeFalse();
    }

    /// <summary>**非整数系数不是格点** / A non-integer coefficient is not a lattice.</summary>
    [Fact]
    public void TheFractionalCoefficient_ShouldNotFormALattice()
    {
        DiscreteConditionLattice.IsIntegerLatticePolynomial(Shifted(0.5, 0.0)).Should().BeFalse(
            "half a lattice step is not a lattice over integers");
    }

    /// <summary>**整数性检查排除 `NaN` 与无穷** / The integrality check excludes NaN and infinity.</summary>
    [Fact]
    public void TheIntegralityCheck_ShouldExcludeNonFiniteValues()
    {
        DiscreteConditionLattice.IsIntegerValued(new Flt64(3.0)).Should().BeTrue();
        DiscreteConditionLattice.IsIntegerValued(new Flt64(-3.0)).Should().BeTrue();
        DiscreteConditionLattice.IsIntegerValued(Flt64.Zero).Should().BeTrue();
        DiscreteConditionLattice.IsIntegerValued(new Flt64(3.5)).Should().BeFalse();
        DiscreteConditionLattice.IsIntegerValued(new Flt64(double.NaN)).Should().BeFalse(
            "NaN must be excluded by a check, not by a coincidence of comparisons");
        DiscreteConditionLattice.IsIntegerValued(new Flt64(double.PositiveInfinity)).Should().BeFalse();
    }

    /// <summary>`coefficient * x + constant`，整数变量 / `coefficient * x + constant`.</summary>
    private static LinearPolynomial<Flt64> Shifted(double coefficient, double constant) =>
        new(
            new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), IntegerVariable) },
            new Flt64(constant));

    private static readonly IntVar IntegerVariable = new("dcl-int");

    private static readonly RealVar ContinuousVariable = CreateContinuousVariable();

    private static RealVar CreateContinuousVariable() {
        var variable = new RealVar("dcl-real");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static readonly RealVar SharedVariable = CreateContinuousVariable();
}
