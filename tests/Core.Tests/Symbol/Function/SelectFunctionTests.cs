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
/// 四条屏蔽边里 `dL` 与 `dU` 的位置不能互换 / `dL` and `dU` cannot swap places in the four masking rows.
/// </summary>
/// <remarks>
/// 补 `SelectFunction`（基线 `Select.kt`）。
///
/// **编码的关键是"被屏蔽的差"**：令 <c>d = then − otherwise</c>、辅助变量 <c>z = mask · d</c>，
/// 于是 <c>result = otherwise + z</c>——**一条等式就够，不需要两条分支不等式。**
///
/// **The encoding turns on "the masked difference"**: `d = then − otherwise` and an auxiliary `z = mask · d`, so
/// `result = otherwise + z` — **one equality suffices.**
/// </remarks>
public class SelectFunctionTests
{
    /// <summary>**`mask = 1` 走 then、`mask = 0` 走 otherwise** / mask 1 takes then, mask 0 takes otherwise.</summary>
    /// <remarks>
    /// **两者都测，因为"分支反了"是这里最容易犯的错**——
    /// **而只测一条时，把两个分支互换的实现会通过。**
    ///
    /// **Both are tested because "the branches are swapped" is the easiest mistake here** —
    /// **with only one, an implementation that exchanges them passes.**
    /// </remarks>
    [Fact]
    public void TheSelect_ShouldPickTheBranchTheMaskSays()
    {
        SelectFunction<Flt64> select = Build(thenConstant: 10.0, elseConstant: 3.0);
        var mask = (BinVar)select.Mask;

        select.Evaluate(Values((mask, 1.0))).Should().Be(
            new Flt64(10.0), "mask = 1 takes the then-branch");
        select.Evaluate(Values((mask, 0.0))).Should().Be(
            new Flt64(3.0), "mask = 0 takes the else-branch");
    }

    /// <summary>**`mask` 既非 0 也非 1 时给 `null`** / A mask that is neither 0 nor 1 gives `null`.</summary>
    /// <remarks>
    /// **不是"取中间值"**：**`mask = 0.5` 在语义上不存在**——
    /// **而那四条边在 `mask = 0.5` 时仍然可满足，给出一个看起来像插值的数。**
    /// **所以语义路径必须显式拒绝，不能依赖编码。**
    ///
    /// **Not "take the middle"**: **`mask = 0.5` does not exist semantically** —
    /// **while the four rows remain satisfiable there, yielding a number that looks like an interpolation.**
    /// **So the semantic path rejects it explicitly rather than relying on the encoding.**
    /// </remarks>
    [Fact]
    public void TheSelect_ShouldRejectANonBinaryMask()
    {
        SelectFunction<Flt64> select = Build(thenConstant: 10.0, elseConstant: 3.0);
        var mask = (BinVar)select.Mask;

        select.Evaluate(Values((mask, 0.5))).Should().BeNull(
            "a mask of one half is an illegal input, not an interpolation");
    }

    /// <summary>**四条屏蔽边的名字、方向与顺序固定** / The four masking rows keep their names, directions and order.</summary>
    [Fact]
    public void TheMaskingRows_ShouldKeepTheirOrderAndDirections()
    {
        SelectFunction<Flt64> select = Build(thenConstant: 10.0, elseConstant: 3.0);
        IReadOnlyList<LinearInequality<Flt64>> rows = select.MaskingRows();

        rows.Should().HaveCount(4);
        rows[0].Name.Should().EndWith("zero_upper");
        rows[1].Name.Should().EndWith("zero_lower");
        rows[2].Name.Should().EndWith("one_upper");
        rows[3].Name.Should().EndWith("one_lower");

        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Comparison.Should().Be(Comparison.GE);
    }

