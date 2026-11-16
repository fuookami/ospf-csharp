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
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 装配：范围与多项式必须用同一个 δ / The range and the polynomial must share one delta.
/// </summary>
/// <remarks>
/// 对齐的 `normalizeDiscreteCondition` 补，并补齐 `transformForRelationIndicator`。
///
/// **这里只做装配**——归一化范围、折叠判断、格点证明三块都已就位。
///
/// Assembles the pieces: range normalization, gap folding and lattice proofs are already in place.
/// </remarks>
public class DiscreteConditionNormalizationTests
{
    /// <summary>
    /// **范围与多项式用同一个 `δ` 平移** / The range shifts by the same delta.
    /// </summary>
    /// <remarks>
    /// **这是这里最重要的一条，也是最容易写错的一条。**
    ///
    /// **`GE` 时多项式变成 `p + δ`，而范围必须变成 `[lower + δ, upper + δ]`。**
    /// **如果范围只平移 `0`，两者错开 `δ`，分支存在性判断就在一个偏移过的区间上做——
    /// 结论可能反，而模型仍然可行。**
    ///
    /// **取 `δ = 2`、范围 `[−5, 3]`：正确结果是 `[−3, 5]`；只平移 0 会得到 `[−5, 3]`，相差 2。**
    ///
    /// **This is the most important criterion here and the easiest to get wrong.**
    ///
    /// **Under `GE` the polynomial becomes `p + δ`, so the range must become `[lower + δ, upper + δ]`.**
    /// **Leaving the range unshifted puts the two off by `δ`, and the branch-existence test then runs on a shifted
    /// interval — the conclusion can flip while the model stays feasible.**
    ///
    /// **With `δ = 2` and range `[−5, 3]` the correct result is `[−3, 5]`; leaving it alone gives `[−5, 3]`, two
    /// apart.**
    /// </remarks>
    [Fact]
    public void TheGe_ShouldShiftTheRangeByTheSameDelta()
    {
        DiscreteConditionLinearization normalized = Normalize(
            relation: Comparison.GE,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(3.0)),
            delta: new Flt64(2.0),
            strictBoundary: new Flt64(2.0));

