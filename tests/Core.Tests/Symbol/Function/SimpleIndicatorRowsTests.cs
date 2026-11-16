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
/// 简单指示行：先做差分，违反侧用松弛后的 M / Difference first, relaxed M on the violated side.
/// </summary>
/// <remarks>
/// 对齐基线 `BigM.kt` 的 `simpleIndicatorConstraints`（LE/GE）与 `nonnegativeIndicatorConstraints`。
///
/// **`diff = lhs − rhs`，之后所有行只针对 `diff`。**
///
/// **`diff = lhs − rhs`, and every row then works on `diff` alone.**
/// </remarks>
public class SimpleIndicatorRowsTests
{
    /// <summary>
    /// **差分里必须含 `rhs` 的项** / The difference must carry the right-hand side's terms.
    /// </summary>
    /// <remarks>
    /// **这是这里最重要的判据。**
    ///
    /// **`lhs ≤ rhs` 写成 `x ≤ 5` 时，`rhs` 是常数——差分后 `diff = x − 5`。**
    /// **如果实现只拿 `lhs`，`diff` 就成了 `x`，条件变成 `x ≤ 0` 而不是 `x ≤ 5`。**
    ///
    /// **而模型仍然可行：少了一个常数项，不是矛盾。** **所以它不会报错，只会算错。**
    ///
    /// **判据取 `x ≤ 5`、`tolerance = 0`、指示 = 1，断言 `x = 5` 处 `_sat` 是紧的。**
    /// **若丢掉常数 5，紧点会落在 `x = 0`。**
    ///
    /// **This is the most important criterion here.**
    ///
    /// **With `x ≤ 5` the right-hand side is a constant, so `diff = x − 5`.**
    /// **Taking `lhs` alone makes `diff = x`, turning the condition into `x ≤ 0` instead of `x ≤ 5`.**
    ///
    /// **The model stays feasible — a missing constant, not a contradiction.** **So it reports nothing and simply
    /// computes wrong.**
    ///
    /// **The criterion takes `x ≤ 5`, `tolerance = 0`, indicator 1, and asserts `_sat` is tight at `x = 5`.**
    /// **Dropping the constant moves the tight point to `x = 0`.**
    /// </remarks>
    [Fact]
    public void TheDifference_ShouldCarryTheRightHandSideConstant()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = LessEqualRows();

