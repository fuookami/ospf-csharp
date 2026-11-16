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
/// 零值指示：偏离有两个方向，所以需要第二个二值 / Deviation has two directions, so a second binary is needed.
/// </summary>
/// <remarks>
/// 对齐基线 `BigM.kt` 的 `zeroIndicatorConstraints` 与 `safeZeroIndicatorConstraints`。
///
/// **一个二值指示只能表达"是或不是"，说不清"往哪边偏"。**
///
/// adds
///
/// **One binary indicator says "is or is not" but not "which way".**
/// </remarks>
public class ZeroIndicatorRowsTests
{
    /// <summary>
    /// **指示 = 1 时落在带内，两侧都被夹住** / At indicator one the value stays inside the band.
    /// </summary>
    /// <remarks>
    /// **`tolerance = 2`，所以带是 `[−2, 2]`。**
    /// **`x = 2` 与 `x = −2` 都应当满足，`x = 3` 与 `x = −3` 都应当被排除。**
    ///
    /// **四个点都测，而不是只测一个**：**只测 `x = 0` 的话，一条"永远成立"的行也能通过。**
    ///
    /// **`tolerance = 2`, so the band is `[−2, 2]`.**
    /// **`x = 2` and `x = −2` must hold; `x = 3` and `x = −3` must be excluded.**
    ///
    /// **All four points, not one**: **testing only `x = 0` would pass for a row that always holds.**
    /// </remarks>
    [Fact]
    public void TheIndicatorOne_ShouldConfineTheValueToTheBand()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        Residual(rows[0], x: 2.0, indicator: 1.0, side: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the upper band edge is tight at the tolerance");
        Residual(rows[1], x: -2.0, indicator: 1.0, side: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the lower band edge is tight at minus the tolerance");

        Residual(rows[0], x: 3.0, indicator: 1.0, side: 0.0).Should().BeGreaterThan(
            0.0, "three is outside the band");
        Residual(rows[1], x: -3.0, indicator: 1.0, side: 0.0).Should().BeLessThan(
            0.0, "minus three is outside the band");
    }

    /// <summary>
    /// **`side` 选方向：`side = 1` 走正向、`side = 0` 走负向** / `side` picks the deviation direction.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据，也是"为什么需要第二个二值"的直接证据。**
    ///
    /// **指示 = 0、`side = 1` 时：`_zero_out_lb` 强制 `poly ≥ strictBoundary`，
    /// 而 `_zero_out_ub` 放开。**
    /// **指示 = 0、`side = 0` 时：反过来。**
    ///
    /// **如果两个方向共用一个二值，模型只能表达"偏离"而说不清偏向哪边——
    /// `+5` 与 `−5` 会被混成一个状态。**
    ///
    /// **This is the batch's core criterion and the direct evidence for "why a second binary".**
    ///
    /// **At indicator 0 with `side = 1`, `_zero_out_lb` enforces `poly ≥ strictBoundary` while `_zero_out_ub` relaxes.
    /// At `side = 0` it is the other way round.**
    ///
    /// **With one binary for both directions the model can express "deviates" but not "which way" — `+5` and `−5`
    /// collapse into one state.**
    /// </remarks>
    [Fact]
    public void TheSideVariable_ShouldPickTheDeviationDirection()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        // 正向：`side = 1`、`x = 5`（`strictBoundary = 4`）应当满足 `_zero_out_lb`。
        // Positive: side 1, x = 5 against a strict boundary of four satisfies `_zero_out_lb`.
        Residual(rows[2], x: 5.0, indicator: 0.0, side: 1.0).Should().BeGreaterThanOrEqualTo(
            0.0, "five is a positive deviation");
        Residual(rows[2], x: 3.0, indicator: 0.0, side: 1.0).Should().BeLessThan(
            0.0, "three does not clear the strict boundary");

        // 负向：`side = 0`、`x = -5` 应当满足 `_zero_out_ub`。
        // Negative: side 0, x = -5 satisfies `_zero_out_ub`.
        Residual(rows[3], x: -5.0, indicator: 0.0, side: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "minus five is a negative deviation");
        Residual(rows[3], x: -3.0, indicator: 0.0, side: 0.0).Should().BeGreaterThan(
            0.0, "minus three does not clear the strict boundary");
    }

    /// <summary>
    /// **带内与带外用的不是同一个松弛量** / The two sides use different relaxations.
    /// </summary>
    /// <remarks>
    /// **`bandM = bigM + |tolerance| = 102`，`outM = bigM + |strictBoundary| = 104`。**
    /// **用同一个会让其中一侧多松或过紧，而两侧的间隔本来就不同。**
    ///
    /// **`bandM = bigM + |tolerance| = 102` and `outM = bigM + |strictBoundary| = 104`.**
    /// **One value for both over- or under-relaxes one side, and the gaps differ to begin with.**
    /// </remarks>
    [Fact]
    public void TheTwoRelaxations_ShouldDiffer()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        CoefficientOf(rows[0], Indicator).Should().Be(
            102.0, "the in-band relaxation uses the tolerance");
        CoefficientOf(rows[2], Indicator).Should().Be(
            104.0, "the deviation relaxation uses the strict boundary");

