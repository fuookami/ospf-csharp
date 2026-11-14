#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
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

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 指示结构：三行链接的完备性，以及校验顺序 / Link completeness, and the order of checks.
/// </summary>
/// <remarks>
/// B1186 按基线 `IndicatorStructure.kt` 补，连同循环依赖的 `ConjoinedIndicator`、`DifferenceIndicator`。
///
/// **合取需要<b>三</b>行而不是两行。**
///
/// B1186 adds the baseline's `IndicatorStructure` with its `ConjoinedIndicator` and `DifferenceIndicator`.
///
/// **A conjunction needs <b>three</b> link rows, not two.**
/// </remarks>
public class IndicatorStructureTests
{
    /// <summary>
    /// **合取的三行缺一行就会被常量 0 替换** / A conjunction missing one link collapses to the constant zero.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **`_link_ge` 说 `result ≤ primary`，`_link_le` 说 `result ≤ conditionResult`**
    /// （**合取不能比任一支大**）。
    /// **只有这两行的话，`result = 0` 永远可行——合取就被常量 0 替换了，
    /// 而模型完全可行、目标值合理。**
    ///
    /// **`_link_lb`（`result ≥ primary + conditionResult − 1`）补上"也不能比任一支小"。**
    ///
    /// **判据在"两真"处断言 `result = 1` 是唯一可行值**：
    /// **`primary = 1`、`conditionResult = 1`、`result = 0` 必须违反 `_link_lb`。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **`_link_ge` says `result ≤ primary` and `_link_le` says `result ≤ conditionResult` (the conjunction is at most
    /// either branch).**
    /// **With only those two, `result = 0` is always feasible — the conjunction is replaced by the constant zero while
    /// the model stays feasible and the objective looks sane.**
    ///
    /// **`_link_lb` (`result ≥ primary + conditionResult − 1`) adds "and not less either".**
    ///
    /// **The criterion asserts `result = 1` is the only feasible value when both hold: with `primary = 1`,
    /// `conditionResult = 1`, `result = 0` must violate `_link_lb`.**
    /// </remarks>
    [Fact]
    public void TheConjunction_ShouldNeedsItsThirdLink()
    {
        IndicatorStructure left = Structure("left", ConditionResult);
        var conjunction = new ConjoinedIndicator(left, ConjunctionResult, "and");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            conjunction.GenerateConstraints(Primary).Value;

        rows.Should().HaveCount(5, "two rows from the condition plus three links");

        LinearInequality<Flt64> lower = rows[4];
        lower.Name.Should().Be("and_link_lb");
        lower.Comparison.Should().Be(Comparison.GE);

        // **两真时 `result` 必须是 1。**
        // **With both true, `result` must be one.**
        Residual(lower, primary: 1.0, condition: 1.0, result: 1.0).Should().BeGreaterThanOrEqualTo(0.0);
        Residual(lower, primary: 1.0, condition: 1.0, result: 0.0).Should().BeLessThan(
            0.0, "zero is exactly what the third link rules out");

        // 任一支为 0 时 `result = 0` 可行。
        // With either branch zero, `result = 0` is feasible.
        Residual(lower, primary: 0.0, condition: 1.0, result: 0.0).Should().BeGreaterThanOrEqualTo(0.0);
        Residual(lower, primary: 1.0, condition: 0.0, result: 0.0).Should().BeGreaterThanOrEqualTo(0.0);
    }

    /// <summary>**前两行各自限制一个方向** / The first two links cap each branch.</summary>
    [Fact]
    public void TheTwoUpperLinks_ShouldCapEachBranch()
    {
        var conjunction = new ConjoinedIndicator(Structure("left", ConditionResult), ConjunctionResult, "and");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            conjunction.GenerateConstraints(Primary).Value;

        rows[2].Name.Should().Be("and_link_ge");
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Name.Should().Be("and_link_le");
        rows[3].Comparison.Should().Be(Comparison.LE);

        // **`primary = 0` 时 `result = 1` 违反 `_link_ge`。**
        // **`primary = 0` with `result = 1` violates `_link_ge`.**
        Residual(rows[2], primary: 0.0, condition: 0.0, result: 1.0).Should().BeGreaterThan(0.0);
        Residual(rows[3], primary: 1.0, condition: 0.0, result: 1.0).Should().BeGreaterThan(0.0);
    }