        normalized.Bounds.Lower.ToDouble().Should().BeApproximately(-3.0, 1e-12);
        normalized.Bounds.Upper.ToDouble().Should().BeApproximately(5.0, 1e-12);
        normalized.Polynomial.Constant.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "the polynomial shifts by the same amount");
    }

    /// <summary>**`LT` 取负范围并交换端点** / `LT` negates the range and swaps the ends.</summary>
    [Fact]
    public void TheLt_ShouldNegateAndSwapTheRange()
    {
        DiscreteConditionLinearization normalized = Normalize(
            relation: Comparison.LT,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(3.0)),
            delta: new Flt64(2.0),
            strictBoundary: new Flt64(2.0));

        // `[−5, 3]` 取负后是 `[−3, 5]`。
        // Negating [-5, 3] gives [-3, 5].
        normalized.Bounds.Lower.ToDouble().Should().BeApproximately(-3.0, 1e-12);
        normalized.Bounds.Upper.ToDouble().Should().BeApproximately(5.0, 1e-12);
        normalized.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            -1.0, 1e-12, "the polynomial is negated");
    }

    /// <summary>**折叠给出固定真值时，两个分支由它直接决定** / A fixed value settles both branches.</summary>
    /// <remarks>
    /// **范围 `[5, 9]` 在 `GT` 下必然为真**（**`lower ≥ δ`**），**于是折叠给出 `FixedValue = True`。**
    ///
    /// **我原本把这条写成"折叠优先于范围重算"，并用变异验证——变异后判据<b>全绿</b>。**
    /// **于是我做了一次穷举：四个关系 × 924 组范围 × 四个 `δ` 值，
    /// 折叠结果与"按变换后范围重算"的结果<b>没有一处不一致</b>。**
    ///
    /// **所以那个 `??` 的左右两侧在当前形态下恒等价，优先级<b>测不出来</b>。**
    /// **它保留的理由是可读性与结构对应，不是行为差异。**
    ///
    /// **判据因此只断言"折叠给出的真值决定两个分支"，不再声称测到了优先级。**
    ///
    /// **Range `[5, 9]` under `GT` is necessarily true** (`lower ≥ δ`), **so the fold gives `FixedValue = True`.**
    ///
    /// **I first wrote this as "the fold takes precedence over recomputation" and mutated it — every criterion
    /// stayed <b>green</b>. So I enumerated: four relations × 924 ranges × four `δ` values, and the fold and the
    /// recomputation from the transformed range <b>never disagreed</b>.**
    ///
    /// **Both sides of that `??` are therefore equivalent in the current shape, and the precedence is
    /// <b>untestable</b>.** **It stays for readability and structural parity with the code, not for a behavioural
    /// difference.**
    ///
    /// **The criterion now asserts only "a folded truth value settles both branches" and no longer claims to test the
    /// precedence.**
    /// </remarks>
    [Fact]
    public void TheFold_ShouldSettleBothBranches()
    {
        DiscreteConditionLinearization normalized = Normalize(
            relation: Comparison.GT,
            bounds: new ConditionBounds<Flt64>(new Flt64(5.0), new Flt64(9.0)),
            delta: Flt64.One,
            strictBoundary: Flt64.One);

        normalized.FixedValue.Should().Be(TruthValue.True);
        normalized.TrueBranchPossible.Should().BeTrue();
        normalized.FalseBranchPossible.Should().BeFalse(
            "a fixed true value leaves the false branch impossible");
    }

    /// <summary>**折不了时按范围算分支存在性** / Without a fold the range decides.</summary>
    [Fact]
    public void TheUnfolded_ShouldDeriveBranchesFromTheRange()
    {
        DiscreteConditionLinearization normalized = Normalize(
            relation: Comparison.GT,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
            delta: Flt64.One,
            strictBoundary: Flt64.One);

        normalized.FixedValue.Should().BeNull("the range straddles the boundary");
        normalized.TrueBranchPossible.Should().BeTrue("the upper end clears the boundary");
        normalized.FalseBranchPossible.Should().BeTrue("the lower end does not");
    }

    /// <summary>
    /// **`delta &gt; δ` 且折不了时，格点证明不通过就拒绝** / A failed lattice proof is rejected.
    /// </summary>
    /// <remarks>
    /// **`p = 3x + 1`、`δ = 2`、`delta = 3`。**
    /// **`GT` 下正侧最近格点是 `1`，`1 &lt; 2`，所以严格边界落在 `0` 与最近正格点之间——
    /// "严格正"与"至少 `δ`"不等价。**
    ///
    /// **`p = 3x + 1`, `δ = 2`, `delta = 3`.**
    /// **Under `GT` the nearest positive lattice point is `1`, and `1 &lt; 2`, so the strict boundary falls between
    /// zero and that point — "strictly positive" is not equivalent to "at least δ".**
    /// </remarks>
    [Fact]
    public void TheUnprovenLattice_ShouldBeRejected()
    {
        Result<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>> unproven =
            DiscreteConditionNormalization.Normalize(
                polynomial: Shifted(3.0, 1.0),
                relation: Comparison.GT,
                bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
                delta: new Flt64(3.0),
                strictBoundary: new Flt64(2.0));

        unproven.Should().BeOfType<Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>>(
            "the lattice is misaligned with the strict boundary");
    }

    /// <summary>**`delta ≤ δ` 时不需要格点证明** / No proof is needed at or below the boundary.</summary>
    [Fact]
    public void TheSmallDelta_ShouldNotNeedAProof()
    {
        Normalize(
            relation: Comparison.GT,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
            delta: Flt64.One,
            strictBoundary: new Flt64(2.0));
    }

    /// <summary>**`EQ`/`NE` 被拒绝** / `EQ` and `NE` are rejected.</summary>
    [Fact]
    public void TheEqAndNe_ShouldBeRejected()
    {
        foreach (Comparison relation in new[] { Comparison.EQ, Comparison.NE }) {
            DiscreteConditionNormalization.Normalize(
                polynomial: Shifted(1.0, 0.0),
                relation: relation,
                bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
                delta: Flt64.One,
                strictBoundary: Flt64.One).Should()
                .BeOfType<Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>>(
                    $"{relation} has no discrete-condition normalization");
        }
    }

    /// <summary>**非正 `delta`/`δ` 被拒绝** / Non-positive parameters are rejected.</summary>
    [Fact]
    public void TheNonPositiveParameters_ShouldBeRejected()
    {
        DiscreteConditionNormalization.Normalize(
            polynomial: Shifted(1.0, 0.0),
            relation: Comparison.GT,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
            delta: Flt64.Zero,
            strictBoundary: Flt64.One).Should()
            .BeOfType<Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>>();

        DiscreteConditionNormalization.Normalize(
            polynomial: Shifted(1.0, 0.0),
            relation: Comparison.GT,
            bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(9.0)),
            delta: Flt64.One,
            strictBoundary: Flt64.Zero).Should()
            .BeOfType<Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>>();
    }

    private static DiscreteConditionLinearization Normalize(
        Comparison relation,
        ConditionBounds<Flt64> bounds,
        Flt64 delta,
        Flt64 strictBoundary) =>
        DiscreteConditionNormalization.Normalize(
            polynomial: Shifted(1.0, 0.0),
            relation: relation,
            bounds: bounds,
            delta: delta,
            strictBoundary: strictBoundary).Value;

    /// <summary>`coefficient * x + constant`，整数变量 / `coefficient * x + constant`.</summary>
    private static LinearPolynomial<Flt64> Shifted(double coefficient, double constant) =>
        new(
            new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), IntegerVariable) },
            new Flt64(constant));

    private static readonly IntVar IntegerVariable = new("dcn-int");
}
