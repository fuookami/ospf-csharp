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
/// 四条边的配对方式不是随便配的 / The pairing of the four edges is not arbitrary.
/// </summary>
/// <remarks>
/// B1152 补 `ConditionBounds`（基线 `Conditional.kt`）与 `McCormickEnvelopeFunction`（基线 `McCormickEnvelope.kt`）。
///
/// **两条 `≥` 必须用"一个角的两侧同号"（`(xL,yL)` 与 `(xU,yU)`），
/// 两条 `≤` 必须用"交叉配对"（`(xU,yL)` 与 `(xL,yU)`）。**
///
/// **把配对弄错会得到一个不含 `x·y` 的多面体——松弛"合法"但无效，而求解器不会报错，
/// 它只会返回一个更好的目标值。**
///
/// adds
///
/// **The two `≥` rows use the same-corner pairing (`(xL,yL)` and `(xU,yU)`) and the two `≤` rows use the cross pairing
/// (`(xU,yL)` and `(xL,yU)`).**
///
/// **Getting the pairing wrong yields a polyhedron that does not contain `x·y` — the relaxation is "legal" and
/// useless, and the solver does not complain; it just reports a better objective.**
/// </remarks>
public class McCormickEnvelopeTests
{
    /// <summary>**四条边的名字、方向与顺序都是固定的** / The four edges' names, directions and order are fixed.</summary>
    /// <remarks>
    /// **顺序固定为下左、下右、上左、上右**，**因为后面的判据逐条核对系数，顺序不稳会让核对变成"找到就行"。**
    ///
    /// **The order is fixed at lower-left, lower-right, upper-left, upper-right**, **because the criteria below check
    /// coefficients row by row, and an unstable order would turn that into "find it anywhere".**
    /// </remarks>
    [Fact]
    public void TheFourEdges_ShouldKeepTheirOrderNamesAndDirections()
    {
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = envelope.EnvelopeRows();

        rows.Should().HaveCount(4);
        rows[0].Name.Should().EndWith("lower_left");
        rows[1].Name.Should().EndWith("lower_right");
        rows[2].Name.Should().EndWith("upper_left");
        rows[3].Name.Should().EndWith("upper_right");

        rows[0].Comparison.Should().Be(Comparison.GE, "a lower edge bounds w from below");
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE, "an upper edge bounds w from above");
        rows[3].Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **两条 `≥` 用的是同角配对、两条 `≤` 用的是交叉配对** / Same-corner for GE, crossed for LE.
    /// </summary>
    /// <remarks>
    /// **逐条核对右端项（`rhs = −角乘积`），因为那正是配对方式的指纹**：
    /// **`≥` 行的右端是 `−xL·yL` 与 `−xU·yU`（同角）；`≤` 行的右端是 `−xU·yL` 与 `−xL·yU`（交叉）。**
    ///
    /// **This checks each row's right-hand side (`rhs = −corner product`), which is the fingerprint of the pairing**:
    /// **the `≥` rows carry `−xL·yL` and `−xU·yU` (same corner); the `≤` rows carry `−xU·yL` and `−xL·yU` (crossed).**
    /// </remarks>
    [Fact]
    public void TheRightHandSides_ShouldShowTheIntendedPairing()
    {
        // x ∈ [-1, 2]、y ∈ [-3, 4] → 四个角乘积：
        // (-1)(-3)=3、(-1)(4)=-4、(2)(-3)=-6、(2)(4)=8
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = envelope.EnvelopeRows();

        // 下左（同角 xL,yL）→ rhs = -3
        rows[0].Rhs.Constant.ToDouble().Should().BeApproximately(-3.0, 1e-12,
            "the same-corner row for (xL, yL) carries -((-1)*(-3))");

        // 下右（同角 xU,yU）→ rhs = -8
        rows[1].Rhs.Constant.ToDouble().Should().BeApproximately(-8.0, 1e-12,
            "the same-corner row for (xU, yU) carries -((2)*(4))");

        // 上左（交叉 xU,yL）→ rhs = +6
        rows[2].Rhs.Constant.ToDouble().Should().BeApproximately(6.0, 1e-12,
            "the crossed row for (xU, yL) carries -((2)*(-3))");

        // 上右（交叉 xL,yU）→ rhs = +4
        rows[3].Rhs.Constant.ToDouble().Should().BeApproximately(4.0, 1e-12,
            "the crossed row for (xL, yU) carries -((-1)*(4))");
    }

