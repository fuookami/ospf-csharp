// 任务取消代价与准时奖励的判据 / Criteria for cancellation cost and on-time reward.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **最小化罚超出、最大化奖到顶；阈值零在两者里含义相反** / Minimisation penalises the excess, maximisation rewards up to the cap, and a zero threshold means opposite things in each.
/// </summary>
/// <remarks>
/// 把两者写成同一个形态是个安静的错：最大化里用 `max(0, x − cap)` 会**奖励超量完成**，
/// 最小化里用 `min(x, bar)` 会**在阈值之外停止惩罚**。两种都数值算得出来、看着合理、结果偏掉。
///
/// Using one form for both is a quiet error: `max(0, x − cap)` in a maximisation **rewards over-delivery**, and `min(x, bar)` in a minimisation **stops penalising beyond the
/// bar**. Both compute, both look plausible, both are wrong.
/// </remarks>
public class TaskCancelAndOnTimeTests {
    /// **提不出取消系数返回失败，不取 1** / A missing cancellation coefficient fails instead of defaulting to 1.
    [Fact]
    public void CancelCost_ShouldFailWhenTheCoefficientCannotBeExtracted() {
        TaskCancelCost.Cost(null)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
                "treating an unpriced cancellation as a penalty of 1 lets the solver cancel a forbidden task cheaply");
    }

    /// **取消系数原样返回** / A supplied coefficient is returned unchanged.
    [Fact]
    public void CancelCost_ShouldReturnTheCoefficientUnchanged() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = TaskCancelCost.Cost(new Flt64(4.0));

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value.ToDouble().Should().BeApproximately(4.0, 1e-12);
    }

    /// **全局开关关闭时不选任何任务** / Nothing is selected with the global switch off.
    [Fact]
    public void CancelSelect_ShouldReturnNothingWhenTheGlobalSwitchIsOff() =>
        TaskCancelCost.Select(false, new List<string> { "A", "B" }, _ => true).Should().BeEmpty();

    /// **不允许取消的任务被排除** / Tasks that forbid cancellation are excluded.
    [Fact]
    public void CancelSelect_ShouldExcludeLockedTasks() {
        IReadOnlyList<string> selected = TaskCancelCost.Select(
            true, new List<string> { "free", "locked" }, task => task != "locked");

        selected.Should().ContainSingle().Which.Should().Be("free");
    }

    /// <summary>
    /// **准时奖励封顶到阈值，超出部分不再加分** / The on-time reward is capped at the threshold and over-delivery earns nothing extra.
    /// </summary>
    /// <remarks>
    /// 判据同时覆盖两侧：`onTime = 3`、`cap = 5` 得 `3×2`；`onTime = 9`、`cap = 5` 得 `5×2`。
    /// 只测"低于阈值"的话，一个**不封顶**的实现完全通过——而超额加分正是最容易被写错的那一半。
    ///
    /// The criterion covers both sides: `onTime = 3, cap = 5` gives `3×2`; `onTime = 9, cap = 5` gives `5×2`. Testing only the below-threshold side would let an **uncapped**
    /// implementation pass entirely — and over-delivery earning extra is exactly the half that gets written wrong.
    /// </remarks>
    [Fact]
    public void OnTimeReward_ShouldCapAtTheThreshold() {
        TaskOnTimeReward.Reward(new Flt64(3.0), threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(6.0, 1e-12, "below the cap the full on-time counts");
        TaskOnTimeReward.Reward(new Flt64(9.0), threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(10.0, 1e-12, "beyond the cap only the cap counts");
    }

    /// <summary>
    /// **阈值零表示不封顶，不是"不给奖励"** / A zero threshold means uncapped, not "no reward".
    /// </summary>
    /// <remarks>
    /// 读成"奖励为 0"会让这条管线在默认参数下**什么都不做**，而调用方以为准时还在被最大化——
    /// 目标是空的，求解器自然给出任意解。判据因此断言阈值为零时奖励**随准时量增长**。
    ///
    /// Reading it as "reward is zero" makes this pipeline **do nothing** at its default arguments while the caller believes on-time is still maximised — the objective is empty
    /// and the solver returns an arbitrary solution. The criterion therefore asserts the reward **grows with the on-time amount** at a zero threshold.
    /// </remarks>
    [Fact]
    public void OnTimeReward_ShouldBeUncappedAtAZeroThreshold() {
        TaskOnTimeReward.Reward(new Flt64(9.0), threshold: 0, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(18.0, 1e-12, "zero means uncapped, not zero reward");
        TaskOnTimeReward.Reward(new Flt64(9.0), threshold: 0, new Flt64(2.0))
            .Should().BeGreaterThan(TaskOnTimeReward.Reward(new Flt64(3.0), threshold: 0, new Flt64(2.0)));
    }

    /// **封顶只在阈值非零时才需要** / A cap is needed only with a non-zero threshold.
    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, true)]
    [InlineData(5UL, true)]
    public void OnTimeReward_ShouldNeedACapOnlyAboveZero(ulong threshold, bool expected) =>
        TaskOnTimeReward.NeedsCap(threshold).Should().Be(expected);

    /// **准时为零时奖励为零** / Zero on-time earns nothing.
    [Fact]
    public void OnTimeReward_ShouldBeZeroForZeroOnTime() {
        TaskOnTimeReward.Reward(Flt64.Zero, threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(0.0, 1e-12);
    }
}
