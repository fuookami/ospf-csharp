// 换型时间取值的判据 / Criteria for the switch time value.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **遮蔽不是钳制：mask 为 0 恒零，mask 为 1 连负时距一起传出** / Masking is not clamping: a zero mask gives zero, a mask of 1 passes even a negative gap.
/// </summary>
/// <remarks>
/// `frontOf[t1, t2]` 是 `start[t1] ≤ start[t2]`，推不出 `end[t1] ≤ start[t2]`——
/// 两个任务重叠时，时距是负的，而遮蔽会把它原样传出。
///
/// The code's `frontOf[t1, t2]` is `start[t1] ≤ start[t2]`, which does not imply `end[t1] ≤ start[t2]` — when two tasks overlap the gap is negative, and masking passes it
/// through unchanged.
/// </remarks>
public class SwitchTimeValueTests {
    /// **不重叠时时距为正** / A positive gap when they do not overlap.
    [Fact]
    public void Gap_ShouldBePositiveWhenTheyDoNotOverlap() =>
        SwitchTimeValue.Gap(endOfLeft: 4.0, startOfRight: 7.0).Should().BeApproximately(3.0, 1e-12);

    /// <summary>
    /// **重叠时时距为负，且不钳制** / An overlapping pair yields a negative gap, unclamped.
    /// </summary>
    /// <remarks>
    /// 这条钉住"照做"这个决定：钳到 0 会让这里返回 0。钳制看起来更"安全"，
    /// 但它与基线不同，而这个差异只在重叠排布上显现——正常模型里时间冲突约束已经排除了重叠。
    /// 记下这个 ceiling 比悄悄改掉更有价值。
    ///
    /// This pins the "copy the code" decision: clamping would return 0 here. Clamping looks "safer" but differs from the code, and the difference only shows on
    /// overlapping arrangements — which the time-conflict constraints already exclude in a full model. Recording the ceiling is worth more than quietly changing it.
    /// </remarks>
    [Fact]
    public void Gap_ShouldBeNegativeAndUnclampedWhenTheyOverlap() =>
        SwitchTimeValue.Gap(endOfLeft: 10.0, startOfRight: 5.0)
            .Should().BeApproximately(-5.0, 1e-12, "the baseline does not clamp");

    /// **首尾相接时时距为零** / A touching pair yields zero.
    [Fact]
    public void Gap_ShouldBeZeroWhenTheyTouch() =>
        SwitchTimeValue.Gap(endOfLeft: 6.0, startOfRight: 6.0).Should().BeApproximately(0.0, 1e-12);

    /// **遮蔽量为零时无论时距正负都为零** / A zero mask yields zero for either sign.
    [Fact]
    public void Masked_ShouldBeZeroWithoutASwitch() {
        SwitchTimeValue.Masked(gap: 3.0, mask: 0).Should().BeApproximately(0.0, 1e-12);
        SwitchTimeValue.Masked(gap: -5.0, mask: 0)
            .Should().BeApproximately(0.0, 1e-12, "a negative gap must not leak through a zero mask");
    }

    /// **遮蔽量为一时时距原样传出** / A mask of one passes the gap unchanged.
    [Fact]
    public void Masked_ShouldPassTheGapThroughWithASwitch() {
        SwitchTimeValue.Masked(gap: 3.0, mask: 1).Should().BeApproximately(3.0, 1e-12);
        SwitchTimeValue.Masked(gap: -5.0, mask: 1)
            .Should().BeApproximately(-5.0, 1e-12, "masking does not clamp the sign");
    }

    /// <summary>
    /// **一对任务不可能在两个执行器上同时换型** / A task pair cannot switch on two executors at once.
    /// </summary>
    /// <remarks>
    /// `thisSwitch ≤ 1` 上界约束的就是这条。判据把 0、1、2 都过一遍：只有 2 及以算越界。
    /// 若实现写成 `>= 1`，一对正常相邻的任务就会被判成越界。
    ///
    /// The code's `thisSwitch ≤ 1` bound enforces exactly this. The criterion covers 0, 1 and 2: only 2 and above exceed it. An implementation written as `>= 1` would
    /// flag a normal adjacent pair as a violation.
    /// </remarks>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ExceedsBound_ShouldTriggerOnlyAboveOne(int occurrences, bool expected) =>
        SwitchTimeValue.ExceedsBound(occurrences).Should().Be(expected);
}