    /// <summary>**每条边的系数是 `w − yScale·x − xScale·y`** / Each edge's coefficients are `w − yScale·x − xScale·y`.</summary>
    /// <remarks>
    /// **把两个缩放因子写反（`xScale` 乘 `x`、`yScale` 乘 `y`）会让四条边对称化，
    /// 于是 `x` 与 `y` 交换之后结果不变——而那是一个松弛错误的方向都看不出来的错。**
    ///
    /// **Swapping the two scale factors would symmetrise the four edges, making the result invariant under exchanging
    /// `x` and `y` — an error invisible from any relaxation direction.**
    /// </remarks>
    [Fact]
    public void EachEdge_ShouldScaleTheFactorsByTheOtherOnesBound()
    {
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = envelope.EnvelopeRows();

        // 行里固定是 [w, x, y] 三个 monomial（各因子都是单变量），所以按位置取。
        // Each row holds exactly [w, x, y] because every factor is a single variable, so positions pin it down.
        rows[0].Lhs.Monomials.Should().HaveCount(3, "w plus one monomial from each single-variable factor");

        // 下左行：leftScale = yL = -3、rightScale = xL = -1 → x 系数 -yL = 3、y 系数 -xL = 1。
        rows[0].Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(3.0, 1e-12,
            "the x coefficient is -yL, which is 3");
        rows[0].Lhs.Monomials[2].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12,
            "the y coefficient is -xL, which is 1");

