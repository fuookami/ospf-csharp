// 迭代列剪枝的判据 / Criteria for iterative column pruning.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **约简成本恰好等于阈值时该列被剔除；固定与保留的列优先** / A column exactly at the threshold is dropped, and fixed or kept columns take precedence.
/// </summary>
/// <remarks>
/// 代码写的是 `!(reducedCost(task) ls maximumReducedCost)`，即 `>=` 时**剔除**。一个"自然"的实现写成 `>`，
/// 两者只在**恰好等于**时不同。恰好等于的列正是"刚刚够格"的列：写错时它被留在列池里，
/// 每轮多扫一遍，而**它永远不会变成有价值的列**。
///
/// The code writes `!(reducedCost(task) ls maximumReducedCost)`, i.e. drop at `>=`. A "natural" implementation writes `>`, and the two differ only at **exactly equal**.
/// A column exactly at the boundary is the one that just barely qualifies: with the wrong comparison it stays in the pool, gets rescanned every round, and **never becomes a
/// column worth having**.
/// </remarks>
public class IterativeColumnPruningTests {
    /// **恰好等于阈值时剔除** / Exactly at the threshold it is dropped.
    [Fact]
    public void ShouldRemove_ShouldDropAtExactlyTheThreshold() {
        IterativeColumnPruning.ShouldRemove(
                new Flt64(8.0), new Flt64(8.0), isFixed: false, isKept: false)
            .Should().BeTrue("the baseline comparison is >=, not >");
    }

    /// **低于阈值时保留** / Below the threshold it is kept.
    [Fact]
    public void ShouldRemove_ShouldKeepBelowTheThreshold() {
        IterativeColumnPruning.ShouldRemove(
                new Flt64(7.5), new Flt64(8.0), isFixed: false, isKept: false)
            .Should().BeFalse("a column whose reduced cost is still below the bar may yet become valuable");
    }

    /// **高于阈值时剔除** / Above the threshold it is dropped.
    [Fact]
    public void ShouldRemove_ShouldDropAboveTheThreshold() {
        IterativeColumnPruning.ShouldRemove(
                new Flt64(9.0), new Flt64(8.0), isFixed: false, isKept: false)
            .Should().BeTrue();
    }

    /// <summary>
    /// **固定与保留的列即便超阈值也保留** / Fixed and kept columns survive even above the threshold.
    /// </summary>
    /// <remarks>
    /// 它们可能因模型结构原因暂时没有约简成本，但下一轮还要用。剔除它们会**丢掉已经确定要保留的决定**，
    /// 而下一轮只能重新发现同一个决定。
    ///
    /// They may temporarily show no reduced cost for structural reasons but are needed next round. Dropping them **discards a decision already made**, only to rediscover it
    /// next round.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldRemove_ShouldKeepFixedAndKeptColumns(bool isFixed, bool isKept) {
        IterativeColumnPruning.ShouldRemove(
                new Flt64(100.0), new Flt64(1.0), isFixed, isKept)
            .Should().BeFalse();
    }

    /// **列数超上限时衰减** / The cutoff decays while the column count is over the ceiling.
    [Fact]
    public void NextMaximumReducedCost_ShouldDecayWhileOverTheCeiling() {
        IterativeColumnPruning.NextMaximumReducedCost(remainingAmount: 11, maximumColumnAmount: 10UL, new Flt64(10.9))
            .ToDouble().Should().BeApproximately(6.0, 1e-12, "floor(10.9)=10, then 20/3=6");
    }

    /// <summary>
    /// **列数不超上限时原样返回** / The cutoff is returned unchanged while at or under the ceiling.
    /// </summary>
    /// <remarks>
    /// 判据必须同时覆盖"恰好等于上限"：一个写成 `>=` 的实现会在等于时也衰减，
    /// 于是列池刚好装满的那一轮被无谓地削一刀，下一轮能拿到的候选更少。
    ///
    /// The criterion must also cover "exactly at the ceiling": an implementation written with `>=` decays there too, so the round where the pool is exactly full gets trimmed
    /// for nothing and the next round has fewer candidates to work with.
    /// </remarks>
    [Theory]
    [InlineData(9, 10UL)]
    [InlineData(10, 10UL)]
    public void NextMaximumReducedCost_ShouldLeaveTheCutoffAloneAtOrUnderTheCeiling(int remaining, ulong ceiling) {
        IterativeColumnPruning.NextMaximumReducedCost(remaining, ceiling, new Flt64(10.9))
            .ToDouble().Should().BeApproximately(10.9, 1e-12);
    }

    /// **负剩余列数是缺陷而不是零** / A negative remaining count is a defect, not zero.
    [Fact]
    public void NextMaximumReducedCost_ShouldRejectANegativeRemainingCount() {
        Action act = () => IterativeColumnPruning.NextMaximumReducedCost(-1, 10UL, new Flt64(10.9));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
