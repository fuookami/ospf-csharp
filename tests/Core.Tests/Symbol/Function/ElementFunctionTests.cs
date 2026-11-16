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
/// 没有下标等式，选择与下标就脱钩了 / Without the index row the selection is decoupled from the index.
/// </summary>
/// <remarks>
/// B1154 补 `ElementFunction`（基线 `Element.kt`）。
///
/// **编码是三段**：one-hot、每个表项一组屏蔽边、以及两条等式（下标等式与结果等式）。
///
/// **最容易漏的是下标等式**：**没有它 `selector` 可以是任何 one-hot 组合，
/// 而结果只是"某个表项的值"——模型仍然可行、目标值仍然合理，只是答案与输入无关。**
///
/// adds
///
/// **The encoding has three parts**: one-hot, a masking group per entry, and two equalities (index and result).
///
/// **The index equality is the one most easily dropped**: **without it `selector` can be any one-hot combination and
/// the result is just "some entry's value" — the model stays feasible and the objective reasonable, while the answer
/// has nothing to do with the input.**
/// </remarks>
public class ElementFunctionTests
{
    /// <summary>**按下标取到正确的表项** / The right entry comes back for each index.</summary>
    /// <remarks>
    /// **逐个下标都测**：**只测一个时，"永远返回第 0 项"的实现会通过。**
    ///
    /// **Every index is tested**: **with only one, an implementation that always returns entry zero passes.**
    /// </remarks>
    [Fact]
    public void TheElement_ShouldReturnTheEntryAtEachIndex()
    {
        ElementFunction<Flt64> element = Build();
        var index = (IntVar)element.Index;

        element.Evaluate(Values((index, 0.0))).Should().Be(new Flt64(4.0), "slot 0 holds 4");
        element.Evaluate(Values((index, 1.0))).Should().Be(new Flt64(7.0), "slot 1 holds 7");
        element.Evaluate(Values((index, 2.0))).Should().Be(new Flt64(2.0), "slot 2 holds 2");
    }

    /// <summary>**`lowerIndex` 是偏移：下标 1 对应槽位 0** / `lowerIndex` offsets: index 1 is slot 0.</summary>
    /// <remarks>
    /// **把两者搞混会让差一位**——**而"差一位"在任何"结果落在表项范围内"的判据下都不会变红。**
    ///
    /// **Confusing the two is an off-by-one** — **and "off by one" never turns red under any "the result lies inside
    /// the entries' range" criterion.**
    /// </remarks>
    [Fact]
    public void TheLowerIndex_ShouldShiftTheAcceptedIndices()
    {
        ElementFunction<Flt64> element = Build(lowerIndex: 1);
        var index = (IntVar)element.Index;

        element.Evaluate(Values((index, 1.0))).Should().Be(new Flt64(4.0), "with lowerIndex = 1, index 1 is slot 0");
        element.Evaluate(Values((index, 3.0))).Should().Be(new Flt64(2.0), "index 3 is slot 2");
        element.Evaluate(Values((index, 0.0))).Should().BeNull("index 0 is below the table");
        element.Evaluate(Values((index, 4.0))).Should().BeNull("index 4 is above the table");
    }

    /// <summary>**下标等式真的存在，且系数是 `−i`** / The index row exists with coefficients `−i`.</summary>
    /// <remarks>
    /// **这是本批最容易漏的一条**。**判据直接核对：`index` 的系数是 `+1`、`selector_i` 的系数是 `−i`、
    /// 右端是 `lowerIndex`。**
    ///
    /// **This is the row most easily dropped.** **The criterion checks directly: `index` coefficient `+1`,
    /// `selector_i` coefficient `−i`, right-hand side `lowerIndex`.**
    /// </remarks>
    [Fact]
    public void TheIndexRow_ShouldWeightEachSelectorByItsSlot()
    {
        ElementFunction<Flt64> element = Build(lowerIndex: 2);
        LinearInequality<Flt64> row = element.IndexRow();

        row.Comparison.Should().Be(Comparison.EQ, "the index is tied by an equality");
        row.Rhs.Constant.ToDouble().Should().BeApproximately(2.0, 1e-12,
            "the right-hand side is lowerIndex");

        // index 的系数是 +1。
        row.Lhs.Monomials[0].Symbol.Should().BeSameAs(element.Index);
        row.Lhs.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);

