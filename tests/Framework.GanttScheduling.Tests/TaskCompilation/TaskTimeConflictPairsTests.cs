// 时间冲突对枚举的判据 / Criteria for conflicting-pair enumeration.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **漏一对是双重占用；允许提前/延迟的任务不该进互斥** / A missed pair is double booking; tasks that may advance or delay must not enter the exclusion.
/// </summary>
/// <remarks>
/// 漏一对的后果是两个任务被排给同一个执行器、时段重叠，容量算成两份而实际只有一份。
/// 多算一对只是多一条恒真的约束——白写。所以判据的重点在"不漏"。
///
/// A missed pair means two tasks assigned to one executor over overlapping intervals, capacity counted twice where only one exists. An extra pair is merely an always-true
/// constraint — wasted. So the criteria focus on not missing any.
///
/// 而允许提前或延迟的任务，时段是**决策变量**：两条"可能重叠"的区间不构成冲突，求解器可以把它们推开。
/// 把它们写进互斥等于**替求解器做了决定**。
///
/// For tasks that may advance or delay, the interval is a **decision variable**: two "possibly overlapping" ranges are not a conflict, and the solver can push them apart. Writing
/// them into the exclusion **makes the decision on the solver's behalf**.
/// </remarks>
public class TaskTimeConflictPairsTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **重叠的任务对全部枚举出来** / Every overlapping pair is enumerated.
    [Fact]
    public void IntersectingPairs_ShouldEnumerateEveryOverlap() {
        var candidates = new List<TaskTimeConflictPairs.Candidate> {
            new("A", Range(0, 4)),
            new("B", Range(2, 6)),
            new("C", Range(6, 8)),
            new("D", Range(7, 9)),
        };

        IReadOnlyList<(TaskTimeConflictPairs.Candidate Left, TaskTimeConflictPairs.Candidate Right)> pairs =
            TaskTimeConflictPairs.IntersectingPairs(candidates);

        pairs.Should().HaveCount(2);
        pairs.Should().Contain(p => p.Left.Id == "A" && p.Right.Id == "B");
        pairs.Should().Contain(p => p.Left.Id == "C" && p.Right.Id == "D");
    }

    /// <summary>
    /// **首尾相接不算冲突** / Touching intervals are not a conflict.
    /// </summary>
    /// <remarks>
    /// `[0,4)` 与 `[4,8)` 在 `4` 点相接：同一个执行器干完第一件正好开始第二件，这是合法的。
    /// 若判据不覆盖这一条，一个把半开区间当成闭区间的实现会**凭空禁止首尾相接的排班**——
    /// 而那正是最紧凑、最常出现的排班。
    ///
    /// `[0,4)` and `[4,8)` meet at `4`: the executor finishes the first job exactly when the second starts, which is legal. Without this criterion, an implementation treating
    /// half-open intervals as closed would **forbid back-to-back scheduling out of nowhere** — and that is the tightest, most common arrangement.
    /// </remarks>
    [Fact]
    public void IntersectingPairs_ShouldNotConflictOnTouchingIntervals() {
        var candidates = new List<TaskTimeConflictPairs.Candidate> {
            new("A", Range(0, 4)),
            new("B", Range(4, 8)),
        };

        TaskTimeConflictPairs.IntersectingPairs(candidates).Should().BeEmpty();
    }

    /// **每对只出现一次** / Each pair appears once.
    [Fact]
    public void IntersectingPairs_ShouldNotDuplicateAPair() {
        var candidates = new List<TaskTimeConflictPairs.Candidate> {
            new("A", Range(0, 5)),
            new("B", Range(1, 6)),
        };

        TaskTimeConflictPairs.IntersectingPairs(candidates).Should().ContainSingle();
    }

    /// **三个两两重叠只得三条对，不是六条** / Three mutually overlapping tasks yield three pairs, not six.
    [Fact]
    public void IntersectingPairs_ShouldNotEmitOrderedDuplicates() {
        var candidates = new List<TaskTimeConflictPairs.Candidate> {
            new("A", Range(0, 5)),
            new("B", Range(1, 6)),
            new("C", Range(2, 7)),
        };

        TaskTimeConflictPairs.IntersectingPairs(candidates).Should().HaveCount(3,
            "A-B, A-C, B-C — not the reverse as well");
    }

    /// <summary>
    /// **允许提前或延迟的任务被排除** / Tasks that may advance or delay are excluded.
    /// </summary>
    /// <remarks>
    /// 判据同时放一个"三项都满足"的任务、一个"时段为空"的任务、一个"允许提前"的任务、一个"允许延迟"的任务。
    /// 只测其一会让实现漏掉其余两条筛选条件。
    ///
    /// The fixture holds one task satisfying all three conditions, one without an interval, one that may advance and one that may delay. Judging only one would let an
    /// implementation drop the other two conditions.
    /// </remarks>
    [Fact]
    public void Select_ShouldKeepOnlyFixedTimeTasks() {
        var tasks = new List<string> { "fixed", "no-time", "advances", "delays" };

        IReadOnlyList<TaskTimeConflictPairs.Candidate> selected = TaskTimeConflictPairs.Select(
            tasks,
            timeOf: task => task == "no-time" ? null : Range(0, 4),
            idOf: task => task,
            advanceEnabled: task => task == "advances",
            delayEnabled: task => task == "delays");

        selected.Should().ContainSingle().Which.Id.Should().Be("fixed");
    }

    /// **候选按时段起点排序** / Candidates are sorted by interval start.
    [Fact]
    public void Select_ShouldSortByStart() {
        var tasks = new List<string> { "late", "early", "middle" };

        IReadOnlyList<TaskTimeConflictPairs.Candidate> selected = TaskTimeConflictPairs.Select(
            tasks,
            timeOf: task => task switch {
                "late" => Range(8, 9),
                "early" => Range(0, 1),
                _ => Range(4, 5),
            },
            idOf: task => task,
            advanceEnabled: _ => false,
            delayEnabled: _ => false);

        selected.Should().HaveCount(3);
        selected[0].Id.Should().Be("early");
        selected[1].Id.Should().Be("middle");
        selected[2].Id.Should().Be("late");
    }

    /// <summary>构造一个时段 / Build an interval.</summary>
    /// <param name="startHour">开始小时 / The start hour.</param>
    /// <param name="endHour">结束小时 / The end hour.</param>
    /// <returns>时段 / The interval.</returns>
    private static TimeRange Range(int startHour, int endHour) =>
        new(Origin.AddHours(startHour), Origin.AddHours(endHour));
}
