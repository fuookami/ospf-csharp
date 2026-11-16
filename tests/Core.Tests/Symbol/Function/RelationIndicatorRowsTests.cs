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
/// 关系指示：用条件自身的范围做两行，不需要 big-M /
/// Relation indicators derive both rows from the condition's own bounds, with no big-M.
/// </summary>
/// <remarks>
/// 对齐基线 `Conditional.kt` 的 `relationIndicatorConstraints`。
///
/// **归一化后的语义要先说清**：**`relation` 是相对<b>零</b>的，`bounds` 只是 `q` 的取值范围。**
///
/// | 关系 | `trueLower` | `falseUpper` | 含义 |
/// | --- | --- | --- | --- |
/// | `GE` | `0` | `−δ` | 真：`q ≥ 0`；假：`q ≤ −δ` |
/// | `GT` | `δ` | `0` | 真：`q ≥ δ`；假：`q ≤ 0` |
/// | `LE` | `0` | `−δ` | 取负后同上 |
/// | `LT` | `δ` | `0` | 取负后同上 |
///
/// **所以 `bounds` 必须<b>跨过零</b>，否则其中一个分支为空。**
/// **我第一次用 `[0, 10]` 测 `GE`：`qLower = 0`、`falseUpper = −2 < qLower`，
/// 假分支为空，条件在整个范围上恒真——两行因此退化成与指示无关。**
/// **判据红了，红的原因是我的输入选错，不是实现。**
///
/// **还有一次同类的错**：**helper 的第二个参数是 `strictBoundary`，而我在所有调用处传了 `0.0`。**
/// **`Normalize` 要求它为正，于是每个判据都拿到 `Failed`，而 `.Value` 在 `Failed` 上是 `null`，
/// 报出来的是 `NullReferenceException`——指向 `Rows(...)` 那一行，完全看不出真正的原因。**
///
/// **"参数位置对了、值传错了"是这个 helper 形状的代价：
/// 三个同类型参数排在一起，编译器帮不上忙。**
///
/// adds
///
/// **The normalized semantics come first: `relation` is relative to <b>zero</b>, and `bounds` is only `q`'s range.**
///
/// **So `bounds` must <b>straddle zero</b>, or one branch is empty.**
/// **My first attempt used `[0, 10]` for `GE`: `qLower = 0` and `falseUpper = −2 < qLower`, so the false branch was
/// empty, the condition held across the whole range, and both rows degenerated to indicator-independent.**
/// **The criterion reddened over my input, not the implementation.**
/// </remarks>
public class RelationIndicatorRowsTests
{
    /// <summary>
    /// **两行都落在条件自己的边界上，与 big-M 无关** / Both rows land on the condition's own bounds.
    /// </summary>
    /// <remarks>
    /// **取 `x` 在 `[−10, 10]`、`relation = GE`、`δ = 2`：**
    /// **`qLower = −10`、`qUpper = 10`、`trueLower = 0`、`falseUpper = −2`。**
    ///
    /// - **`lowerAdjustment = 0 − (−10) = 10`**
    /// - **`upperAdjustment = 10 − (−2) = 12`**
    ///
    /// **于是 `_lower` 在指示 = 1 时是 `x ≥ 0`，`_upper` 在指示 = 0 时是 `x ≤ −2`。**
    ///
    /// **两个系数直接来自范围差，没有 big-M 参与。**
    ///
    /// **With `x` over `[−10, 10]`, `GE`, `δ = 2`: `qLower = −10`, `qUpper = 10`, `trueLower = 0`,
    /// `falseUpper = −2`.** **So `lowerAdjustment = 10` and `upperAdjustment = 12`; `_lower` is `x ≥ 0` at indicator 1
    /// and `_upper` is `x ≤ −2` at indicator 0.** **Both coefficients come from the range; no big-M takes part.**
    /// </remarks>
    [Fact]
    public void TheRows_ShouldLandOnTheConditionsOwnBounds()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(Comparison.GE, 2.0);

        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("r_lower");
        rows[0].Comparison.Should().Be(Comparison.GE);
        rows[1].Name.Should().Be("r_upper");
        rows[1].Comparison.Should().Be(Comparison.LE);

