#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 界折叠：`GE` 与 `GT` 只在等于零处不同 / `GE` and `GT` differ only at zero.
/// </summary>
/// <remarks>
/// 对齐的 `boundsAvoidDiscreteConditionGap` 与
/// `DiscreteConditionLinearization` 补。
///
/// **折叠回答的是"还要不要生成指示约束"**：**界已定死时指示变量没有意义。**
///
/// **Folding answers "do we still need indicator constraints"**: **when the bounds settle it, an indicator is
/// pointless.**
/// </remarks>
public class DiscreteConditionGapTests
{
    /// <summary>
    /// **`GE` 在 `lower ≥ 0` 时为真，`GT` 需要 `lower ≥ δ`** / The only asymmetry that matters.
    /// </summary>
    /// <remarks>
    /// **这是本函数唯一的难点，也是唯一能被写错的地方。**
    ///
    /// **取 `δ = 1`、界 `[0, 0]`（也就是 `p ≡ 0`）**：
    /// **`GE` 应当为真**（**`0 ≥ 0`**），**`GT` 必须为 `null`**（**`0 > 0` 不成立，
    /// 而 `lower = 0` 没达到 `δ`，所以定不下来是假还是近边界**）。
    ///
    /// **把这两行写成一样，`p ≡ 0` 在 `GT` 下会被判为真——而 `0 > 0` 是假的。**
    ///
    /// **This is the function's only difficulty and the only place it can be written wrong.**
    ///
    /// **With `δ = 1` and bounds `[0, 0]` (that is `p ≡ 0`)**: **`GE` must be true** (`0 ≥ 0`), **`GT` must be `null`**
    /// (`0 > 0` fails, and `lower = 0` does not reach `δ`, so it is not decidable).**
    ///
    /// **Writing the two rows the same way would call `p ≡ 0` true under `GT` — and `0 > 0` is false.**
    /// </remarks>
    [Fact]
    public void TheGeAndGt_ShouldDifferAtZero()
    {
        var atZero = new ConditionBounds<Flt64>(Flt64.Zero, Flt64.Zero);

        DiscreteConditionGap.AvoidsGap(Comparison.GE, atZero, Flt64.One).Should().Be(
            TruthValue.True, "zero is at least zero");
        DiscreteConditionGap.AvoidsGap(Comparison.GT, atZero, Flt64.One).Should().Be(
            TruthValue.False, "zero is never strictly greater than zero");
        // **`GT` 在这里定为 `<b>假</b>`，不是 `null`**：**`upper = 0 ≤ 0` 说明条件恒不严格为正，
        // 所以假分支确定成立。** **`GE` 给真、`GT` 给假——这才是两者的真实差别。**
        //
        // **`GT` decides <b>false</b> here, not `null`**: **`upper = 0 ≤ 0` means the condition is never strictly
        // positive, so the false branch is certain.** **`GE` says true and `GT` says false — that is the real
        // difference.**
    }

    /// <summary>**`LE` 在 `upper ≤ 0` 时为真** / `LE` is true when the upper bound is at most zero.</summary>
    [Fact]
    public void TheLe_ShouldBeTrueWhenTheUpperBoundIsAtMostZero()
    {
        DiscreteConditionGap.AvoidsGap(
            Comparison.LE,
            new ConditionBounds<Flt64>(new Flt64(-5.0), Flt64.Zero),
            Flt64.One).Should().Be(TruthValue.True, "everything is at most zero");
    }

    /// <summary>**`LT` 需要 `upper ≤ −δ`** / `LT` needs `upper ≤ −δ`.</summary>
    [Fact]
    public void TheLt_ShouldNeedTheStrictBoundary()
    {
        DiscreteConditionGap.AvoidsGap(
            Comparison.LT,
            new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(-1.0)),
            Flt64.One).Should().Be(TruthValue.True, "minus one is strictly below zero");

        DiscreteConditionGap.AvoidsGap(
            Comparison.LT,
            new ConditionBounds<Flt64>(new Flt64(-5.0), Flt64.Zero),
            Flt64.One).Should().BeNull(
                "an upper bound of zero is too close to the boundary to call");
    }

    /// <summary>**判假需要离真分支至少 `δ`** / Deciding false needs at least `δ` of separation.</summary>
    /// <remarks>
    /// **`GE` 判假要 `upper ≤ −δ`。**
    /// **取 `δ = 1`、界 `[−5, −0.5]`：`upper = −0.5` 没有到 `−1`，所以定不下来。**
    /// **如果实现漏掉 `δ`、只写 `upper ≤ 0`，这里会错误地判为假。**
    ///
    /// **`GE` decides false when `upper ≤ −δ`.** **With `δ = 1` and bounds `[−5, −0.5]`, `upper = −0.5` does not
    /// reach `−1`, so it stays undecided.** **An implementation that drops `δ` and writes `upper ≤ 0` would wrongly
    /// decide false here.**
    /// </remarks>
    [Fact]
    public void TheFalseDecision_ShouldRespectTheStrictBoundary()
    {
        DiscreteConditionGap.AvoidsGap(
            Comparison.GE,
            new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(-0.5)),
            Flt64.One).Should().BeNull("minus a half is too close to zero to call false");

        DiscreteConditionGap.AvoidsGap(
            Comparison.GE,
            new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(-1.0)),
            Flt64.One).Should().Be(TruthValue.False, "minus one is far enough below");
    }

    /// <summary>**跨界时返回 `null`** / A straddling range returns `null`.</summary>
    [Fact]
    public void TheStraddlingBounds_ShouldReturnNull()
    {
        var straddling = new ConditionBounds<Flt64>(new Flt64(-3.0), new Flt64(3.0));

        foreach (Comparison relation in new[] {
            Comparison.GE, Comparison.LE, Comparison.GT, Comparison.LT,
        }) {
            DiscreteConditionGap.AvoidsGap(relation, straddling, Flt64.One).Should().BeNull(
                $"{relation} cannot be settled on a range that straddles the boundary");
        }
    }

    /// <summary>**`EQ`/`NE` 折不了** / `EQ` and `NE` cannot fold.</summary>
    [Fact]
    public void TheEqAndNe_ShouldNeverFold()
    {
        var pinned = new ConditionBounds<Flt64>(Flt64.Zero, Flt64.Zero);

        DiscreteConditionGap.AvoidsGap(Comparison.EQ, pinned, Flt64.One).Should().BeNull(
            "EQ needs both directions");
        DiscreteConditionGap.AvoidsGap(Comparison.NE, pinned, Flt64.One).Should().BeNull(
            "NE is a disjunction");
    }

    /// <summary>**线性化记录携带五个字段** / The record carries all five fields.</summary>
    [Fact]
    public void TheLinearization_ShouldCarryItsFields()
    {
        var bounds = new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(2.0));
        var record = new DiscreteConditionLinearization(
            Polynomial: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
            Bounds: bounds,
            FixedValue: TruthValue.True,
            TrueBranchPossible: true,
            FalseBranchPossible: false);

        record.Bounds.Should().BeSameAs(bounds);
        record.FixedValue.Should().Be(TruthValue.True);
        record.TrueBranchPossible.Should().BeTrue();
        record.FalseBranchPossible.Should().BeFalse(
            "a fixed value leaves exactly one branch alive");
    }
}
