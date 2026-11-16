// 任务代价累加的判据 / Criteria for task cost accumulation.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **目标只保留最近一轮的代价项；这不是笔误，是有意如此** / The objective keeps only the latest round's cost terms, which is deliberate rather than a typo.
/// </summary>
/// <remarks>
/// `addColumns` 每次都先 `taskCost.flush`，然后只对本次新增的列追加 `cost × x[task]`。
/// 之前迭代的列还在模型里（`_x` 累积、约束不变），但它们的**代价项已经不在目标中**——
/// 目标值偏小，不会报错，列生成越久越明显。
///
/// Its `addColumns` always calls `taskCost.flush` and then appends `cost × x[task]` for this iteration's new columns only. Earlier columns remain in the model
/// (`_x` accumulates, constraints stay) but their **cost terms are gone from the objective** — the objective reads too small, nothing is reported, and it gets worse the longer
/// column generation runs.
/// </remarks>
public class TaskCostAccumulationTests {
    private static Result<IReadOnlyList<TaskCostAccumulation<string>.Term>, ErrorCode, Error<ErrorCode>> AddRound(
        TaskCostAccumulation<string> accumulator,
        IReadOnlyList<string> tasks) =>
        accumulator.AddRound(tasks, task => new Flt64(task.Length));

    /// **本轮的项都在** / This round's terms are all present.
    [Fact]
    public void AddRound_ShouldKeepThisRoundsTerms() {
        var accumulator = new TaskCostAccumulation<string>();

        AddRound(accumulator, new List<string> { "ab", "cde" });

        accumulator.Terms.Should().HaveCount(2);
    }

    /// **代价原样保留** / The costs are kept unchanged.
    [Fact]
    public void AddRound_ShouldKeepTheCosts() {
        var accumulator = new TaskCostAccumulation<string>();

        AddRound(accumulator, new List<string> { "abcd" });

        accumulator.Terms[0].Weight.ToDouble().Should().BeApproximately(4.0, 1e-12);
    }

    /// <summary>
    /// **第二轮清掉了第一轮** / The second round clears the first.
    /// </summary>
    /// <remarks>
    /// 这条是"沿用 flush"的凭据，而且**能失败**：若实现改成累积（`AddRange` 而不 `Clear`），
    /// 两轮之后会得到三项。累积其实更"对"，但这里没有采用，这个差异只在多轮列生成时显现，
    /// 且目标值偏小而**不报错**。
    ///
    /// This is the evidence for copying the flush, and it **can fail**: an accumulating implementation (`AddRange` without `Clear`) leaves three entries after two rounds.
    /// Accumulating is arguably more correct, but it is not what happens here, and the difference only shows across rounds as a too-small objective with **no error**.
    /// </remarks>
    [Fact]
    public void AddRound_ShouldNotRetainThePreviousRound() {
        var accumulator = new TaskCostAccumulation<string>();

        AddRound(accumulator, new List<string> { "a" });
        accumulator.Terms.Should().HaveCount(1);

        AddRound(accumulator, new List<string> { "bb", "ccc" });
        accumulator.Terms.Should().HaveCount(2, "the flush drops the first round");
        accumulator.Terms.Should().NotContain(term => term.Task == "a");
    }

    /// **保留轮数是 1** / The retained round count is 1.
    [Fact]
    public void RetainedIterations_ShouldBeOne() =>
        TaskCostAccumulation<string>.RetainedIterations.Should().Be(1,
            "the baseline flushes, so only the latest round survives");

    /// **空批次清空目标** / An empty round empties the objective.
    [Fact]
    public void AddRound_ShouldEmptyTheTableForAnEmptyRound() {
        var accumulator = new TaskCostAccumulation<string>();
        AddRound(accumulator, new List<string> { "abc" });

        AddRound(accumulator, new List<string>());

        accumulator.Terms.Should().BeEmpty("flush then add nothing leaves an empty objective");
    }

    /// <summary>
    /// **有一列不可定价就整轮失败，且原表不动** / One unpriced column fails the round and leaves the table untouched.
    /// </summary>
    /// <remarks>
    /// 是 `cost(task).solverCostOrNull(Flt64.infinity)!!`——取不到就视为无穷。
    /// 静默取 `1` 会让"不可定价的列"变得比真实列更便宜，列生成于是**偏爱制造这类列**。
    /// 失败时保留上一轮的表，比留下一张半轮的表更容易排查。
    ///
 /// This implementation is `cost(task).solverCostOrNull(Flt64.infinity)!!` — an absent cost becomes infinity. Defaulting to `1` makes an unpriced column cheaper than real ones, so
    /// column generation **favours producing such columns**. Keeping the previous round's table on failure is easier to diagnose than a half round.
    /// </remarks>
    [Fact]
    public void AddRound_ShouldFailAndLeaveTheTableUntouched() {
        var accumulator = new TaskCostAccumulation<string>();
        AddRound(accumulator, new List<string> { "ab" });

        Result<IReadOnlyList<TaskCostAccumulation<string>.Term>, ErrorCode, Error<ErrorCode>> failed =
            accumulator.AddRound(new List<string> { "ok", "bad" }, task => task == "bad" ? null : new Flt64(2.0));

        failed.Should().BeOfType<Failed<IReadOnlyList<TaskCostAccumulation<string>.Term>, ErrorCode, Error<ErrorCode>>>();
        accumulator.Terms.Should().HaveCount(1, "the previous round is preserved on failure");
        accumulator.Terms[0].Task.Should().Be("ab");
    }
}
