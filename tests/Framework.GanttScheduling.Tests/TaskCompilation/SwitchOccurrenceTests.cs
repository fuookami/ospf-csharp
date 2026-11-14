// 换型发生条件的判据 / Criteria for when a switch occurs.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **换型是"相邻"，不是"任意先后"；两支的严格性不同** / A switch is "adjacent", not merely ordered; and the two branches differ in strictness.
/// </summary>
/// <remarks>
/// 漏掉"中间不能夹着别的任务"不是"少算一点"，而是**同一个换型被数很多遍**：
/// 执行器上排了 `A → B → C`，若只看"指派了 + 先后"，`A→C` 也会被当成一次换型，
/// 换型时间被计入四次，而排程本身完全正常。
///
/// Dropping "nothing may sit in between" does not merely under-count — it **counts the same switch many times**: with `A → B → C` on one executor, "assigned and ordered"
/// alone treats `A→C` as a switch too, charging switch time four times over while the schedule looks perfectly normal.
/// </remarks>
public class SwitchOccurrenceTests {
    /// **可移分支：条件全满足才发生** / Movable branch: all conditions must hold.
    [Fact]
    public void Flexible_ShouldHoldWhenEveryConditionHolds() =>
        SwitchOccurrence.Flexible(
            sameTask: false, bothAssigned: true, frontOf: true, new List<bool> { false, false })
            .Should().BeTrue();

    /// <summary>
    /// **可移分支：任意一个第三个任务居中就不发生** / Movable branch: any third task in between blocks the switch.
    /// </summary>
    /// <remarks>
    /// 判据用"两个第三个任务，只有**第二个**居中"的输入——只查第一个的实现会漏掉。
    /// 这是换型时间被重复计入的直接来源。
    ///
    /// The criterion feeds two third tasks with **only the second** in between: an implementation checking only the first misses it. That is the direct source of switch time
    /// being charged repeatedly.
    /// </remarks>
    [Fact]
    public void Flexible_ShouldFailWhenAnyThirdTaskSitsInBetween() {
        SwitchOccurrence.Flexible(
                sameTask: false, bothAssigned: true, frontOf: true, new List<bool> { false, true })
            .Should().BeFalse("a task in the middle means this pair is not adjacent");
        SwitchOccurrence.Flexible(
                sameTask: false, bothAssigned: true, frontOf: true, new List<bool> { true, false })
            .Should().BeFalse();
    }

    /// **可移分支：对角线、未指派、不在前都不发生** / Movable branch: the diagonal, missing assignment and non-precedence all block.
    [Fact]
    public void Flexible_ShouldFailOnEachMissingCondition() {
        SwitchOccurrence.Flexible(true, true, true, new List<bool>()).Should().BeFalse("a task does not switch to itself");
        SwitchOccurrence.Flexible(false, false, true, new List<bool>()).Should().BeFalse("both must be on the executor");
        SwitchOccurrence.Flexible(false, true, false, new List<bool>()).Should().BeFalse("the order must hold");
    }

    /// **已定分支：第三个任务严格居中才阻断** / Fixed branch: only a third start strictly between blocks.
    [Fact]
    public void Fixed_ShouldFailWhenAThirdStartIsStrictlyBetween() {
        SwitchOccurrence.Fixed(false, true, leftStart: 0.0, rightStart: 10.0, new List<double> { 5.0 })
            .Should().BeFalse();
    }

    /// <summary>
    /// **已定分支：第三个任务贴着端点不算居中** / Fixed branch: a third task touching an endpoint is not in between.
    /// </summary>
    /// <remarks>
    /// `5` 与左端同时开始、`10` 与右端同时开始，都**不**严格落在开区间内。
    /// 若实现写成闭区间，`A → B` 会被误判为"中间有活"，于是**真实的换型消失**——排程里换型时间凭空变少。
    ///
    /// `5` starting with the left end and `10` starting with the right end are **not** strictly inside the open interval. Writing a closed interval would misjudge `A → B` as
    /// "something in between", so **a real switch vanishes** and switch time shrinks out of nowhere.
    /// </remarks>
    [Fact]
    public void Fixed_ShouldIgnoreThirdStartsTouchingTheEndpoints() {
        SwitchOccurrence.Fixed(false, true, leftStart: 5.0, rightStart: 10.0, new List<double> { 5.0, 10.0 })
            .Should().BeTrue("touching an endpoint is not strictly between");
    }

    /// <summary>
    /// **已定分支：同时开始不算换型** / Fixed branch: equal starts are not a switch.
    /// </summary>
    /// <remarks>
    /// 这一支用 `<` 严格不等，而**可移分支的 `frontOf` 用 `≤`**。同一个"同时开始"的排布在两支里结论相反，
    /// 这是基线的原样。判据两条都钉住，否则把两支的严格性搞混也测不出来。
    ///
    /// This branch uses strict `&lt;`, while **the movable branch's `frontOf` uses `≤`**. The same "starts together" arrangement gives opposite answers in the two branches, and
    /// that is the baseline verbatim. Both are pinned, otherwise swapping the two strictnesses would go unnoticed.
    /// </remarks>
    [Fact]
    public void Fixed_ShouldNotSwitchOnEqualStarts() {
        SwitchOccurrence.Fixed(false, true, leftStart: 5.0, rightStart: 5.0, new List<double>())
            .Should().BeFalse("finishing one and starting the next needs a strict order");
        SwitchOccurrence.Flexible(false, true, frontOf: true, new List<bool>())
            .Should().BeTrue("the movable branch tests ≤, so equal starts do qualify");
    }

    /// **已定分支：对角线不换型** / Fixed branch: the diagonal is not a switch.
    [Fact]
    public void Fixed_ShouldFailOnTheDiagonal() {
        SwitchOccurrence.Fixed(true, true, leftStart: 0.0, rightStart: 10.0, new List<double>())
            .Should().BeFalse();
    }
}