        CoefficientOf(rows[2], Side).Should().Be(
            -104.0, "the direction column is relaxed by the same outside amount");
    }

    /// <summary>
    /// **`strictBoundary` 必须严格大于 `tolerance`** / The boundary must exceed the tolerance.
    /// </summary>
    /// <remarks>
    /// **否则"带内"与"带外"在边界上重叠，一个点可以同时声称"在带内"与"偏离了"。**
    /// **这种重叠不会让模型不可行，只会让它有第二个解——而那是最难查的一类错误。**
    ///
    /// **Otherwise "inside" and "outside" overlap at the edge and a point can claim both.**
    /// **That overlap does not make the model infeasible — it gives it a second solution, the hardest kind of bug to
    /// find.**
    /// </remarks>
    [Fact]
    public void TheBoundaryNotExceedingTheTolerance_ShouldBeRejected()
    {
        ZeroIndicatorRows.Build(
            Linear(), Indicator, Side, new Flt64(100.0), new Flt64(4.0), new Flt64(4.0), "z").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a boundary equal to the tolerance lets a point be both in and out");

        ZeroIndicatorRows.Build(
            Linear(), Indicator, Side, new Flt64(100.0), new Flt64(4.0), new Flt64(2.0), "z").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a boundary below the tolerance is worse still");
    }

    /// <summary>**同一个变量当指示与方向列被拒绝** / One variable for both columns is rejected.</summary>
    [Fact]
    public void TheSelfSided_ShouldBeRejected()
    {
        ZeroIndicatorRows.Build(
            Linear(), Indicator, Indicator, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "z").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "two states cannot share one binary");
    }

    /// <summary>**四行的名字与方向** / The four rows' names and directions.</summary>
    [Fact]
    public void TheFourRows_ShouldCarryTheirNamesAndDirections()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        rows.Should().HaveCount(4);
        rows[0].Name.Should().Be("z_zero_band_ub");
        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Name.Should().Be("z_zero_band_lb");
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Name.Should().Be("z_zero_out_lb");
        rows[2].Comparison.Should().Be(Comparison.GE);
        rows[3].Name.Should().Be("z_zero_out_ub");
        rows[3].Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **非零检测的极性与零值检测相反** / Nonzero detection has the opposite polarity.
    /// </summary>
    /// <remarks>
    /// **同一个看起来一样的 `indicator` 参数，两个函数里含义相反：**
    /// **零值检测说"指示 = 1 时落在带内"，非零检测说"指示 = 1 时落在带外"。**
    ///
    /// **所以紧的那一侧对调：带行在<b>指示 = 0</b> 时紧，外行在<b>指示 = 1</b> 时紧。**
    ///
    /// **这是本批的核心判据，也是两个函数最容易被当成同一个的地方。**
    ///
    /// **The same-looking `indicator` parameter means the opposite: zero detection says "indicator 1 is inside the
    /// band", nonzero detection says "indicator 1 is outside".**
    ///
    /// **So the binding side swaps: the band rows bind at indicator 0 and the outer rows at indicator 1.**
    ///
    /// **This is the batch's core criterion, and the place where the two functions are most easily conflated.**
    /// </remarks>
    [Fact]
    public void TheNonzeroPolarity_ShouldBeTheOppositeOfZero()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = NonzeroRows();

        rows.Should().HaveCount(4);
        rows[0].Name.Should().Be("nz_band_ub");
        rows[1].Name.Should().Be("nz_band_lb");
        rows[2].Name.Should().Be("nz_out_lb");
        rows[3].Name.Should().Be("nz_out_ub");

        // **指示 = 0 时是"零"：夹进 `[−2, 2]`。**
        // **Indicator 0 means zero here: confined to [-2, 2].**
        Residual(rows[0], x: 2.0, indicator: 0.0, side: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[1], x: -2.0, indicator: 0.0, side: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: 3.0, indicator: 0.0, side: 0.0).Should().BeGreaterThan(
            0.0, "three is not zero");

        // **指示 = 1 时是"非零"：`side = 1` 走正、`side = 0` 走负。**
        // **Indicator 1 means nonzero: side 1 goes positive, side 0 negative.**
        Residual(rows[2], x: 4.0, indicator: 1.0, side: 1.0).Should().BeApproximately(
            0.0, 1e-9, "four clears the strict boundary with side one");
        Residual(rows[3], x: -4.0, indicator: 1.0, side: 0.0).Should().BeApproximately(
            0.0, 1e-9, "minus four clears the strict boundary with side zero");
    }

    /// <summary>
    /// **外行右侧是 `strict − 2·outM`，两个 `−outM` 项必须都减掉** /
    /// The outer row's right-hand side subtracts `outM` twice.
    /// </summary>
    /// <remarks>
    /// **`_out_lb` 同时带 `ind` 与 `side` 两个 `−outM` 项。**
    /// **指示 = 1、`side = 1` 时它们相加正好抵消 `2·outM`，于是落到 `strict`。**
    ///
    /// **只减一个 `outM` 会让紧点偏移整个 `outM`——而模型仍然可行。**
    ///
    /// **这条判据直接断言那两个点在紧点上、且相邻的越界点被排除。**
    ///
    /// **`_out_lb` carries two `−outM` terms (`ind` and `side`).** **At indicator 1 with `side = 1` they cancel the
    /// `2·outM`, landing on `strict`.**
    ///
    /// **Subtracting only one `outM` shifts the tight point by a whole `outM` — while the model stays feasible.**
    ///
    /// **The criterion asserts both tight points and excludes their out-of-range neighbours.**
    /// </remarks>
    [Fact]
    public void TheOuterRows_ShouldSubtractTheOutMOnceTwice()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = NonzeroRows();

        // `_out_lb`：指示 = 1、`side = 1` 时紧在 `x = 4`；`x = 3` 被排除。
        // `_out_lb`: tight at x = 4 for indicator 1 with side 1; x = 3 is excluded.
        Residual(rows[2], x: 4.0, indicator: 1.0, side: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[2], x: 3.0, indicator: 1.0, side: 1.0).Should().BeLessThan(
            0.0, "three does not clear the strict boundary");

        // `_out_ub`：指示 = 1、`side = 0` 时紧在 `x = −4`；`x = −3` 被排除。
        // `_out_ub`: tight at x = -4 for indicator 1 with side 0; x = -3 is excluded.
        Residual(rows[3], x: -4.0, indicator: 1.0, side: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[3], x: -3.0, indicator: 1.0, side: 0.0).Should().BeGreaterThan(
            0.0, "minus three does not clear the strict boundary");
    }

    /// <summary>**带行在指示 = 1 时放开** / The band rows relax at indicator 1.</summary>
    [Fact]
    public void TheBandRows_ShouldRelaxAtIndicatorOne()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = NonzeroRows();

        Residual(rows[0], x: 100.0, indicator: 1.0, side: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "the upper band row relaxes by bigM plus the tolerance");
        Residual(rows[1], x: -100.0, indicator: 1.0, side: 0.0).Should().BeGreaterThanOrEqualTo(
            0.0, "the lower band row relaxes by bigM plus the tolerance");
    }

    /// <summary>**非零检测沿用同一套输入校验** / Nonzero detection reuses the same input checks.</summary>
    [Fact]
    public void TheNonzero_ShouldReuseTheInputChecks()
    {
        ZeroIndicatorRows.BuildNonzero(
            Linear(), Indicator, Side, new Flt64(100.0), new Flt64(4.0), new Flt64(4.0), "nz").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "the boundary must exceed the tolerance here too");

        ZeroIndicatorRows.BuildNonzero(
            Linear(), Indicator, Indicator, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "nz").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "two states still cannot share one binary");
    }

    private static IReadOnlyList<LinearInequality<Flt64>> NonzeroRows() =>
        ZeroIndicatorRows.BuildNonzero(
            Linear(), Indicator, Side, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "nz").Value;

    private static IReadOnlyList<LinearInequality<Flt64>> Rows() =>
        ZeroIndicatorRows.Build(
            Linear(), Indicator, Side, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "z").Value;

    private static double CoefficientOf(LinearInequality<Flt64> row, ISymbol symbol) {
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (monomial.Symbol.Equals(symbol)) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new Xunit.Sdk.XunitException($"no term for `{symbol}` in `{row.Name}`");
    }

    private static double Residual(
        LinearInequality<Flt64> row, double x, double indicator, double side) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, x, indicator, side);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, x, indicator, side);
        }

        return total;
    }

    private static double ValueOf(ISymbol symbol, double x, double indicator, double side) =>
        symbol.Equals(Indicator) ? indicator
            : symbol.Equals(Side) ? side
            : symbol.Equals(X) ? x
            : 0.0;

    private static LinearPolynomial<Flt64> Linear() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero);

    private static readonly RealVar X = new("z-x");

    private static readonly BinVar Indicator = new("z-ind");

    private static readonly BinVar Side = new("z-side");
}
