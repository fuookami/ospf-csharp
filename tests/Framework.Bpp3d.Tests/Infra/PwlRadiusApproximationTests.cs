// PWL 半径平方近似的判据 / Criteria for the piecewise-linear r² approximation.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **弦必须是 r² 的弦，断点必须真盖住区间** / The chord must be r²'s chord, and breakpoints must actually cover the interval.
/// </summary>
/// <remarks>
/// `PWLRadiusApproximation.cs` 早就存在，这里做的是**补上它缺的断点校验**并给既有行为立判据。
/// 之所以值得写判据：自定义断点不校验时会被静默接受，然后给出一个"看起来正常"但误差远超标称的近似。
///
/// `PWLRadiusApproximation.cs` already existed; **adds the missing breakpoint validation** and pins the existing behaviour. Criteria are worth writing
/// because unvalidated custom breakpoints are accepted silently and then produce an approximation that looks normal while its error is far beyond what was claimed.
/// </remarks>
public class PwlRadiusApproximationTests {
    /// **均匀断点覆盖区间，弦在断点处精确** / Uniform breakpoints cover the interval and the chord is exact at the breakpoints.
    [Fact]
    public void FromRadiusInterval_ShouldInterpolateExactlyAtBreakpoints() {
        var pwl = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(MaxSegments: 4, BreakpointStrategy: PwlBreakpointStrategy.Uniform));

