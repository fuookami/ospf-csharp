// 活动候选索引的判据 / Criteria for the activity selection index.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **未知候选返回空而不是第 0 列；"恰好一次"覆盖执行与取消两者** / An unknown choice yields null rather than column 0, and "exactly once" covers execution and cancellation alike.
/// </summary>
/// <remarks>
/// 返回零会让调用方以为拿到了第 0 列，把追加约束加到别的任务上——**约束加错了位置，而模型依然可解**。
/// 只数执行列会让"既执行又取消"的活动通过校验，只数取消列会把"执行两次"读成没选。
///
/// Returning zero would make the caller believe it received column 0 and attach the added constraint to a different task — **misplaced constraints, with the model still
/// solvable**. Counting only execution columns lets an activity that is both executed and cancelled pass; counting only cancellation columns reads "executed twice" as never
/// selected.
/// </remarks>
public class ActivitySelectionIndexTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **可执行候选能查到，其下标是它在列表里的位置** / An executable choice resolves to its position in the list.
    [Fact]
    public void VariableOf_ShouldResolveAnExecutableChoice() {
        var key = new ActivityChoiceKey("A", "m1", Origin);
        var index = new ActivitySelectionIndex(new List<(ActivityChoiceKey, ActivityPlacement<Flt64>?)> {
            (key, Placement(key)),
        });

        index.VariableOf(key).Should().Be(0);
        index.Keys.Should().ContainSingle();
        index.CancellableTasks.Should().BeEmpty();
    }

    /// **未知候选返回空，不是 0** / An unknown choice yields null, not 0.
    [Fact]
    public void VariableOf_ShouldReturnNullForAnUnknownChoice() {
        var known = new ActivityChoiceKey("A", "m1", Origin);
        var unknown = new ActivityChoiceKey("A", "m2", Origin);
        var index = new ActivitySelectionIndex(new List<(ActivityChoiceKey, ActivityPlacement<Flt64>?)> {
            (known, Placement(known)),
        });

        index.VariableOf(unknown).Should().BeNull("returning 0 would silently point at a different task's column");
    }

    /// **无放置的候选被识别为显式取消** / A choice without a placement is recognised as an explicit cancellation.
    [Fact]
    public void CancellationVariableOf_ShouldResolveACancellableTask() {
        var executed = new ActivityChoiceKey("A", "m1", Origin);
        var cancelled = new ActivityChoiceKey("B", "cancel", Origin);
        var index = new ActivitySelectionIndex(new List<(ActivityChoiceKey, ActivityPlacement<Flt64>?)> {
            (executed, Placement(executed)),
            (cancelled, null),
        });

        index.CancellationVariableOf("B").Should().Be(1);
        index.CancellationVariableOf("A").Should().BeNull("A is executed, so it has no cancellation column");
        index.CancellationVariableOf("Z").Should().BeNull("Z is not a candidate at all");
    }

    /// **恰好选一：正常选择无违规** / Exactly once: a well-formed selection has no violations.
    [Fact]
    public void ValidateExactlyOne_ShouldPassAWellFormedSelection() {
        ActivitySelectionIndex.ValidateExactlyOne(
                new[] { "A", "B" },
                new[] { 0, 1 },
                new Dictionary<int, string> { [0] = "A", [1] = "B" })
            .Should().BeEmpty();
    }

    /// <summary>
    /// **执行与取消都计入选择次数** / Both execution and cancellation count towards the selection count.
    /// </summary>
    /// <remarks>
    /// 活动 `A` 同时被选中"执行"和"取消"：两列都指向 `A`，计数为 2，必须报违规。
    /// 只数执行列的实现在这里会看到 1 而放过——而"既执行又取消"在物理上自相矛盾。
    ///
    /// Activity `A` is selected both for execution and for cancellation: both columns point at `A`, the count is 2, and a violation must be reported. An implementation
    /// counting only execution columns would see 1 and let it pass — while "executed **and** cancelled" is physically contradictory.
    /// </remarks>
    [Fact]
    public void ValidateExactlyOne_ShouldCountExecutionAndCancellationTogether() {
        ActivitySelectionIndex.ValidateExactlyOne(
                new[] { "A" },
                new[] { 0, 1 },
                new Dictionary<int, string> { [0] = "A", [1] = "A" })
            .Should().Contain("selection:A:2");
    }

    /// **既没执行也没取消的活动被报出** / An activity with neither execution nor cancellation is reported.
    [Fact]
    public void ValidateExactlyOne_ShouldReportAnUnselectedActivity() {
        ActivitySelectionIndex.ValidateExactlyOne(
                new[] { "A", "B" },
                new[] { 0 },
                new Dictionary<int, string> { [0] = "A", [1] = "B" })
            .Should().Contain("selection:B:0");
    }

    /// <summary>构造一次放置 / Build a placement.</summary>
    /// <param name="key">候选身份 / The choice identity.</param>
    /// <returns>放置 / The placement.</returns>
    private static ActivityPlacement<Flt64> Placement(ActivityChoiceKey key) =>
        new(key,
            new TimeRange(Origin.AddHours(1), Origin.AddHours(2)),
            Array.Empty<ActivityReservation<Flt64>>());
}
