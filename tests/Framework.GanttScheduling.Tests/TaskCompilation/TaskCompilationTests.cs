#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
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

    [Fact]
    public void TaskCompilation_Register_ShouldReturnOk() {
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        Result<Success, ErrorCode, Error<ErrorCode>> result = compilation.Register(new object());
        result.IsOk.Should().BeTrue();
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

    [Fact]
    public void IterativeTaskCompilation_Register_ShouldReturnOk() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        Result<Success, ErrorCode, Error<ErrorCode>> result = compilation.Register(new object());
        result.IsOk.Should().BeTrue();
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

    [Fact]
    public void Makespan_Register_ShouldReturnOk() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, compilation);
        var makespan = new Makespan<TestTask, Executor, AssignmentPolicy<Executor>>(tasks, taskTime);
        Result<Success, ErrorCode, Error<ErrorCode>> result = makespan.Register(new object());
        result.IsOk.Should().BeTrue();
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
    public void TaskSchedulingSwitch_Register_ShouldReturnOk() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var sw = new TaskSchedulingSwitch<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, tasks, executors, compilation);
        Result<Success, ErrorCode, Error<ErrorCode>> result = sw.Register(new object());
        result.IsOk.Should().BeTrue();
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

    [Fact]
    public void TaskSchedulingTaskTime_Register_ShouldReturnOk() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        var taskTime = new TaskSchedulingTaskTime<TestTask, Executor, AssignmentPolicy<Executor>>(
            tw, new List<TestTask>(), compilation);
        Result<Success, ErrorCode, Error<ErrorCode>> result = taskTime.Register(new object());
        result.IsOk.Should().BeTrue();
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
        constraint.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ExecutorCostMinimization_Invoke_ShouldReturnOk() {
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), executors);
        var minimization = new ExecutorCostMinimization<Executor, AssignmentPolicy<Executor>>(
            executors, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("executor_cost_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ExecutorLeisureMinimization_Invoke_ShouldReturnOk() {
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), executors, withExecutorLeisure: true);
        var minimization = new ExecutorLeisureMinimization<Executor, AssignmentPolicy<Executor>>(
            executors, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("executor_leisure_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskCancelMinimization_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>(), taskCancelEnabled: true);
        var minimization = new TaskCancelMinimization<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_cancel_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskCompilationConstraint_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, new List<Executor>());
        var constraint = new TaskCompilationConstraint<Executor, AssignmentPolicy<Executor>>(
            tasks, compilation);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_compilation");
        constraint.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskConflictConstraint_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1"), CreateTask("T2") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var constraint = new TaskConflictConstraint<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation, (e, t1, t2) => true);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_conflict");
        constraint.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskTimeConflictConstraint_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var constraint = new TaskTimeConflictConstraint<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation);
        ((IMetaConstraintGroup)constraint).Name.Should().Be("task_time_conflict");
        constraint.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void MakespanMinimization_Invoke_ShouldReturnOk() {
        TimeWindow<Flt64> tw = CreateTestTimeWindow();
        object makespan = new object();
        var minimization = new MakespanMinimization<Executor, AssignmentPolicy<Executor>>(
            tw, makespan);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("makespan_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void SwitchCostMinimization_Invoke_ShouldReturnOk() {
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
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskExecutorCostMinimization_Invoke_ShouldReturnOk() {
        var tasks = new List<TestTask> { CreateTask("T1") };
        var executors = new List<Executor> { CreateExecutor("E1") };
        var compilation = new TaskCompilation<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors);
        var minimization = new TaskExecutorCostMinimization<TestTask, Executor, AssignmentPolicy<Executor>>(
            tasks, executors, compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_executor_cost_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
    }

    [Fact]
    public void TaskCostMinimization_Invoke_ShouldReturnOk() {
        var compilation = new IterativeTaskCompilation<TestTask, TestTask, Executor, AssignmentPolicy<Executor>>(
            new List<TestTask>(), new List<Executor>());
        var minimization = new TaskCostMinimization<Executor, AssignmentPolicy<Executor>>(compilation);
        ((IMetaConstraintGroup)minimization).Name.Should().Be("task_cost_minimization");
        minimization.Invoke(new object()).IsOk.Should().BeTrue();
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