        // **指示 = 1：`_lower` 紧在 `x = 0`（`x ≥ 0`）。**
        // **Indicator 1: `_lower` is tight at x = 0.**
        Residual(rows[0], x: 0.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: -1.0, indicator: 1.0).Should().BeLessThan(0.0, "minus one is not non-negative");
    }

    /// <summary>
    /// **两行在指示 = 1 与 = 0 时互换松紧** / The two rows swap binding sides.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **`_lower` 在指示 = 1 时紧、`_upper` 在指示 = 0 时紧。**
    /// **把两个松弛量写反，两条行都会落到错误位置，而<b>模型仍然可行</b>——
    /// 只是条件在指示 = 1 时没有被强制。**
    ///
    /// **所以判据在<b>两条行、两个指示值</b>四处都断言。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **Swapping the two relaxations puts both rows in the wrong place while the <b>model stays feasible</b> — the
    /// condition is merely not enforced at indicator 1.**
    ///
    /// **So the criterion asserts at all four points: two rows, two indicator values.**
    /// </remarks>
    [Fact]
    public void TheTwoRows_ShouldSwapWhichOneBinds()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(Comparison.GE, 2.0);

        // 下界行：指示 = 1 紧在 `x ≥ 0`；指示 = 0 放开到 `x ≥ −10`。
        // Lower row: tight at x >= 0 for indicator 1; relaxed to x >= -10 for zero.
        Residual(rows[0], x: 0.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: -10.0, indicator: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the lower row relaxes to the range's own lower edge");
        Residual(rows[0], x: -11.0, indicator: 0.0).Should().BeLessThan(
            0.0, "and it does not relax past the range");

        // 上界行：指示 = 0 紧在 `x ≤ −2`；指示 = 1 放开到 `x ≤ 10`。
        // Upper row: tight at x <= -2 for indicator 0; relaxed to x <= 10 for one.
        Residual(rows[1], x: -2.0, indicator: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the upper row binds the false branch");
        Residual(rows[1], x: -1.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "minus one is not in the false branch");
        Residual(rows[1], x: 10.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "the upper row relaxes to the range's own upper edge");
        Residual(rows[1], x: 11.0, indicator: 1.0).Should().BeGreaterThan(
            0.0, "and it does not relax past the range");
    }

    /// <summary>**`LE` 会先取负** / `LE` negates the polynomial first.</summary>
    /// <remarks>
    /// **`x ≤ 0` 折成 `−x ≥ 0`，而 `qLower`/`qUpper` 也跟着换位。**
    /// **只取负多项式而不换范围，两个调整量会算出相反的符号。**
    ///
    /// **`x ≤ 0` folds into `−x ≥ 0`, and `qLower`/`qUpper` swap accordingly.**
    /// **Negating only the polynomial without the range yields adjustment signs that are both wrong.**
    /// </remarks>
    [Fact]
    public void TheLeRelation_ShouldNegateAndStillBind()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(Comparison.LE, 2.0);

        // **指示 = 1 时 `x ≤ 0` 应当被强制。**
        // **`x <= 0` must be enforced at indicator 1.**
        Residual(rows[0], x: 0.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: 1.0, indicator: 1.0).Should().BeLessThan(0.0, "one is above zero");
    }

    /// <summary>
    /// **`GT`/`LT` 的真分支从 `δ` 开始** / `GT`/`LT` start the true branch at `δ`.
    /// </summary>
    /// <remarks>
    /// **严格关系需要一个正间隔：真分支 `q ≥ δ`，假分支 `q ≤ 0`。**
    /// **这正是"严格"的编码方式——不是把 `<` 变成 `≤`，而是在两个分支之间留一条空带。**
    ///
    /// **Strict relations need a positive gap: true is `q ≥ δ`, false is `q ≤ 0`.**
    /// **That is how "strict" is encoded — not by turning `<` into `≤`, but by leaving an empty band between the
    /// branches.**
    /// </remarks>
    [Fact]
    public void TheStrictRelations_ShouldLeaveAGap()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(Comparison.GT, 2.0);

        // 指示 = 1：`x ≥ 2`。
        // Indicator 1: x >= 2.
        Residual(rows[0], x: 2.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], x: 1.0, indicator: 1.0).Should().BeLessThan(0.0);

        // 指示 = 0：`x ≤ 0`，而 `x = 1` 落在空带里，两个分支都不接受。
        // Indicator 0: x <= 0, and x = 1 falls in the gap that neither branch accepts.
        Residual(rows[1], x: 0.0, indicator: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[1], x: 1.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "one lies in the strict gap and is not in the false branch");
    }

    /// <summary>
    /// **多项式不得包含指示变量自己** / The polynomial must not contain its own indicator.
    /// </summary>
    /// <remarks>
    /// **否则 `ind` 既在被判定的式子里又在判定它。**
    /// **后果不是"不可行"，而是这条约束在 `ind = 1` 时变成 `(1 − adj)·1 ≥ …`——
    /// 指示列自己承担了一部分判定，语义整个走样。**
    ///
    /// **Otherwise `ind` both governs and is governed.**
    /// **The consequence is not infeasibility: at `ind = 1` the row becomes `(1 − adj)·1 ≥ …`, so the indicator carries
    /// part of the decision and the semantics drift entirely.**
    /// </remarks>
    [Fact]
    public void TheSelfReferencingPolynomial_ShouldBeRejected()
    {
        var selfReferencing = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Indicator) }, Flt64.Zero);

        RelationIndicatorRows.Build(
            selfReferencing, Indicator, Comparison.GE, Range(), new Flt64(2.0), "r").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "an indicator cannot appear in the polynomial it indicates");
    }

    /// <summary>**非二值指示被拒绝** / A non-binary indicator is rejected.</summary>
    [Fact]
    public void TheNonBinaryIndicator_ShouldBeRejected()
    {
        RelationIndicatorRows.Build(
            Linear(), X, Comparison.GE, Range(), new Flt64(2.0), "r").Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a continuous column cannot carry a truth value");
    }

    /// <summary>**四种关系都能建出来** / All four relations build.</summary>
    /// <remarks>
    /// **`LT` 也要单独立一例**：**它的取负路径与 `LE` 相同，但 `trueLower` 不同——
    /// 只测其中一个的话，`GT`/`LT` 的那个 `δ` 分支可能整个漏掉。**
    ///
    /// **`LT` gets its own case**: **its negation matches `LE`'s while `trueLower` differs — testing only one would let
    /// the `δ` variant of `GT`/`LT` go entirely unexercised.**
    /// </remarks>
    [Fact]
    public void TheFourRelations_ShouldAllBuild()
    {
        foreach (Comparison relation in new[] {
            Comparison.LE, Comparison.GE, Comparison.LT, Comparison.GT,
        }) {
            IReadOnlyList<LinearInequality<Flt64>> rows = Rows(relation, 2.0);

            rows.Should().HaveCount(2, $"{relation} has a two-row encoding");
            rows[0].Name.Should().Be("r_lower");
            rows[1].Name.Should().Be("r_upper");

            // **四种关系都必须在指示 = 1 时把真分支的边界夹住。**
            //
            // **`LE`/`LT` 先取负（`q = −x`），所以真分支的边界落在 `x` 的<b>负</b>侧：**
            //
            // | 关系 | 真分支 | 指示 = 1 的紧点 |
            // | --- | --- | --- |
            // | `GE` | `x ≥ 0` | `x = 0` |
            // | `GT` | `x ≥ 2` | `x = 2` |
            // | `LE` | `−x ≥ 0` | `x = 0` |
            // | `LT` | `−x ≥ 2` | `x = −2` |
            //
            // **`LT` 与 `GT` 的紧点符号相反——这是取负的必然后果，不是实现差异。**
            //
            // **All four must confine the true branch's edge at indicator 1.**
            // **`LE`/`LT` negate first (`q = −x`), so the true edge lands on `x`'s <b>negative</b> side.**
            // **`LT` and `GT` therefore have opposite tight-point signs — a consequence of negation, not of the
            // implementation.**
            double tight = relation switch {
                Comparison.GT => 2.0,
                Comparison.LT => -2.0,
                _ => 0.0,
            };

            Residual(rows[0], x: tight, indicator: 1.0).Should().BeApproximately(
                0.0, 1e-9, $"{relation}'s lower row lands on the true edge");
        }
    }

    private static ConditionBounds<Flt64> Range() =>
        new(new Flt64(-10.0), new Flt64(10.0));

    private static IReadOnlyList<LinearInequality<Flt64>> Rows(
        Comparison relation, double strictBoundary) =>
        RelationIndicatorRows.Build(
            Linear(), Indicator, relation, Range(), new Flt64(strictBoundary), "r").Value;

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

    private static readonly RealVar X = new("ri-x");

    private static readonly BinVar Indicator = new("ri-ind");
}
