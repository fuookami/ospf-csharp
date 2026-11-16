// 列回收的判据 / Criteria for column retirement.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **回收的两条规则，两条都有不报错的错法** / Two retirement rules, both with silent failure modes.
/// </summary>
/// <remarks>
/// 1. `fixed` 与 `kept` 都会否决回收——它们存在的意义就是"这一列无论多贵都留着"。
/// 只判其中一个，另一类列会被撤掉，而撤掉的列**不会报错，只是从解里消失**。
/// 2. 按 `iteration` 取变量而不是按下标——两者在"每轮都加满列"时恰好相等，写错了也看不出来。
///
/// 1. Both `fixed` and `kept` veto retirement — their whole point is "keep this column no matter how expensive". Testing only one retires the other kind, and a retired column
///    **raises nothing, it merely disappears from the solution**.
/// 2. Index by `iteration`, not by position — the two coincide whenever every round fills completely, so the mistake is invisible.
/// </remarks>
public class ColumnRetirementTests {
    /// **检验数达到上限就回收** / A column at the cutoff is retired.
    [Fact]
    public void ShouldRetire_ShouldRetireAtTheCutoff() =>
        ColumnRetirement.ShouldRetire(
                reducedCost: 5.0,
                maximumReducedCost: 5.0,
                @fixed: false,
                kept: false,
                alreadyRemoved: false)
            .Should().BeTrue("`ls` is strict, so equality retires");

    /// **检验数低于上限就留着** / A column below the cutoff stays.
    [Fact]
    public void ShouldRetire_ShouldKeepBelowTheCutoff() =>
        ColumnRetirement.ShouldRetire(4.9, 5.0, @fixed: false, kept: false, alreadyRemoved: false)
            .Should().BeFalse();

    /// **`fixed` 否决回收** / `fixed` vetoes retirement.
    [Fact]
    public void ShouldRetire_ShouldKeepFixedColumns() =>
        ColumnRetirement.ShouldRetire(99.0, 5.0, @fixed: true, kept: false, alreadyRemoved: false)
            .Should().BeFalse("a fixed column is kept no matter how expensive");

    /// **`kept` 否决回收** / `kept` vetoes retirement.
    [Fact]
    public void ShouldRetire_ShouldKeepKeptColumns() =>
        ColumnRetirement.ShouldRetire(99.0, 5.0, @fixed: false, kept: true, alreadyRemoved: false)
            .Should().BeFalse("a kept column is kept no matter how expensive");

    /// **两个否决同时成立也留着** / Both vetoes together still keep it.
    [Fact]
    public void ShouldRetire_ShouldKeepWhenBothVetoesHold() =>
        ColumnRetirement.ShouldRetire(99.0, 5.0, @fixed: true, kept: true, alreadyRemoved: false)
            .Should().BeFalse();

    /// **已移除的不再处理** / An already-removed column is not processed again.
    [Fact]
    public void ShouldRetire_ShouldSkipAlreadyRemovedColumns() =>
        ColumnRetirement.ShouldRetire(99.0, 5.0, @fixed: false, kept: false, alreadyRemoved: true)
            .Should().BeFalse();

    /// <summary>
    /// **按迭代号取变量，不按下标** / Index by iteration, not by position.
    /// </summary>
    /// <remarks>
    /// 判据造出"第 0 轮 3 列、第 1 轮 1 列"的形状：任务 `T` 属于第 1 轮，它在第 1 轮的变量里排第 0 个。
    /// 按下标取会拿到第 0 轮的变量——于是固定的、移除的都是**别的轮次**的列。
    ///
    /// The criterion builds "round 0 has 3 columns, round 1 has 1": task `T` belongs to round 1 and sits at position 0 there. Indexing by position takes round 0's variable, so the
    /// pin and the removal hit **another round's** column.
    /// </remarks>
    [Fact]
    public void ForIteration_ShouldTakeTheTasksOwnRound() {
        var rounds = new List<string> { "round0", "round1" };

        ColumnRetirement.ForIteration(rounds, iteration: 1).Should().Be("round1");
        ColumnRetirement.ForIteration(rounds, iteration: 0).Should().Be("round0");
    }

    /// <summary>
    /// **轮次越界必须由本方法拒绝，而不是靠列表下标抛** / An out-of-range round must be rejected here, not by the list indexer.
    /// </summary>
    /// <remarks>
    /// `List&lt;T&gt;` 的下标越界也抛 `ArgumentOutOfRangeException`，所以只断言异常类型**区分不出**
    /// "本方法检查了"与"直接让它去下标"。判据改断言 `ParamName`：本方法抛的是 `iteration`，
    /// 列表下标抛的是 `index`。
    ///
    /// `List&lt;T&gt;`'s indexer also throws `ArgumentOutOfRangeException`, so asserting only the exception type **cannot tell** "this method checked" from "let the indexer do it". The
    /// criterion asserts `ParamName` instead: this method throws `iteration`, the list indexer throws `index`.
    /// </remarks>
    [Fact]
    public void ForIteration_ShouldRejectOutOfRangeRounds() {
        var rounds = new List<string> { "round0" };

        ((Action)(() => ColumnRetirement.ForIteration(rounds, iteration: 3)))
            .Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("iteration", "the guard must reject it before indexing");
        ((Action)(() => ColumnRetirement.ForIteration(rounds, iteration: -1)))
            .Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("iteration");
    }
}
