#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// CVaR 是尾部平均，不是平均值，也不是分位点 / CVaR is a tail average, not a mean and not a quantile.
/// </summary>
/// <remarks>
/// B1136 补 CvarFunction（基线 Cvar.kt）。
/// 定义是 min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]。
///
/// 三个容易混淆的量：**平均值**（全部情景）、**VaR**（一个分位点）、**CVaR**（尾部平均）。
/// 在离散分布上，**CVaR 介于 VaR 与最坏情况之间**，而这条不等式是这一批唯一能同时否掉
/// “写成平均值”和“写成分位点”的判据。
///
/// adds
/// Defined as min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ].
///
/// Three quantities get confused: **the mean** (all scenarios), **VaR** (a quantile) and **CVaR** (the tail average).
/// On a discrete distribution **CVaR sits between VaR and the worst case**, and that inequality is the only thing
/// here that rejects both "it is the mean" and "it is the quantile".
/// </remarks>
public class CvarFunctionTests
{
    /// <summary>**CVaR 不高于最坏情况、且不低于平均值** / CVaR sits between the mean and the worst case.</summary>
    /// <remarks>
    /// 情景损失取 0、10、20、30，等概率 0.25，alpha = 0.5。
    /// 平均值 15，最坏 30。**CVaR 必须落在这两者之间**：
    /// **写成平均值会得到 15（恰好等于下界，会被"不低于平均值"这条放过），
    /// 所以还要单独断言它严格大于平均值。**
    ///
    /// Scenarios 0, 10, 20, 30 with equal probability 0.25 and alpha = 0.5.
    /// The mean is 15 and the worst is 30. **CVaR must land between them**:
    /// **written as the mean it would return 15 (the lower bound itself, which "not below the mean" lets through),
    /// so it also has to be asserted strictly greater than the mean.**
    /// </remarks>
    [Fact]
    public void TheCvar_ShouldSitBetweenTheMeanAndTheWorst()
    {
        CvarFunction<Flt64> cvar = Make(
            losses: new[] { 0.0, 10.0, 20.0, 30.0 },
            probabilities: new[] { 0.25, 0.25, 0.25, 0.25 },
            alpha: 0.5);

        double value = Value(cvar, 0.0, 10.0, 20.0, 30.0);

        value.Should().BeGreaterThan(15.0, "CVaR looks past the median into the tail, so it exceeds the mean");
        value.Should().BeLessThanOrEqualTo(30.0, "no tail average can exceed the worst scenario");
    }

    /// <summary>**所有情景相等时，CVaR 等于那个值** / With identical scenarios, CVaR is that value.</summary>
    /// <remarks>
    /// 这条排掉“公式里某处多乘了一个系数”：**当所有 L_i 都等于 c 时，max(L_i - t, 0) 在 t = c 处全为 0，
    /// 于是 CVaR = c。任何系数写错都会让结果偏离 c。**
    ///
    /// This rejects "a coefficient is wrong somewhere": **with every L_i equal to c, all max(L_i - t, 0) vanish at
    /// t = c, so CVaR = c. Any wrong coefficient moves the answer off c.**
    /// </remarks>
    [Fact]
    public void TheIdenticalScenarios_ShouldGiveThatValue()
    {
        CvarFunction<Flt64> cvar = Make(
            losses: new[] { 7.0, 7.0, 7.0 },
            probabilities: new[] { 0.5, 0.25, 0.25 },
            alpha: 0.9);

        Value(cvar, 7.0, 7.0, 7.0).Should().BeApproximately(7.0, 1e-12,
            "a degenerate distribution has no tail to average over");
    }

    /// <summary>**alpha 越大，CVaR 越靠近最坏情况** / A larger alpha pushes CVaR toward the worst case.</summary>
    /// <remarks>
    /// alpha 越大，尾部越窄，**平均的对象就越接近最坏那几个情景**。
    /// **把分母写成 alpha（而不是 1-alpha），这条会红**：
    /// **因为 alpha 增大时分母也增大，尾部权重反而变小，趋势就反了。**
    ///
    /// A larger alpha means a narrower tail, **so the average is taken over scenarios closer to the worst**.
    /// **Dividing by alpha instead of 1-alpha turns this red**: **the denominator grows with alpha, the tail weights
    /// shrink, and the trend reverses.**
    /// </remarks>
    [Fact]
    public void TheHigherAlpha_ShouldMoveTowardTheWorst()
    {
        double low = Value(Make(
            losses: new[] { 0.0, 10.0, 20.0, 30.0 },
            probabilities: new[] { 0.25, 0.25, 0.25, 0.25 },
            alpha: 0.5), 0.0, 10.0, 20.0, 30.0);

        double high = Value(Make(
            losses: new[] { 0.0, 10.0, 20.0, 30.0 },
            probabilities: new[] { 0.25, 0.25, 0.25, 0.25 },
            alpha: 0.9), 0.0, 10.0, 20.0, 30.0);

        high.Should().BeGreaterThan(low,
            "a narrower tail averages over losses closer to the worst, so the value rises");
    }

