#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 条件结果的四条行把结果夹成 0 或输入 / Four rows pin the result to zero or the input.
/// </summary>
/// <remarks>
/// 对齐基线 `model/intermediate/ConditionalValue.kt`。
///
/// **指示 = 0 时 `_zero_*` 两条把 `result` 夹成 0；指示 = 1 时 `_then_*` 两条把它夹成 `input`。**
///
/// adds
///
/// **At indicator 0 the two `_zero_*` rows pin `result` to zero; at indicator 1 the two `_then_*` pin it to `input`.**
/// </remarks>
public class ConditionalValueTests
{
    /// <summary>**四条行的名字与方向** / The four rows' names and directions.</summary>
    [Fact]
    public void TheFourRows_ShouldCarryTheirNamesAndDirections()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        rows.Should().HaveCount(4, "two rows per branch");
        rows.Select(row => row.Name).Should().Equal(
            "cv_zero_ub", "cv_zero_lb", "cv_then_ub", "cv_then_lb");
        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Comparison.Should().Be(Comparison.GE);
    }

    /// <summary>
    /// **指示 = 0 时结果被夹成 0** / At indicator zero the result is pinned to zero.
    /// </summary>
    /// <remarks>
    /// **把 `indicator = 0` 代进 `_zero_ub` 与 `_zero_lb`：
    /// `result ≤ 0` 与 `result ≥ 0`，于是 `result = 0`。**
    ///
    /// **代值是这里唯一能证伪的办法**：**四条行"构造正确"与"它们真的把结果夹住"是两件事，
    /// 而系数写反了行照样能构造出来。**
    ///
    /// **Substituting `indicator = 0` into `_zero_ub` and `_zero_lb` gives `result ≤ 0` and `result ≥ 0`, hence
    /// `result = 0`.**
    ///
    /// **Substitution is the only way to falsify this here**: **"the four rows were constructed" and "they actually pin
    /// the result" are different claims, and flipped coefficients still construct cleanly.**
    /// </remarks>
    [Fact]
    public void TheIndicatorZero_ShouldPinTheResultToZero()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        // `result = 0` 是可行点（残差符号满足关系）。
        // result = 0 is a feasible point (each residual has the right sign).
        Evaluate(rows[0], result: 0.0, indicator: 0.0).Should().BeLessThanOrEqualTo(
            0.0, "_zero_ub holds at result = 0");
        Evaluate(rows[1], result: 0.0, indicator: 0.0).Should().BeGreaterThanOrEqualTo(
            0.0, "_zero_lb holds at result = 0");

        // 而 `result = 1` 被 `_zero_ub` 排除、`result = -1` 被 `_zero_lb` 排除：
        // 两侧都收紧，所以 0 是唯一解。
        // And result = 1 violates `_zero_ub`, result = -1 violates `_zero_lb`: both sides bind, so zero is the only
        // solution.
        Evaluate(rows[0], result: 1.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "_zero_ub rejects a positive result");
        Evaluate(rows[1], result: -1.0, indicator: 0.0).Should().BeLessThan(
            0.0, "_zero_lb rejects a negative result");
    }

    /// <summary>**指示 = 1 时结果跟随输入** / At indicator one the result follows the input.</summary>
    [Fact]
    public void TheIndicatorOne_ShouldPinTheResultToTheInput()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        // `_zero_ub` 在指示 = 1 时退化成 `result <= upper = 10`：`result = 10` 处残差为 0。
        // `_zero_ub` relaxes to result <= upper = 10 at indicator one: the residual is zero at result = 10.
        Evaluate(rows[0], result: 10.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-12, "_zero_ub is tight at the upper bound");

        // `_then_ub` 与 `_then_lb` 把结果夹成输入 `3x`（x = 2 时是 6）。
        // **在 `result = 6` 处两条行的残差都是 0，而在 `result = 0` 处各偏一侧：
        // `_then_ub` 允许（残差为负），`_then_lb` 拒绝（残差也为负）。**
        // **这正是"上下夹住"的形状，比只断言"能取到 6"更能说明方向。**
        //
        // `_then_ub` and `_then_lb` pin the result to the input 3x (six when x is two).
        // **Both residuals are zero at result = 6, and at result = 0 they point the same way (negative) — which
        // `_then_ub` allows and `_then_lb` rejects.** **That is the "pinned from above and below" shape, and it says
        // more about direction than merely "six is reachable".**
        Evaluate(rows[2], result: 6.0, indicator: 1.0, x: 2.0).Should().BeApproximately(
            0.0, 1e-12, "_then_ub is tight at result = input");
        Evaluate(rows[3], result: 6.0, indicator: 1.0, x: 2.0).Should().BeApproximately(
            0.0, 1e-12, "_then_lb is tight at result = input");

        Evaluate(rows[3], result: 0.0, indicator: 1.0, x: 2.0).Should().BeLessThan(
            0.0, "_then_lb rejects a result below the input");
        Evaluate(rows[2], result: 12.0, indicator: 1.0, x: 2.0).Should().BeGreaterThan(
            0.0, "_then_ub rejects a result above the input");
    }

    /// <summary>
    /// **结果与指示是同一个变量时被拒绝** / Same variable as result and indicator is rejected.
    /// </summary>
    /// <remarks>
    /// **两个分支要求它同时等于 0 与 `input`，四条行直接冲突。**
    /// **拒绝得早，比让求解器报"不可行"要好——后者不会指出是哪一对约束的矛盾。**
    ///
    /// **The branches demand it be zero and equal to `input` at once.**
    /// **Rejecting early beats a solver reporting "infeasible" — the latter does not say which pair conflicts.**
    /// </remarks>
    [Fact]
    public void TheSelfIndicating_ShouldBeRejected()
    {
        var value = new ConditionalValue(
            Input: Linear(),
            ResultVariable: Indicator,
            Bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(10.0)),
            Name: "cv");

        value.GenerateConstraints(Indicator, Converter).Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "one variable cannot be both the result and the indicator");
    }

    /// <summary>
    /// **连续指示被拒绝** / A continuous indicator is rejected.
    /// </summary>
    /// <remarks>
    /// **连续指示会让两个分支同时"部分激活"，`result` 变成 0 与 `input` 之间的某个值——
    /// 模型可行，结果是捏造出来的。**
    ///
    /// **A continuous indicator activates both branches partially, leaving `result` somewhere between zero and
    /// `input` — the model stays feasible and the result is fabricated.**
    /// </remarks>
    [Fact]
    public void TheContinuousIndicator_ShouldBeRejected()
    {
        var value = new ConditionalValue(
            Input: Linear(),
            ResultVariable: Result,
            Bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(10.0)),
            Name: "cv");

        value.GenerateConstraints(Continuous, Converter).Should()
            .BeOfType<Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                    "a continuous indicator makes both branches partially active");
    }

    /// <summary>**倒序范围被拒绝** / Reversed bounds are rejected.</summary>
    [Fact]
    public void TheReversedBounds_ShouldBeRejected()
    {
        // `ConditionBounds` 的构造函数本身就拒绝倒序，所以这里断言那一层。
        // The `ConditionBounds` constructor itself rejects reversed bounds, so assert that layer.
        Action reversed = () =>
            _ = new ConditionBounds<Flt64>(new Flt64(10.0), new Flt64(-5.0));

        reversed.Should().Throw<ArgumentException>();
    }

    private static IReadOnlyList<LinearInequality<Flt64>> Rows() =>
        new ConditionalValue(
            Input: Linear(),
            ResultVariable: Result,
            Bounds: new ConditionBounds<Flt64>(new Flt64(-5.0), new Flt64(10.0)),
            Name: "cv").GenerateConstraints(Indicator, Converter).Value;

    /// <summary>代值返回 `lhs − rhs` / Substitute and return `lhs − rhs`.</summary>
    private static double Evaluate(
        LinearInequality<Flt64> row, double result, double indicator, double x = 0.0) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, result, indicator, x);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, result, indicator, x);
        }

        return total;
    }

    private static double ValueOf(
        Fuookami.Ospf.Math.Symbol.ISymbol symbol, double result, double indicator, double x) =>
        symbol.Equals(Result) ? result
            : symbol.Equals(Indicator) ? indicator
            : symbol.Equals(X) ? x
            : 0.0;

    private static LinearPolynomial<Flt64> Linear() =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(3.0), X) }, Flt64.Zero);

    private static readonly RealVar X = CreateX();

    private static RealVar CreateX() {
        var variable = new RealVar("cv-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static readonly RealVar Result = new("cv-result");

    private static readonly BinVar Indicator = new("cv-indicator");

    private static readonly RealVar Continuous = new("cv-continuous");

    private static readonly IIntoValue<Flt64> Converter = new TestConverter();

    private sealed class TestConverter : IIntoValue<Flt64> {
        public Flt64 One => Flt64.One;

        public Flt64 Zero => Flt64.Zero;

        public Flt64 IntoValue(Flt64 value) => value;

        public Flt64 IntoValue(double value) => new(value);

        public Flt64 IntoValue(object value) => value is Flt64 typed
            ? typed
            : new Flt64(Convert.ToDouble(value));

        public Flt64 FromValue(Flt64 value) => value;
    }
}
