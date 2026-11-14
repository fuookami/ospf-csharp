#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 两个大 M 的符号相反 / The two big-M terms have opposite signs.
/// </summary>
/// <remarks>
/// B1156 补 `ComplementarityFunction`（基线 `Complementarity.kt`）。
///
/// **六条约束、一个二值变量 `s`**，而第 5、6 行各自只有一个大 M 项：
/// **`s = 0` 时第 5 行把 `x` 逼到 0、`s = 1` 时第 6 行把 `y` 逼到 0——靠的是两侧都非负。**
///
/// B1156 adds `ComplementarityFunction`.
///
/// **Six rows and one binary `s`**, with rows 5 and 6 carrying a single big-M term each:
/// **at `s = 0` row 5 pins `x` to zero, at `s = 1` row 6 pins `y` to zero — relying on both sides being
/// non-negative.**
/// </remarks>
public class ComplementarityFunctionTests
{
    /// <summary>**两个大 M 的符号相反** / The two big-M terms differ in sign.</summary>
    /// <remarks>
    /// **第 5 行是 `x − xU·s ≤ 0`（selector 系数 `−xU`）、第 6 行是 `y + yU·s ≤ yU`（selector 系数 `+yU`）。**
    /// **写成都用负号会让 `s = 1` 时 `y` 反而被放大。**
    ///
    /// **Row 5 is `x − xU·s ≤ 0` (selector coefficient `−xU`) and row 6 is `y + yU·s ≤ yU` (coefficient `+yU`).**
    /// **Making both negative enlarges `y` at `s = 1` instead.**
    /// </remarks>
    [Fact]
    public void TheTwoBigMTerms_ShouldHaveOppositeSigns()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = function.Rows();

        rows.Should().HaveCount(6);
        rows[4].Name.Should().EndWith("x_mutex");
        rows[5].Name.Should().EndWith("y_mutex");

        // 第 5 行的 selector 系数是 −xU = −10。
        // Row 5's selector coefficient is -xU = -10.
        SelectorCoefficient(rows[4], function).Should().BeApproximately(-10.0, 1e-12,
            "x is bounded by xU times the selector");

        // 第 6 行的 selector 系数是 +yU = +20。
        // Row 6's selector coefficient is +yU = +20.
        SelectorCoefficient(rows[5], function).Should().BeApproximately(20.0, 1e-12,
            "y is bounded by yU times one minus the selector, which turns into a plus yU on the left");

