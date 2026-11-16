// PWL 十进制口径的判据 / Criteria for the decimal PWL domain.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **PWL 十进制口径** / The decimal PWL domain.
/// </summary>
/// <remarks>
/// 判据的重点：**误差在十进制域内算完，不往返 `double`**、**相对误差的分母是真值 `r²` 而不是近似值**、
/// **弦在断点处精确（误差恰为 0）**。
///
/// The criteria focus on **the error being computed entirely in the decimal domain without a `double` round trip**, on **the relative error's denominator being the true `r²` rather than the
/// approximation**, and on **the chord being exact at the breakpoints (error exactly zero)**.
/// </remarks>
public class PwlDecimalDomainTests {
    private static double D(FltX value) => value.ToFlt64().ToDouble();

    /// <summary>
    /// **弦在断点处精确：误差恰为 0，不是"很小"** / The chord is exact at breakpoints: the error is exactly zero, not merely small.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 每个断点处 `ActualError` **恰为 `FltX.Zero`**（十进制域内相减，同一个数减自己就是 0）。
    /// 2. `double` 重载在同一处给出的是**近似 0**——两者不一致正是这里要修的：往返转换让"精确"变成"大约"。
    /// 3. 区间中点处误差**严格大于 0**（弦在凸函数下方）。
    ///
    /// Three paired points: at every breakpoint `ActualError` **is exactly `FltX.Zero`**, since the subtraction happens in the decimal domain where a number minus itself is zero; the `double`
    /// overload gives merely approximately zero there, and that discrepancy is what fixes, a round trip turning "exact" into "roughly"; and at the interval midpoint the error is
    /// **strictly positive**, since the chord lies below the convex function.
    /// </remarks>
    [Fact]
    public void ActualError_ShouldBeExactlyZeroAtBreakpoints() {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 4, BreakpointStrategy: PwlBreakpointStrategy.Uniform));

        foreach (double breakpoint in approximation.Breakpoints) {
            var radius = new FltX(breakpoint);
            approximation.ActualError(radius).Should().Be(FltX.Zero,
                "a number minus itself is zero in the decimal domain, whatever the radius");
        }

        // **2.5 是段 [2,3] 的中点**；3.0 本身是断点，误差为 0——选错采样点会把"弦在下方"写成"弦是精确的"。
        // **2.5 is the midpoint of the segment [2,3]**; 3.0 is itself a breakpoint with zero error, and picking it would report "the chord lies below" as "the chord is exact".
        var midpoint = new FltX(2.5);
        D(approximation.ActualError(midpoint)).Should().BeGreaterThan(0.0,
            "the chord sits below r squared at the midpoint of a segment");
    }

    /// <summary>
    /// **十进制与 `double` 往返的结果确实不同** / The decimal result really differs from the `double` round trip.
    /// </summary>
    /// <remarks>
    /// 上一条判据只证明"十进制算得出 0"，**证明不了"十进制和 `double` 不一样"**——两个变异（误差往返 `double`、
    /// 求值走 `double` 版）都因此存活。这条用 `decimal` 里可表示、`double` 里必然舍掉的那一位来分开两者：
    ///
    /// `r = 2.0000000000000000001`。`double` 把它舍成 `2.0`（正好是断点，误差 `0`）；
    /// 十进制下它不是断点，弦与 `r²` 的差是 `1e-19` 量级——**大于 0**。
    ///
    /// 于是"误差 `> 0`"与"求值 `!= 4.0`"各自成为可检出的断言，而不是被 1e-12 的容差吃掉。
    ///
    /// The previous criterion only proves the decimal path reaches zero; it **cannot prove the decimal and `double` results differ**, which is why both round-trip mutations survived. This one
    /// separates them using a digit that `decimal` holds and `double` must drop: with `r = 2.0000000000000000001`, `double` rounds it to `2.0` — a breakpoint with error `0` — whereas in decimal it
    /// is not a breakpoint and the gap between the chord and `r²` is of order `1e-19`, **strictly positive**. So "the error is above zero" and "the value is not `4.0`" each become a detectable
    /// assertion instead of being swallowed by a 1e-12 tolerance.
    /// </remarks>
    [Fact]
    public void DecimalPath_ShouldDifferFromTheDoubleRoundTrip() {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 4, BreakpointStrategy: PwlBreakpointStrategy.Uniform));

        var radius = new FltX(2.0000000000000000001m);
        radius.ToFlt64().ToDouble().Should().Be(2.0, "the extra digit cannot survive a double");

        approximation.ActualError(radius.ToFlt64().ToDouble()).Should().Be(0.0,
            "the double path lands exactly on a breakpoint");
        approximation.ActualError(radius).Should().BeGreaterThan(FltX.Zero,
            "in decimal the radius is off the breakpoint, so the chord misses r squared");

        approximation.Evaluate(radius.ToFlt64().ToDouble()).Should().Be(4.0);
        approximation.Evaluate(radius).Should().NotBe(new FltX(4.0),
            "the decimal evaluation carries the extra digit through");
    }

    /// <summary>
    /// **`FromSolver` 走的是十进制重载** / `FromSolver` goes through the decimal overload.
    /// </summary>
    /// <remarks>
    /// 两点成对：同一个"多一位"的半径，`FromSolver` 给出的绝对误差**大于 0**（十进制口径）；
    /// 而若改回 `double` 重载，它会是**恰好 0**。这正是变异"提取层退回 `double` 重载"的检出点。
    ///
    /// Two paired points: for the same one-extra-digit radius `FromSolver` reports an absolute error **above zero** (the decimal reading), whereas going back to the `double` overload would make it
    /// **exactly zero** — the exact point at which the "extraction reverts to `double`" mutation is caught.
    /// </remarks>
    [Fact]
    public void FromSolver_ShouldUseTheDecimalOverload() {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 4, BreakpointStrategy: PwlBreakpointStrategy.Uniform));
        var envelope = new ConservativeRadiusEnvelope(
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter));

        var radius = new FltX(2.0000000000000000001m);
        var extracted = PwlExtractedRadius.FromSolver(
            variableName: "r",
            solverRadius: radius,
            solverRadiusSquared: radius * radius,
            envelope: envelope,
            approximation: approximation);

        extracted.PwlAbsoluteError.Should().BeGreaterThan(FltX.Zero,
            "the decimal overload sees the radius as off the breakpoint");
    }

    /// <summary>
    /// **相对误差的分母是真值 `r²`，不是近似值** / The relative error's denominator is the true `r²`, not the approximation.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `ActualRelativeError(r)` **等于** `ActualError(r) / (r * r)`——分母是 `r²`。
    /// 2. 用近似值当分母会得到**不同的数**（这里近似值更小，所以那个数更大），两者必须分得开。
    /// 3. 半径极小时（`r²` 低于 1e-15 阈值）相对误差**取 0**，而不是除零或无穷。
    ///
    /// Three paired points: `ActualRelativeError(r)` **equals** `ActualError(r) / (r * r)`, the denominator being `r²`; using the approximation instead gives **a different number** (smaller here, so
    /// that quotient is larger), and the two must be distinguishable; and at a tiny radius, where `r²` falls under the 1e-15 threshold, the relative error **is zero** rather than a division by zero
    /// or an infinity.
    /// </remarks>
    [Fact]
    public void ActualRelativeError_ShouldDivideByTheTrueSquaredRadius() {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 2, BreakpointStrategy: PwlBreakpointStrategy.Uniform));

        var radius = new FltX(2.5);
        FltX squared = radius * radius;
        FltX expected = approximation.ActualError(radius) / squared;
        FltX actual = approximation.ActualRelativeError(radius);
        D(actual).Should().BeApproximately(D(expected), 1e-12);

        FltX viaApproximation = approximation.ActualError(radius) / approximation.Evaluate(radius);
        D(viaApproximation).Should().NotBeApproximately(D(actual), 1e-9,
            "using the approximation as the denominator biases the error upward");

        approximation.ActualRelativeError(new FltX(1e-9)).Should().Be(FltX.Zero,
            "below the squared-radius threshold the relative error is defined as zero");
    }

    /// <summary>
    /// **十进制求值与 `double` 求值在容差内一致** / The decimal and double evaluations agree within tolerance.
    /// </summary>
    /// <remarks>
    /// 两点成对：`Evaluate(FltX)` 与 `Evaluate(double)` 在同一点**在 1e-12 内一致**（否则就是算法分叉，不是精度问题）；
    /// 且 `Evaluate` 在断点处**恰等于 `r²`**。
    ///
    /// Two paired points: `Evaluate(FltX)` and `Evaluate(double)` agree **within 1e-12** at the same point, otherwise the algorithms have diverged rather than merely differing in precision; and
    /// `Evaluate` at a breakpoint **equals `r²` exactly**.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldMatchTheDoubleVersionAndBeExactAtBreakpoints() {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 4, BreakpointStrategy: PwlBreakpointStrategy.Uniform));

        foreach (double sample in new[] { 1.0, 1.5, 2.0, 2.7, 3.0, 4.4, 5.0 }) {
            var radius = new FltX(sample);
            D(approximation.Evaluate(radius)).Should().BeApproximately(approximation.Evaluate(sample), 1e-12);
        }

        foreach (double breakpoint in approximation.Breakpoints) {
            var radius = new FltX(breakpoint);
            approximation.Evaluate(radius).Should().Be(radius * radius,
                "the chord passes exactly through r squared at a breakpoint");
        }
    }
}
