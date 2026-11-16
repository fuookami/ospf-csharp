#nullable enable

using FluentAssertions;
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
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 前置为假时门控必须松弛 / The gate must relax when the antecedent is false.
/// </summary>
/// <remarks>
/// 对齐补非折叠蕴含。
///
/// **蕴含与合取的唯一区别就在"前置为假时"**：**如果那时约束仍然生效，
/// 它就不是蕴含，而是合取——而两者在"前置为真"的点上完全一样。**
///
/// **The only difference between an implication and a conjunction is the false-antecedent case**: **if the constraint
/// still bites there, it is a conjunction, not an implication — and the two agree completely wherever the antecedent
/// is true.**
/// </remarks>
public class ImpliedConditionTests
{
    /// <summary>**三行，且第三行把两个二元量链起来** / Three rows, the third linking the binaries.</summary>
    /// <remarks>
    /// **只有 ①② 时 `indicator` 是自由的**：**求解器可以为了满足区间把它设成 1，
    /// 于是"蕴含"变成"永远成立"。**
    ///
    /// **With only ①② the `indicator` is free**: **the solver may set it to one to satisfy the interval, turning
    /// "implies" into "always".**
    /// </remarks>
    [Fact]
    public void TheGenerateConstraints_ShouldEmitThreeRowsIncludingTheLink()
    {
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
            Build().GenerateConstraints(Antecedent(), Tolerance, IIntoValue<Flt64>.Identity);

        rows.Should().BeOfType<Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        rows.Value.Should().HaveCount(3, "two gates plus one link");

        rows.Value[0].Name.Should().EndWith("_con_gated_lower");
        rows.Value[1].Name.Should().EndWith("_con_gated_upper");
        rows.Value[2].Name.Should().EndWith("_imply_link");
        rows.Value[0].Comparison.Should().Be(Comparison.GE);
        rows.Value[1].Comparison.Should().Be(Comparison.LE);
        rows.Value[2].Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **前置为假时下界门控确实松弛** / The lower gate really relaxes on a false antecedent.
    /// </summary>
    /// <remarks>
    /// **这是"蕴含"与"合取"的分界线，也是这里最重要的一条。**
    ///
    /// **把 `antecedent = 0`、`indicator = 0` 代入下界行**：
    /// **`input ≥ lower − lowerAdjust`，而 `lowerAdjust = tolerance − lower`**，
    /// **于是右端 = `lower − (tolerance − lower) = 2·lower − tolerance`。**
    ///
    /// **取 `lower = −10`、`tolerance = 1`：右端 = `−21`。**
    /// **而如果实现里把 `lowerAdjust` 写成 `bounds.lower`（即 `−10`），
    /// 右端会变成 `−10 − (−10) = 0`，于是前置为假时仍要求 `input ≥ 0`——那就是合取。**
    ///
    /// **两个右端相差 21，不会被舍入吞掉。**
    ///
    /// **This is the dividing line between an implication and a conjunction, and the batch's most important
    /// criterion.**
    ///
    /// **Substituting `antecedent = 0`, `indicator = 0` into the lower row**: **`input ≥ lower − lowerAdjust` with
    /// `lowerAdjust = tolerance − lower`**, **so the right-hand side is `2·lower − tolerance`.**
    ///
    /// **With `lower = −10` and `tolerance = 1` that is `−21`.** **Had the implementation used `bounds.lower`
    /// (`−10`) for `lowerAdjust`, the right-hand side would be `0`, still demanding `input ≥ 0` with a false
    /// antecedent — a conjunction.**
    ///
    /// **The two differ by 21, which rounding cannot swallow.**
    /// </remarks>
    [Fact]
    public void TheLowerGate_ShouldRelaxWhenTheAntecedentIsFalse()
    {
        ImpliedCondition condition = Build();
        LinearInequality<Flt64> lower = condition.GenerateConstraints(
            Antecedent(), Tolerance, IIntoValue<Flt64>.Identity).Value[0];

        // 前置与指示都为 0 时，右端必须是 2·lower − tolerance = −21。
        // With both binaries at zero the right-hand side must be 2*lower - tolerance = -21.
        double rhs = lower.Rhs.Constant.ToDouble();
        rhs.Should().BeApproximately(
            -21.0, 1e-12,
            "the gate offset must be tolerance - lower, not lower, or the row still bites");
        rhs.Should().NotBeApproximately(
            0.0, 1e-9, "a right-hand side of zero would mean the constraint never relaxes");
    }

    /// <summary>**前置为假时上界门控也松弛** / The upper gate relaxes too.</summary>
    /// <remarks>
    /// **上界行在 `antecedent = 0`、`indicator = 0` 时是 `input ≤ upperAdjust = upper`。**
    /// **这一侧不需要偏移抵消**——**因为 `antecedent` 的系数是 `+upperAdjust`，
    /// 前置为 0 时它直接消失，右端仍是 `upper`。**
    /// **但若把 `antecedent` 的符号写反（`−upperAdjust`），前置为 1 时约束会变成 `input ≤ upper + upper`，
    /// 即前置成立时反而更松——同样是"看起来对"的错误。**
    ///
    /// **With `antecedent = 0` and `indicator = 0` the upper row is `input ≤ upperAdjust = upper`.** **This side needs
    /// no offset cancellation** — **the antecedent's coefficient is `+upperAdjust`, so a false antecedent simply drops
    /// it and the right-hand side stays `upper`.** **But flipping that sign makes the row `input ≤ 2·upper` when the
    /// antecedent holds — looser exactly where it should bite.**
    /// </remarks>
    [Fact]
    public void TheUpperGate_ShouldUseAPositiveAntecedentCoefficient()
    {
        ImpliedCondition condition = Build();
        LinearInequality<Flt64> upper = condition.GenerateConstraints(
            Antecedent(), Tolerance, IIntoValue<Flt64>.Identity).Value[1];

        double antecedentCoefficient = 0.0;
        foreach (LinearMonomial<Flt64> monomial in upper.Lhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol, Antecedent())) {
                antecedentCoefficient = monomial.Coefficient.ToDouble();
            }
        }