    /// <summary>
    /// **三个列必须互不相同（按标识，不按名字）** / The three columns must be distinct by identity, not by name.
    /// </summary>
    /// <remarks>
    /// **两个<b>同名</b>的 `BinVar` 在这个模型层是不同的列。**
    /// **如果"互不相同"用名字比较，这一对会被判为相同而错误拒绝；
    /// 如果只比较名字而不比较标识，同一列会被当成两列而接受。**
    ///
    /// **判据两侧都测。**
    ///
    /// **Two <b>same-named</b> `BinVar`s are distinct columns at this layer.**
    /// **Comparing names would wrongly reject that pair; ignoring identity would wrongly accept one column used twice.**
    ///
    /// **The criterion tests both sides.**
    /// </remarks>
    [Fact]
    public void TheThreeColumns_ShouldBeDistinctByIdentityNotName()
    {
        // 同名的两个不同变量：应当被接受。
        // Two same-named but distinct variables: must be accepted.
        var sameNameA = new BinVar("dup");
        var sameNameB = new BinVar("dup");
        var accepted = new ConjoinedIndicator(Structure("left", ConditionResult), sameNameA, "and");

        accepted.GenerateConstraints(Primary).Should()
            .BeOfType<Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "two same-named variables are still two columns");

        var rejected = new ConjoinedIndicator(Structure("left", ConditionResult), Primary, "and");