        // selector_0 的系数是 0、selector_1 是 -1、selector_2 是 -2。
        row.Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(0.0, 1e-12,
            "slot 0 contributes 0 * selector_0");
        row.Lhs.Monomials[2].Coefficient.ToDouble().Should().BeApproximately(-1.0, 1e-12,
            "slot 1 contributes -1 * selector_1");
        row.Lhs.Monomials[3].Coefficient.ToDouble().Should().BeApproximately(-2.0, 1e-12,
            "slot 2 contributes -2 * selector_2");
    }

    /// <summary>**one-hot：`Σ selector_i = 1`** / The one-hot row sums the selectors to one.</summary>
    /// <remarks>
    /// **没有 one-hot，<c>Σ z_i</c> 会把所有表项加起来**——**结果等式仍然成立，只是值是和的而非其中一项。**
    ///
    /// **Without it, `Σ z_i` adds up every entry** — **the result equality still holds, but the value is the sum
    /// rather than one entry.**
    /// </remarks>
    [Fact]
    public void TheOneHotRow_ShouldSumToExactlyOne()
    {
        ElementFunction<Flt64> element = Build();
        LinearInequality<Flt64> row = element.OneHotRow();

        row.Comparison.Should().Be(Comparison.EQ);
        row.Lhs.Monomials.Should().HaveCount(3, "one term per table-entry selector");
        row.Rhs.Constant.ToDouble().Should().BeApproximately(1.0, 1e-12);

        for (int slot = 0; slot < 3; ++slot) {
            row.Lhs.Monomials[slot].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12);
        }
    }

    /// <summary>**每个表项四条屏蔽边，且 `lower`/`upper` 的位置与 `Select` 一致** / Four masking rows per entry.</summary>
    /// <remarks>
    /// **`one_upper` 用 `lower`、`one_lower` 用 `upper`**——与 `SelectFunction` 的 `dL`/`dU` 位置关系相同。
    /// **写反会让 `selector = 0` 时被屏蔽值被迫非零。**
    ///
    /// **`one_upper` uses `lower` and `one_lower` uses `upper`**, matching `SelectFunction`'s `dL`/`dU` placement.
    /// **A swap forces the masked value non-zero when `selector = 0`.**
    /// </remarks>
    [Fact]
    public void TheMaskingRows_ShouldPlaceLowerAndUpperLikeSelect()
    {
        ElementFunction<Flt64> element = Build();
        IReadOnlyList<LinearInequality<Flt64>> rows = element.MaskingRows(1);

        rows.Should().HaveCount(4);
        rows[0].Name.Should().EndWith("zero_upper");
        rows[1].Name.Should().EndWith("zero_lower");
        rows[2].Name.Should().EndWith("one_upper");
        rows[3].Name.Should().EndWith("one_lower");

        // slot 1 的区间是 [6, 8]。
        // zero_upper: z - 8*sel <= 0  → selector 系数 -8
        // zero_lower: z - 6*sel >= 0  → selector 系数 -6
        rows[0].Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(-8.0, 1e-12);
        rows[1].Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(-6.0, 1e-12);

        // one_upper: z - input - 6*sel + 6 <= 0 → 右端 -6
        // one_lower: z - input - 8*sel + 8 >= 0 → 右端 -8
        rows[2].Rhs.Constant.ToDouble().Should().BeApproximately(-6.0, 1e-12,
            "one_upper anchors on the lower bound");
        rows[3].Rhs.Constant.ToDouble().Should().BeApproximately(-8.0, 1e-12,
            "one_lower anchors on the upper bound");
    }

    /// <summary>**`selector = 0` 时四条边允许被屏蔽值为 0** / At selector 0 the rows allow a zero.</summary>
    [Fact]
    public void TheMaskingRows_ShouldAllowZeroWhenNotSelected()
    {
        ElementFunction<Flt64> element = Build();
        IReadOnlyList<LinearInequality<Flt64>> rows = element.MaskingRows(1);

        foreach (LinearInequality<Flt64> row in rows) {
            double lhs = MaskRowValue(row, element, slot: 1, selector: 0.0, z: 0.0);
            double rhs = row.Rhs.Constant.ToDouble();

            bool satisfied = row.Comparison switch {
                Comparison.LE => lhs <= rhs + 1e-12,
                Comparison.GE => lhs >= rhs - 1e-12,
                _ => global::System.Math.Abs(lhs - rhs) <= 1e-12,
            };

            satisfied.Should().BeTrue(
                $"{row.Name} must accept z = 0 when unselected: lhs = {lhs}, rhs = {rhs}");
        }
    }

    /// <summary>**`selector = 1` 时四条边把被屏蔽值夹成表项值** / At selector 1 the rows pin the entry value.</summary>
    [Fact]
    public void TheMaskingRows_ShouldPinTheEntryWhenSelected()
    {
        ElementFunction<Flt64> element = Build();
        IReadOnlyList<LinearInequality<Flt64>> rows = element.MaskingRows(1);

        // slot 1 的表项是常数 7。
        foreach (LinearInequality<Flt64> row in rows) {
            double lhs = MaskRowValue(row, element, slot: 1, selector: 1.0, z: 7.0);
            double rhs = row.Rhs.Constant.ToDouble();

            bool satisfied = row.Comparison switch {
                Comparison.LE => lhs <= rhs + 1e-12,
                Comparison.GE => lhs >= rhs - 1e-12,
                _ => global::System.Math.Abs(lhs - rhs) <= 1e-12,
            };

            satisfied.Should().BeTrue(
                $"{row.Name} must accept z = entry when selected: lhs = {lhs}, rhs = {rhs}");
        }
    }

    /// <summary>**连续下标被拒绝** / A continuous index is rejected.</summary>
    /// <remarks>
    /// **连续下标会让 one-hot 加下标等式把 `index` 逼成整数——看起来"自动取整"，
    /// 实际上是模型在替调用方做四舍五入。**
    ///
    /// **A continuous index lets one-hot plus the index equality force it to an integer — which looks like "automatic
    /// rounding" but is the model rounding on the caller's behalf.**
    /// </remarks>
    [Fact]
    public void TheElement_ShouldRejectAContinuousIndex()
    {
        var continuous = new RealVar("element-continuous");
        continuous.Range.Geq(Flt64.Zero);
        continuous.Range.Leq(new Flt64(2.0));

        Action build = () => _ = new ElementFunction<Flt64>(
            index: continuous,
            values: new[] { Constant(4.0), Constant(7.0), Constant(2.0) },
            converter: IIntoValue<Flt64>.Identity,
            valueBounds: Bounds());
        build.Should().Throw<ArgumentException>("an element lookup needs an integer index");
    }

    /// <summary>造一个三表项的元素 / Build a three-entry element.</summary>
    private static ElementFunction<Flt64> Build(int lowerIndex = 0) {
        // 下标区间不参与本函数的推导（表项区间由 valueBounds 显式给出），所以这里不设。
        // The index range plays no part here: the entry intervals come from valueBounds, so none is set.
        var index = new IntVar("element-index");

        return new ElementFunction<Flt64>(
            index: index,
            values: new[] { Constant(4.0), Constant(7.0), Constant(2.0) },
            converter: IIntoValue<Flt64>.Identity,
            lowerIndex: lowerIndex,
            valueBounds: Bounds(),
            name: "element");
    }

    /// <summary>三个表项的区间：`[4,4]`、`[6,8]`、`[2,2]` / The entries' intervals.</summary>
    private static IReadOnlyList<ConditionBounds<Flt64>> Bounds() => new[] {
        new ConditionBounds<Flt64>(new Flt64(4.0), new Flt64(4.0)),
        new ConditionBounds<Flt64>(new Flt64(6.0), new Flt64(8.0)),
        new ConditionBounds<Flt64>(new Flt64(2.0), new Flt64(2.0)),
    };

    /// <summary>把一条屏蔽边在给定点上求值 / Evaluate a masking row at a point.</summary>
    private static double MaskRowValue(
        LinearInequality<Flt64> row, ElementFunction<Flt64> element, int slot, double selector, double z) {
        // 屏蔽边只含三类符号：被屏蔽值、selector、以及表项自身的 monomial。
        // A masking row holds three kinds of symbols: the masked value, the selector, and the entry's monomials.
        double total = row.Lhs.Constant.ToDouble();
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            double value = 0.0;
            if (ReferenceEquals(monomial.Symbol, element.MaskedValues[slot])) {
                value = z;
            } else if (ReferenceEquals(monomial.Symbol, element.Selectors[slot])) {
                value = selector;
            }

            total += monomial.Coefficient.ToDouble() * value;
        }

        return total;
    }

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        params (IVariableItem Variable, double Value)[] pairs) {
        var values = new Dictionary<ISymbol, Flt64>();
        foreach ((IVariableItem variable, double value) in pairs) {
            values[variable] = new Flt64(value);
        }

        return values;
    }
}