        pwl.SegmentCount.Should().Be(4);
        pwl.Breakpoints[0].Should().BeApproximately(1.0, 1e-12);
        pwl.Breakpoints[pwl.Breakpoints.Count - 1].Should().BeApproximately(5.0, 1e-12);
        foreach (double breakpoint in pwl.Breakpoints) {
            pwl.Evaluate(breakpoint).Should().BeApproximately(breakpoint * breakpoint, 1e-12,
                "the piecewise line is built as the chord of r², so it meets r² at every breakpoint");
        }
    }

    /// <summary>
    /// **弦在段内是上界（保守方向）** / Inside a segment the chord is an upper bound, which is the safe direction.
    /// </summary>
    /// <remarks>
    /// 凸函数的弦在图上**方**：`r0=1, r1=2` 时中点的弦值是 `(1+2)·1.5-2 = 2.5`，而 `r² = 2.25`。
    /// 第一版判据写成了"弦在下界"，那是凹函数的性质——**方向搞反会让判据自己变成错的**，
    /// 所以这里既断言方向，也断言方向恰好是保守的：占位估计偏大，箱子不会被判断成过满。
    ///
    /// The chord of a convex function lies **above** the graph: with `r0=1, r1=2` the midpoint chord is `(1+2)·1.5-2 = 2.5` while `r² = 2.25`. The first version of this
    /// criterion claimed the chord lies below, which is the concave property — **getting the direction wrong makes the criterion itself wrong**, so both the direction and
    /// its conservativeness are asserted here: the footprint is over-estimated, so a bin is never declared fuller than it is.
    /// </remarks>
    [Fact]
    public void FromRadiusInterval_ShouldOverEstimateInsideSegments() {
        var pwl = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(MaxSegments: 4));

        double midpoint = (pwl.Breakpoints[0] + pwl.Breakpoints[1]) / 2.0;
        double approximate = pwl.Evaluate(midpoint);

        approximate.Should().BeGreaterThan(midpoint * midpoint,
            "the chord of a convex function lies above it, which over-estimates the footprint");
        pwl.ActualError(midpoint).Should().BeGreaterThan(0.0);
        pwl.MaxRelativeError.Should().BeGreaterThan(0.0);
        pwl.MaxAbsoluteError.Should().BeGreaterThan(0.0);
    }

    /// <summary>
    /// **自定义断点没盖住区间必须被拒** / Breakpoints that miss the interval must be rejected.
    /// </summary>
    /// <remarks>
    /// 这条正是这里补的能力。校验缺失时，`[2, 3]` 这样只盖住区间中段的断点会被接受，
    /// 于是 `[rMin, 2)` 与 `(3, rMax]` 全靠外推，而外推的误差**不在** `MaxRelativeError` 里体现。
    ///
    /// This is the capability added here. Without validation, breakpoints covering only the middle of the interval — `[2, 3]` — are accepted, so `[rMin, 2)` and
    /// `(3, rMax]` rely entirely on extrapolation, whose error is **not** reflected in `MaxRelativeError`.
    /// </remarks>
    [Fact]
    public void FromRadiusInterval_ShouldRejectBreakpointsThatStartAboveRMin() {
        // 只违反下界：起点 4 > 1，终点 10 恰好盖住 rMax。只查上界的实现会放过它。
        // Violates only the lower bound: starts at 4 > 1 while ending exactly at rMax. An implementation that only checks the upper end lets it through.
        Action startsTooHigh = () => PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 10.0,
            config: new PwlRadiusApproximationConfig(CustomBreakpoints: new List<double> { 4.0, 6.0, 10.0 }));

        startsTooHigh.Should().Throw<ArgumentException>(
            "radii in [rMin, 4) would rely on extrapolation the reported error never measures");
    }

    /// **自定义断点没盖住区间上端必须被拒** / Breakpoints that stop short of the upper end must be rejected.
    /// <remarks>
    /// 与上一条**分开写**：一个"两侧都不够"的输入会同时触发两条检查，去掉任意一条它仍被拒——
    /// 那样的判据分不出是哪条检查在起作用。
    ///
    /// Kept **separate** from the previous case: an input failing on both ends trips both checks, so removing either one still gets it rejected and the criterion cannot
    /// tell which check did the work.
    /// </remarks>
    [Fact]
    public void FromRadiusInterval_ShouldRejectBreakpointsThatEndBelowRMax() {
        Action endsTooLow = () => PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 10.0,
            config: new PwlRadiusApproximationConfig(CustomBreakpoints: new List<double> { 1.0, 3.0, 6.0 }));

        endsTooLow.Should().Throw<ArgumentException>(
            "radii in (6, rMax] would rely on extrapolation the reported error never measures");
    }

    /// **断点不严格递增必须被拒** / Non-increasing breakpoints must be rejected.
    [Fact]
    public void FromRadiusInterval_ShouldRejectNonIncreasingBreakpoints() {
        Action repeated = () => PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(CustomBreakpoints: new List<double> { 1.0, 3.0, 3.0, 5.0 }));

        repeated.Should().Throw<ArgumentException>(
            "a zero-width segment divides by zero when the evaluator picks it");
    }

    /// **断点数超过 maxSegments 必须被拒** / More breakpoints than `maxSegments` must be rejected.
    [Fact]
    public void FromRadiusInterval_ShouldRejectBreakpointsAboveTheSegmentCap() {
        Action tooMany = () => PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 5.0,
            config: new PwlRadiusApproximationConfig(
                MaxSegments: 2,
                CustomBreakpoints: new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 }));

        tooMany.Should().Throw<ArgumentException>("the caller's own segment cap must hold");
    }

    /// **段数推导达不到容限时如实报告** / The derivation reports honestly when the tolerance cannot be met.
    [Fact]
    public void DeriveSegmentCount_ShouldReportFailureInsteadOfRelaxingTheTolerance() {
        SegmentCountDerivation impossible = PwlRadiusSquaredApproximation.DeriveSegmentCount(
            rMin: 0.001, rMax: 1000.0, relativeErrorTolerance: 1e-12, maxSegments: 2);

        impossible.MeetsTolerance.Should().BeFalse("a two-segment fit cannot reach 1e-12 over six decades");
        impossible.RecommendedSegments.Should().Be(2, "the cap is reported, not exceeded silently");
        impossible.AchievedMaxRelativeError.Should().BeGreaterThan(1e-12);
    }

    /// **容限可达时给出满足容限的最小段数** / When reachable, the derivation yields the smallest passing segment count.
    [Fact]
    public void DeriveSegmentCount_ShouldReturnASegmentCountThatMeetsTheTolerance() {
        SegmentCountDerivation derivation = PwlRadiusSquaredApproximation.DeriveSegmentCount(
            rMin: 1.0, rMax: 2.0, relativeErrorTolerance: 0.01, maxSegments: 64);

        derivation.MeetsTolerance.Should().BeTrue();
        derivation.AchievedMaxRelativeError.Should().BeLessThanOrEqualTo(0.01);
        derivation.RecommendedSegments.Should().BeGreaterThan(0);
        derivation.Info().Should().Contain("MeetsTolerance=True");
    }
}
