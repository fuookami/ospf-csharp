// 跨轮冲突约束的判据 / Criteria for cross-round conflict constraints.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **`task1 != task2` 不能省：省掉就变成 `2x ≤ 1`，每一列被压成 0** / `task1 != task2` cannot be dropped: without it the row becomes `2x ≤ 1` and every column is forced to 0.
/// </summary>
/// <remarks>
/// 当 `otherIteration` 就是本轮、`task2` 就是 `task1` 自己时，两边是**同一个变量**。
/// 约束退化成 `2x ≤ 1`，二值列于是只能取 0——**排程直接无解，或者解里什么任务都没做**。
///
/// When `otherIteration` is the current round and `task2` is `task1` itself, both sides are **the same variable**. The row becomes `2x ≤ 1`, so a binary column can only be 0 —
/// **the schedule is infeasible, or feasible with nothing done**.
/// </remarks>
public class CrossRoundConflictTests {
    /// **同一任务不生成约束** / No constraint for the same task.
    [Fact]
    public void ShouldAdd_ShouldBeFalseForTheSameTask() =>
        CrossRoundConflict.ShouldAdd(sameTask: true, conflict: true).Should().BeFalse(
            "pairing a column with itself gives 2x ≤ 1, which forces x = 0");

    /// **不冲突时不生成** / Nothing is emitted when there is no conflict.
    [Fact]
    public void ShouldAdd_ShouldBeFalseWithoutAConflict() =>
        CrossRoundConflict.ShouldAdd(sameTask: false, conflict: false).Should().BeFalse();

    /// **不同任务且冲突时生成** / Emitted for distinct tasks that conflict.
    [Fact]
    public void ShouldAdd_ShouldBeTrueForDistinctConflictingTasks() =>
        CrossRoundConflict.ShouldAdd(sameTask: false, conflict: true).Should().BeTrue();

    /// <summary>
    /// **把自组合那一项算一遍：`2x ≤ 1` 只剩 `x = 0`** / Working out the self-pairing case: `2x ≤ 1` leaves only `x = 0`.
    /// </summary>
    /// <remarks>
    /// 这条不是重复断言，而是把"为什么不能省那个判断"算给读者看：
    /// 右端是 `1`，同一变量出现两次，系数和是 `2`，所以 `0` 是唯一可行的二值取值。
    ///
    /// This is not a duplicate assertion but a worked example of "why that test cannot be dropped": the right-hand side is `1`, the same variable appears twice for a total
    /// coefficient of `2`, so `0` is the only feasible binary value.
    /// </remarks>
    [Fact]
    public void RightHandSide_ShouldMakeSelfPairingInfeasible() {
        CrossRoundConflict.RightHandSide.Should().BeApproximately(1.0, 1e-12);

        double coefficientSumWhenPairingWithSelf = 2.0;
        (coefficientSumWhenPairingWithSelf * 1.0 <= CrossRoundConflict.RightHandSide)
            .Should().BeFalse("x = 1 violates 2x ≤ 1, so the column can never be selected");
        (coefficientSumWhenPairingWithSelf * 0.0 <= CrossRoundConflict.RightHandSide)
            .Should().BeTrue("x = 0 is the only feasible value");
    }

    /// <summary>
    /// **右端是 1，不是 2** / The right-hand side is 1, not 2.
    /// </summary>
    /// <remarks>
    /// 写成 `≤ 2` 会让 `xi + x ≤ 2` 对两个二值列**恒真**——约束看着在，实际什么都没排除。
    /// 判据必须能分辨这两个常数：`1` 时两列不能同时为 1，`2` 时可以。
    ///
    /// Writing `≤ 2` makes `xi + x ≤ 2` **always true** for two binary columns — the row looks present while excluding nothing. The criterion must tell the two constants apart:
    /// with `1` they cannot both be 1, with `2` they can.
    /// </remarks>
    [Fact]
    public void RightHandSide_ShouldActuallyExclude() {
        double bothSelected = 2.0;

        (bothSelected <= CrossRoundConflict.RightHandSide).Should().BeFalse("both columns cannot be 1");
        (bothSelected <= 2.0).Should().BeTrue("with ≤ 2 the row would be vacuous");
    }
}
