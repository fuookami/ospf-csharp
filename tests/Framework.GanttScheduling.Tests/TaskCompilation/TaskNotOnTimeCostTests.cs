// 不准时代价的判据 / Criteria for the not-on-time cost.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **阈值零在两侧都是"不封顶"，但形态相反：一个奖到顶，一个只罚超出** / A zero threshold means "uncapped" on both sides, but the forms are opposite: one rewards up to the cap, the other charges only the excess.
/// </summary>
/// <remarks>
/// 阈值都是 5、系数都是 1、`x` 都是 9 时，准时得 **5**、不准时得 **4**。
/// 这个差就是判据的分辨力：若有人把一条写成另一条的形态，两者的答案会互换。
///
/// With threshold 5, coefficient 1 and `x = 9`, on-time gives **5** and not-on-time gives **4**. That difference is the discriminating power: writing one in the other's form swaps
/// the answers.
/// </remarks>
public class TaskNotOnTimeCostTests {
    /// **阈值处不罚** / Nothing is charged at the threshold.
    [Fact]
    public void Cost_ShouldBeZeroAtTheThreshold() =>
        TaskNotOnTimeCost.Cost(new Flt64(5.0), threshold: 5, new Flt64(1.0)).ToDouble()
            .Should().BeApproximately(0.0, 1e-12);

    /// **低于阈值不罚** / Nothing is charged below the threshold.
    [Fact]
    public void Cost_ShouldBeZeroBelowTheThreshold() =>
        TaskNotOnTimeCost.Cost(new Flt64(2.0), threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(0.0, 1e-12);

    /// **只罚超出阈值的部分** / Only the excess over the threshold is charged.
    [Fact]
    public void Cost_ShouldChargeOnlyTheExcess() =>
        TaskNotOnTimeCost.Cost(new Flt64(9.0), threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(8.0, 1e-12, "(9 − 5) × 2, not 9 × 2");

    /// <summary>
    /// **阈值零表示全额计罚，不是不罚** / A zero threshold charges everything, not nothing.
    /// </summary>
    /// <remarks>
    /// 读成"阈值为零就不罚"会让默认参数下的这条管线**完全不约束滞后**——目标是空的，求解器随便排，
    /// 而调用方以为准时时长在被最小化。
    ///
    /// Reading it as "no penalty at a zero threshold" makes this pipeline **constrain no lateness at all** at its default arguments — the objective is empty, the solver
    /// schedules arbitrarily, and the caller believes lateness is being minimised.
    /// </remarks>
    [Fact]
    public void Cost_ShouldChargeEverythingAtAZeroThreshold() =>
        TaskNotOnTimeCost.Cost(new Flt64(9.0), threshold: 0, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(18.0, 1e-12, "zero means uncapped, not free");

    /// <summary>
    /// **与准时奖励同输入不同答案** / Same inputs, different answers from the on-time reward.
    /// </summary>
    /// <remarks>
    /// 这是这里最重要的一条：它把"罚超出"与"奖到顶"钉成**两个不同的函数**。
    /// 若两条管线写成了同一个形态，这里只会出现一个数字。
    ///
    /// This is the batch's most important criterion: it pins "charge the excess" and "reward up to the cap" as **two different functions**. If both pipelines shared one form,
    /// only one number would ever appear here.
    /// </remarks>
    [Fact]
    public void Cost_ShouldDifferFromTheOnTimeReward() {
        Flt64 notOnTime = TaskNotOnTimeCost.Cost(new Flt64(9.0), threshold: 5, new Flt64(1.0));
        Flt64 onTime = TaskOnTimeReward.Reward(new Flt64(9.0), threshold: 5, new Flt64(1.0));

        notOnTime.ToDouble().Should().BeApproximately(4.0, 1e-12, "the excess");
        onTime.ToDouble().Should().BeApproximately(5.0, 1e-12, "the cap");
        notOnTime.ToDouble().Should().NotBeApproximately(onTime.ToDouble(), 1e-12,
            "the two forms must not collapse into one");
    }

    /// **阈值零时两侧仍然不同** / The two still differ at a zero threshold.
    [Fact]
    public void Cost_ShouldDifferFromTheOnTimeRewardAtAZeroThreshold() {
        TaskNotOnTimeCost.Cost(new Flt64(9.0), 0, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(18.0, 1e-12);
        TaskOnTimeReward.Reward(new Flt64(9.0), threshold: 0, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(18.0, 1e-12,
                "at a zero threshold both are plain linear, which is exactly the shared convention");
    }

    /// **只有阈值非零时才罚超出** / Only a non-zero threshold charges just the excess.
    [Theory]
    [InlineData(0UL, false)]
    [InlineData(1UL, true)]
    [InlineData(5UL, true)]
    public void ChargesOnlyExcess_ShouldFollowTheThreshold(ulong threshold, bool expected) =>
        TaskNotOnTimeCost.ChargesOnlyExcess(threshold).Should().Be(expected);

    /// **负值在零阈值下线性计罚** / A negative value is charged linearly at a zero threshold.
    [Fact]
    public void Cost_ShouldHandleZeroNotOnTime() =>
        TaskNotOnTimeCost.Cost(Flt64.Zero, threshold: 5, new Flt64(2.0)).ToDouble()
            .Should().BeApproximately(0.0, 1e-12);
}