    /// <summary>
    /// **`dL` 与 `dU` 的位置是这两条边的指纹** / `dL` and `dU` placement is the fingerprint.
    /// </summary>
    /// <remarks>
    /// **`then = 10`、`otherwise = 3` → `d = 7`、`dL = dU = 7`（两者都是常数多项式）。**
    /// **这时 `dL` 与 `dU` 相等，位置写反看不出来**——
    /// **所以这条判据改用"区间有宽度"的分支：`then ∈ [1, 10]`、`otherwise ∈ [3, 4]`。**
    ///
    /// **于是 `dL = 1 − 4 = −3`、`dU = 10 − 3 = 7`，两者不等。**
    ///
    /// **`With then = 10 and otherwise = 3, d = 7 and dL = dU = 7`, where a swap is invisible** —
    /// **so this criterion uses branches with width: `then ∈ [1, 10]` and `otherwise ∈ [3, 4]`.**
    /// **That gives `dL = 1 − 4 = −3` and `dU = 10 − 3 = 7`, which differ.**
    /// </remarks>
    [Fact]
    public void TheMaskingRows_ShouldPlaceDLAndDUDifferently()
    {
        SelectFunction<Flt64> select = BuildWithWidths();
        select.DifferenceBounds.Lower.ToDouble().Should().BeApproximately(-3.0, 1e-12);
        select.DifferenceBounds.Upper.ToDouble().Should().BeApproximately(7.0, 1e-12);

        IReadOnlyList<LinearInequality<Flt64>> rows = select.MaskingRows();

        // zero_upper: z − dU·mask ≤ 0 → mask 的系数是 −7。
        // zero_lower: z − dL·mask ≥ 0 → mask 的系数是 +3。
        MaskCoefficient(rows[0]).Should().BeApproximately(-7.0, 1e-12,
            "zero_upper scales the mask by -dU, which is -7");
        MaskCoefficient(rows[1]).Should().BeApproximately(3.0, 1e-12,
            "zero_lower scales the mask by -dL, which is 3");

        // one_upper: z − d + dL(1−mask) ≤ 0 → mask 系数 −dL = +3，右端 −dL = +3。
        // one_lower: z − d + dU(1−mask) ≥ 0 → mask 系数 −dU = −7，右端 −dU = −7。
        MaskCoefficient(rows[2]).Should().BeApproximately(3.0, 1e-12,
            "one_upper scales the mask by -dL, which is 3");
        MaskCoefficient(rows[3]).Should().BeApproximately(-7.0, 1e-12,
            "one_lower scales the mask by -dU, which is -7");

        rows[2].Rhs.Constant.ToDouble().Should().BeApproximately(3.0, 1e-12);
        rows[3].Rhs.Constant.ToDouble().Should().BeApproximately(-7.0, 1e-12);
    }

    /// <summary>**`mask = 0` 时四条边允许 `z = 0`** / At mask 0 the four rows allow z = 0.</summary>
    /// <remarks>
    /// **这是屏蔽边唯一真正要证明的性质**：**`z = mask·d` 在 `mask = 0` 时必须能为 0。**
    /// **把 `zero_upper` 里的 `dU` 写成 `dL`（这里 `dL = −3`）会让 `z ≤ −3·0 = 0` 仍成立，**
    /// **但 `one_lower` 写成 `dL` 就会在 `mask = 1` 时要求 `z ≥ d + (−3)(0) = 7`，于是 `z` 被迫等于 7——
    /// 那恰好是对的，因为 `d` 恒为 7。**
    ///
    /// **所以真正有区分力的是 `mask = 0` 这一侧**：**代入 `z = 0`、`mask = 0`，四条边必须全部成立。**
    ///
    /// **This is the one property the masking rows really have to prove**: **`z = mask·d` must be able to be 0 at
    /// `mask = 0`.**
    /// **The discriminating side is `mask = 0`**: **substituting `z = 0, mask = 0`, all four rows must hold.**
    /// </remarks>
    [Fact]
    public void TheMaskingRows_ShouldAllowZeroAtMaskZero()
    {
        SelectFunction<Flt64> select = BuildWithWidths();
        IReadOnlyList<LinearInequality<Flt64>> rows = select.MaskingRows();

        foreach (LinearInequality<Flt64> row in rows) {
            // mask = 0 时 z 必须是 0。取 x = 4、y = 4（都在区间内），于是 d = 0。
            // At mask 0, z must be 0. With x = 4 and y = 4 (both inside), d = 0.
            double lhs = MaskRowValue(row, mask: 0.0, z: 0.0, x: 4.0, y: 4.0);
            double rhs = row.Rhs.Constant.ToDouble();

            bool satisfied = row.Comparison switch {
                Comparison.LE => lhs <= rhs + 1e-12,
                Comparison.GE => lhs >= rhs - 1e-12,
                _ => global::System.Math.Abs(lhs - rhs) <= 1e-12,
            };

            satisfied.Should().BeTrue(
                $"{row.Name} must accept z = 0 at mask = 0: lhs = {lhs}, rhs = {rhs}");
        }
    }

