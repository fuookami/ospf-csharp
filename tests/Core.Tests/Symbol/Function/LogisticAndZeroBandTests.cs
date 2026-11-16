#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// sigmoid 的两个参数各有各的失效方式 / The sigmoid's two parameters fail in different ways.
/// </summary>
/// <remarks>
/// B1151 补 `LogisticApproximationFunction`（基线 `NonlinearPiecewise.kt`）与 `ZeroBand`（基线 `ZeroBand.kt`）。
///
/// **中点搞错，曲线整体平移；陡度搞错，曲线整体变平或反号——两者都不报错。**
/// **而"值都在 0 与 1 之间"这条对任何 sigmoid 形态的实现都成立，分不开它们。**
///
/// adds
///
/// **A wrong midpoint shifts the curve; a wrong steepness flattens or mirrors it — neither throws.**
/// **And "values lie between 0 and 1" holds for any sigmoid-shaped implementation, so it separates nothing.**
/// </remarks>
public class LogisticAndZeroBandTests
{
    /// <summary>**中点处必须是 0.5** / The value at the midpoint must be 0.5.</summary>
    /// <remarks>
    /// **`σ(0) = 0.5` 对任何 `k` 都成立，所以这条只测中点，不测陡度**——
    /// **而中点写错时它会立刻红：曲线平移之后，`m` 处的值不再是 0.5。**
    ///
    /// **`σ(0) = 0.5` holds for any `k`, so this pins the midpoint and not the steepness** —
    /// **and a wrong midpoint turns it red at once: once the curve is shifted, the value at `m` is no longer 0.5.**
    /// </remarks>
    [Fact]
    public void TheLogistic_ShouldBeHalfAtTheMidpoint()
    {
        LogisticApproximationFunction<Flt64> function = LogisticApproximationFunction<Flt64>.Create(
            input: EmptyPolynomial(),
            lower: 0.0,
            upper: 10.0,
            converter: IIntoValue<Flt64>.Identity,
            steepness: 2.0,
            midpoint: 5.0,
            segments: 10).Value;

        // 索引 5 对应的采样点正好是 x = 5 = midpoint。
        function.SamplingPoints[5][0].ToDouble().Should().Be(5.0, "the sample grid is uniform over [0, 10]");
        function.SamplingPoints[5][1].ToDouble().Should().BeApproximately(
            0.5, 1e-12, "sigma is one half exactly at its midpoint");

        // 端点的走向：x 越大越接近 1。
        function.SamplingPoints[0][1].ToDouble().Should().BeLessThan(0.5);
        function.SamplingPoints[10][1].ToDouble().Should().BeGreaterThan(0.5);
    }

    /// <summary>**陡度必须真的被用上** / The steepness must actually be used.</summary>
    /// <remarks>
    /// **中点固定、只改陡度：`k = 1` 与 `k = 4` 在同一个 `x` 上必须给出不同的值。**
    /// **而"在 0 与 1 之间"对两者都成立。**
    ///
    /// **With the midpoint fixed and only the steepness changed, `k = 1` and `k = 4` must differ at the same `x`.**
    /// **"Between 0 and 1" holds for both.**
    /// </remarks>
    [Fact]
    public void TheLogistic_ShouldUseTheSteepness()
    {
        LogisticApproximationFunction<Flt64> gentle = LogisticApproximationFunction<Flt64>.Create(
            input: EmptyPolynomial(),
            lower: 0.0, upper: 4.0, converter: IIntoValue<Flt64>.Identity,
            steepness: 1.0, midpoint: 2.0, segments: 4).Value;
        LogisticApproximationFunction<Flt64> steep = LogisticApproximationFunction<Flt64>.Create(
            input: EmptyPolynomial(),
            lower: 0.0, upper: 4.0, converter: IIntoValue<Flt64>.Identity,
            steepness: 4.0, midpoint: 2.0, segments: 4).Value;

        // x = 1 → z = k*(-1)：k=1 给 σ(-1)=0.2689，k=4 给 σ(-4)=0.0180。
        gentle.SamplingPoints[1][1].ToDouble().Should().BeApproximately(
            LogisticApproximationFunction<Flt64>.Source(-1.0), 1e-12);
        steep.SamplingPoints[1][1].ToDouble().Should().BeApproximately(
            LogisticApproximationFunction<Flt64>.Source(-4.0), 1e-12);

        steep.SamplingPoints[1][1].ToDouble().Should().BeLessThan(
            gentle.SamplingPoints[1][1].ToDouble(),
            "a steeper curve is closer to zero below the midpoint");
    }