        rejected.GenerateConstraints(Primary).Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "the same column cannot serve as both the AND result and the primary");
        _ = sameNameB;
    }

    /// <summary>
    /// **校验顺序：合取结构非法先于零容差带非法报出** / The conjunction error surfaces before the zero-band one.
    /// </summary>
    /// <remarks>
    /// **两步都是 `IllegalArgument`，所以调换顺序不会让任何判据失败——只让错误消息出现在对方的场景里。**
    ///
    /// **判据构造一个<b>同时</b>违反两者的结构**（合取 + 零容差带），
    /// **然后断言报出的是"合取不支持附加结果"那条。**
    ///
    /// **Both steps are `IllegalArgument`, so swapping them fails no criterion — it only puts each message into the
    /// other's scenario.**
    ///
    /// **The criterion builds a structure violating <b>both</b> (conjunction plus zero band) and asserts the
    /// "conjoined conditions do not support additional results" message wins.**
    /// </remarks>
    [Fact]
    public void TheConjunctionError_ShouldSurfaceBeforeTheZeroBandOne()
    {
        IndicatorStructure primary = Structure("primary", Primary);
        var conjunction = new ConjoinedIndicator(Structure("left", ConditionResult), ConjunctionResult, "and");

        // **必须让<b>零带检查也真的会失败</b>，否则调换顺序看不见。**
        //
        // **我第一次只加了 `ZeroBand`，而零带检查的条件是
        // "零带 且（条件范围 或 条件值 或 蕴含 或 等价列包含 side）"——
        // 四个附加项都是空的，所以那一步根本不触发。**
        //
        // **于是顺序调换之后两步都走不到，判据全绿。**
        //
        // **The zero-band check must <b>also fail</b>, or swapping the order is invisible.**
        //
        // **My first attempt added only `ZeroBand`, but that check requires "zero band AND (bounds OR conditional value
        // OR implication OR a side column among the equivalents)" — all four were empty, so the step never fired.**
        //
        // **Swapping the order then reddened nothing.**
        IndicatorStructure combined = primary with {
            Conjunction = conjunction,
            ZeroBand = new ZeroBand<Flt64>(new Flt64(1.0), Side),
            ConditionalValue = new ConditionalValue(
                Input: Linear(),
                ResultVariable: ValueResult,
                Bounds: new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(1.0)),
                Name: "cv"),
        };

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            combined.GenerateConstraints();

        result.Should().BeOfType<Failed<
            IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<
            IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain(
                "do not support additional results",
                "the conjunction check runs before the zero-band one");
    }

    /// <summary>
    /// **合取与差值不能同时给** / A conjunction and a difference cannot both be present.
    /// </summary>
    [Fact]
    public void TheConjunctionAndDifference_ShouldBeMutuallyExclusive()
    {
        var both = new IndicatorStructure(
            Input: Linear(), ResultVariable: Primary, BigM: new Flt64(100.0),
            Tolerance: new Flt64(2.0), Converter: IIntoValue<Flt64>.Identity, Name: "both") {
            Conjunction = new ConjoinedIndicator(Structure("l", ConditionResult), ConjunctionResult, "and"),
            Difference = new DifferenceIndicator(Structure("d", ConditionResult), DifferenceResult, "diff"),
        };

        both.GenerateConstraints().Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "an AND and an exclusive difference cannot share one structure");
    }

    /// <summary>
    /// **差值的等式允许"两真"被表示成 0，第二行排除它** / The exclusive row excludes the "both true reads as zero".
    /// </summary>
    /// <remarks>
    /// **`_bter_result` 是 `result − primary + conditionResult = 0`。**
    /// **它允许 `primary = 1`、`conditionResult = 1`、`result = 0`——
    /// 那种组合下"两个条件同时成立"会被表示成 `result = 0`，与"两个都不成立"混淆。**
    ///
    /// **`_bter_exclusive`（`primary + conditionResult ≤ 1`）把它排除。**
    ///
    /// **`_bter_result` is `result − primary + conditionResult = 0`.**
    /// **It admits `primary = 1`, `conditionResult = 1`, `result = 0` — a state where "both hold" reads as
    /// `result = 0`, indistinguishable from "neither holds".**
    ///
    /// **`_bter_exclusive` (`primary + conditionResult ≤ 1`) rules it out.**
    /// </remarks>
    [Fact]
    public void TheDifference_ShouldExcludeTheBothTrueZero()
    {
        var difference = new DifferenceIndicator(Structure("d", ConditionResult), DifferenceResult, "diff");

        IReadOnlyList<LinearInequality<Flt64>> rows =
            difference.GenerateConstraints(Primary).Value;

        rows.Should().HaveCount(4);
        rows[2].Name.Should().Be("diff_bter_result");
        rows[2].Comparison.Should().Be(Comparison.EQ);
        rows[3].Name.Should().Be("diff_bter_exclusive");
        rows[3].Comparison.Should().Be(Comparison.LE);

        // **两真时等式给出 `result = 0`（`0 − 1 + 1 = 0`），而排他行必须违反。**
        // **Both true gives `result = 0` from the equality (`0 − 1 + 1 = 0`), while the exclusive row must fail.**
        Residual(rows[2], primary: 1.0, condition: 1.0, result: 0.0).Should().BeApproximately(
            0.0, 1e-9, "the equality alone admits the both-true state");
        Residual(rows[3], primary: 1.0, condition: 1.0, result: 0.0).Should().BeGreaterThan(
            0.0, "and the exclusive row is what rules it out");
    }

    /// <summary>**保留结果列的顺序固定** / The retained-column order is fixed.</summary>
    [Fact]
    public void TheRetainedColumns_ShouldFollowTheFixedOrder()
    {
        IndicatorStructure plain = Structure("plain", Primary);

        plain.RetainedResultVariables.Should().HaveCount(1);
        plain.RetainedResultVariables[0].Should().BeSameAs(Primary);

        IndicatorStructure rich = plain with {
            EquivalentResults = new[] { Equivalent },
            ZeroBand = new ZeroBand<Flt64>(new Flt64(1.0), Side),
        };

        rich.RetainedResultVariables.Should().HaveCount(3);
        rich.RetainedResultVariables[0].Should().BeSameAs(Primary);
        rich.RetainedResultVariables[1].Should().BeSameAs(Equivalent);
        rich.RetainedResultVariables[2].Should().BeSameAs(Side);
    }

    /// <summary>**非二值结果被拒绝** / A non-binary result is rejected.</summary>
    [Fact]
    public void TheNonBinaryResult_ShouldBeRejected()
    {
        var continuous = new IndicatorStructure(
            Input: Linear(), ResultVariable: X, BigM: new Flt64(100.0),
            Tolerance: new Flt64(2.0), Converter: IIntoValue<Flt64>.Identity, Name: "bad");

        continuous.GenerateConstraints().Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a continuous column cannot carry a truth value");
    }

    /// <summary>**非正容差被拒绝** / A non-positive tolerance is rejected.</summary>
    [Fact]
    public void TheNonPositiveTolerance_ShouldBeRejected()
    {
        var zero = new IndicatorStructure(
            Input: Linear(), ResultVariable: Primary, BigM: new Flt64(100.0),
            Tolerance: Flt64.Zero, Converter: IIntoValue<Flt64>.Identity, Name: "bad");

        zero.GenerateConstraints().Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a zero tolerance cannot separate the two branches");
    }

    private static IndicatorStructure Structure(string name, IVariableItem result) =>
        new(Input: Linear(), ResultVariable: result, BigM: new Flt64(100.0),
            Tolerance: new Flt64(2.0), Converter: IIntoValue<Flt64>.Identity, Name: name);

    private static double Residual(
        LinearInequality<Flt64> row, double primary, double condition, double result) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble()
                * ValueOf(monomial.Symbol, primary, condition, result);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble()
                * ValueOf(monomial.Symbol, primary, condition, result);
        }

        return total;
    }

    private static double ValueOf(
        ISymbol symbol, double primary, double condition, double result) =>
        symbol.Equals(Primary) ? primary
            : symbol.Equals(ConditionResult) ? condition
            : symbol.Equals(ConjunctionResult) || symbol.Equals(DifferenceResult) ? result
            : 0.0;

    private static LinearPolynomial<Flt64> Linear() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero);

    private static readonly RealVar X = new("is-x");

    private static readonly BinVar Primary = new("is-primary");

    private static readonly BinVar ConditionResult = new("is-cond");

    private static readonly BinVar ConjunctionResult = new("is-and");

    private static readonly BinVar DifferenceResult = new("is-diff");

    private static readonly BinVar Equivalent = new("is-eq");

    private static readonly BinVar Side = new("is-side");

    private static readonly RealVar ValueResult = new("is-value");
}
