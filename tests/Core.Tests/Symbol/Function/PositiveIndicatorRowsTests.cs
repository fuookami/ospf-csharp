#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
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
/// 正向指示：两侧的 Big-M 不是同一个数 / The two sides use different Big-M values.
/// </summary>
/// <remarks>
/// B1180 按基线 `BigM.kt` 的 `positiveIndicatorConstraints` 与 `relaxBigM` 补。
///
/// **下界行用 `bigM + |tol|`，上界行用 `bigM`；这个差别只在指示 = 0 时显现。**
///
/// B1180 adds the baseline's `positiveIndicatorConstraints` and `relaxBigM`.
///
/// **The lower row uses `bigM + |tol|` and the upper row `bigM`; the difference shows only at indicator 0.**
/// </remarks>
public class PositiveIndicatorRowsTests
{
    /// <summary>
    /// **指示 = 1 时 `poly ≥ tolerance`** / At indicator one the polynomial clears the tolerance.
    /// </summary>
    /// <remarks>
    /// **取 `poly = x`、`tolerance = 2`、`bigM = 100`、`x = 3`：**
    /// **指示 = 1 时下界行应<b>满足</b>（`3 ≥ 2`）。**
    ///
    /// **而 `x = 1` 时应当<b>违反</b>——那样才说明这条行真的在起作用，
    /// 而不是"恰好对某个值成立"。**
    ///
    /// **With `poly = x`, `tolerance = 2`, `bigM = 100` and `x = 3`:** **the lower row must <b>hold</b> at indicator 1
    /// (`3 ≥ 2`).**
    ///
    /// **And it must <b>fail</b> at `x = 1` — that is what shows the row actually binds rather than happening to hold at
    /// one point.**
    /// </remarks>
    [Fact]
    public void TheIndicatorOne_ShouldEnforceTheTolerance()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        Residual(rows[1], x: 3.0, indicator: 1.0).Should().BeGreaterThanOrEqualTo(
            0.0, "three clears a tolerance of two");
        Residual(rows[1], x: 1.0, indicator: 1.0).Should().BeLessThan(
            0.0, "one does not clear a tolerance of two");
    }

    /// <summary>**指示 = 0 时 `poly ≤ 0`** / At indicator zero the polynomial must not be positive.</summary>
    [Fact]
    public void TheIndicatorZero_ShouldForceNonPositive()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        Residual(rows[0], x: 0.0, indicator: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "zero is non-positive");
        Residual(rows[0], x: -5.0, indicator: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "minus five is non-positive");
        Residual(rows[0], x: 3.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "three is positive and must be excluded");
    }

    /// <summary>
    /// **下界行在指示 = 0 时放开到恰好 `−bigM`** / The lower row relaxes to exactly `−bigM`.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据，也是唯一能被写错且看不出来的地方。**
    ///
    /// **下界行要的是 `poly ≥ tolerance − M·(1 − ind)`。** **照抄 `M` 会在指示 = 0 时得到
    /// `poly ≥ tolerance − M`，比 `−M` <b>多松了一个 tolerance</b>。**
    ///
    /// **两种写法都允许所有合法解，所以模型不会错——只是松弛变松，而且看不出原因。**
    ///
    /// **判据直接核对 `RelaxBigM` 的值与下界行在指示 = 0 时的残差为 0 的那个点。**
    ///
    /// **This is the batch's core criterion and the only place it can be written wrong invisibly.**
    ///
    /// **The lower row wants `poly ≥ tolerance − M·(1 − ind)`. Copying `M` gives `poly ≥ tolerance − M` at indicator 0 —
    /// <b>one tolerance looser</b> than `−M`.**
    ///
    /// **Both admit every legal solution, so the model is not wrong — the relaxation is merely weaker, with nothing
    /// showing why.**
    ///
    /// **The criterion checks the `RelaxBigM` value directly and the point where the lower row is tight at indicator 0.**
    /// </remarks>
    [Fact]
    public void TheLowerRow_ShouldRelaxToExactlyMinusBigM()
    {
        PositiveIndicatorRows.RelaxBigM(new Flt64(100.0), new Flt64(2.0)).ToDouble().Should().Be(
            102.0, "the relaxation adds the absolute tolerance");

        // 下界行在 `x = -100`、指示 = 0 处应当是紧的：`-100 - 0 >= 2 - 102 = -100`。
        // The lower row is tight at x = -100, indicator 0: -100 - 0 >= 2 - 102 = -100.
        Residual(Rows()[1], x: -100.0, indicator: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the relaxation lands exactly on minus big-M");

        // 而 `x = -101` 被这一行排除——松弛不是无限的。
        // And x = -101 is excluded, so the relaxation is not unbounded.
        Residual(Rows()[1], x: -101.0, indicator: 0.0).Should().BeLessThan(0.0);
    }

    /// <summary>
    /// **负 tolerance 也要让松弛为正** / A negative tolerance must still relax positively.
    /// </summary>
    /// <remarks>
    /// **`tolerance` 可以是负数**（**那时"正分支"其实是"至少 `−tol`"**）。
    /// **若用 `bigM + tol` 而不是 `bigM + |tol|`，负 tolerance 会让放开量<b>变小</b>，
    /// 于是下界行在指示 = 0 时反而更紧，可能切掉合法解。**
    ///
    /// **`tolerance` may be negative** (**the "positive" branch is then really "at least `−tol`"**).
    /// **Using `bigM + tol` instead of `bigM + |tol|` shrinks the relaxation for a negative tolerance, so the lower row
    /// gets <b>tighter</b> at indicator 0 and can cut off legal solutions.**
    ///
    /// **本实现直接拒绝负 tolerance**（基线也要求 `≥ 0`），所以这条判据断言拒绝，而不是断言松弛仍为正。
    ///
    /// **This implementation rejects a negative tolerance outright (the baseline also requires `≥ 0`), so the criterion
    /// asserts the rejection rather than a positive relaxation.**
    /// </remarks>
    [Fact]
    public void TheNegativeTolerance_ShouldBeRejected()
    {
        PositiveIndicatorRows.Build(
            Linear(), Indicator, new Flt64(100.0), new Flt64(-1.0), "pi").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a negative tolerance is not a positive-branch edge");

        // 而 `RelaxBigM` 本身对负数取绝对值，所以即使被别处调用也不会把放宽量变成负数。
        // `RelaxBigM` itself takes the absolute value, so even if called elsewhere it cannot shrink the relaxation.
        PositiveIndicatorRows.RelaxBigM(new Flt64(100.0), new Flt64(-2.0)).ToDouble().Should().Be(102.0);
    }

    /// <summary>
    /// **上界行在指示 = 1 时放开到恰好 `bigM`，不是 `bigM + |tol|`** / The upper row relaxes to exactly bigM.
    /// </summary>
    /// <remarks>
    /// **这条判据是一次"全绿变异"补出来的。**
    ///
    /// **我第一次只钉住下界行的放宽量，然后把上界行的系数换成 `lowerRelaxM`——
    /// 判据全绿。原因是没有任何判据观察过"指示 = 1 时上界放开到哪里"。**
    ///
    /// **那个变异不会切掉合法解（`102` 比 `100` 更松），所以它<b>不是错误</b>——
    /// 但它让上界比需要的松了两个单位，而这件事在判据里看不见。**
    ///
    /// **补法：断言 `x = 100` 处上界行是紧的、`x = 101` 被排除。**
    ///
    /// **This criterion came out of an all-green mutation.**
    ///
    /// **I first pinned only the lower row's relaxation and then switched the upper row's coefficient to
    /// `lowerRelaxM` — every criterion stayed green, because none observed where the upper bound relaxes to at
    /// indicator 1.**
    ///
    /// **That mutation does not cut legal solutions (`102` is looser than `100`), so it is <b>not an error</b> — but it
    /// relaxes the upper bound two units more than needed, and nothing saw it.**
    ///
    /// **Fix: assert the upper row is tight at `x = 100` and excludes `x = 101`.**
    /// </remarks>
    [Fact]
    public void TheUpperRow_ShouldRelaxToExactlyBigM()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        Residual(rows[0], x: 100.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "the upper bound relaxes to exactly big-M");
        Residual(rows[0], x: 101.0, indicator: 1.0).Should().BeGreaterThan(
            0.0, "one past big-M is excluded, so the relaxation is not wider than needed");
    }

    /// <summary>**非正 Big-M 被拒绝** / A non-positive big-M is rejected.</summary>
    [Fact]
    public void TheNonPositiveBigM_ShouldBeRejected()
    {
        PositiveIndicatorRows.Build(
            Linear(), Indicator, Flt64.Zero, Flt64.One, "pi").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        PositiveIndicatorRows.Build(
            Linear(), Indicator, new Flt64(-5.0), Flt64.One, "pi").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**两行的名字与方向** / The two rows' names and directions.</summary>
    [Fact]
    public void TheTwoRows_ShouldCarryTheirNamesAndDirections()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("pi_positive_ub");
        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Name.Should().Be("pi_positive_lb");
        rows[1].Comparison.Should().Be(Comparison.GE);
    }

    private static IReadOnlyList<LinearInequality<Flt64>> Rows() =>
        PositiveIndicatorRows.Build(
            Linear(), Indicator, new Flt64(100.0), new Flt64(2.0), "pi").Value;

    /// <summary>把 `x` 与 `indicator` 代入，返回 `lhs − rhs` / Substitute and return `lhs − rhs`.</summary>
    private static double Residual(
        LinearInequality<Flt64> row, double x, double indicator) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, x, indicator);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, x, indicator);
        }

        return total;
    }

    private static double ValueOf(ISymbol symbol, double x, double indicator) =>
        symbol.Equals(Indicator) ? indicator : symbol.Equals(X) ? x : 0.0;

    private static LinearPolynomial<Flt64> Linear() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero);

    private static readonly RealVar X = new("pi-x");

    private static readonly BinVar Indicator = new("pi-ind");
}
