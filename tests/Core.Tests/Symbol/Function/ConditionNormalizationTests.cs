#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 归一化：真假分支之间必须有正间隔 / The branches must be separated by a positive gap.
/// </summary>
/// <remarks>
/// 对齐的 `normalizedCondition` / `validateConditionBounds`。
///
/// **"归一化"是把四种关系折算成"严格真"的形态。**
///
/// **Normalising folds the four relations into a "strictly true" shape.**
/// </remarks>
public class ConditionNormalizationTests
{
    /// <summary>**`LT`/`LE` 要取负，并且报告这件事** / The negating relations say so.</summary>
    /// <remarks>
    /// **`NegatePolynomial` 是最容易被忽略的字段：忘了取负会让整个真假判断反向，
    /// 而模型仍然可行——只是条件被取反了。**
    ///
    /// **`NegatePolynomial` is the easiest field to overlook: forgetting to negate flips the whole truth decision,
    /// and the model stays feasible — the condition is merely inverted.**
    /// </remarks>
    [Fact]
    public void TheNegatingRelations_ShouldReportAndNegate()
    {
        foreach (Comparison relation in new[] { Comparison.LT, Comparison.LE }) {
            NormalizedCondition normalized = ConditionNormalization.Normalize(
                relation,
                new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
                Flt64.One).Value;

            normalized.NegatePolynomial.Should().BeTrue($"{relation} must negate the polynomial");
            normalized.QLower.ToDouble().Should().BeApproximately(
                -4.0, 1e-12, "the negated lower bound is minus the original upper");
            normalized.QUpper.ToDouble().Should().BeApproximately(
                2.0, 1e-12, "the negated upper bound is minus the original lower");
        }

        foreach (Comparison relation in new[] { Comparison.GT, Comparison.GE }) {
            NormalizedCondition normalized = ConditionNormalization.Normalize(
                relation,
                new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
                Flt64.One).Value;

            normalized.NegatePolynomial.Should().BeFalse($"{relation} does not negate");
            normalized.QLower.ToDouble().Should().BeApproximately(-2.0, 1e-12);
        }
    }

    /// <summary>**严格关系把真分支下边界放在 `δ`，非严格放在 `0`** / Strict relations put the true edge at δ.</summary>
    [Fact]
    public void TheStrictRelations_ShouldPutTheTrueEdgeAtTheBoundary()
    {
        NormalizedCondition strict = ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            Flt64.One).Value;
        NormalizedCondition nonStrict = ConditionNormalization.Normalize(
            Comparison.GE,
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            Flt64.One).Value;

