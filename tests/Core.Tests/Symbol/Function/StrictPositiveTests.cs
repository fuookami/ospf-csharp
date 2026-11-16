#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// `p ≥ 0` 平移成 `p + δ &gt; 0`，而这需要格点证明 / The shift needs a lattice proof.
/// </summary>
/// <remarks>
/// 对齐的 `StrictPositiveCondition` 与 `toStrictPositiveCondition`。
///
/// **`p ≥ 0` 与 `p &gt; 0` 不是同一个条件**，**`p + δ &gt; 0` 等价于 `p &gt; −δ`。**
/// **两者只在 `p` 落在步长 `δ` 的格点上时相同。**
///
/// **`p ≥ 0` and `p &gt; 0` are not the same condition**, and **`p + δ &gt; 0` is equivalent to `p &gt; −δ`.** **They agree
/// only when `p` sits on a lattice of spacing `δ`.**
/// </remarks>
public class StrictPositiveTests
{
    /// <summary>**`GE` 平移常数项，`GT` 不动** / `GE` shifts, `GT` does not.</summary>
    /// <remarks>
    /// **这是四条规则里唯一需要平移的两条之一。**
    /// **如果 `GE` 也走"不变"分支，`p ≥ 0` 会被当成 `p &gt; 0`——**
    /// **在整数上丢掉 `p = 0` 这一个解，而模型仍然可行。**
    ///
    /// **One of the two rules that shift.** **Were `GE` to take the "unchanged" branch, `p ≥ 0` would be treated as
    /// `p &gt; 0`** — **dropping the single solution `p = 0` on the integers while the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheGe_ShouldShiftTheConstantAndGtShouldNot()
    {
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ge =
            StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.GE, Flt64.One);
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> gt =
            StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.GT, Flt64.One);

        ge.Value.Constant.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "p + delta with p = x + 1 and delta = 1");
        gt.Value.Constant.ToDouble().Should().BeApproximately(
            1.0, 1e-12, "GT needs no shift");
    }

    /// <summary>**`LE` 先取负再平移** / `LE` negates then shifts.</summary>
    /// <remarks>
    /// **顺序错了结果一样吗？** **`−p + δ` 与 `−(p + δ)` 差 `2δ`。**
    /// **取 `p = x + 1`、`δ = 1`：正确结果是 `−x`（`−1 + 1 = 0`）；
    /// 顺序反了会得到 `−x − 2`。** **两者差 2。**
    ///
    /// **Does the order matter?** **`−p + δ` and `−(p + δ)` differ by `2δ`.** **With `p = x + 1` and `δ = 1` the
    /// correct result is `−x`; the opposite order gives `−x − 2`.**
    /// </remarks>
    [Fact]
    public void TheLe_ShouldNegateBeforeShifting()
    {
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> le =
            StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.LE, Flt64.One);

        le.Value.Constant.ToDouble().Should().BeApproximately(
            0.0, 1e-12, "minus one plus one is zero");
        le.Value.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            -1.0, 1e-12, "the monomial is negated");
    }

    /// <summary>**`LT` 只取负** / `LT` only negates.</summary>
    [Fact]
    public void TheLt_ShouldOnlyNegate()
    {
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> lt =
            StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.LT, Flt64.One);

        lt.Value.Constant.ToDouble().Should().BeApproximately(-1.0, 1e-12);
        lt.Value.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(-1.0, 1e-12);
    }

    /// <summary>**`EQ`/`NE` 被拒绝** / `EQ` and `NE` are rejected.</summary>
    /// <remarks>
    /// **`p ≠ 0` 是"或者严格正或者严格负"，一个严格正条件表达不了。**
    /// **挑一个方向会让一半的解消失，而模型仍然可行。**
    ///
    /// **`p != 0` is "either strictly positive or strictly negative", which one strictly positive condition cannot
    /// express.** **Picking a side would delete half the solutions while the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheEqAndNe_ShouldBeRejected()
    {
        StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.EQ, Flt64.One).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>("EQ has no strictly positive form");
        StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.NE, Flt64.One).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>("NE is a disjunction");
    }

    /// <summary>
    /// **`δ` 超过格点步长时被拒绝** / A delta above the lattice spacing is rejected.
    /// </summary>
    /// <remarks>
    /// **`p = 2x + 1` 的格点步长是 2，`δ = 3` 超过它。**
    /// **此时 `p + 3 &gt; 0` 等价于 `p &gt; −3`，而 `p` 能取到 `−1`——
    /// 于是 `p = −1` 满足严格正形式却不满足 `p ≥ 0`。**
    /// **拒绝是对的：等价性没了。**
    ///
    /// **`p = 2x + 1` has lattice spacing two, and `δ = 3` exceeds it.** **Then `p + 3 &gt; 0` means `p &gt; −3`, and `p`
    /// can reach `−1` — so `p = −1` satisfies the strict form but not `p ≥ 0`.** **Rejecting is correct: the
    /// equivalence is gone.**
    /// </remarks>
    [Fact]
    public void TheGe_ShouldRejectADeltaAboveTheLatticeSpacing()
    {
        StrictPositiveTransformation.ToStrictPositive(Even(), Comparison.GE, new Flt64(3.0)).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "a delta of three is coarser than the spacing of two");
        StrictPositiveTransformation.ToStrictPositive(Even(), Comparison.GE, new Flt64(2.0)).Should()
            .BeOfType<Ok<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "a delta equal to the spacing is provably safe");
    }

    /// <summary>**非整数变量的格点不可知时退回常数检查** / Non-integer variables fall back.</summary>
    /// <remarks>
    /// **常数项与 0 同侧时安全**（**`c ≥ 0` 时 `p ≥ 0` 恒成立**），
    /// **而常数项落在 `(−δ, 0)` 里时不安全。**
    /// **这是基线给出的全部保证：这种情形下等价性没有被证明，只是没有被证伪。**
    ///
    /// **A constant on the same side of zero is safe** (with `c ≥ 0`, `p ≥ 0` holds outright), **while one inside
    /// `(−δ, 0)` is not.** **That is all the code guarantees: the equivalence is not proved there, merely not
    /// disproved.**
    /// </remarks>
    [Fact]
    public void TheGe_ShouldFallBackToTheConstantCheckForContinuousVariables()
    {
        // 常数 −0.5 落在 (−1, 0) 里，不安全。
        // The constant -0.5 lies in (-1, 0), which is unsafe.
        StrictPositiveTransformation.ToStrictPositive(
            ContinuousWithConstant(-0.5), Comparison.GE, Flt64.One).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "a constant inside the shift window breaks the equivalence");

        // 常数 0.5 与 0 同侧，安全。
        // The constant 0.5 is on the same side of zero, which is safe.
        StrictPositiveTransformation.ToStrictPositive(
            ContinuousWithConstant(0.5), Comparison.GE, Flt64.One).Should()
            .BeOfType<Ok<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非正 `δ` 被拒绝** / A non-positive delta is rejected.</summary>
    [Fact]
    public void TheDelta_ShouldBePositive()
    {
        StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.GE, Flt64.Zero).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>("a zero delta does not shift");
        StrictPositiveTransformation.ToStrictPositive(Integral(), Comparison.GT, new Flt64(-1.0)).Should()
            .BeOfType<Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**不等式重载先做 `lhs − rhs`，常数项也要减** / The inequality overload subtracts both parts.</summary>
    /// <remarks>
    /// **只减 monomial 而漏掉常数项，会让差值偏一个常数。**
    /// **判据用 `x + 3 ≤ 1`，正确的差值是 `x + 2`；漏掉常数项会得到 `x`。**
    ///
    /// **Subtracting only the monomials and not the constant offsets the difference by a constant.** **The criterion uses
    /// `x + 3 ≤ 1`, whose correct difference is `x + 2`; dropping the constant gives `x`.**
    /// </remarks>
    [Fact]
    public void TheInequalityOverload_ShouldSubtractTheConstantTerm()
    {
        var inequality = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, SharedVariable) }, new Flt64(3.0)),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            Comparison: Comparison.LE,
            Name: "ineq");

        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> transformed =
            StrictPositiveTransformation.ToStrictPositive(inequality, Flt64.One);

        // `x + 2 ≤ 0` → 取负得 `−x − 2` → 平移得 `−x − 1`。
        // x + 2 <= 0 becomes -x - 2, then shifted to -x - 1.
        transformed.Value.Constant.ToDouble().Should().BeApproximately(
            -1.0, 1e-12, "the difference is x + 2, negated and shifted by one");
        transformed.Value.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(-1.0, 1e-12);
    }

    /// <summary>**包装类型保留变换后的多项式** / The wrapper carries the transformed polynomial.</summary>
    [Fact]
    public void TheCondition_ShouldCarryTheTransformedPolynomial()
    {
        Result<StrictPositiveCondition, ErrorCode, Error<ErrorCode>> condition =
            StrictPositiveTransformation.ToCondition(Integral(), Comparison.GE, Flt64.One);

        condition.Value.Polynomial.Constant.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "the wrapper holds the shifted polynomial, not the original");
    }

    /// <summary>`x + 1`，整数变量 / `x + 1` over an integer variable.</summary>
    private static LinearPolynomial<Flt64> Integral() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, IntegerVariable) }, Flt64.One);

    /// <summary>`2x + 1`，格点步长 2 / `2x + 1`, lattice spacing two.</summary>
    private static LinearPolynomial<Flt64> Even() =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(2.0), IntegerVariable) }, Flt64.One);

    /// <summary>`x + constant`，连续变量 / `x + constant` over a continuous variable.</summary>
    private static LinearPolynomial<Flt64> ContinuousWithConstant(double constant) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, SharedVariable) }, new Flt64(constant));

    private static readonly IntVar IntegerVariable = CreateIntegerVariable();

    /// <summary>整数变量，不设界 / An integer variable with no bounds.</summary>
    /// <remarks>
    /// **界对"格点步长"无关**：**步长来自系数与变量类型，不来自取值范围。**
    /// **而且 `Integer` 的构造在这里解析不到——判据不需要它。**
    ///
    /// **The bounds are irrelevant to the lattice spacing**: **spacing comes from the coefficients and the variable
    /// type, not from the value range.** **And `Integer` does not resolve here, which the criterion does not need.**
    /// </remarks>
    private static IntVar CreateIntegerVariable() => new("sp-int");

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("sp-real");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }
}