        // 下右行：leftScale = yU = 4、rightScale = xU = 2 → x 系数 -4、y 系数 -2。
        rows[1].Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(-4.0, 1e-12);
        rows[1].Lhs.Monomials[2].Coefficient.ToDouble().Should().BeApproximately(-2.0, 1e-12);
    }

    /// <summary>**每一条边都真的包含 `x·y`** / Every edge actually contains `x·y`.</summary>
    /// <remarks>
    /// **这是包络的定义性质，也是最该测的一条**：
    /// **把 `x = xL`、`y = yL`（或其它三个角）代入每一条边，让 `w = x·y` 时四条都必须成立。**
    ///
    /// **配对配错时，某些角会违反某些边**——**而"角上取等号"这个性质在正确实现下对全部四个角都成立。**
    ///
    /// **This is the envelope's defining property and the one worth testing most**:
    /// **substituting `x = xL, y = yL` (and the other three corners) with `w = x·y` must satisfy all four rows.**
    ///
    /// **With a wrong pairing some corner violates some row** — **while "tight at every corner" holds for all four
    /// corners under a correct implementation.**
    /// </remarks>
    [Fact]
    public void EveryEdge_ShouldHoldAtEveryCorner()
    {
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = envelope.EnvelopeRows();

        (double X, double Y)[] corners = { (-1.0, -3.0), (-1.0, 4.0), (2.0, -3.0), (2.0, 4.0) };

        foreach ((double x, double y) in corners) {
            double product = x * y;
            foreach (LinearInequality<Flt64> row in rows) {
                double lhs = RowValue(row, x, y, product);
                double rhs = row.Rhs.Constant.ToDouble();

                bool satisfied = row.Comparison switch {
                    Comparison.GE => lhs >= rhs - 1e-12,
                    Comparison.LE => lhs <= rhs + 1e-12,
                    Comparison.EQ => global::System.Math.Abs(lhs - rhs) <= 1e-12,
                    _ => true,
                };

                satisfied.Should().BeTrue(
                    $"{row.Name} must hold at the corner ({x}, {y}) with w = {product}: lhs = {lhs}, rhs = {rhs}");
            }
        }
    }

    /// <summary>**结果变量的范围是四个角乘积的包围** / The result range spans the four corner products.</summary>
    /// <remarks>
    /// **`(-1)(-3)=3`、`(-1)(4)=-4`、`(2)(-3)=-6`、`(2)(4)=8` → 范围是 `[-6, 8]`。**
    /// **注意最小值来自交叉角、最大值来自交叉角——若只算 `(xL,yL)` 与 `(xU,yU)` 会得到 `[3, 8]`，那是错的。**
    ///
    /// **The corners are 3, -4, -6 and 8, so the range is `[-6, 8]`.**
    /// **Note the minimum comes from the same-corner pair and the maximum from it too, but only after all four are
    /// considered — using only `(xL,yL)` and `(xU,yU)` would give `[3, 8]`, which is wrong.**
    /// </remarks>
    [Fact]
    public void TheResultBounds_ShouldSpanAllFourCornerProducts()
    {
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);

        envelope.ResultBounds.Lower.ToDouble().Should().BeApproximately(-6.0, 1e-12,
            "the smallest corner product is (2)(-3) = -6");
        envelope.ResultBounds.Upper.ToDouble().Should().BeApproximately(8.0, 1e-12,
            "the largest corner product is (2)(4) = 8");
    }

    /// <summary>**越界的输入给 `null`，而不是夹到端点** / Out-of-range inputs give `null`, not a clamp.</summary>
    /// <remarks>
    /// **夹到端点会给出一个看起来合法的数，而那个数对应的是另一个输入。**
    ///
    /// **Clamping yields a plausible-looking number that corresponds to a different input.**
    /// </remarks>
    [Fact]
    public void TheProduct_ShouldReturnNullOutsideTheBox()
    {
        McCormickEnvelopeFunction<Flt64> envelope = Build(-1.0, 2.0, -3.0, 4.0);
        var x = (RealVar)envelope.Left.Monomials[0].Symbol;
        var y = (RealVar)envelope.Right.Monomials[0].Symbol;

        envelope.EvaluateProduct(Values((x, 1.0), (y, 2.0))).Should().Be(
            new Flt64(2.0), "1 * 2 is inside the box");

        envelope.EvaluateProduct(Values((x, 5.0), (y, 2.0))).Should().BeNull(
            "x = 5 leaves the box [-1, 2]");

        envelope.EvaluateProduct(Values((x, 1.0), (y, -9.0))).Should().BeNull(
            "y = -9 leaves the box [-3, 4]");
    }

    /// <summary>**`ConditionBounds` 拒绝上下界写反** / `ConditionBounds` rejects swapped bounds.</summary>
    /// <remarks>
    /// **空区间不是"一个空集合"，而是调用方把两个字段写反了**——
    /// **而空区间会让四条不等式互相矛盾，求解器只报告"不可行"，错误信息离原因很远。**
    ///
    /// **An empty interval is not "an empty set" but a caller who swapped the two fields** —
    /// **and it makes the four rows contradict each other, so the solver reports only "infeasible" and the message is
    /// far from the cause.**
    /// </remarks>
    [Fact]
    public void TheConditionBounds_ShouldRejectSwappedBounds()
    {
        Action swapped = () => _ = new ConditionBounds<Flt64>(new Flt64(2.0), new Flt64(-1.0));
        swapped.Should().Throw<ArgumentException>("the lower bound must not exceed the upper bound");

        // 退化的区间（lower == upper）是合法的：它就是一个点。
        // A degenerate interval (lower == upper) is legal: it is a single point.
        new ConditionBounds<Flt64>(new Flt64(2.0), new Flt64(2.0))
            .Contains(new Flt64(2.0)).Should().BeTrue();
    }

    /// <summary>造一个包络，两个因子各是一个变量 / Build an envelope over two plain variables.</summary>
    private static McCormickEnvelopeFunction<Flt64> Build(
        double xLower, double xUpper, double yLower, double yUpper) {
        var x = new RealVar("mccormick-x");
        var y = new RealVar("mccormick-y");

        return new McCormickEnvelopeFunction<Flt64>(
            left: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            right: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
            leftBounds: new ConditionBounds<Flt64>(new Flt64(xLower), new Flt64(xUpper)),
            rightBounds: new ConditionBounds<Flt64>(new Flt64(yLower), new Flt64(yUpper)),
            converter: IIntoValue<Flt64>.Identity,
            name: "mccormick");
    }

    /// <summary>把一行在给定点上求值 / Evaluate one row at a point.</summary>
    private static double RowValue(LinearInequality<Flt64> row, double x, double y, double w) {
        // 位置约定：[w, x, y]。
        // Positional convention: [w, x, y].
        return row.Lhs.Constant.ToDouble()
            + row.Lhs.Monomials[0].Coefficient.ToDouble() * w
            + row.Lhs.Monomials[1].Coefficient.ToDouble() * x
            + row.Lhs.Monomials[2].Coefficient.ToDouble() * y;
    }

    /// <summary>把若干 (变量, 值) 打包成评估字典 / Pack pairs into an evaluation dictionary.</summary>
    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        params (RealVar Variable, double Value)[] pairs) {
        var values = new Dictionary<ISymbol, Flt64>();
        foreach ((RealVar variable, double value) in pairs) {
            values[variable] = new Flt64(value);
        }

        return values;
    }
}
