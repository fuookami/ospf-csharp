#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TestAggregation = Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.TaskSchedulingAggregation<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AbstractUnplannedTask<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor, Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AssignmentPolicy<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor>>, Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor, Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AssignmentPolicy<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor>>;
using TestTask = Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AbstractUnplannedTask<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor, Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AssignmentPolicy<Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

public class TaskCompilationTests {
    private static TimeWindow<Flt64> CreateTestTimeWindow() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(new TimeRange(start, end), true, TimeSpan.FromHours(1));
    }

    private static Executor CreateExecutor(string id) => new(id, $"Executor_{id}");

    private static TestTask CreateTask(string id) => new(id, $"Task_{id}", new AssignmentPolicy<Executor>());

    // ---- SolverTimeWindowBoundary ----

    [Fact]
    public void SolverTimeWindowBoundary_ShouldHaveSource() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        boundary.Source.Should().BeSameAs(tw);
    }

    [Fact]
    public void SolverTimeWindowBoundary_Continues_ShouldMatchSource() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        boundary.Continues.Should().Be(tw.Continues);
    }

    [Fact]
    public void SolverTimeWindowBoundary_ValueOf_Duration_ShouldReturnSeconds() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        Flt64 result = boundary.ValueOf(TimeSpan.FromHours(2));
        result.Should().Be(new Flt64(7200.0));
    }

    [Fact]
    public void SolverTimeWindowBoundary_ValueOf_Instant_ShouldReturnOffsetFromStart() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        DateTimeOffset instant = tw.Start.AddHours(3);
        Flt64 result = boundary.ValueOf(instant);
        result.Should().Be(new Flt64(10800.0));
    }

    [Fact]
    public void SolverTimeWindowBoundary_InstantOf_ShouldRoundTrip() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        DateTimeOffset instant = tw.Start.AddHours(5);
        Flt64 value = boundary.ValueOf(instant);
        DateTimeOffset result = boundary.InstantOf(value);
        result.Should().Be(instant);
    }

    [Fact]
    public void SolverTimeWindowBoundary_DurationValue_ShouldBeTotalSeconds() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        boundary.DurationValue.Should().Be(new Flt64(86400.0));
    }

    [Fact]
    public void SolverTimeWindowBoundary_EndValue_ShouldBeTotalSeconds() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        boundary.EndValue.Should().Be(new Flt64(86400.0));
    }

    [Fact]
    public void SolverTimeWindowBoundary_DistanceValue_ShouldReturnDifference() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var boundary = new SolverTimeWindowBoundary(tw);
        DateTimeOffset from = tw.Start.AddHours(1);
        DateTimeOffset to = tw.Start.AddHours(4);
        boundary.DistanceValue(from, to).Should().Be(new Flt64(10800.0));
    }

    // ---- TaskSolutionSummary ----

    [Fact]
    public void TaskSolutionSummary_ShouldCountCorrectly() {
        var summary = new TaskSolutionSummary(2, 1, 3);
        summary.AssignedTaskCount.Should().Be(2UL);
        summary.CanceledTaskCount.Should().Be(1UL);
        summary.TotalTaskCount.Should().Be(3UL);
    }

    [Fact]
    public void TaskSolutionSummary_Equality_ShouldWork() {
        var s1 = new TaskSolutionSummary(5, 2, 7);
        var s2 = new TaskSolutionSummary(5, 2, 7);
        s1.Should().Be(s2);
    }

    // ---- TaskAggregation (model) ----

    [Fact]
    public void TaskAggregation_Initial_ShouldBeEmpty() {
        var agg = new TaskAggregation<TestTask, Executor, AssignmentPolicy<Executor>>();
        agg.Tasks.Should().BeEmpty();
        agg.TasksIteration.Should().BeEmpty();
        agg.RemovedTasks.Should().BeEmpty();
        agg.LastIterationTasks.Should().BeEmpty();
    }

    [Fact]
    public void TaskAggregation_AddColumns_ShouldTrackIterations() {
        var agg = new TaskAggregation<TestTask, Executor, AssignmentPolicy<Executor>>();
        TestTask t1 = CreateTask("T1");
        TestTask t2 = CreateTask("T2");
        IReadOnlyList<TestTask> result = agg.AddColumns(new[] { t1, t2 });
        result.Should().HaveCount(2);
        agg.Tasks.Should().HaveCount(2);
        agg.TasksIteration.Should().HaveCount(1);
        agg.LastIterationTasks.Should().HaveCount(2);
    }

    [Fact]
    public void TaskAggregation_Clear_ShouldResetAll() {
        var agg = new TaskAggregation<TestTask, Executor, AssignmentPolicy<Executor>>();
        agg.AddColumns(new[] { CreateTask("T1") });
        agg.Clear();
        agg.Tasks.Should().BeEmpty();
        agg.TasksIteration.Should().BeEmpty();
        agg.RemovedTasks.Should().BeEmpty();
    }

    // ---- Compilation ----

    [Fact]
    public void TaskCompilation_Constructor_ShouldSetProperties() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, taskCancelEnabled: true, withExecutorLeisure: true);
        compilation.TaskCancelEnabled.Should().BeTrue();
        compilation.WithExecutorLeisure.Should().BeTrue();
        compilation.Tasks.Should().HaveCount(1);
        compilation.Executors.Should().HaveCount(1);
    }

    /// <summary>
    /// **注册到真模型会建出真变量；注册到非模型必须失败** / Registering into a real model builds real variables; into a non-model it must fail.
    /// </summary>
    /// <remarks>
    /// 这条判据原本传 `new object()` 并断言 `Ok`——它钉住的正是"空壳静默成功"那个缺陷（B529）。
    /// 现在 `Register` 真的往模型里加变量，传 `object` 只能失败。
    ///
    /// This criterion used to pass `new object()` and assert `Ok` — pinning exactly the "silent stub" defect (B529). `Register` now really adds variables, so an `object` can only
    /// fail.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldRejectANonModel() {
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        compilation.Register(new object()).IsOk.Should().BeFalse(
            "a compilation can only be registered into a linear meta model");
    }

    /// **注册会建出分配变量、取消变量与空闲变量** / Registering builds assignment, cancellation and leisure variables.
    [Fact]
    public void TaskCompilation_Register_ShouldBuildTheThreeColumnFamilies() {
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2") };
        var executors = new List<Executor> { CreateExecutor("E1"), CreateExecutor("E2") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, taskCancelEnabled: true, withExecutorLeisure: true);
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.Assignment.Should().HaveCount(4, "one assignment column per (task, executor)");
        compilation.Cancellation.Should().HaveCount(2, "one cancellation column per task");
        compilation.Leisure.Should().HaveCount(2, "one leisure column per executor");
        // 计数对占位对象也成立，所以还要钉住**类型**：只有真变量才能进模型。
        // Counts hold for placeholders too, so the **type** is pinned as well: only real variables can enter the model.
        compilation.Assignment.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>();
        // `TaskAssignment` 是**中间符号**，与 `x` 变量同名不同物（B538）。
        // `TaskAssignment` is the **intermediate symbol**, a different thing from the `x` variable that shares its name (B538).
        compilation.TaskAssignment.Should().HaveCount(4, "one intermediate symbol per (task, executor)");
        compilation.TaskAssignment.Values.Should().AllBeOfType<LinearExpressionSymbol>();
        compilation.Cancellation.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>();
        compilation.Leisure.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>();
    }

    /// **注册前只有占位对象** / Before registration there are only placeholders.
    [Fact]
    public void TaskCompilation_Register_ShouldLeavePlaceholdersBeforeRegistration() {
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { CreateTask("T1") }, new List<Executor> { CreateExecutor("E1") });

        compilation.Assignment.Values.Should().NotContainItemsAssignableTo<IVariableItem>(
            "the constructor only fills placeholders; real variables come from registration");
    }

    /// <summary>
    /// **开关关掉就不该有那族变量** / A disabled switch must not produce that column family.
    /// </summary>
    /// <remarks>
    /// 取消列与空闲列各由一个开关控制。**无条件建出来**的后果不是报错，而是模型平白多出一批
    /// 无人约束的 0/1 变量：求解器会去搜它们，代价是慢，而答案看起来完全正常。
    ///
    /// The cancellation and leisure columns are each gated by a switch. Building them **unconditionally** raises nothing; it merely adds a batch of unconstrained 0/1 variables that the
    /// solver then searches — slower, with a perfectly normal-looking answer.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldSkipDisabledColumnFamilies() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, taskCancelEnabled: false, withExecutorLeisure: false);
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.Cancellation.Values.Should().NotContainItemsAssignableTo<IVariableItem>(
            "task cancellation is disabled, so no cancellation variable may exist");
        compilation.Leisure.Values.Should().NotContainItemsAssignableTo<IVariableItem>(
            "executor leisure is disabled, so no leisure variable may exist");
        compilation.Assignment.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>(
            "the assignment columns are unconditional");
    }

    /// <summary>
    /// **值域收紧真的写进了变量** / Range tightening really lands on the variables.
    /// </summary>
    /// <remarks>
    /// 只断言"表里有几个条目"看不出收紧有没有生效——占位对象与真变量在这件事上一样。
    /// 判据去读变量的**上下界**：任务的可用执行器集合为空，于是每个组合都被固定为 0。
    ///
    /// Asserting only "the table has N entries" cannot see whether tightening happened — placeholders and real variables look alike there. The criterion reads the variables'
    /// **bounds**: the task's enabled-executor set is empty, so every combination is pinned to 0.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldPinForbiddenCombinations() {
        TestTask task = CreateTask("T1");
        Executor executor = CreateExecutor("E1");
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { task }, new List<Executor> { executor });
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        var variable = (CombinationVariableItem<UInt8, Binary>)compilation.Assignment[(task, executor)];
        variable.LowerBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12);
        variable.UpperBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12);
        variable.Range.Fixed.Should().BeTrue("an executor outside the task's enabled set is forbidden");
    }

    /// <summary>
    /// **锁定覆盖"不允许取消"** / The lock overrides "cancel disabled".
    /// </summary>
    /// <remarks>
    /// 一个任务既 `CancelEnabled = false` 又被锁定：基线先写 0 再写 1，**后写的赢**，结果是 1。
    /// 反过来写成"不允许取消优先"会得到 0——模型于是取消了一个**明确要求保留**的任务。
    ///
    /// A task with `CancelEnabled = false` that is also locked: the baseline writes 0 then 1, **the later write wins**, giving 1. Getting the order wrong yields 0 — the model then
    /// cancels a task that was **explicitly required to stay**.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldLetTheLockOverrideTheCancelFlag() {
        TestTask task = CreateTask("T1");
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { task },
            new List<Executor> { CreateExecutor("E1") },
            lockCancelTasks: new HashSet<TestTask> { task },
            taskCancelEnabled: true);
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        var variable = (CombinationVariableItem<UInt8, Binary>)compilation.Cancellation[task];
        variable.LowerBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-12);
        variable.Range.Fixed.Should().BeTrue("the lock is written after the cancel flag and wins");
    }

    /// <summary>
    /// **每个任务一条 `taskCompilation == 1`** / One `taskCompilation == 1` per task.
    /// </summary>
    /// <remarks>
    /// 这条约束就是"任务要么被完成、要么被明确取消"。少了它，模型可以**什么都不做**而完全可行：
    /// 答案合法、求解成功，只是所有任务都没排。
    ///
    /// This constraint *is* "every task is either done or explicitly cancelled". Without it the model can **do nothing at all** and remain feasible: the answer is legal, the solve
    /// succeeds, and every task is simply unscheduled.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldAddOneCompilationConstraintPerTask() {
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2"), CreateTask("T3") };
        var executors = new List<Executor> { CreateExecutor("E1"), CreateExecutor("E2") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(3,
            "one `taskCompilation == 1` per task, independent of how many executors there are");
    }

    /// **取消开关改变模型里的符号数量** / The cancel switch changes the symbol count in the model.
    [Fact]
    public void TaskCompilation_Register_ShouldIncludeCancellationOnlyWhenEnabled() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };

        var withoutCancel = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, taskCancelEnabled: false);
        using var modelA = new LinearMetaModel<Flt64>();
        withoutCancel.Register(modelA).IsOk.Should().BeTrue();

        var withCancel = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, taskCancelEnabled: true);
        using var modelB = new LinearMetaModel<Flt64>();
        withCancel.Register(modelB).IsOk.Should().BeTrue();

        modelB.Tokens.Tokens.Count.Should().BeGreaterThan(
            modelA.Tokens.Tokens.Count,
            "cancelling adds the `y` column to the model");

        // 只数符号抓不到"表达式成分错了"，所以直接看约束左边有几项：
        // 不开取消 = x 一项；开取消 = y + x 两项。
        // Counting symbols cannot see a wrong expression composition, so the constraint's LHS is read directly: without cancelling it is one term (x), with cancelling two (y + x).
        LinearInequalityConstraint<Flt64> constraintA =
            modelA.Constraints.Cast<LinearInequalityConstraint<Flt64>>().Single();
        LinearInequalityConstraint<Flt64> constraintB =
            modelB.Constraints.Cast<LinearInequalityConstraint<Flt64>>().Single();
        constraintA.Inequality.Lhs.Monomials.Should().HaveCount(1, "x alone when cancelling is off");
        constraintB.Inequality.Lhs.Monomials.Should().HaveCount(2, "y plus x when cancelling is on");
    }

    /// <summary>
    /// **执行器编译是 `OR`，不是求和** / The executor compilation is an `OR`, not a sum.
    /// </summary>
    /// <remarks>
    /// 一个执行器上排了 3 个任务：求和得到 3，`OR` 得到 1。"该执行器被用了"是**布尔事实**，
    /// 求和把它变成"用了几个"——于是任何"每个执行器最多一项成本"的目标都会按任务数重复收费，而解仍然可行。
    ///
    /// An executor carrying 3 tasks sums to 3 while `OR` gives 1. "This executor is used" is a **boolean fact**, and summing turns it into "how many" — so any "one cost per used
    /// executor" objective charges once per task, while the solution stays feasible.
    /// </remarks>
    [Fact]
    public void TaskCompilation_Register_ShouldUseAnOrForExecutorCompilation() {
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2"), CreateTask("T3") };
        var executors = new List<Executor> { CreateExecutor("E1"), CreateExecutor("E2") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.ExecutorCompilation.Should().HaveCount(2, "one entry per executor");
        compilation.ExecutorCompilation.Values.Should().AllBeOfType<LinearExpressionSymbol>();

        // OR 的结果变量各一项：每执行器一条 OR 辅助符号。
        // One OR result variable each: one OR helper symbol per executor.
        model.Tokens.Tokens
            .Count(token => token.Variable.Name.StartsWith("executor_compilation_or_", StringComparison.Ordinal)
                && token.Variable.Name.EndsWith("_or", StringComparison.Ordinal))
            .Should().Be(2, "one OR result variable per executor");

        // OR 的输入项数必须等于任务数：每个任务都是"该执行器被用了"的一个来源。
        // 只取第一个任务的话，执行器上排了 T2/T3 而 T1 没排时，OR 会算出"没用过"。
        // The OR must take one input per task: every task is a source of "this executor is used". Taking only the first means an executor carrying T2/T3 but not T1 evaluates to "unused".
        model.Tokens.Tokens
            .Count(token => token.Variable.Name.StartsWith("executor_compilation_or_", StringComparison.Ordinal)
                && token.Variable.Name.Contains("_nz", StringComparison.Ordinal))
            .Should().Be(2 * tasks.Count, "one non-zero indicator per (executor, task)");
    }

    /// **含空闲时空闲列进执行器编译，不含时不进** / The leisure column joins the executor compilation only when enabled.
    [Fact]
    public void TaskCompilation_Register_ShouldIncludeLeisureOnlyWhenEnabled() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };

        var without = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, withExecutorLeisure: false);
        using var modelA = new LinearMetaModel<Flt64>();
        without.Register(modelA).IsOk.Should().BeTrue();

        var with = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, withExecutorLeisure: true);
        using var modelB = new LinearMetaModel<Flt64>();
        with.Register(modelB).IsOk.Should().BeTrue();

        // 执行器编译表达式的项数：只有 OR 结果 = 1 项；含空闲 = 2 项。
        // Term count of the executor compilation expression: OR result only = 1; with leisure = 2.
        var expressionA = (LinearExpressionSymbol)without.ExecutorCompilation[executors[0]];
        var expressionB = (LinearExpressionSymbol)with.ExecutorCompilation[executors[0]];
        expressionA.Polynomial.Monomials.Should().HaveCount(1, "OR result only when leisure is off");
        expressionB.Polynomial.Monomials.Should().HaveCount(2, "OR result plus z when leisure is on");
    }

    [Fact]
    public void IterativeTaskCompilation_Constructor_ShouldSetProperties() {
        var originTasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            originTasks, executors);
        compilation.TaskCancelEnabled.Should().BeTrue();
        compilation.WithExecutorLeisure.Should().BeTrue();
        compilation.OriginTasks.Should().HaveCount(1);
        compilation.Executors.Should().HaveCount(1);
    }

    /// <summary>
    /// **迭代编译注册到非模型必须失败** / Registering an iterative compilation into a non-model must fail.
    /// </summary>
    /// <remarks>
    /// 与 `TaskCompilation` 同一条规则：这条判据原本传 `new object()` 并断言 `Ok`，
    /// 钉住的正是"空壳静默成功"那个缺陷（B529）。
    ///
    /// Same rule as `TaskCompilation`: this criterion used to pass `new object()` and assert `Ok`, pinning exactly the "silent stub" defect (B529).
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_Register_ShouldRejectANonModel() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        compilation.Register(new object()).IsOk.Should().BeFalse(
            "a compilation can only be registered into a linear meta model");
    }

    /// <summary>
    /// **迭代编译的 `taskCompilation` 只有 `y`，没有 `Σ x`** / The iterative `taskCompilation` is `y` alone, without `Σ x`.
    /// </summary>
    /// <remarks>
    /// `x` 是每轮追加的，注册时还不知道有几轮。写成 `Σ x` 会在注册那一刻固化成空和，
    /// 之后每轮新加的列都进不了这条等式——于是"任务必须完成或取消"退化成"任务必须取消"，
    /// 所有任务都被取消而没有任何报错。
    ///
    /// `x` is appended per round and the round count is unknown at registration. Writing `Σ x` freezes an empty sum at that moment, and every later round's columns stay out of the
    /// equation — so "a task is done or cancelled" degrades to "a task is cancelled": every task gets cancelled with no error at all.
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_Register_ShouldNotSumAssignmentColumns() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { CreateTask("T1") }, new List<Executor> { CreateExecutor("E1") });
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        var taskCompilation = (LinearExpressionSymbol)compilation.TaskCompilationTable[
            compilation.OriginTasks[0]];
        taskCompilation.Polynomial.Monomials.Should().HaveCount(
            1,
            "`y` alone: the assignment columns do not exist yet");
    }

    /// <summary>
    /// **锁定的任务在迭代编译里也被固定为"不取消"** / A locked task is pinned to "not cancelled" in the iterative compilation too.
    /// </summary>
    /// <remarks>
    /// 迭代编译的取消语义与普通编译不同：**默认允许取消**，只有被锁定的任务才固定为 1。
    /// 漏掉这条，列生成会把锁定的任务取消掉——而"锁定"的全部意思就是"不许取消"。
    ///
    /// The iterative compilation's cancellation semantics differ from the plain one: **cancelling is allowed by default**, and only locked tasks are pinned to 1. Dropping this lets
    /// column generation cancel a locked task — and "locked" means exactly "may not be cancelled".
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_Register_ShouldPinLockedTasks() {
        TestTask locked = CreateTask("LOCKED");
        TestTask free = CreateTask("FREE");
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { locked, free },
            new List<Executor> { CreateExecutor("E1") },
            lockedCancelTasks: new HashSet<TestTask> { locked });
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        var cancelVariables = model.Tokens.Tokens
            .Select(token => token.Variable)
            .OfType<CombinationVariableItem<UInt8, Binary>>()
            .Where(variable => variable.Name.StartsWith("y_", StringComparison.Ordinal))
            .ToList();
        cancelVariables.Should().HaveCount(2, "one cancellation column per origin task");
        cancelVariables[0].Range.Fixed.Should().BeTrue("the first task is locked");
        cancelVariables[0].LowerBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-12);
        cancelVariables[1].Range.Fixed.Should().BeFalse("the second task is not locked");
    }

    /// <summary>
    /// **迭代编译的执行器编译用 `xor` 而不是 `OR`** / The iterative executor compilation uses `xor`, not `OR`.
    /// </summary>
    /// <remarks>
    /// 理由同上：`x` 还不存在。基线用独立变量 `xor[e]`，其语义由跨轮冲突约束（B523）与 `xor` 下界（B522）钉住。
    ///
    /// Same reason: `x` does not exist yet. The baseline uses a standalone `xor[e]`, whose meaning is pinned by the cross-round conflict constraints (B523) and the `xor` lower
    /// bound (B522).
    /// </remarks>
    [Fact]
    public void IterativeTaskCompilation_Register_ShouldUseXorInsteadOfAnOr() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask> { CreateTask("T1") },
            new List<Executor> { CreateExecutor("E1"), CreateExecutor("E2") });
        using var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.ExecutorCompilation.Should().HaveCount(2, "one entry per executor");
        compilation.ExecutorCompilation.Values.Should().AllBeOfType<LinearExpressionSymbol>();

        var expression = (LinearExpressionSymbol)compilation.ExecutorCompilation[compilation.Executors[0]];
        expression.Polynomial.Monomials.Should().HaveCount(2, "xor plus z");
        expression.Polynomial.Monomials.Should().NotContain(
            monomial => monomial.Symbol.Name.StartsWith("executor_compilation_or_", StringComparison.Ordinal),
            "an `OR` cannot be built before the assignment columns exist");

        // 两项必须是**不同的**符号：`xor[e] + z[e]`。两项都指向 xor（或都指向 z）时，
        // 表达式变成 `2·xor[e]`——空闲不再计入执行器成本，而项数仍然是 2。
        // The two terms must be **distinct** symbols: `xor[e] + z[e]`. Two references to xor (or to z) make the expression `2·xor[e]` — leisure no longer counts towards the executor
        // cost, while the term count stays 2.
        expression.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should().OnlyHaveUniqueItems();
        expression.Polynomial.Monomials.Should().Contain(
            monomial => monomial.Symbol.Name.StartsWith("z_", StringComparison.Ordinal),
            "the leisure column must be one of the terms");
        expression.Polynomial.Monomials.Should().Contain(
            monomial => monomial.Symbol.Name.StartsWith("xor_", StringComparison.Ordinal),
            "the xor column must be one of the terms");
    }

    // ---- Makespan ----

    [Fact]
    public void Makespan_Constructor_ShouldSetProperties() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, compilation);
        var makespan = new Makespan<TestTask, Executor, AssignmentPolicy<Executor>>(tasks, taskTime);
        makespan.Tasks.Should().HaveCount(1);
        makespan.TaskTime.Should().BeSameAs(taskTime);
    }

    /// <summary>
    /// **`Makespan.Register` 已接线（B546）：非模型失败** / Wired as of B546; a non-model fails.
    /// </summary>
    /// <remarks>
    /// 这条判据原来是"注册返回 Ok"。接线之后它的意义变成"**施加到非模型上仍然失败**"——
    /// 若哪天有人把守卫换成 `return Ok`，这里会变红。
    ///
    /// This criterion used to be "register returns Ok". Once wired its meaning becomes "**applying to a non-model still fails**" — if someone replaces the guard with `return Ok`,
    /// this turns red.
    /// </remarks>
    [Fact]
    public void Makespan_Register_ShouldRejectANonModel() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, compilation);
        var makespan = new Makespan<TestTask, Executor, AssignmentPolicy<Executor>>(tasks, taskTime);
        makespan.Register(new object()).IsOk.Should().BeFalse(
            "a makespan can only be registered into a linear meta model");
    }

    // ---- Switch ----

    [Fact]
    public void TaskSchedulingSwitch_Constructor_ShouldSetProperties() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var sw = new TaskSchedulingSwitch<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, executors, compilation);
        sw.Tasks.Should().HaveCount(2);
        sw.Executors.Should().HaveCount(1);
    }

    [Fact]
    /// <summary>
    /// **换型注册只接受线性模型，且要求编译已注册** / The switch only registers into a linear model, and only after the compilation.
    /// </summary>
    /// <remarks>
    /// 原来这条判据断言 `Register(new object())` 返回 `Ok`——那是空壳的行为，绿灯亮在"什么都没做"上。B551 把换型做实之后，
    /// 非模型必须被拒；同时登记"编译尚未注册"这一种，因为它原来会从循环里抛运行时异常。
    ///
    /// This used to assert `Register(new object())` returns `Ok` — the stub's behaviour, a green light on doing nothing. Now that B551 made the switch real, a non-model must be
    /// rejected; the "compilation not registered" case is pinned too, since it used to throw from inside the loops.
    /// </remarks>
    public void TaskSchedulingSwitch_Register_ShouldRejectANonModelOrAnUnregisteredCompilation() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var sw = new TaskSchedulingSwitch<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, executors, compilation);
        Result<Success, ErrorCode, Error<ErrorCode>> result = sw.Register(new object());
        result.IsOk.Should().BeFalse("a non-model is not a linear meta model");

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
        sw.Register(model).IsOk.Should().BeFalse("the compilation has not been registered yet");
    }

    // ---- TaskTime ----

    [Fact]
    public void TaskSchedulingTaskTime_Constructor_ShouldSetProperties() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, compilation, advanceEnabled: true, delayEnabled: true);
        taskTime.AdvanceEnabled.Should().BeTrue();
        taskTime.DelayEnabled.Should().BeTrue();
        taskTime.Tasks.Should().HaveCount(1);
    }

    /// <summary>
    /// **`TaskSchedulingTaskTime.Register` 已接线（B546）：非模型失败** / Wired as of B546; a non-model fails.
    /// </summary>
    /// <remarks>
    /// 空任务列表也要先过模型检查：第一版若按"没有任务就没事可做"直接返回 `Ok`，
    /// 后续真正的模型注册就永远走不到那条路。
    ///
    /// Even an empty task list goes through the model check first: a first version returning `Ok` for "no tasks, nothing to do" would never reach the real registration path.
    /// </remarks>
    [Fact]
    public void TaskSchedulingTaskTime_Register_ShouldRejectANonModel() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, new List<TestTask>(), compilation);
        taskTime.Register(new object()).IsOk.Should().BeFalse(
            "task times can only be registered into a linear meta model");
    }

    [Fact]
    public void TaskSchedulingTaskTime_DefaultFlags_ShouldBeFalse() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, new List<TestTask>(), compilation);
        taskTime.AdvanceEnabled.Should().BeFalse();
        taskTime.DelayEnabled.Should().BeFalse();
        taskTime.AdvanceEarliestEndTimeEnabled.Should().BeFalse();
        taskTime.DelayLastEndTimeEnabled.Should().BeFalse();
        taskTime.OverMaxAdvanceEnabled.Should().BeFalse();
        taskTime.OverMaxDelayEnabled.Should().BeFalse();
    }

    // ---- Shadow Price Keys ----

    [Fact]
    public void ExecutorCompilationShadowPriceKey_Equality() {
        Executor e = CreateExecutor("E1");
        var k1 = new ExecutorCompilationShadowPriceKey<Executor>(e);
        var k2 = new ExecutorCompilationShadowPriceKey<Executor>(e);
        k1.Should().Be(k2);
    }

    [Fact]
    public void TaskCompilationShadowPriceKey_Equality() {
        TestTask t = CreateTask("T1");
        var k1 = new TaskCompilationShadowPriceKey<Executor, AssignmentPolicy<Executor>>(t);
        var k2 = new TaskCompilationShadowPriceKey<Executor, AssignmentPolicy<Executor>>(t);
        k1.Should().Be(k2);
    }

    [Fact]
    public void TaskAdvanceTimeShadowPriceKey_Equality() {
        TestTask t = CreateTask("T1");
        var k1 = new TaskAdvanceTimeShadowPriceKey<Executor, AssignmentPolicy<Executor>>(t);
        var k2 = new TaskAdvanceTimeShadowPriceKey<Executor, AssignmentPolicy<Executor>>(t);
        k1.Should().Be(k2);
    }

    // ---- Limits - Pipeline Invoke ----

    [Fact]
    public void ExecutorCompilationConstraint_Invoke_ShouldReturnOk() {
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), executors);
        var constraint = new ExecutorCompilationConstraint<Executor, AssignmentPolicy<Executor>>(
            executors, compilation);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("executor_compilation");
        using var model = new LinearMetaModel<Flt64>();
        constraint.Invoke(new object()).IsOk.Should().BeFalse("a constraint can only be applied to a linear meta model");
        constraint.Invoke(model).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ExecutorCostMinimization_ShouldStaySilentWithoutAnExtractorAndFailOnANonModel() {
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), executors);
        var minimization = new ExecutorCostMinimization<Executor, AssignmentPolicy<Executor>>(
            executors, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("executor_cost_minimization");
        // 基线写的是 `coefficient?.let { … }`：**没给提取器就什么都不做**，那是语义而不是漏接线。
        // The baseline is `coefficient?.let { … }`: with no extractor it does nothing, and that is semantics rather than missing wiring.
        minimization.Invoke(new object()).IsOk.Should().BeTrue(
            "without an extractor the executor cost does not exist, so even a non-model is not an error");

        var withExtractor = new ExecutorCostMinimization<Executor, AssignmentPolicy<Executor>>(
            executors, compilation, coefficient: _ => Flt64.One);
        withExtractor.Invoke(new object()).IsOk.Should().BeFalse(
            "with an extractor the objective exists and a non-model must be rejected");
    }

    /// <summary>
    /// **`ExecutorLeisureMinimization` 已接线（B545），非模型仍然失败** / Wired as of B545; a non-model still fails.
    /// </summary>
    /// <remarks>
    /// 必须**给系数**才走到模型检查：没给系数时基线直接 `Ok`（`coefficient?.let { … }`），
    /// 于是这条判据若不给系数，永远看不到 `IllegalArgument`。
    ///
    /// A coefficient is **required** to reach the model check: without one the baseline returns `Ok` (`coefficient?.let { … }`), so a criterion that omits it would never see
    /// `IllegalArgument`.
    /// </remarks>
    [Fact]
    public void ExecutorLeisureMinimization_Invoke_ShouldRejectANonModel() {
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), executors, withExecutorLeisure: true);
        var minimization = new ExecutorLeisureMinimization<Executor, AssignmentPolicy<Executor>>(
            executors, compilation, _ => new Flt64(1.0));
        ((IMetaConstraintGroup)minimization).Name.Should().Be("executor_leisure_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "a leisure objective can only be applied to a linear meta model");
    }

    /// <summary>
    /// **`TaskCancelMinimization` 已接线（B545），非模型仍然失败** / Wired as of B545; a non-model still fails.
    /// </summary>
    [Fact]
    public void TaskCancelMinimization_Invoke_ShouldRejectANonModel() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>(), taskCancelEnabled: true);
        var minimization = new TaskCancelMinimization<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_cancel_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "a cancellation objective can only be applied to a linear meta model");
    }

    [Fact]
    public void TaskCompilationConstraint_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var constraint = new TaskCompilationConstraint<Executor, AssignmentPolicy<Executor>>(
            tasks, compilation);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_compilation");
        // **必须传真模型**：`Invoke` 现在真的加行，非模型只能失败。这条原本传 `new object()` 断言 `Ok`，
        // 钉住的正是"空壳静默成功"——与 `TaskCompilation_Register_ShouldRejectANonModel` 同一个教训（B529）。
        // **A real model is required**: `Invoke` now really adds rows, so a non-model can only fail. This used to pass `new object()` and assert `Ok`, pinning the "silent stub" defect —
        // the same lesson as `TaskCompilation_Register_ShouldRejectANonModel` (B529).
        using var model = new LinearMetaModel<Flt64>();
        constraint.Invoke(new object()).IsOk.Should().BeFalse("a constraint can only be applied to a linear meta model");
        constraint.Invoke(model).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskConflictConstraint_Invoke_ShouldFailWhileUnwired() {
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var constraint = new TaskConflictConstraint<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation, (e, t1, t2) => true);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_conflict");
        // 未接线就失败，不要静默成功：返回 Ok 等于宣称"该约束已施加且未违反"。
        // Fail while unwired; an Ok would claim the constraint was applied and is satisfied.
        constraint.Invoke(new object()).IsOk.Should().BeFalse(
            "an unwired limit must fail loudly instead of pretending to be applied");
    }

    [Fact]
    public void TaskTimeConflictConstraint_Invoke_ShouldFailWhileUnwired() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var constraint = new TaskTimeConflictConstraint<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_time_conflict");
        // 未接线就失败，不要静默成功：返回 Ok 等于宣称"该约束已施加且未违反"。
        // Fail while unwired; an Ok would claim the constraint was applied and is satisfied.
        constraint.Invoke(new object()).IsOk.Should().BeFalse(
            "an unwired limit must fail loudly instead of pretending to be applied");
    }

    [Fact]
    public void MakespanMinimization_Invoke_ShouldFailWhileUnwired() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        object makespan = new object();
        var minimization = new MakespanMinimization<Executor, AssignmentPolicy<Executor>>(
            tw, makespan);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("makespan_minimization");
        // 未接线就失败，不要静默成功：返回 Ok 等于宣称"该约束已施加且未违反"。
        // Fail while unwired; an Ok would claim the constraint was applied and is satisfied.
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "an unwired limit must fail loudly instead of pretending to be applied");
    }

    [Fact]
    public void SwitchCostMinimization_Invoke_ShouldFailWhileUnwired() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var sw = new TaskSchedulingSwitch<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, executors, compilation);
        var minimization = new SwitchCostMinimization<TestTask, Executor, AssignmentPolicy<Executor>>(
            executors, tasks, sw);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("switch_cost_minimization");
        // 未接线就失败，不要静默成功：返回 Ok 等于宣称"该约束已施加且未违反"。
        // Fail while unwired; an Ok would claim the constraint was applied and is satisfied.
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "an unwired limit must fail loudly instead of pretending to be applied");
    }

    [Fact]
    public void TaskExecutorCostMinimization_Invoke_ShouldRejectANonModel() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var minimization = new TaskExecutorCostMinimization<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_executor_cost_minimization");
        // 这条判据原来是"未接线就失败"。接线之后它的意义变成"**施加到非模型上仍然失败**"（B560）。
        // This criterion used to be "fails while unwired"; once wired (B560) it means "**applying to a non-model still fails**".
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "applying the objective to a non-model must fail loudly");
    }

    /// <summary>
    /// **`TaskCostMinimization` 已接线（B544），非模型仍然失败** / `TaskCostMinimization` is wired (B544); a non-model still fails.
    /// </summary>
    /// <remarks>
    /// 这条判据原来是"未接线就失败"。接线之后它的意义变成"**施加到非模型上仍然失败**"——
    /// 若哪天有人把守卫换成 `return Ok`，这里会变红。
    ///
    /// This criterion used to be "fails while unwired". Once wired its meaning becomes "**applying to a non-model still fails**" — if someone replaces the guard with `return Ok`,
    /// this turns red.
    /// </remarks>
    [Fact]
    public void TaskCostMinimization_Invoke_ShouldRejectANonModel() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        var minimization = new TaskCostMinimization<Executor, AssignmentPolicy<Executor>>(compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_cost_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "a cost objective can only be applied to a linear meta model");
    }

    // ---- Aggregation ----

    [Fact]
    public void TaskSchedulingAggregation_Constructor_ShouldCreateCompilationAndTaskTime() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var agg = new TaskSchedulingAggregation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, executors, taskCancelEnabled: true);
        agg.Compilation.Should().NotBeNull();
        agg.TaskTime.Should().NotBeNull();
        agg.TimeBoundary.Should().NotBeNull();
        agg.Compilation.TaskCancelEnabled.Should().BeTrue();
    }
}
