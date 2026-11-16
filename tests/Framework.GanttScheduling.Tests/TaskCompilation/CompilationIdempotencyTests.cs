// 重复注册的判据 / Criteria for registering twice.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **重复注册不建第二套变量与约束** / Registering twice builds no second variable set and no second constraint set.
/// </summary>
/// <remarks>
/// `register` 每一步都有 `if (!::x.isInitialized)` 守卫。少了守卫，第二次注册会：
///
/// | 重复的东西 | 后果 |
/// | --- | --- |
/// | 第二组 **同名** `x` / `y` / `z` 变量 | 模型里两套变量各自可被选中，而"任务必须完成"只约束其中一套 |
/// | 第二条 `taskCompilation[t] == 1` | 约束翻倍，最优点不变但求解变慢；更糟的是它让"重复注册"看起来**没发生** |
///
/// 两套同名变量在解里读起来完全一样，只在**哪个变量被选中**时才发现问题——
/// 而那时错误的变量已经被当成正确答案返回了。
///
/// Every step of its `register` is guarded by `if (!::xxx.isInitialized)`. Without the guards, a second registration would produce:
///
/// | What gets duplicated | Consequence |
/// | --- | --- |
/// | a second set of **same-named** `x` / `y` / `z` variables | two variable sets in the model, each selectable, while "a task must be done" constrains only one of them |
/// | a second `taskCompilation[t] == 1` | doubled constraints; the optimum is unchanged but the solve is slower — and worse, it makes the duplicate registration look like it **did not happen** |
///
/// Two same-named variable sets read identically in a solution; the problem only shows when **which variable got selected** matters — and by then the wrong variable has been returned
/// as the answer.
/// </remarks>
public class CompilationIdempotencyTests {
    private static (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation, LinearMetaModel<Flt64> Model) MakeRegistered() {
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1"), new("T2", "T2") },
            new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") },
            taskCancelEnabled: true,
            withExecutorLeisure: true);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, model);
    }

    /// **重复注册不再多出变量** / A second registration adds no variables.
    [Fact]
    public void Register_ShouldNotDuplicateTheVariables() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, LinearMetaModel<Flt64> model) = MakeRegistered();
        int tokensAfterFirst = model.Tokens.Tokens.Count;

        compilation.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Tokens.Count.Should().Be(tokensAfterFirst, "re-registering must not add a second variable set");
    }

    /// <summary>
    /// **重复注册不再多出约束** / A second registration adds no constraints.
    /// </summary>
    /// <remarks>
    /// `taskCompilation[t] == 1` 是"任务必须被完成或明确取消"这条约束本身。
    /// 加两遍会让模型有两条一模一样的行——**最优点不变**，所以任何"解对了"的判据都看不出来。
    ///
    /// `taskCompilation[t] == 1` is the "a task must be done or explicitly cancelled" constraint itself. Adding it twice gives the model two identical rows — the **optimum is
    /// unchanged**, so no "the answer is right" criterion can see it.
    /// </remarks>
    [Fact]
    public void Register_ShouldNotDuplicateTheConstraints() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, LinearMetaModel<Flt64> model) = MakeRegistered();
        int constraintsAfterFirst = model.Constraints.Count;

        compilation.Register(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsAfterFirst, "re-registering must not duplicate `taskCompilation == 1`");
    }

    /// **重复注册拿到的是同一批对象** / A second registration yields the very same objects.
    [Fact]
    public void Register_ShouldReuseTheSameColumnObjects() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, LinearMetaModel<Flt64> model) = MakeRegistered();
        IAbstractTask<TestExecutor, TestAssignmentPolicy> task = compilation.Tasks[0];
        object assignmentBefore = compilation.Assignment[(task, compilation.Executors[0])];
        object cancellationBefore = compilation.Cancellation[task];

        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.Assignment[(task, compilation.Executors[0])]
            .Should().BeSameAs(assignmentBefore, "the `x` column must be the same object, not a lookalike");
        compilation.Cancellation[task]
            .Should().BeSameAs(cancellationBefore, "the `y` column must be the same object, not a lookalike");
    }

    /// **第二次注册仍返回成功** / A second registration still returns Ok.
    [Fact]
    public void Register_ShouldReturnOkOnASecondCall() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, LinearMetaModel<Flt64> model) = MakeRegistered();

        compilation.Register(model).IsOk.Should().BeTrue("re-registering is an idempotent no-op, not an error");
    }

    /// <summary>
    /// **注册到第二个模型会复用同一批变量** / Registering into a second model reuses the same variables.
    /// </summary>
    /// <remarks>
    /// 守卫是"变量只建一次"，不是"只能注册一个模型"：把同一个编译注册到主问题与松弛模型上是正常用法。
    /// 把守卫写成"注册过就不再注册"会让第二个模型**一个变量都没有**——松弛模型于是无约束，
    /// 它的解看起来永远可行。
    ///
    /// guard is "build the variables once", not "register into one model only": registering the same compilation into a main problem and a relaxed model is normal use.
    /// Writing the guard as "already registered, do nothing" leaves the second model with **no variables at all** — the relaxed model is unconstrained, and its solutions always look
    /// feasible.
    /// </remarks>
    [Fact]
    public void Register_ShouldReuseTheColumnsOnASecondModel() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, _) = MakeRegistered();
        IAbstractTask<TestExecutor, TestAssignmentPolicy> task = compilation.Tasks[0];
        object assignmentBefore = compilation.Assignment[(task, compilation.Executors[0])];
        object cancellationBefore = compilation.Cancellation[task];
        object leisureBefore = compilation.Leisure[compilation.Executors[0]];
        object intermediateBefore = compilation.TaskAssignment[(task, compilation.Executors[0])];
        using var second = new LinearMetaModel<Flt64>();

        compilation.Register(second).IsOk.Should().BeTrue();

        second.Tokens.Tokens.Should().NotBeEmpty("the second model receives the same columns");
        second.Constraints.Count(c => c.Name.StartsWith("task_compilation_", System.StringComparison.Ordinal))
            .Should().Be(compilation.Tasks.Count, "the `taskCompilation == 1` constraints are per model, not per compilation");
        compilation.Assignment[(task, compilation.Executors[0])]
            .Should().BeSameAs(assignmentBefore, "no second `x` set is built");
        compilation.Cancellation[task]
            .Should().BeSameAs(cancellationBefore, "no second `y` set is built");
        compilation.Leisure[compilation.Executors[0]]
            .Should().BeSameAs(leisureBefore, "no second `z` set is built");
        compilation.TaskAssignment[(task, compilation.Executors[0])]
            .Should().BeSameAs(intermediateBefore, "no second `task_assignment` set is built");
    }

    /// **开关关掉时重复注册也不多出变量** / With the switches off, re-registering still adds nothing.
    [Fact]
    public void Register_ShouldStayIdempotentWithTheSwitchesOff() {
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") },
            new List<TestExecutor> { new("E1", "E1") },
            taskCancelEnabled: false,
            withExecutorLeisure: false);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        int tokensAfterFirst = model.Tokens.Tokens.Count;
        int constraintsAfterFirst = model.Constraints.Count;

        compilation.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Tokens.Count.Should().Be(tokensAfterFirst);
        model.Constraints.Count.Should().Be(constraintsAfterFirst);
    }
}
