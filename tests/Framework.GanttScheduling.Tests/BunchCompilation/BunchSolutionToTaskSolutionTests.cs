// BunchSolution → TaskSolution 转换的判据 / Criteria for the BunchSolution to TaskSolution conversion.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// **只有"非空分配"的任务进入已分配列表** / Only tasks carrying a non-empty assignment count as assigned.
/// </summary>
/// <remarks>
/// 转换的判据来自`TaskSolution.Companion.invoke`：遍历所有任务束里的任务，只有当任务的分配策略**非空**时才计入已分配。
/// 把"在某个任务束里出现过"当成"已分配"，会把**排上了但没有执行者与时间**的任务报成已排产——
/// 而这类任务恰恰是需要被重新安排的那些。
///
/// The criterion follows its `TaskSolution.Companion.invoke`: walk every task in every bunch and count it as assigned only when its assignment policy is **non-empty**.
/// Treating "appears in some bunch" as "assigned" reports tasks that **have neither an executor nor a time** as already scheduled — and those are exactly the ones that still
/// need arranging.
/// </remarks>
public class BunchSolutionToTaskSolutionTests {
    private static readonly TimeRange TaskTime = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));

    /// **空的分配策略不计入已分配** / An empty assignment policy does not count as assigned.
    [Fact]
    public void ToTaskSolution_ShouldSkipTasksWithAnEmptyAssignment() {
        var executor = new TestExecutor("E1", "E1");
        var assigned = new TestTask("T1", "T1", TaskTime) {
            AssignmentPolicy = new TestAssignmentPolicy(executor, TaskTime),
        };
        var unassigned = new TestTask("T2", "T2", TaskTime) {
            AssignmentPolicy = new TestAssignmentPolicy(),
        };

        var solution = new BunchSolution<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            new[] { Bunch(executor, new[] { assigned, unassigned }) },
            Array.Empty<TestTask>());

        var result = solution.ToTaskSolution();

        result.AssignedTasks.Should().ContainSingle()
            .Which.Id.Should().Be("T1", "T2 carries neither an executor nor a time, so it is not assigned");
    }

    /// <summary>
    /// **没有分配策略的任务同样不计入** / A task with no assignment policy at all is also excluded.
    /// </summary>
    /// <remarks>
    /// `null` 与"空的分配策略"是两种写法、同一种含义。只处理其一的实现会让 `null` 那一支抛 `NullReferenceException`
    /// 或把任务误算成已分配——两种都错，而错法完全不同。
    ///
    /// `null` and "an empty policy" are two spellings of one meaning. An implementation handling only one of them either throws a `NullReferenceException` on the `null` branch
    /// or miscounts the task as assigned — both wrong, and wrong in completely different ways.
    /// </remarks>
    [Fact]
    public void ToTaskSolution_ShouldSkipTasksWithoutAnyPolicy() {
        var executor = new TestExecutor("E1", "E1");
        var noPolicy = new TestTask("T1", "T1", TaskTime);
        var assigned = new TestTask("T2", "T2", TaskTime) {
            AssignmentPolicy = new TestAssignmentPolicy(executor, TaskTime),
        };

        var solution = new BunchSolution<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            new[] { Bunch(executor, new[] { noPolicy, assigned }) },
            Array.Empty<TestTask>());

        var result = solution.ToTaskSolution();

        result.AssignedTasks.Should().ContainSingle().Which.Id.Should().Be("T2");
    }

    /// **已取消任务原样传递** / Cancelled tasks are carried over unchanged.
    [Fact]
    public void ToTaskSolution_ShouldCarryTheCancelledTasksOver() {
        var executor = new TestExecutor("E1", "E1");
        var cancelled = new TestTask("C1", "C1", TaskTime);

        var solution = new BunchSolution<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Array.Empty<TestTaskBunch>(),
            new[] { cancelled });

        var result = solution.ToTaskSolution();

        result.AssignedTasks.Should().BeEmpty();
        result.CanceledTasks.Should().ContainSingle().Which.Id.Should().Be("C1");
    }

    /// **跨多个任务束收集，不遗漏** / Tasks are collected across every bunch.
    [Fact]
    public void ToTaskSolution_ShouldCollectAcrossEveryBunch() {
        var first = new TestExecutor("E1", "E1");
        var second = new TestExecutor("E2", "E2");
        var a = new TestTask("A", "A", TaskTime) { AssignmentPolicy = new TestAssignmentPolicy(first, TaskTime) };
        var b = new TestTask("B", "B", TaskTime) { AssignmentPolicy = new TestAssignmentPolicy(second, TaskTime) };

        var solution = new BunchSolution<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            new[] { Bunch(first, new[] { a }), Bunch(second, new[] { b }) },
            Array.Empty<TestTask>());

        solution.ToTaskSolution().AssignedTasks.Should().HaveCount(2);
    }

    /// <summary>构造一个任务束 / Build a task bunch.</summary>
    /// <param name="executor">执行器 / The executor.</param>
    /// <param name="tasks">任务 / The tasks.</param>
    /// <returns>任务束 / The bunch.</returns>
    private static TestTaskBunch Bunch(TestExecutor executor, IReadOnlyList<TestTask> tasks) =>
        new(executor,
            new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now),
            tasks);
}