        strict.TrueLower.ToDouble().Should().BeApproximately(1.0, 1e-12, "p > 0 needs a gap of delta");
        strict.FalseUpper.ToDouble().Should().BeApproximately(0.0, 1e-12);
        nonStrict.TrueLower.ToDouble().Should().BeApproximately(0.0, 1e-12, "p >= 0 starts at zero");
        nonStrict.FalseUpper.ToDouble().Should().BeApproximately(-1.0, 1e-12);
    }

    /// <summary>
    /// **`δ = 0` 被拒绝，因为两个分支会在零点相接** / A zero delta is rejected: the branches meet.
    /// </summary>
    /// <remarks>
    /// **这是这里最重要的一条。**
    /// **`δ = 0` 时 `TrueLower = FalseUpper = 0`，于是"给定一个点，它算真还是假"没有答案——
    /// 而模型仍然可行，因为指示约束两边都能满足。**
    ///
    /// **这里报错。判据必须验证这个报错，而不只是"返回了失败"**——
    /// **因为它是"正间隔"规则在起作用，而不是别的检查先挡住了。**
    ///
    /// **This is the most important criterion here.**
    /// **At `δ = 0`, `TrueLower = FalseUpper = 0`, so "is this point true or false" has no answer — while the model
    /// stays feasible, because both sides of the indicator rows can be satisfied.**
    ///
    /// **The code errors. The criterion must verify <b>that</b> error rather than merely "it failed"** —
    /// **because the positive-gap rule is what fires here, not some earlier check.**
    /// </remarks>
    [Fact]
    public void TheZeroBoundary_ShouldBeRejectedByAPositiveBoundaryCheck()
    {
        Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> zero = ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            Flt64.Zero);

        zero.Should().BeOfType<Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>>();
        ((Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>)zero).Error.Message.Should()
            .Contain("positive", "the strict boundary itself must be positive, not just the gap");

        ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            new Flt64(-1.0)).Should()
            .BeOfType<Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>>(
                "a negative boundary is meaningless");
    }

    /// <summary>
    /// **倒序范围在构造 `ConditionBounds` 时就被拒绝** / Reversed bounds are rejected at construction.
    /// </summary>
    /// <remarks>
    /// **我原本以为能在 `Normalize` 里测到这条检查，跑出来是 `ArgumentException`——
    /// 因为 `ConditionBounds` 的构造函数本身就不接受倒序。**
    ///
    /// **这是"检查在哪一层"的问题**：**归一化里那句 `lower ≤ upper` 是<b>防御性重复</b>，
    /// 正常调用路径上永远到不了。** **判据因此改为断言构造层的行为。**
    ///
    /// **I first expected to exercise this check inside `Normalize` and got an `ArgumentException` instead —
    /// `ConditionBounds`'s own constructor rejects reversed bounds.**
    ///
    /// **That is a question of which layer checks**: **the `lower ≤ upper` test inside normalization is
    /// <b>defensive duplication</b> that the normal path never reaches.** **The criterion now asserts the
    /// constructor's behaviour.**
    /// </remarks>
    [Fact]
    public void TheGap_ShouldHoldForEveryRelation()
    {
        var wide = new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0));

        foreach (Comparison relation in new[] {
            Comparison.GE, Comparison.LE, Comparison.GT, Comparison.LT,
        }) {
            NormalizedCondition normalized = ConditionNormalization.Normalize(
                relation, wide, new Flt64(0.25)).Value;

            normalized.FalseUpper.CompareTo(normalized.TrueLower).Should().BeLessThan(
                0, $"{relation} must leave a strictly positive gap");
        }
    }

    /// <summary>
    /// **正间隔检查独立于边界检查** / The gap check is independent of the boundary check.
    /// </summary>
    /// <remarks>
    /// **这是一次真实的判据失败留下的**：**我先用 `δ = 0` 去测正间隔，
    /// 但 `δ = 0` 先被"边界必须为正"挡住，于是<b>删掉正间隔检查后判据仍然全绿</b>**——
    /// **那条判据测的是边界检查，不是间隔检查。**
    ///
    /// **要让正间隔成为唯一拦截者，需要 `δ > 0` 但 `TrueLower == FalseUpper`。
    /// `GT` 下 `TrueLower = δ`、`FalseUpper = 0`，永不相等。所以这条路走不通——
    /// 正间隔检查在四种关系下都是<b>多余</b>的。**
    ///
    /// **这是一个诚实的结论：这条检查在当前形态下不可能被触发。**
    /// **保留它的理由是防御——若 `TrueLower`/`FalseUpper` 的推导改了（比如引入容差带），
    /// 它是唯一挡得住"分支相接"的地方。**
    ///
    /// **判据因此断言"四种关系下 `FalseUpper < TrueLower` 恒成立"，
    /// 而不是假装能测到那条 `return Fail`。**
    ///
    /// **This came from a real criterion failure**: **I first used `δ = 0`, but that is caught earlier by "the
    /// boundary must be positive", so <b>deleting the gap check left everything green</b>** — **that criterion tested
    /// the boundary check, not the gap check.**
    ///
    /// **Reaching `δ > 0` with `TrueLower == FalseUpper` is impossible: under `GT`, `TrueLower = δ` and
    /// `FalseUpper = 0`. So the gap check is <b>redundant</b> under all four relations.**
    ///
    /// **That is the honest conclusion: it cannot fire in its current shape.** **It stays as defence — should the
    /// derivation change (a tolerance band, say), it is the only thing preventing "the branches touch".**
    ///
    /// **The criterion asserts "`FalseUpper < TrueLower` for all four relations" rather than pretending to reach that
    /// `return Fail`.**
    /// </remarks>
    [Fact]
    public void TheReversedBounds_ShouldBeRejectedAtConstruction()
    {
        Action reversed = () => _ = new ConditionBounds<Flt64>(new Flt64(4.0), new Flt64(-2.0));
        reversed.Should().Throw<ArgumentException>("the bounds type itself refuses lower above upper");
    }

    /// <summary>**真分支存在性按 `qUpper ≥ trueLower`** / The true branch exists when `qUpper ≥ trueLower`.</summary>
    [Fact]
    public void TheBranchExistence_ShouldFollowTheEdges()
    {
        // 范围 [−2, 4]、GT、δ = 1：qUpper = 4 ≥ 1，真分支存在；
        // qLower = −2 ≤ 0，假分支也存在。
        // Range [-2, 4] under GT with delta one: both branches exist.
        NormalizedCondition both = ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            Flt64.One).Value;
        both.TrueBranchExists.Should().BeTrue();
        both.FalseBranchExists.Should().BeTrue();

        // 范围 [5, 9]：qLower = 5 > 0，假分支不存在。
        // Range [5, 9]: qLower is above zero, so the false branch cannot exist.
        NormalizedCondition onlyTrue = ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(5.0), new Flt64(9.0)),
            Flt64.One).Value;
        onlyTrue.TrueBranchExists.Should().BeTrue();
        onlyTrue.FalseBranchExists.Should().BeFalse("everything in the range is above zero");
    }

    /// <summary>
    /// **与两个分支都不相交时无解** / A range meeting neither branch has no solution.
    /// </summary>
    /// <remarks>
    /// **这里报错而不是返回"两个都是 `false`"——后者会让调用方以为"条件恒假"，
    /// 而实际是"这个范围不合法"。**
    ///
    /// **这个情形构造起来不平凡**：**`GT` 下真分支是 `q ≥ 1`、假分支是 `q ≤ 0`，
    /// 而 `q` 的范围是 `[qLower, qUpper]`。要让两者都不相交，需要 `qUpper &lt; 1` 且 `qLower > 0`，
    /// 也就是 `0 &lt; qLower ≤ qUpper &lt; 1`。**
    ///
    /// **取范围 `[0.2, 0.8]`、`δ = 1`：真分支要 `q ≥ 1`（不满足），假分支要 `q ≤ 0`（不满足）。**
    ///
    /// **The code errors rather than returning "both false"** — **the latter would read as "the condition is always
    /// false" while the range is simply illegal.**
    ///
    /// **The case takes care to build**: **under `GT` the true branch is `q ≥ 1` and the false branch `q ≤ 0`, while
    /// `q` ranges over `[qLower, qUpper]`. Meeting neither needs `qUpper &lt; 1` and `qLower > 0`, i.e.
    /// `0 &lt; qLower ≤ qUpper &lt; 1`.**
    ///
    /// **Range `[0.2, 0.8]` with `δ = 1`: the true branch needs `q ≥ 1` (no) and the false branch `q ≤ 0` (no).**
    /// </remarks>
    [Fact]
    public void TheRangeMeetingNeitherBranch_ShouldBeRejected()
    {
        Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> gap = ConditionNormalization.Normalize(
            Comparison.GT,
            new ConditionBounds<Flt64>(new Flt64(0.2), new Flt64(0.8)),
            Flt64.One);

        gap.Should().BeOfType<Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>>();
        ((Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>)gap).Error.Message.Should()
            .Contain("neither", "the range intersects no branch at all");
    }

    /// <summary>**`Validate` 与 `Normalize` 给出一致的判定** / `Validate` agrees with `Normalize`.</summary>
    [Fact]
    public void TheValidate_ShouldAgreeWithNormalize()
    {
        ConditionNormalization.Validate(
            new ConditionBounds<Flt64>(new Flt64(-2.0), new Flt64(4.0)),
            Comparison.GT,
            Flt64.One).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        ConditionNormalization.Validate(
            new ConditionBounds<Flt64>(new Flt64(0.2), new Flt64(0.8)),
            Comparison.GT,
            Flt64.One).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "a range meeting neither branch must fail in Validate as well");
    }
}
