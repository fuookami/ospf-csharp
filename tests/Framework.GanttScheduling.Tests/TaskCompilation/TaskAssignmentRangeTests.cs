// 分配变量值域收紧的判据 / Criteria for assignment range tightening.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **后写的规则覆盖先写的；已定执行器即使不可用也会被钉成必须** / The later rule overrides the earlier one, and a fixed executor is pinned to required even when it is not enabled.
/// </summary>
/// <remarks>
/// 写法是先按可用集合写 `0`，再按"执行器已定"写 `1`，**后写的生效**。
/// 若实现把两条当成交集处理，那个任务会**既被禁止又被要求**——模型直接不可解，而错误信息只会说"不可行"。
///
/// The code writes `0` from the enabled set first, then `1` for "executor is fixed", so **the later write wins**. Treating the two as an intersection leaves that task
/// **both forbidden and required** — the model becomes infeasible, and the only message is "infeasible".
/// </remarks>
public class TaskAssignmentRangeTests {
    /// **可用且无固定执行器时保持 0..1** / Free when the executor is enabled and nothing is fixed.
    [Fact]
    public void For_ShouldKeepItFreeWhenNothingIsFixed() {
        TaskAssignmentRange.For(
                executorEnabled: true, executorFixed: false, isTaskExecutor: false, cancelEnabled: false)
            .Should().Be(AssignmentRange.Free);
    }

    /// **不可用的执行器被禁止** / A disabled executor is forbidden.
    [Fact]
    public void For_ShouldForbidADisabledExecutor() {
        TaskAssignmentRange.For(
                executorEnabled: false, executorFixed: false, isTaskExecutor: false, cancelEnabled: false)
            .Should().Be(AssignmentRange.Forbidden);
    }

    /// **已定的执行器在任务不可取消时被钉成必须** / A fixed executor is pinned to required when the task cannot be cancelled.
    [Fact]
    public void For_ShouldRequireTheFixedExecutorWhenCancellingIsForbidden() {
        TaskAssignmentRange.For(
                executorEnabled: true, executorFixed: true, isTaskExecutor: true, cancelEnabled: false)
            .Should().Be(AssignmentRange.Required);
    }

    /// <summary>
    /// **已定的执行器允许取消时保持 0..1** / A fixed executor stays free when the task may be cancelled.
    /// </summary>
    /// <remarks>
    /// "必须用它"与"只能用它的取消"是两件事：允许取消时，选它就是执行、不选它就是取消。
    /// 把这种情况也钉成 `1` 会**禁止取消一个明确允许取消的任务**。
    ///
    /// "Must use it" and "may cancel it instead" are two different things: when cancelling is allowed, selecting it means executing and not selecting means cancelling. Pinning
    /// this case to `1` would **forbid cancelling a task that is explicitly cancellable**.
    /// </remarks>
    [Fact]
    public void For_ShouldKeepItFreeWhenCancellingIsAllowed() {
        TaskAssignmentRange.For(
                executorEnabled: true, executorFixed: true, isTaskExecutor: true, cancelEnabled: true)
            .Should().Be(AssignmentRange.Free);
    }

    /// **已定时其它执行器一律禁止** / Every other executor is forbidden once one is fixed.
    [Fact]
    public void For_ShouldForbidEveryOtherExecutorWhenFixed() {
        TaskAssignmentRange.For(
                executorEnabled: true, executorFixed: true, isTaskExecutor: false, cancelEnabled: false)
            .Should().Be(AssignmentRange.Forbidden);
        TaskAssignmentRange.For(
                executorEnabled: true, executorFixed: true, isTaskExecutor: false, cancelEnabled: true)
            .Should().Be(AssignmentRange.Forbidden);
    }

    /// <summary>
    /// **第 2 条覆盖第 1 条：已定但不可用的执行器最终仍被钉成必须** / Rule 2 overrides rule 1: a fixed executor that is not enabled still ends up required.
    /// </summary>
    /// <remarks>
    /// 这是基线最容易读错的一处。判据必须用"已定 + 不在可用集合 + 不允许取消"这种输入，
    /// 并把期望钉在**必须**——若实现取交集，这里会得到"禁止"，与"必须"正好相反。
    ///
    /// This is the easiest part of the code to misread. The fixture must use "fixed + not enabled + cannot cancel" and pin the expectation to **required** — an
    /// implementation taking an intersection yields "forbidden" here, the exact opposite.
    /// </remarks>
    [Fact]
    public void For_ShouldLetTheFixedRuleOverrideTheEnabledRule() {
        TaskAssignmentRange.For(
                executorEnabled: false, executorFixed: true, isTaskExecutor: true, cancelEnabled: false)
            .Should().Be(AssignmentRange.Required);
    }

    /// **固定值域不需要再搜索** / A fixed range needs no further search.
    [Theory]
    [InlineData(0.0, 0.0, false)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(0.0, 1.0, true)]
    public void NeedsSearch_ShouldOnlyBeTrueForTheFreeRange(double low, double high, bool expected) =>
        TaskAssignmentRange.NeedsSearch(new AssignmentRange(low, high)).Should().Be(expected);
}
