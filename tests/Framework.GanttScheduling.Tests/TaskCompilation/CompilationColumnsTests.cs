// 编译列的判据 / Criteria for the compilation columns.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **取消列每个任务一条、空闲列每个执行器一条、分配列每个组合一条** / One cancellation column per task, one leisure column per executor, one assignment column per combination.
/// </summary>
/// <remarks>
/// 少一条列就是"那个任务根本没有对应的决策变量"，而约束会去引用一个不存在的列。
/// 这类缺陷不让模型报错——它只是**少了一条约束或约束了一个不存在的变量**。
///
/// A missing column means "that task has no decision variable at all", while a constraint goes on to reference a column that is not there. The defect raises nothing — it merely
/// **drops a constraint or constrains a nonexistent variable**.
/// </remarks>
public class CompilationColumnsTests {
    /// **普通编译的三张表在构造期就齐** / A plain compilation's three tables are complete at construction.
    [Fact]
    public void TaskCompilation_ShouldFillAllThreeTablesAtConstruction() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var tasks = new List<TestTask> { new("T1", "T1"), new("T2", "T2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);

        compilation.Cancellation.Should().HaveCount(2, "one cancellation column per task");
        compilation.Leisure.Should().HaveCount(2, "one leisure column per executor");
        compilation.TaskAssignment.Should().HaveCount(4, "one assignment column per task-executor combination");
    }

    /// <summary>
    /// **分配列是任务与执行器的组合，不是各自一条** / The assignment table is keyed by the combination, not by either side alone.
    /// </summary>
    /// <remarks>
    /// 判据必须能分开"组合表"与"只按任务"或"只按执行器"建的表：后两者的数量在前者恰等于其中一方时都一样。
    /// 用 2 个任务 × 3 个执行器，组合表 6 条，而只按任一方都只有 2 或 3 条。
    ///
    /// The criterion must tell a combination table from one keyed by tasks alone or executors alone: the latter two have the same count whenever one side happens to match.
    /// With 2 tasks × 3 executors the combination table has 6 entries, while either single-sided table has 2 or 3.
    /// </remarks>
    [Fact]
    public void TaskCompilation_ShouldKeyAssignmentByCombination() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2"), new("E3", "E3") };
        var tasks = new List<TestTask> { new("T1", "T1"), new("T2", "T2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);

        compilation.TaskAssignment.Should().HaveCount(6);
        compilation.TaskAssignment.Should().ContainKey((tasks[0], executors[0]));
        compilation.TaskAssignment.Should().ContainKey((tasks[1], executors[2]));
    }

    /// <summary>
    /// **迭代编译的取消列随任务增长** / The iterative compilation's cancellation table grows with its tasks.
    /// </summary>
    /// <remarks>
    /// 迭代编译最初`originTasks` 是空的，任务由列生成逐步加入。判据用空任务构造，
    /// 断言取消列**此刻为空**——若实现把构造期那批任务（一个都没有）缓存下来，这张表就永远是空的，
    /// 之后加入的任务**永远拿不到取消列**。
    ///
    /// An iterative compilation starts with empty `originTasks` and gains tasks through column generation. The criterion constructs it from an empty list and asserts the
    /// cancellation table **is empty right now** — if the implementation cached the construction-time tasks (there are none), the table would stay empty forever and later tasks
    /// would **never get a cancellation column**.
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_ShouldStartWithEmptyColumnTables() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), executors);

        compilation.Cancellation.Should().BeEmpty("no tasks yet");
        compilation.TaskAssignment.Should().BeEmpty("no tasks yet");
        compilation.Leisure.Should().HaveCount(2, "executors are known from the start");
    }

    /// <summary>
    /// **加入任务后取消列与分配列立刻跟上** / Cancellation and assignment tables follow as soon as tasks are added.
    /// </summary>
    /// <remarks>
    /// 这条才是"现取"的真正判据：上一版只断言"初始为空"，而**初始为空在缓存实现下同样成立**——
    /// 于是两个变异体都存活了。必须**先加任务再读**，才分得开现取与快照。
    ///
    /// This is the real freshness criterion: the previous version only asserted "empty initially", and **an empty initial state holds under a caching implementation too** — so two
    /// mutants survived. Tasks must be **added before reading** for fresh reads and snapshots to differ.
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_ShouldFollowTasksAddedLater() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), executors);

        compilation.Aggregation.AddColumns(new List<TestIterativeTask> {
            new(new TestTask("T1", "T1"), iteration: 0),
        });

        compilation.Cancellation.Should().ContainSingle("the task was added after construction");
        compilation.TaskAssignment.Should().HaveCount(2, "one task × two executors");
    }

    /// <summary>工厂的组合表按全部组合填满 / The pair factory fills every combination.</summary>
    [Fact]
    public void BuildPairs_ShouldCoverEveryCombination() {
        var tasks = new List<string> { "A", "B" };
        var executors = new List<string> { "E1", "E2", "E3" };

        IReadOnlyDictionary<(string Task, string Executor), object> table =
            CompilationTable.BuildPairs(tasks, executors);

        table.Should().HaveCount(6);
        table.Should().OnlyHaveUniqueItems();
    }
}
