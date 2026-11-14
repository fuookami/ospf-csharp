// 执行器编译表的判据 / Criteria for the executor compilation table.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **两个编译实现都必须把表填满；表在构造期就该存在** / Both compilation implementations must fill the table, and it must exist at construction.
/// </summary>
/// <remarks>
/// 表少一项**不会报错**，只是那个执行器少一条约束。所以判据要直接查表，而不是查"注册是否成功"。
/// 而"构造期就有表"同样重要：若表要等 `Register` 之后才存在，那么在注册之前做的任何检查都是空转。
///
/// A missing table entry **raises nothing** — one executor simply has one fewer constraint. Hence the criteria inspect the table directly rather than "did registration
/// succeed". "The table exists at construction" matters too: if it only appeared after `Register`, any check made before registering would be vacuous.
/// </remarks>
public class ExecutorCompilationTableTests {
    /// **工厂为每个执行器各建一条** / The factory creates one entry per executor.
    [Fact]
    public void Build_ShouldCreateOneEntryPerExecutor() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2"), Executor("E3") };

        IReadOnlyDictionary<TestExecutor, object> table = CompilationTable.Build(executors);

        table.Should().HaveCount(3);
        table.Keys.Should().Contain(executors);
    }

    /// **空执行器列表得到空表** / An empty executor list yields an empty table.
    [Fact]
    public void Build_ShouldReturnAnEmptyTableForNoExecutors() =>
        CompilationTable.Build(new List<TestExecutor>()).Should().BeEmpty();

    /// <summary>
    /// **键是传入的那批对象本身，不是按 Id 相等** / The keys are the very objects passed in, not matched by Id.
    /// </summary>
    /// <remarks>
    /// `Executor` **没有覆写 `Equals`**，所以字典用的是引用相等：同一个 `Id` 的另一份实例查不到。
    /// 这条判据钉住这个事实——否则有人会以为"按 Id 查表"是安全的，
    /// 然后在重建执行器列表的地方**静默少掉一条约束**。查表必须用同一批实例。
    ///
    /// `Executor` **does not override `Equals`**, so the dictionary uses reference equality: another instance with the same `Id` will not be found. The criterion pins this fact —
    /// otherwise someone will assume "look up by Id" is safe and then **silently lose one constraint** wherever executors are rebuilt. Lookups must use the same instances.
    /// </remarks>
    [Fact]
    public void Build_ShouldKeyByReferenceIdentity() {
        TestExecutor first = Executor("E1");
        var executors = new List<TestExecutor> { first, Executor("E2") };

        IReadOnlyDictionary<TestExecutor, object> table = CompilationTable.Build(executors);

        table.Should().ContainKey(first);
        table.Should().NotContainKey(Executor("E1"),
            "an equal-looking executor built elsewhere is a different key");
    }

    /// **`TaskCompilation` 构造后表即存在** / `TaskCompilation` exposes the table right after construction.
    [Fact]
    public void TaskCompilation_ShouldExposeTheTableAtConstruction() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), executors);

        compilation.ExecutorCompilation.Should().HaveCount(2);
        compilation.ExecutorCompilation.Keys.Should().Contain(executors);
    }

    /// **`IterativeTaskCompilation` 构造后表即存在** / `IterativeTaskCompilation` exposes the table right after construction.
    [Fact]
    public void IterativeTaskCompilation_ShouldExposeTheTableAtConstruction() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2"), Executor("E3") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), executors);

        compilation.ExecutorCompilation.Should().HaveCount(3);
        compilation.ExecutorCompilation.Keys.Should().Contain(executors);
    }

    /// <summary>
    /// **两个编译实现都不漏执行器** / Neither compilation implementation drops an executor.
    /// </summary>
    /// <remarks>
    /// 判据同时跑两个实现并断言**数量一致**：只测其一，另一个的表短一项也发现不了，而短表正是"少一条约束"的来源。
    ///
    /// The criterion runs both implementations and asserts the **counts agree**: testing only one would not reveal a shorter table in the other, and a short table is exactly
    /// where "one fewer constraint" comes from.
    /// </remarks>
    [Fact]
    public void BothCompilations_ShouldCoverTheSameExecutorSet() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var plain = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(new List<TestTask>(), executors);
        var iterative = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), executors);

        plain.ExecutorCompilation.Should().HaveSameCount(iterative.ExecutorCompilation);
        iterative.ExecutorCompilation.Keys.Should().Contain(plain.ExecutorCompilation.Keys);
    }

    /// <summary>构造执行器 / Build an executor.</summary>
    /// <param name="id">身份 / The identity.</param>
    /// <returns>执行器 / The executor.</returns>
    private static TestExecutor Executor(string id) => new(id, id);
}