    /// <summary>**alpha = 0 时 CVaR 等于平均值** / At alpha = 0 CVaR equals the mean.</summary>
    /// <remarks>
    /// alpha = 0 意味着尾部是整个分布，**此时 CVaR 的定义退化成平均值**——
    /// **这是一条恒等式，不是近似**：**它同时验证分母与权重都写对了。**
    ///
    /// At alpha = 0 the tail is the whole distribution, **where CVaR degenerates to the mean** —
    /// **an identity rather than an approximation, so it validates both the denominator and the weights.**
    /// </remarks>
    [Fact]
    public void TheZeroAlpha_ShouldEqualTheMean()
    {
        CvarFunction<Flt64> cvar = Make(
            losses: new[] { 0.0, 10.0, 20.0, 30.0 },
            probabilities: new[] { 0.25, 0.25, 0.25, 0.25 },
            alpha: 0.0);

        Value(cvar, 0.0, 10.0, 20.0, 30.0).Should().BeApproximately(15.0, 1e-9,
            "at alpha = 0 the tail is the whole distribution, so CVaR is the mean");
    }

    /// <summary>**alpha 越界要当场拒绝** / An out-of-range alpha is rejected outright.</summary>
    /// <remarks>
    /// alpha = 1 时 1-alpha 为零，**而这个除法不会抛异常——它会变成 inf 或 NaN，然后悄悄污染整条目标函数。**
    ///
    /// At alpha = 1 the denominator is zero, **and that division does not throw — it yields inf or NaN and quietly
    /// poisons the whole objective.**
    /// </remarks>
    [Fact]
    public void TheOutOfRangeAlpha_ShouldBeRejected()
    {
        Action atOne = () => Make(new[] { 0.0, 1.0 }, new[] { 0.5, 0.5 }, alpha: 1.0);
        Action negative = () => Make(new[] { 0.0, 1.0 }, new[] { 0.5, 0.5 }, alpha: -0.1);

        atOne.Should().Throw<ArgumentException>("alpha = 1 leaves an empty tail and a zero denominator");
        negative.Should().Throw<ArgumentException>("alpha below zero has no meaning as a confidence level");
    }

    /// <summary>**概率之和不为一要拒绝** / Probabilities that do not sum to one are rejected.</summary>
    [Fact]
    public void TheUnnormalisedProbabilities_ShouldBeRejected()
    {
        Action unbalanced = () => Make(new[] { 0.0, 1.0 }, new[] { 0.5, 0.6 }, alpha: 0.5);

        unbalanced.Should().Throw<ArgumentException>(
            "a weighted average over weights that do not sum to one is not an average");
    }

    /// <summary>**情景数与概率数不等要拒绝** / Mismatched scenario and probability counts are rejected.</summary>
    [Fact]
    public void TheMismatchedCounts_ShouldBeRejected()
    {
        Action mismatched = () => Make(new[] { 0.0, 1.0, 2.0 }, new[] { 0.5, 0.5 }, alpha: 0.5);

        mismatched.Should().Throw<ArgumentException>(
            "a loss without a probability has no weight, and a probability without a loss has nothing to weight");
    }

    /// <summary>对一个具体的损失向量求值 / Evaluate on a concrete loss vector.</summary>
    private static double Value(CvarFunction<Flt64> cvar, params double[] losses) {
        var values = new Dictionary<ISymbol, Flt64>();
        for (int index = 0; index < losses.Length; ++index) {
            values[Variables[index]] = new Flt64(losses[index]);
        }

        cvar.Evaluate(values).Should().NotBeNull("the loss vector is fully specified");
        return cvar.Evaluate(values)!.Value.ToDouble();
    }

    /// <summary>造一个 CVaR 函数，每个情景是一个独立变量 / Build one, each scenario an independent variable.</summary>
    private static CvarFunction<Flt64> Make(double[] losses, double[] probabilities, double alpha) {
        var polynomials = new List<LinearPolynomial<Flt64>>();
        for (int index = 0; index < losses.Length; ++index) {
            polynomials.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), Variables[index]) },
                Flt64.Zero));
        }

        var weights = new List<Flt64>();
        foreach (double probability in probabilities) {
            weights.Add(new Flt64(probability));
        }

        return new CvarFunction<Flt64>(
            losses: polynomials,
            probabilities: weights,
            alpha: new Flt64(alpha),
            converter: IIntoValue<Flt64>.Identity);
    }

    private static readonly RealVar[] Variables = {
        new("s0"), new("s1"), new("s2"), new("s3"),
    };
}