    /// <summary>**中点左移会让同一个 `x` 上的值变大** / Shifting the midpoint left raises the value at a fixed x.</summary>
    /// <remarks>
    /// **上一条固定了中点、改陡度；这一条固定陡度、改中点。**
    /// **两条合起来才能分开"两个参数被写反了"这种错**——
    /// **只测其中一条时，把 `k` 与 `m` 互换的实现仍可能通过。**
    ///
    /// **The previous criterion fixes the midpoint and changes the steepness; this fixes the steepness and changes the
    /// midpoint.** **Only together do they separate "the two arguments are swapped"** —
    /// **with either alone, an implementation that swaps `k` and `m` can still pass.**
    /// </remarks>
    [Fact]
    public void TheLogistic_ShouldMoveWithTheMidpoint()
    {
        LogisticApproximationFunction<Flt64> atZero = LogisticApproximationFunction<Flt64>.Create(
            input: EmptyPolynomial(),
            lower: -5.0, upper: 5.0, converter: IIntoValue<Flt64>.Identity,
            steepness: 1.0, midpoint: 0.0, segments: 10).Value;
        LogisticApproximationFunction<Flt64> atNegativeTwo = LogisticApproximationFunction<Flt64>.Create(
            input: EmptyPolynomial(),
            lower: -5.0, upper: 5.0, converter: IIntoValue<Flt64>.Identity,
            steepness: 1.0, midpoint: -2.0, segments: 10).Value;

        // x = 1 → z = 1 - m：m=0 给 σ(1)=0.7311，m=-2 给 σ(3)=0.9526。
        atZero.SamplingPoints[6][1].ToDouble().Should().BeApproximately(
            LogisticApproximationFunction<Flt64>.Source(1.0), 1e-12);
        atNegativeTwo.SamplingPoints[6][1].ToDouble().Should().BeApproximately(
            LogisticApproximationFunction<Flt64>.Source(3.0), 1e-12);
    }

    /// <summary>**极端输入下源函数仍是有限数且落在 [0, 1] 内** / The source stays finite and inside [0, 1] at the extremes.</summary>
    /// <remarks>
    /// **我第一版在这里断言"永远不会取到 0 与 1"，被实测打红**：
    /// **`σ(-800)` 在双精度下下溢成 `0.0`，`σ(700)` 舍入成 `1.0`**——
    /// **`e^(-800) ≈ 10^-347` 小于最小次正规数，而 `1/(1+10^-305)` 舍入到 1。**
    ///
    /// **所以这条判据只钉合约：不出现 `NaN`、不出现 `±∞`、始终落在 `[0, 1]`。**
    /// **两支写法的好处在于中间量不是无穷——而中间量不可从结果观察，
    /// 所以这条判据不声称它验证了那件事。**
    ///
    /// **The first version asserted "never exactly 0 or 1" and was turned red by measurement**:
    /// **`σ(-800)` underflows to `0.0` in double and `σ(700)` rounds to `1.0`** —
    /// **`e^(-800) ≈ 10^-347` is below the smallest subnormal, and `1/(1+10^-305)` rounds to one.**
    ///
    /// **So this criterion pins only the contract: no `NaN`, no `±∞`, always inside `[0, 1]`.**
    /// **The two branches' benefit is that an intermediate is not infinite — and an intermediate is not observable
    /// from the result, so this criterion does not claim to have verified that.**
    /// </remarks>
    [Fact]
    public void TheStableSigmoid_ShouldStayFiniteAtTheExtremes()
    {
        double[] extremes = { -1e6, -800.0, -700.0, -40.0, 0.0, 40.0, 700.0, 800.0, 1e6 };

        foreach (double z in extremes) {
            double value = LogisticApproximationFunction<Flt64>.Source(z);
            double.IsFinite(value).Should().BeTrue($"sigma({z}) must be a finite number, not NaN or an infinity");
            value.Should().BeInRange(0.0, 1.0, $"sigma({z}) is a probability and cannot leave [0, 1]");
        }
    }

    /// <summary>**两条分支在 0 处必须连续** / The two branches meet at zero.</summary>
    /// <remarks>
    /// **`Source(0)` 走的是 `z >= 0` 那一支，而 `Source(-1e-12)` 走另一支。**
    /// **若两支的常数写错了一个，跳变会在 0 附近出现，而"值在 0 与 1 之间"仍成立。**
    ///
    /// **`Source(0)` takes the `z >= 0` branch while `Source(-1e-12)` takes the other.**
    /// **A wrong constant in either branch shows up as a jump near zero, while "between 0 and 1" still holds.**
    /// </remarks>
    [Fact]
    public void TheTwoBranches_ShouldMeetAtZero()
    {
        LogisticApproximationFunction<Flt64>.Source(0.0).Should().Be(
            0.5, "the z >= 0 branch at zero is exactly one half");
        LogisticApproximationFunction<Flt64>.Source(-1e-12).Should().BeApproximately(
            0.5, 1e-11, "the other branch must agree at the boundary");
    }

    /// <summary>**`ZeroBand` 原样保存容差与侧变量** / `ZeroBand` keeps exactly what it was given.</summary>
    /// <remarks>
    /// **它是个纯数据载体，不做校验也不算零**——**这条钉住"它没有偷偷改变容差"**
    /// （**例如取绝对值**：**那会把"故意传负容差"变成一个静默的零带，而调用方以为自己写错了会被发现**）。
    ///
    /// **It is a pure carrier that validates nothing and computes no zero** — **this pins that it does not quietly
    /// change the tolerance** (**absolute-valuing it, say**: **that would turn a deliberately negative tolerance into a
    /// silent band while the caller believes their mistake would be caught**).
    /// </remarks>
    [Fact]
    public void TheZeroBand_ShouldKeepTheToleranceAsGiven()
    {
        var side = new BinVar("zeroband-side");
        var band = new ZeroBand<Flt64>(new Flt64(-0.25), side);

        band.Tolerance.Should().Be(new Flt64(-0.25), "the carrier does not absolute-value anything");
        band.SideVariable.Should().BeSameAs(side, "the side variable is kept, not copied");
    }

    /// <summary>空多项式：与既有测试的写法一致 / Empty, as in the existing test.</summary>
    private static LinearPolynomial<Flt64> EmptyPolynomial() => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        Flt64.Zero);
}