        Residual(rows[0], x: 5.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "_sat is tight at x = 5, which is only true if the constant five is in the difference");
        Residual(rows[0], x: 6.0, indicator: 1.0).Should().BeGreaterThan(
            0.0, "six exceeds five");
    }

    /// <summary>
    /// **`rhs` 带变量时，那些项也必须进差分** / A variable on the right side must enter the difference too.
    /// </summary>
    /// <remarks>
    /// **这是"常数不算数"的补充。**
    ///
    /// **上一条判据用 `x ≤ 5`，而 `5` 是 `rhs` 的<b>常数</b>——`rhs` 的 <b>monomial 列表是空的</b>，
    /// 所以"跳过 rhs 的 monomial"那个变异对它是<b>无操作</b>，判据因此全绿。**
    ///
    /// **这一条改用 `x ≤ y + 5`：`rhs` 有一个 monomial `y`，丢掉它会让 `diff` 少一项。**
    ///
    /// **This supplements "constants do not count".**
    ///
    /// **The previous criterion used `x ≤ 5`, and `5` is the right-hand side's <b>constant</b> — its <b>monomial list is
    /// empty</b>, so the "skip rhs monomials" mutation is a <b>no-op</b> for it and the criterion stayed green.**
    ///
    /// **This one uses `x ≤ y + 5`, whose right-hand side carries a monomial `y`; dropping it loses a term.**
    /// </remarks>
    [Fact]
    public void TheVariableOnTheRight_ShouldEnterTheDifference()
    {
        var inequality = new LinearInequality<Flt64>(
            Lhs: Linear(),
            Rhs: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, Y) }, new Flt64(5.0)),
            Comparison: Comparison.LE,
            Name: "le-y");

        IReadOnlyList<LinearInequality<Flt64>> rows = SimpleIndicatorRows.Build(
            inequality, Indicator, new Flt64(100.0), Flt64.Zero, new Flt64(2.0), "ley").Value;

        // **`diff = x − y − 5`。在指示 = 1、`x = 8`、`y = 3` 处差分是 0，`_sat` 应当紧。**
        // **若丢掉 `y`，紧点会落在 `x = 5` 而不是 `x = 8`。**
        //
        // diff = x - y - 5. At indicator 1, x = 8, y = 3 the difference is zero and `_sat` is tight.
        // Dropping y moves the tight point from x = 8 to x = 5.
        Residual2(rows[0], x: 8.0, y: 3.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "the right-hand variable is part of the difference");
    }

    /// <summary>**指示 = 1 时差分不超过容差** / At indicator one the difference stays within tolerance.</summary>
    [Fact]
    public void TheSatisfiedSide_ShouldBindAtIndicatorOne()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = LessEqualRows();

        Residual(rows[0], x: 5.0, indicator: 1.0).Should().BeLessThanOrEqualTo(0.0);
        Residual(rows[0], x: 6.0, indicator: 1.0).Should().BeGreaterThan(0.0);
        Residual(rows[0], x: 6.0, indicator: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "the row relaxes at indicator zero");
    }

    /// <summary>
    /// **违反侧用 `bigM + strictBoundary`，不是 `bigM`** / The violated side uses the relaxed M.
    /// </summary>
    /// <remarks>
    /// **与下界行同一个理由：`strictBoundary` 是违反侧的间隔，
    /// 照抄 `bigM` 会让违反侧多松 `strictBoundary`。**
    ///
    /// **取 `bigM = 100`、`strictBoundary = 2`：放松量应为 `102`。**
    /// **判据断言 `x = 5 + 2 = 7` 处 `_violated` 恰好是紧的**
    /// （**`diff + 102·ind = 5 − 5 + 102 = 102`，右侧 `2`，残差 `100`……
    /// 该点在指示 = 1 时，而违反行在指示 = 1 时是放开的）。**
    ///
    /// **直接核对数值更清楚：`RelaxBigM(100, 2) = 102`，而 `_violated` 的指示项系数是 `102` 而不是 `100`。**
    ///
    /// **Same reason as the earlier lower row: `strictBoundary` is the violation-side gap, and copying `bigM` relaxes it by
    /// an extra `strictBoundary`.**
    ///
    /// **With `bigM = 100` and `strictBoundary = 2` the relaxation is `102`.**
    ///
    /// **The clearest check is numeric: `RelaxBigM(100, 2) = 102`, and the `_violated` row's indicator coefficient is
    /// `102`, not `100`.**
    /// </remarks>
    [Fact]
    public void TheViolatedSide_ShouldUseTheRelaxedM()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = LessEqualRows();

        double coefficient = IndicatorCoefficient(rows[1]);
        coefficient.Should().Be(102.0, "the violated side relaxes by big-M plus the strict boundary");

        // **紧点要解方程，不能靠心算：`diff + 102·ind ≥ 2`，而 `diff = x − 5`。**
        //
        // - **指示 = 1：`x − 5 + 102 ≥ 2` → `x ≥ −95`，紧点在 `x = −95`**
        // - **指示 = 0：`x − 5 ≥ 2` → `x ≥ 7`，那正是"违反"的分界**
        //
        // **我第一次写的 `x = −97` 是错的（把 `bigM` 当成了唯一项，忘了右侧的 `2`），
        // 判据因此变红——而红的原因是我的算术，不是实现。**
        //
        // **The tight point must be solved, not eyeballed: `diff + 102·ind ≥ 2` with `diff = x − 5`.**
        //
        // - **Indicator 1: `x − 5 + 102 ≥ 2` → `x ≥ −95`, tight at `x = −95`**
        // - **Indicator 0: `x − 5 ≥ 2` → `x ≥ 7`, exactly the violation boundary**
        //
        // **My first attempt used `x = −97` (treating big-M as the only term and dropping the right-hand two), so the
        // criterion reddened — over my arithmetic, not the implementation.**
        Residual(rows[1], x: -95.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "the violated row relaxes exactly to its own M");
        Residual(rows[1], x: 7.0, indicator: 0.0).Should().BeApproximately(
            0.0, 1e-9, "at indicator zero the violated row lands on the strict boundary");
    }

    /// <summary>**`GE` 分支的方向与容差** / The `GE` branch's direction and tolerance.</summary>
    [Fact]
    public void TheGeBranch_ShouldNegateTheConstants()
    {
        var inequality = new LinearInequality<Flt64>(
            Lhs: Linear(),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0)),
            Comparison: Comparison.GE,
            Name: "ge");

        IReadOnlyList<LinearInequality<Flt64>> rows = SimpleIndicatorRows.Build(
            inequality, Indicator, new Flt64(100.0), Flt64.Zero, new Flt64(2.0), "ge").Value;

        // **`x ≥ 5` 在指示 = 1 时应当被强制：`x = 5` 处 `_sat` 紧，`x = 4` 违反。**
        // **`x >= 5` binds at indicator 1: `_sat` is tight at x = 5 and violated at x = 4.**
        Residual(rows[0], x: 5.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: 4.0, indicator: 1.0).Should().BeLessThan(0.0);
    }

    /// <summary>**非负检测的方向与正向指示相反** / Non-negativity has the opposite polarity.</summary>
    /// <remarks>
    /// **指示 = 1 时 `poly ≥ 0`（允许等于零），指示 = 0 时 `poly ≤ −tolerance`（严格负）。**
    ///
    /// **`≥ 0` 而不是 `≥ tolerance`**：**两个问题的分界线不同。**
    ///
    /// **Indicator 1 means `poly ≥ 0` (zero allowed), indicator 0 means `poly ≤ −tolerance` (strictly negative).**
    ///
    /// **`≥ 0` rather than `≥ tolerance`**: **different dividers.**
    /// </remarks>
    [Fact]
    public void TheNonnegative_ShouldAllowZeroAtIndicatorOne()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = NonnegativeRows();

        // 指示 = 1、`x = 0`：允许等于零。
        // Indicator 1 with x = 0: zero is allowed.
        Residual(rows[0], x: 0.0, indicator: 1.0).Should().BeGreaterThanOrEqualTo(
            0.0, "non-negative includes zero");
        Residual(rows[0], x: -1.0, indicator: 1.0).Should().BeLessThan(
            0.0, "minus one is not non-negative");

        // 指示 = 0、`x = -2`：严格负，`-2 ≤ -2` 恰好为真。
        // Indicator 0 with x = -2: strictly negative, and -2 <= -2 holds.
        Residual(rows[1], x: -2.0, indicator: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the upper row forces the strict negative side");
        Residual(rows[1], x: 0.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "zero is not strictly negative");
    }

    /// <summary>
    /// **`EQ` 走零容差带，而不是被拒绝** / `EQ` goes through the zero band instead of being rejected.
    /// </summary>
    /// <remarks>
    /// **`EQ` 曾经与 `LT`/`GT`/`NE` 一起被拒绝——那是 当时零容差带还没就位的后果。**
    ///
    /// **`diff == 0` 的"偏离"有两个方向，所以需要方向列。**
    /// **调用方不给 `sideVar` 时临建一个，而<b>临建的列必须被调用方登记</b>——
    /// 否则它是自由的，方向就成了任意值，而模型完全可行。**
    ///
    /// **`EQ` was rejected together with `LT`/`GT`/`NE` — a consequence of the zero band not existing.**
    ///
    /// **"Deviates from zero" has two directions, so `EQ` needs a direction column.**
    /// **One is created when the caller supplies none, and <b>a created column must be registered by the caller</b> —
    /// otherwise it is free, the direction is arbitrary, and the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheEq_ShouldUseTheZeroBand()
    {
        var equality = new LinearInequality<Flt64>(
            Lhs: Linear(),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: Comparison.EQ,
            Name: "eq");

        IReadOnlyList<LinearInequality<Flt64>> rows = SimpleIndicatorRows.Build(
            equality, Indicator, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "eq").Value;

        rows.Should().HaveCount(4, "the zero band contributes two in-band and two deviation rows");
        // **不用 `Select`**：**`Result` 上也有一个 `Select` 扩展，重载会冲突。**
        // **Avoid `Select`: `Result` carries a `Select` extension too, and the overloads collide.**
        var names = new List<string>(rows.Count);
        foreach (LinearInequality<Flt64> row in rows) {
            names.Add(row.Name);
        }

        names.Should().Equal("eq_zero_band_ub", "eq_zero_band_lb", "eq_zero_out_lb", "eq_zero_out_ub");

        // **带内：指示 = 1 时 `|x| ≤ 2`。**
        // **In band: `|x| <= 2` at indicator 1.**
        Residual2(rows[0], x: 2.0, y: 0.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual2(rows[1], x: -2.0, y: 0.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **`EQ` 不传方向列时临建一个，而不是失败** / `EQ` creates a direction column when none is given.
    /// </summary>
    /// <remarks>
    /// **基线在 `sideVar` 为 `null` 时 `BinVar("${namePrefix}_side")`。**
    ///
    /// **这条判据只断言"能建出来且有四条行"——
    /// 它<b>不</b>断言那个临建的列被登记了，因为登记是调用方的责任，而这里没有调用方。**
    /// **把这条限制写出来，比让读的人以为"临建就完事了"要好。**
    ///
    /// **The baseline creates `BinVar("${namePrefix}_side")` when `sideVar` is null.**
    ///
    /// **The criterion only asserts "it builds, with four rows" — it does <b>not</b> assert the created column was
    /// registered, because registering is the caller's job and there is no caller here.**
    /// **Stating that limit beats letting a reader think "created, done".**
    /// </remarks>
    [Fact]
    public void TheEqWithoutASideColumn_ShouldCreateOne()
    {
        var equality = new LinearInequality<Flt64>(
            Lhs: Linear(),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: Comparison.EQ,
            Name: "eq-auto");

        IReadOnlyList<LinearInequality<Flt64>> rows = SimpleIndicatorRows.Build(
            equality, Indicator, new Flt64(100.0), new Flt64(2.0), new Flt64(4.0), "eq-auto").Value;

        rows.Should().HaveCount(4, "the created column still yields the four zero-band rows");
    }

    /// <summary>**`LT`/`GT`/`NE` 被拒绝** / `LT`/`GT`/`NE` are rejected.</summary>
    [Fact]
    public void TheUnsupportedRelations_ShouldBeRejected()
    {
        foreach (Comparison relation in new[] {
            Comparison.LT, Comparison.GT, Comparison.NE,
        }) {
            var inequality = new LinearInequality<Flt64>(
                Lhs: Linear(),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison: relation,
                Name: "unsupported");

            SimpleIndicatorRows.Build(
                inequality, Indicator, new Flt64(100.0), Flt64.Zero, new Flt64(2.0), "u").Should()
                .BeOfType<Failed<
                    IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                        $"{relation} has no simple indicator encoding");
        }
    }

    private static IReadOnlyList<LinearInequality<Flt64>> LessEqualRows() =>
        SimpleIndicatorRows.Build(
            new LinearInequality<Flt64>(
                Lhs: Linear(),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0)),
                Comparison: Comparison.LE,
                Name: "le"),
            Indicator, new Flt64(100.0), Flt64.Zero, new Flt64(2.0), "le").Value;

    private static IReadOnlyList<LinearInequality<Flt64>> NonnegativeRows() =>
        SimpleIndicatorRows.BuildNonnegative(
            Linear(), Indicator, new Flt64(100.0), new Flt64(2.0), "nn").Value;

    /// <summary>取出指示项的系数 / Read the indicator coefficient.</summary>
    private static double IndicatorCoefficient(LinearInequality<Flt64> row) {
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (monomial.Symbol.Equals(Indicator)) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new Xunit.Sdk.XunitException($"no indicator term in `{row.Name}`");
    }

    private static double Residual2(
        LinearInequality<Flt64> row, double x, double y, double indicator) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble() * ValueOf2(monomial.Symbol, x, y, indicator);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble() * ValueOf2(monomial.Symbol, x, y, indicator);
        }

        return total;
    }

    private static double ValueOf2(ISymbol symbol, double x, double y, double indicator) =>
        symbol.Equals(Indicator) ? indicator
            : symbol.Equals(X) ? x
            : symbol.Equals(Y) ? y
            : 0.0;

    private static double Residual(LinearInequality<Flt64> row, double x, double indicator) {
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

    private static readonly RealVar X = new("si-x");

    private static readonly RealVar Y = new("si-y");

    private static readonly BinVar Indicator = new("si-ind");
}