        // **注意：`Antecedent` 每次返回同一个静态实例，所以这里能匹配上。**
        // Note: Antecedent returns the same static instance each time, so this matches.
        antecedentCoefficient.Should().BeApproximately(
            10.0, 1e-12, "the antecedent coefficient is +upper, so a true antecedent tightens the row");
    }

    /// <summary>**同一个变量同时当前置与指示会被拒绝** / One variable as both is rejected.</summary>
    /// <remarks>
    /// **那时第 ③ 行退化成 `0 ≤ 0`，链路消失而模型仍可行。**
    ///
    /// **Row ③ then degenerates to `0 ≤ 0`, the link vanishes and the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheGenerateConstraints_ShouldRejectOneVariableAsBothRoles()
    {
        var shared = new BinVar("shared");
        var condition = new ImpliedCondition(
            input: Input(),
            indicatorVariable: shared,
            bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
            name: "self");

        condition.GenerateConstraints(shared, Tolerance, IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "the link row would degenerate");
    }

    /// <summary>**非二元变量被拒绝** / Non-binary variables are rejected.</summary>
    [Fact]
    public void TheGenerateConstraints_ShouldRejectNonBinaryVariables()
    {
        var continuous = new RealVar("continuous");
        continuous.Range.Geq(Flt64.Zero);
        continuous.Range.Leq(Flt64.One);

        var condition = new ImpliedCondition(
            input: Input(),
            indicatorVariable: continuous,
            bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
            name: "continuous");

        condition.GenerateConstraints(Antecedent(), Tolerance, IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "a continuous indicator has no 0/1 meaning");
    }

    /// <summary>**非正容差被拒绝** / A non-positive tolerance is rejected.</summary>
    [Fact]
    public void TheGenerateConstraints_ShouldRejectNonPositiveTolerance()
    {
        ImpliedCondition condition = Build();

        condition.GenerateConstraints(Antecedent(), Flt64.Zero, IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "a zero tolerance makes the gate offset vanish");
        condition.GenerateConstraints(Antecedent(), new Flt64(-1.0), IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`lower > 0` 与 `upper < tolerance` 被拒绝** / Both bounds checks hold.</summary>
    /// <remarks>
    /// **这两种情形下 `lowerAdjust` 或 `upperAdjust` 会变成 0 或负数，
    /// 于是门控行的系数消失，前置为假时约束不再松弛。**
    ///
    /// **In both cases `lowerAdjust` or `upperAdjust` becomes zero or negative, the gate coefficient vanishes, and the
    /// constraint stops relaxing when the antecedent is false.**
    /// </remarks>
    [Fact]
    public void TheGenerateConstraints_ShouldRejectBoundsThatBreakTheGate()
    {
        var positiveLower = new ImpliedCondition(
            input: Input(),
            indicatorVariable(0),
            bounds: new ConditionBounds<Flt64>(new Flt64(1.0), new Flt64(10.0)),
            name: "positive-lower");
        positiveLower.GenerateConstraints(Antecedent(), Tolerance, IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "a positive lower bound makes the offset negative");

        var smallUpper = new ImpliedCondition(
            input: Input(),
            indicatorVariable(1),
            bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(0.5)),
            name: "small-upper");
        smallUpper.GenerateConstraints(Antecedent(), Tolerance, IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "an upper bound below the tolerance makes the gate vanish");
    }

    private static readonly Flt64 Tolerance = new(1.0);

    private static readonly BinVar SharedAntecedent = new("antecedent");

    private static BinVar Antecedent() => SharedAntecedent;

    private static BinVar indicatorVariable(int which) => which == 0
        ? SharedIndicatorA
        : SharedIndicatorB;

    private static readonly BinVar SharedIndicatorA = new("indicator-a");
    private static readonly BinVar SharedIndicatorB = new("indicator-b");

    private static ImpliedCondition Build() =>
        new(
            input: Input(),
            indicatorVariable: SharedIndicatorA,
            bounds: new ConditionBounds<Flt64>(new Flt64(-10.0), new Flt64(10.0)),
            name: "implied");

    private static LinearPolynomial<Flt64> Input() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, SharedVariable) }, Flt64.Zero);

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("ic-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }
}
