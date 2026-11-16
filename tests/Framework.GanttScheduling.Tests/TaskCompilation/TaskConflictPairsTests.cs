using System;
// 通用冲突对枚举的判据 / Criteria for the generic conflicting-pair enumeration.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **判定是注入的，循环只有一份；每对只出现一次** / The predicate is injected, the loop exists once, and every pair appears once.
/// </summary>
/// <remarks>
/// 基线的冲突族有两份几乎一样的双循环，差别只在判定那一行。写两遍、改一处忘另一处就是**漏一对**，
/// 而漏一对是双重占用：两个任务排给同一执行器、时段重叠，容量算两份而实际只有一份。
///
/// 判据必须能分开"每对一次"与"两个方向各一次"——后者不会出错，但会让模型白长一截。
///
/// The baseline's conflict family has two almost identical double loops differing in one line. Writing it twice and forgetting one copy means **a missed pair**, and a missed
/// pair is double booking: two tasks on one executor over overlapping intervals, capacity counted twice where only one exists. The criteria must tell "once per pair" from "once
/// per direction" — the latter is not wrong, but wastes model size.
/// </remarks>
public class TaskConflictPairsTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **判定为真的对全部枚举出来** / Every pair satisfying the predicate is enumerated.
    [Fact]
    public void Pairs_ShouldEnumerateEverySatisfyingPair() {
        var tasks = new List<string> { "A", "B", "C" };

        IReadOnlyList<(string Left, string Right)> pairs = TaskConflictPairs.Pairs(
            tasks, (lhs, rhs) => lhs == "A" && rhs == "B");

        pairs.Should().ContainSingle();
        pairs[0].Left.Should().Be("A");
        pairs[0].Right.Should().Be("B");
    }

    /// **每对只出现一次（不是两个方向各一次）** / Each pair appears once, not once per direction.
    [Fact]
    public void Pairs_ShouldNotEmitBothDirections() {
        var tasks = new List<string> { "A", "B", "C" };

        IReadOnlyList<(string Left, string Right)> pairs = TaskConflictPairs.Pairs(tasks, (_, _) => true);

        pairs.Should().HaveCount(3, "A-B, A-C, B-C");
    }

    /// **判定为假时不产生任何对** / No pair is produced when the predicate never holds.
    [Fact]
    public void Pairs_ShouldReturnNothingWhenNothingConflicts() =>
        TaskConflictPairs.Pairs(new List<string> { "A", "B" }, (_, _) => false).Should().BeEmpty();

    /// **单个或空任务列表不产生对** / A single task or an empty list produces no pairs.
    [Fact]
    public void Pairs_ShouldHandleDegenerateLists() {
        TaskConflictPairs.Pairs(new List<string>(), (_, _) => true).Should().BeEmpty();
        TaskConflictPairs.Pairs(new List<string> { "A" }, (_, _) => true).Should().BeEmpty();
    }

    /// <summary>
    /// **每个执行器各轮一遍，执行器参与判定** / Every executor is swept, and the executor takes part in the predicate.
    /// </summary>
    /// <remarks>
    /// 判据让判定**只对第一个执行器为真**：若实现把执行器丢掉、或只扫第一个，结果都会变。
    /// 只断言"数量等于执行器数 × 对数"的话，一个忽略执行器的实现也能通过。
    ///
    /// The predicate holds **only for the first executor**: dropping the executor, or sweeping only the first one, both change the outcome. Asserting merely "count equals
    /// executor count × pair count" would let an implementation that ignores the executor pass.
    /// </remarks>
    [Fact]
    public void PairsForEach_ShouldLetTheExecutorDecide() {
        var executors = new List<string> { "E1", "E2" };
        var tasks = new List<string> { "A", "B" };

        IReadOnlyList<(string Executor, string Left, string Right)> pairs = TaskConflictPairs.PairsForEach(
            executors, tasks, (executor, _, _) => executor == "E1");

        pairs.Should().ContainSingle();
        pairs[0].Executor.Should().Be("E1");
    }

    /// **每个执行器都有一轮完整的对** / Each executor gets a full sweep of pairs.
    [Fact]
    public void PairsForEach_ShouldSweepEveryExecutor() {
        var executors = new List<string> { "E1", "E2", "E3" };
        var tasks = new List<string> { "A", "B", "C" };

        IReadOnlyList<(string Executor, string Left, string Right)> pairs =
            TaskConflictPairs.PairsForEach(executors, tasks, (_, _, _) => true);

        pairs.Should().HaveCount(9, "3 executors × 3 unordered pairs");
        pairs.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// **时间冲突对与通用枚举给出同一批对** / The time-conflict pairs agree with the generic enumeration.
    /// </summary>
    /// <remarks>
    /// 这条钉住"只有一份实现"：若有人把时间冲突的循环再写一遍并与通用枚举分叉，两边结果会不同——
    /// 而其中一份必然是错的。
    ///
    /// This pins "only one implementation": if the time-conflict loop were written again and diverged from the generic enumeration, the two results would differ — and one of
    /// them would necessarily be wrong.
    /// </remarks>
    [Fact]
    public void TimeConflictPairs_ShouldAgreeWithTheGenericEnumeration() {
        var candidates = new List<TaskTimeConflictPairs.Candidate> {
            new("A", Range(0, 4)),
            new("B", Range(2, 6)),
            new("C", Range(6, 8)),
        };

        IReadOnlyList<(TaskTimeConflictPairs.Candidate Left, TaskTimeConflictPairs.Candidate Right)> timePairs =
            TaskTimeConflictPairs.IntersectingPairs(candidates);
        IReadOnlyList<(TaskTimeConflictPairs.Candidate Left, TaskTimeConflictPairs.Candidate Right)> genericPairs =
            TaskConflictPairs.Pairs(candidates, (lhs, rhs) => lhs.Time.WithIntersection(rhs.Time));

        timePairs.Should().BeEquivalentTo(genericPairs);
    }

    /// <summary>构造一个时段 / Build an interval.</summary>
    /// <param name="startHour">开始小时 / The start hour.</param>
    /// <param name="endHour">结束小时 / The end hour.</param>
    /// <returns>时段 / The interval.</returns>
    private static TimeRange Range(int startHour, int endHour) =>
        new(Origin.AddHours(startHour), Origin.AddHours(endHour));
}