    /// <summary>**`mask = 1` 时四条边把 `z` 夹成恰好 `d`** / At mask 1 the four rows pin z to exactly d.</summary>
    /// <remarks>
    /// **这一侧同样是定义性质**：**`z = 1·d = d`，所以四条边在 `z = d` 处必须成立。**
    /// **与上一条合起来，`mask ∈ {0,1}` 时 `z` 被唯一确定。**
    ///
    /// **The same defining property on this side**: **`z = 1·d = d`, so all four rows must hold at `z = d`.**
    /// **Together with the previous criterion this pins `z` uniquely for `mask ∈ {0, 1}`.**
    /// </remarks>
    [Fact]
    public void TheMaskingRows_ShouldPinZToDAtMaskOne()
    {
        SelectFunction<Flt64> select = BuildWithWidths();
        IReadOnlyList<LinearInequality<Flt64>> rows = select.MaskingRows();

        foreach (LinearInequality<Flt64> row in rows) {
            // mask = 1 时 z = d。取 x = 10、y = 3（区间端点），于是 d = 7、z = 7。
            // At mask 1, z = d. With x = 10 and y = 3 (the ends), d = 7 and z = 7.
            double lhs = MaskRowValue(row, mask: 1.0, z: 7.0, x: 10.0, y: 3.0);
            double rhs = row.Rhs.Constant.ToDouble();

            bool satisfied = row.Comparison switch {
                Comparison.LE => lhs <= rhs + 1e-12,
                Comparison.GE => lhs >= rhs - 1e-12,
                _ => global::System.Math.Abs(lhs - rhs) <= 1e-12,
            };

            satisfied.Should().BeTrue(
                $"{row.Name} must accept z = d at mask = 1: lhs = {lhs}, rhs = {rhs}");
        }
    }

    /// <summary>**结果等式是 `result − z − otherwise = 0`** / The result row is `result − z − otherwise = 0`.</summary>
    [Fact]
    public void TheResultRow_ShouldSubtractZAndOtherwise()
    {
        SelectFunction<Flt64> select = Build(thenConstant: 10.0, elseConstant: 3.0);
        LinearInequality<Flt64> row = select.ResultRow();

        row.Comparison.Should().Be(Comparison.EQ, "the result is defined by an equality");

        // result 的系数是 +1、z 的系数是 -1、常数是 -3（否则的常数取反）。
        row.Lhs.Monomials[0].Symbol.Should().BeSameAs(select.ResultVar);
        row.Lhs.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
        row.Lhs.Monomials[1].Symbol.Should().BeSameAs(select.MaskedDifferenceVar);
        row.Lhs.Monomials[1].Coefficient.ToDouble().Should().Be(-1.0);
        row.Lhs.Constant.ToDouble().Should().BeApproximately(-3.0, 1e-12,
            "the else-branch's constant is subtracted, so 3 becomes -3");
    }

    /// <summary>**连续的选择变量被拒绝** / A continuous selector is rejected.</summary>
    /// <remarks>
    /// **连续变量会让四条边仍然构造成功，只是 `mask = 0.5` 时给出一个介于两者之间的值。**
    /// **那看起来像插值，而语义上不存在这个值。**
    ///
    /// **A continuous variable leaves the four rows buildable, yielding an in-between value at `mask = 0.5`.**
    /// **It looks like interpolation, and no such value exists semantically.**
    /// </remarks>
    [Fact]
    public void TheSelect_ShouldRejectAContinuousSelector()
    {
        var continuous = new RealVar("select-continuous");
        continuous.Range.Geq(Flt64.Zero);
        continuous.Range.Leq(Flt64.One);

        Action build = () => _ = new SelectFunction<Flt64>(
            mask: continuous,
            then: Constant(10.0),
            otherwise: Constant(3.0),
            converter: IIntoValue<Flt64>.Identity,
            thenBounds: new ConditionBounds<Flt64>(new Flt64(10.0), new Flt64(10.0)),
            otherwiseBounds: new ConditionBounds<Flt64>(new Flt64(3.0), new Flt64(3.0)));
        build.Should().Throw<ArgumentException>("a select needs a binary selector");
    }

