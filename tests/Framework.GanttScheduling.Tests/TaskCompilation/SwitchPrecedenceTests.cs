// 先后指示量的判据 / Criteria for the precedence indicator.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **对角线恒零；同一时刻开始算"不晚于"** / The diagonal is identically zero, and equal starts count as "no later than".
/// </summary>
/// <remarks>
/// `frontOf` 是 `IfFunction` 判定 `estimateStartTime[t1] ≤ estimateStartTime[t2]`，
/// 但 `task1 == task2` 时直接给空符号（恒零）。这个例外不能省：`t ≤ t` 恒真，走正常构造会得到 `1`，
/// 于是**每个任务都声称排在自己前面**——多出来的关系不产生任何错误或警告，数值全都算得出来。
///
/// `frontOf` is an `IfFunction` testing `estimateStartTime[t1] ≤ estimateStartTime[t2]`, but `task1 == task2` yields an empty symbol (identically zero).
/// That exception cannot be dropped: `t ≤ t` is always true, so the normal construction gives `1` — **every task claiming to precede itself**, raising no error or warning
/// while every number still computes.
/// </remarks>
public class SwitchPrecedenceTests {
    /// <summary>
    /// **对角线恒为零，即使两侧开始时刻相同** / The diagonal is zero even when both starts are equal.
    /// </summary>
    /// <remarks>
    /// 判据用**两侧相同**的输入，因为只有这种输入才分得开"恒零"与"走正常判定"：
    /// 若实现漏掉对角线例外，`t ≤ t` 会给 `true`。
    ///
    /// The criterion feeds **identical** sides, because only that input separates "identically zero" from "the normal test": an implementation missing the exception
    /// returns `true` for `t ≤ t`.
    /// </remarks>
    [Fact]
    public void IsFrontOf_ShouldBeFalseOnTheDiagonal() {
        SwitchPrecedence.IsFrontOf(sameTask: true, leftStart: 5.0, rightStart: 5.0)
            .Should().BeFalse("a task never precedes itself");
        SwitchPrecedence.IsFrontOf(sameTask: true, leftStart: 1.0, rightStart: 9.0)
            .Should().BeFalse("the diagonal is forced to zero regardless of the starts");
    }

    /// **左开始更早时成立** / True when the left task starts earlier.
    [Fact]
    public void IsFrontOf_ShouldHoldWhenLeftStartsEarlier() =>
        SwitchPrecedence.IsFrontOf(sameTask: false, leftStart: 1.0, rightStart: 5.0).Should().BeTrue();

    /// **左开始更晚时不成立** / False when the left task starts later.
    [Fact]
    public void IsFrontOf_ShouldFailWhenLeftStartsLater() =>
        SwitchPrecedence.IsFrontOf(sameTask: false, leftStart: 5.0, rightStart: 1.0).Should().BeFalse();

    /// <summary>
    /// **同一时刻开始算成立** / Equal starts count as true.
    /// </summary>
    /// <remarks>
    /// `leq` 不是 `lt`。写成 `<` 会让**所有同时开始的先后关系全部消失**——
    /// 而"两个任务可以同时开始"在排班里非常常见。
    ///
 /// The implementation tests `leq`, not `lt`. Writing `&lt;` makes **every** same-instant relation vanish — and "two tasks may start together" is very common in scheduling.
    /// </remarks>
    [Fact]
    public void IsFrontOf_ShouldHoldOnEqualStarts() {
        SwitchPrecedence.IsFrontOf(sameTask: false, leftStart: 5.0, rightStart: 5.0)
            .Should().BeTrue("the baseline tests ≤, not <");
    }

    /// <summary>
    /// **两张表都含对角线，但只有对角线上是 `false`** / Both tables include the diagonal, and only the diagonal is false.
    /// </summary>
    /// <remarks>
    /// 判据同时钉住"数量是任务平方"与"对角线恰好 `false`"：一个跳过对角线的表数量会少 `任务数`，
    /// 一个不处理对角线的表会让对角线为 `true`。
    ///
    /// The criterion pins both "count is task squared" and "the diagonal is exactly false": a table skipping it has `task count` fewer entries, and a table not handling it has
    /// true on the diagonal.
    /// </remarks>
    [Fact]
    public void Indicators_ShouldIncludeTheDiagonalAsFalse() {
        var tasks = new List<string> { "A", "B", "C" };
        var starts = new Dictionary<string, double> { ["A"] = 0.0, ["B"] = 4.0, ["C"] = 8.0 };

        IReadOnlyDictionary<(string Left, string Right), bool> table =
            SwitchPrecedence.Indicators(tasks, task => starts[task]);

        table.Should().HaveCount(9, "3 × 3");
        table[("A", "A")].Should().BeFalse();
        table[("B", "B")].Should().BeFalse();
        table[("C", "C")].Should().BeFalse();
        table[("A", "C")].Should().BeTrue("A starts first");
        table[("C", "A")].Should().BeFalse("C starts last");
    }

    /// **同时开始的两个任务互相都成立** / Two tasks starting together satisfy the indicator both ways.
    [Fact]
    public void Indicators_ShouldBeSymmetricOnEqualStarts() {
        var tasks = new List<string> { "A", "B" };
        var starts = new Dictionary<string, double> { ["A"] = 7.0, ["B"] = 7.0 };

        IReadOnlyDictionary<(string Left, string Right), bool> table =
            SwitchPrecedence.Indicators(tasks, task => starts[task]);

        table[("A", "B")].Should().BeTrue();
        table[("B", "A")].Should().BeTrue();
        table[("A", "A")].Should().BeFalse("the diagonal stays zero even so");
    }
}