        rows[5].Rhs.Constant.ToDouble().Should().BeApproximately(20.0, 1e-12,
            "the right-hand side of the y row is yU");
    }

    /// <summary>**`s = 0` 时第 5 行把 `x` 逼到 0** / At s = 0 row 5 pins x to zero.</summary>
    /// <remarks>
    /// **代入 `s = 0`、`x = 0`，第 5 行必须成立；而 `x = xU` 时第 5 行必须不成立**——
    /// **后者才是"逼到 0"的含义。** **只测前者的话，一个 `x ≤ xU` 的平凡约束也能通过。**
    ///
    /// **Substituting `s = 0, x = 0` must satisfy row 5, while `x = xU` must violate it** —
    /// **the latter is what "pins to zero" means.** **Testing only the first lets a trivial `x ≤ xU` row pass.**
    /// </remarks>
    [Fact]
    public void TheXMutexRow_ShouldPinXToZeroWhenTheSelectorIsZero()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);
        LinearInequality<Flt64> row = function.Rows()[4];

        double atZero = MutexValue(row, function, sideIsX: true, sideValue: 0.0, selector: 0.0);
        double atUpper = MutexValue(row, function, sideIsX: true, sideValue: 10.0, selector: 0.0);

        atZero.Should().BeLessThanOrEqualTo(0.0, "x = 0 with s = 0 satisfies the row");
        atUpper.Should().BeGreaterThan(0.0, "x = xU with s = 0 violates it, which is what pins x to zero");
    }

    /// <summary>**`s = 1` 时第 6 行把 `y` 逼到 0** / At s = 1 row 6 pins y to zero.</summary>
    [Fact]
    public void TheYMutexRow_ShouldPinYToZeroWhenTheSelectorIsOne()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);
        LinearInequality<Flt64> row = function.Rows()[5];

        double atZero = MutexValue(row, function, sideIsX: false, sideValue: 0.0, selector: 1.0);
        double atUpper = MutexValue(row, function, sideIsX: false, sideValue: 20.0, selector: 1.0);

        // 右端是 yU，所以比较对象是 yU 而不是 0。
        // The right-hand side is yU, so the comparison is against yU rather than zero.
        atZero.Should().BeLessThanOrEqualTo(20.0, "y = 0 with s = 1 satisfies the row");
        atUpper.Should().BeGreaterThan(20.0,
            "y = yU with s = 1 violates it, which is what pins y to zero");
    }

    /// <summary>**`Evaluate` 是违反指示，不是满足指示** / `Evaluate` reports a violation, not satisfaction.</summary>
    /// <remarks>
    /// **两侧同时严格为正时返回 1，否则返回 0。** **这是基线的语义，也是最容易读反的一处**——
    /// **如果读成"满足时返回 1"，那么 1 会被当成"互补成立"，而它恰恰相反。**
    ///
    /// **Returns 1 when both sides are strictly positive and 0 otherwise.** **That is the baseline's semantics and the
    /// easiest thing here to read backwards** — **read as "1 means satisfied", it would be taken to mean
    /// complementarity holds, which is the opposite.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldReportAViolationAsOne()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);

        function.Evaluate(Values(function, x: 3.0, y: 4.0)).Should().Be(
            Flt64.One, "both sides positive is a violation, so the indicator is one");
        function.Evaluate(Values(function, x: 0.0, y: 4.0)).Should().Be(
            Flt64.Zero, "x is zero, so complementarity holds and the indicator is zero");
        function.Evaluate(Values(function, x: 3.0, y: 0.0)).Should().Be(
            Flt64.Zero, "y is zero, so complementarity holds");
        function.Evaluate(Values(function, x: 0.0, y: 0.0)).Should().Be(
            Flt64.Zero, "both zero also holds");
    }

    /// <summary>**负的界被拒绝** / A negative bound is rejected.</summary>
    /// <remarks>
    /// **六条约束里第 5、6 行靠非负性把另一侧逼到 0。**
    /// **一个负的 `xL` 会让 `s = 0` 时的 `x = 0` 不成立，于是互补性静默失效（两侧都能为正）。**
    ///
    /// **Rows 5 and 6 rely on non-negativity to force the other side to zero.**
    /// **A negative `xL` breaks `x = 0` at `s = 0`, so complementarity silently stops holding (both sides can be
    /// positive).**
    /// </remarks>
    [Fact]
    public void TheComplementarity_ShouldRejectANegativeBound()
    {
        Action negativeLeft = () => _ = new ComplementarityFunction<Flt64>(
            x: Variable("comp-x"),
            y: Variable("comp-y"),
            converter: IIntoValue<Flt64>.Identity,
            xBounds: new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(10.0)),
            yBounds: new ConditionBounds<Flt64>(new Flt64(0.0), new Flt64(20.0)));
        negativeLeft.Should().Throw<ArgumentException>(
            "a negative lower bound breaks the force-to-zero mechanism");
    }

    /// <summary>**越界的输入给 `null`** / Out-of-range inputs give `null`.</summary>
    [Fact]
    public void TheEvaluate_ShouldRejectOutOfRangeInput()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);

        function.Evaluate(Values(function, x: 11.0, y: 0.0)).Should().BeNull("x is above its upper bound");
        function.Evaluate(Values(function, x: 0.0, y: 21.0)).Should().BeNull("y is above its upper bound");
    }

    /// <summary>**四个域约束在两侧各自的端点上都成立** / The four domain rows hold at both sides' ends.</summary>
    [Fact]
    public void TheDomainRows_ShouldCoverBothSides()
    {
        ComplementarityFunction<Flt64> function = Build(xUpper: 10.0, yUpper: 20.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = function.Rows();

        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[0].Rhs.Constant.ToDouble().Should().BeApproximately(10.0, 1e-12, "x's upper is ten");
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[1].Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12, "x's lower is zero");

        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[2].Rhs.Constant.ToDouble().Should().BeApproximately(20.0, 1e-12, "y's upper is twenty");
        rows[3].Comparison.Should().Be(Comparison.GE);
        rows[3].Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12, "y's lower is zero");
    }

    private static ComplementarityFunction<Flt64> Build(double xUpper, double yUpper) =>
        new(
            x: Variable("comp-x"),
            y: Variable("comp-y"),
            converter: IIntoValue<Flt64>.Identity,
            xBounds: new ConditionBounds<Flt64>(new Flt64(0.0), new Flt64(xUpper)),
            yBounds: new ConditionBounds<Flt64>(new Flt64(0.0), new Flt64(yUpper)),
            name: "comp");

    /// <summary>读一条互斥行里 selector 的系数 / Read the selector coefficient in a mutex row.</summary>
    private static double SelectorCoefficient(
        LinearInequality<Flt64> row, ComplementarityFunction<Flt64> function) {
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol, function.Selector)) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new InvalidOperationException($"row {row.Name} has no selector monomial");
    }

    /// <summary>把一条互斥行在给定点上求值 / Evaluate a mutex row at a point.</summary>
    private static double MutexValue(
        LinearInequality<Flt64> row,
        ComplementarityFunction<Flt64> function,
        bool sideIsX,
        double sideValue,
        double selector) {
        double total = row.Lhs.Constant.ToDouble();
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            double value = 0.0;
            if (ReferenceEquals(monomial.Symbol, function.Selector)) {
                value = selector;
            } else if (monomial.Symbol.Name == (sideIsX ? "comp-x" : "comp-y")) {
                value = sideValue;
            } else if (monomial.Symbol.Name == (sideIsX ? "comp-y" : "comp-x")) {
                // 另一侧在这条行里不该出现；出现就说明构造错了。
                // The other side must not appear here; if it does, the row is built wrong.
                throw new InvalidOperationException($"row {row.Name} mentions the other side");
            }

            total += monomial.Coefficient.ToDouble() * value;
        }

        return total;
    }

    private static LinearPolynomial<Flt64> Variable(string name) {
        var variable = new RealVar(name);
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);
    }

    /// <summary>
    /// 用函数自己的两个变量打包取值 / Build the dictionary keyed by the function's own variables.
    /// </summary>
    /// <remarks>
    /// **不能用 `new RealVar("comp-x")` 新建**：**`ISymbol` 的字典查找不会把两个同名的不同实例当成同一个，
    /// 于是求值永远拿不到值、恒返回 `null`。**
    /// **第一版就是这么写的，判据报 `but found <null>`。**
    ///
    /// **Creating fresh `new RealVar("comp-x")` does not work**: **a dictionary lookup does not treat two distinct
    /// instances with the same name as equal, so evaluation never finds its inputs and always returns `null`.**
    /// **The first version did exactly that and the criterion reported `but found <null>`.**
    /// </remarks>
    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        ComplementarityFunction<Flt64> function, double x, double y) =>
        new Dictionary<ISymbol, Flt64> {
            [function.X.Monomials[0].Symbol] = new Flt64(x),
            [function.Y.Monomials[0].Symbol] = new Flt64(y),
        };
}
