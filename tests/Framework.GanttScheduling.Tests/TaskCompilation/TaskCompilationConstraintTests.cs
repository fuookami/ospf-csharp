// 任务编译约束的判据 / Criteria for the task compilation constraint.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **表是"当前任务"的表，必须每次现取** / The table belongs to the current tasks and must be read fresh.
/// </summary>
/// <remarks>
/// 迭代编译在列生成过程中持续追加任务。若在构造期抓一张快照，追加的任务永远查不到——
/// 它们在模型里**一条编译要求都没有**，而排程结果看起来完全正常：所有任务都排上了，只是某个任务少了它那一条。
///
/// An iterative compilation keeps appending tasks during column generation. A snapshot taken at construction leaves later tasks unfindable — they carry **no compilation
/// requirement** in the model at all, while the schedule looks entirely normal: every task assigned, one task simply missing its row.
/// </remarks>
public class TaskCompilationConstraintTests {
    /// **全部任务都有表达式时成功** / Success when every task has an expression.
    [Fact]
    public void Invoke_ShouldSucceedWhenEveryTaskHasAnExpression() {
        var tasks = new List<TestTask> { Task("T1"), Task("T2") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new StubCompilationTaskTable<TestTask>(tasks));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// **缺一个即失败并指明是哪个** / One missing task fails and names it.
    [Fact]
    public void Invoke_ShouldFailAndNameTheMissingTask() {
        var tasks = new List<TestTask> { Task("T1"), Task("T2") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new StubCompilationTaskTable<TestTask>(new List<TestTask> { tasks[0] }));

        Try result = constraint.Invoke(model);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("T2");
        constraint.MissingExpressions().Should().ContainSingle().Which.Id.Should().Be("T2");
    }

    /// **部分缺失时报告的是第一个** / A partial gap reports the first one.
    [Fact]
    public void Invoke_ShouldReportTheFirstGapOnly() {
        var tasks = new List<TestTask> { Task("T1"), Task("T2"), Task("T3") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new StubCompilationTaskTable<TestTask>(new List<TestTask> { tasks[0] }));

        Try result = constraint.Invoke(model);

        string message = ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message;
        message.Should().Contain("T2");
        message.Should().NotContain("T3");
    }

    /// **表未接线时不检查** / No check is made while the table is not wired.
    [Fact]
    public void Invoke_ShouldSkipTheCheckWhileTheTableIsNotWired() {
        var tasks = new List<TestTask> { Task("T1") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new StubCompilation());

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// **空任务列表成功** / An empty task list succeeds.
    [Fact]
    public void Invoke_ShouldSucceedOnAnEmptyTaskList() {
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            new List<TestTask>(), new StubCompilationTaskTable<TestTask>(new List<TestTask>()));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// <summary>
    /// **每次调用都现取表，不缓存** / The table is read fresh on every call and never cached.
    /// </summary>
    /// <remarks>
    /// 替身第一次读到全表、之后读到空表。若约束缓存了表，第二次仍以为全都在；现取则第二次失败。
    /// 判据必须**连续调用两次**——只调用一次的话，缓存与现取完全一样。
    ///
    /// The double returns the full table on the first read and an empty one afterwards. A caching constraint still believes everything is present on the second call; a fresh
    /// read fails. The criterion must **call twice** — with a single call, caching and fresh reads are indistinguishable.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldReadTheTableFreshOnEveryCall() {
        var tasks = new List<TestTask> { Task("T1") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new ChangingCompilation<TestTask>(tasks));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.Invoke(model).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "the second read no longer sees T1, so a cached table would hide that");
    }

    /// <summary>构造一个任务 / Build a task.</summary>
    /// <param name="id">身份 / The identity.</param>
    /// <returns>任务 / The task.</returns>
    private static TestTask Task(string id) => new(id, id);
}