    /// <summary>造一个选择，两个分支都是常数 / Build a select over two constants.</summary>
    private static SelectFunction<Flt64> Build(double thenConstant, double elseConstant) {
        var mask = new BinVar("select-mask");
        return new SelectFunction<Flt64>(
            mask: mask,
            then: Constant(thenConstant),
            otherwise: Constant(elseConstant),
            converter: IIntoValue<Flt64>.Identity,
            thenBounds: new ConditionBounds<Flt64>(new Flt64(thenConstant), new Flt64(thenConstant)),
            otherwiseBounds: new ConditionBounds<Flt64>(new Flt64(elseConstant), new Flt64(elseConstant)),
            name: "select");
    }

    /// <summary>造一个有宽度的选择：`then = x ∈ [1,10]`、`otherwise = y ∈ [3,4]` / Build with width.</summary>
    private static SelectFunction<Flt64> BuildWithWidths() {
        var mask = new BinVar("select-wide-mask");
        var x = new RealVar("select-wide-x");
        var y = new RealVar("select-wide-y");
        x.Range.Geq(new Flt64(1.0));
        x.Range.Leq(new Flt64(10.0));
        y.Range.Geq(new Flt64(3.0));
        y.Range.Leq(new Flt64(4.0));

        return new SelectFunction<Flt64>(
            mask: mask,
            then: Variable(x),
            otherwise: Variable(y),
            converter: IIntoValue<Flt64>.Identity,
            name: "select_wide");
    }

    /// <summary>读一条屏蔽边里 mask 的系数 / Read the mask coefficient in one masking row.</summary>
    private static double MaskCoefficient(LinearInequality<Flt64> row) {
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol, row.Lhs.Monomials[0].Symbol) == false &&
                monomial.Symbol is BinVar) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new InvalidOperationException($"row {row.Name} has no mask monomial");
    }

    /// <summary>把一条屏蔽边在给定点上求值 / Evaluate a masking row at a point.</summary>
    /// <remarks>
    /// **`d = then − otherwise = x − y`，所以 `x` 与 `y` 必须分别代入**——
    /// **第一版把两者都代成 `d` 的值，于是 `−x + y` 算成了 7 而不是 0，判据自己被写错了。**
    ///
    /// **`d = then − otherwise = x − y`, so `x` and `y` are substituted separately** —
    /// **the first version substituted `d` for both, so `−x + y` came out as 7 rather than 0 and the criterion was
    /// wrong against itself.**
    /// </remarks>
    private static double MaskRowValue(
        LinearInequality<Flt64> row, double mask, double z, double x, double y) {
        double total = row.Lhs.Constant.ToDouble();
        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            double value = monomial.Symbol switch {
                BinVar => mask,
                RealVar variable when variable.Name.EndsWith("_select_delta", StringComparison.Ordinal) => z,
                RealVar variable when variable.Name.EndsWith("select-wide-x", StringComparison.Ordinal) => x,
                RealVar variable when variable.Name.EndsWith("select-wide-y", StringComparison.Ordinal) => y,
                _ => 0.0,
            };
            total += monomial.Coefficient.ToDouble() * value;
        }

        return total;
    }

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static LinearPolynomial<Flt64> Variable(RealVar variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        params (IVariableItem Variable, double Value)[] pairs) {
        var values = new Dictionary<ISymbol, Flt64>();
        foreach ((IVariableItem variable, double value) in pairs) {
            values[variable] = new Flt64(value);
        }

        return values;
    }
}
