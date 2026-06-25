#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
// ===== Test helper types =====

public class TestExecutor : Executor {
    public TestExecutor(string id, string name) : base(id, name) { }
}

public class TestAssignmentPolicy : IAssignmentPolicy<TestExecutor> {
    public TestAssignmentPolicy() { }
    public TestAssignmentPolicy(TestExecutor executor, TimeRange time) {
        Executor = executor;
        Time = time;
    }

    public TestExecutor? Executor { get; }
    public TimeRange? Time { get; }
}

public class TestTask : IAbstractTask<TestExecutor, TestAssignmentPolicy> {
    public TestTask(string id, string name, TimeRange? time = null) {
        Id = id;
        Name = name;
        Time = time;
    }

    public string Id { get; }
    public string Name { get; }
    public TestAssignmentPolicy? AssignmentPolicy { get; set; }
    public TimeRange? Time { get; }
    public int Index { get; private set; }
    public void SetIndexed() => Index = ManualIndexed.Impl.NextIndex(GetType());
    public bool? PartialEq(IAbstractTask<TestExecutor, TestAssignmentPolicy> rhs) {
        if (ReferenceEquals(this, rhs)) {
            return true;
        }

        if (rhs is not TestTask other) {
            return false;
        }

        return Id == other.Id;
    }
}

public class TestPlannedTask : AbstractPlannedTask<SingleStepTaskPlan<TestExecutor>, TestExecutor, TestAssignmentPolicy> {
    public TestPlannedTask(SingleStepTaskPlan<TestExecutor> plan, TestAssignmentPolicy? policy = null)
        : base(plan, policy) { }
}

public class TestTaskBunch : AbstractTaskBunch<TestTask, TestExecutor, TestAssignmentPolicy> {
    public TestTaskBunch(
        TestExecutor executor,
        ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy> usability,
        IReadOnlyList<TestTask> tasks,
        ICost<Flt64>? cost = null,
        long iteration = -1)
        : base(executor, usability, tasks, cost, iteration) { }
}

public class TestSlotBunch : AbstractTaskBunch<TestTask, TestExecutor, TestAssignmentPolicy>,
    ISlotBasedBunch<TestTask, TestExecutor, TestAssignmentPolicy> {
    public TestSlotBunch(
        TestExecutor executor,
        ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy> usability,
        IReadOnlyList<TestTask> tasks,
        TimeRange slot,
        int slotIndex,
        ICost<Flt64>? cost = null,
        long iteration = -1)
        : base(executor, usability, tasks, cost, iteration) {
        Slot = slot;
        SlotIndex = slotIndex;
    }

    public TimeRange Slot { get; }
    public int SlotIndex { get; }
}

// ===== Tests =====

public class BunchAggregationTests {
    private static readonly TimeRange DefaultTaskTime = new(
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BunchAggregation_Empty_ShouldHaveEmptyCollections() {
        var agg = new BunchAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        agg.Bunches.Should().BeEmpty();
        agg.BunchesIteration.Should().BeEmpty();
        agg.RemovedBunches.Should().BeEmpty();
        agg.LastIterationBunches.Should().BeEmpty();
    }

    [Fact]
    public void BunchAggregation_AddColumns_ShouldAddBunches() {
        var agg = new BunchAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var bunch = new TestTaskBunch(executor, usability, new[] { task }, cost);

        IReadOnlyList<TestTaskBunch> result = agg.AddColumns(new[] { bunch });

        result.Should().HaveCount(1);
        agg.Bunches.Should().HaveCount(1);
        agg.BunchesIteration.Should().HaveCount(1);
    }

    [Fact]
    public void BunchAggregation_AddColumns_Deduplication() {
        var agg = new BunchAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var bunch1 = new TestTaskBunch(executor, usability, new[] { task }, cost);
        var bunch2 = new TestTaskBunch(executor, usability, new[] { task }, cost);

        agg.AddColumns(new[] { bunch1 });
        agg.AddColumns(new[] { bunch2 });

        // Second add should be deduplicated
        agg.Bunches.Should().HaveCount(1);
    }

    [Fact]
    public void BunchAggregation_RemoveColumn_ShouldTrackRemoved() {
        var agg = new BunchAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var bunch = new TestTaskBunch(executor, usability, new[] { task }, cost);

        agg.AddColumns(new[] { bunch });
        agg.RemoveColumn(bunch);

        agg.RemovedBunches.Should().Contain(bunch);
        agg.Bunches.Should().BeEmpty();
    }

    [Fact]
    public void BunchAggregation_Clear_ShouldResetAll() {
        var agg = new BunchAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var bunch = new TestTaskBunch(executor, usability, new[] { task }, cost);

        agg.AddColumns(new[] { bunch });
        agg.Clear();

        agg.Bunches.Should().BeEmpty();
        agg.BunchesIteration.Should().BeEmpty();
        agg.RemovedBunches.Should().BeEmpty();
    }
}

public class SlotBasedBunchAggregationTests {
    private static readonly TimeRange DefaultTaskTime = new(
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public void SlotBasedBunchAggregation_SameColumnAs_ShouldCheckSlot() {
        var agg = new SlotBasedBunchAggregation<TestSlotBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var slot = new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));
        var bunch1 = new TestSlotBunch(executor, usability, new[] { task }, slot, 0, cost);
        var bunch2 = new TestSlotBunch(executor, usability, new[] { task }, slot, 0, cost);

        agg.AddColumns(new[] { bunch1 });
        agg.AddColumns(new[] { bunch2 });

        // Same slot, same tasks -> deduplicated
        agg.Bunches.Should().HaveCount(1);
    }

    [Fact]
    public void SlotBasedBunchAggregation_DifferentSlot_ShouldNotDedup() {
        var agg = new SlotBasedBunchAggregation<TestSlotBunch, TestTask, TestExecutor, TestAssignmentPolicy>();
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task = new TestTask("T1", "Task1", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var slot1 = new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));
        var slot2 = new TimeRange(new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero));
        var bunch1 = new TestSlotBunch(executor, usability, new[] { task }, slot1, 0, cost);
        var bunch2 = new TestSlotBunch(executor, usability, new[] { task }, slot2, 1, cost);

        agg.AddColumns(new[] { bunch1 });
        agg.AddColumns(new[] { bunch2 });

        // Different slots -> not deduplicated
        agg.Bunches.Should().HaveCount(2);
    }
}

public class BunchSolutionTests {
    private static readonly TimeRange DefaultTaskTime = new(
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BunchSolutionSummary_ShouldCalculateCorrectly() {
        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        var task1 = new TestTask("T1", "Task1", DefaultTaskTime);
        var task2 = new TestTask("T2", "Task2", DefaultTaskTime);
        var cost = new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("base", new Flt64(10.0)) }, new Flt64(10.0));
        var bunch = new TestTaskBunch(executor, usability, new[] { task1, task2 }, cost);
        var canceledTask = new TestTask("T3", "Task3");

        var solution = new BunchSolution<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            new[] { bunch },
            new[] { canceledTask });

        solution.Summary.BunchCount.Should().Be(1UL);
        solution.Summary.AssignedTaskCount.Should().Be(2UL);
        solution.Summary.CanceledTaskCount.Should().Be(1UL);
        solution.Summary.TotalTaskCount.Should().Be(3UL);
    }
}

public class BunchCompilationTests {
    [Fact]
    public void BunchCompilation_Constructor_ShouldInitialize() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };

        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);

        compilation.TaskCancelEnabled.Should().BeTrue();
        compilation.WithExecutorLeisure.Should().BeTrue();
        compilation.Bunches.Should().BeEmpty();
    }

    [Fact]
    public void BunchCompilation_Register_ShouldSucceed() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);

        Result<Success, ErrorCode, Error<ErrorCode>> result = compilation.Register(new object());

        result.IsOk.Should().BeTrue();
    }
}

public class SlotBasedBunchCompilationTests {
    [Fact]
    public void SlotBasedBunchCompilation_BunchesInSlot_ShouldReturnEmptyForUnknownSlot() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };
        var slots = new List<TimeRange>
        {
            new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero))
        };

        var compilation = new SlotBasedBunchCompilation<TestSlotBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, slots);

        var unknownSlot = new TimeRange(
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 1, 0, 0, TimeSpan.Zero));
        compilation.BunchesInSlot(unknownSlot).Should().BeEmpty();
    }
}

public class TaskReverseTests {
    [Fact]
    public void TaskReverse_Contains_ShouldReturnFalseForUnknownPair() {
        var reverse = new TaskReverse<TestTask, TestExecutor, TestAssignmentPolicy>(
            Array.Empty<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>(),
            new Dictionary<TaskKey, IReadOnlyList<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>>(),
            new Dictionary<TaskKey, IReadOnlyList<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>>());

        var task1 = new TestTask("T1", "Task1");
        var task2 = new TestTask("T2", "Task2");

        reverse.Contains(task1, task2).Should().BeFalse();
    }

    [Fact]
    public void TaskReverse_LeftFind_ShouldReturnEmptyForUnknownTask() {
        var reverse = new TaskReverse<TestTask, TestExecutor, TestAssignmentPolicy>(
            Array.Empty<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>(),
            new Dictionary<TaskKey, IReadOnlyList<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>>(),
            new Dictionary<TaskKey, IReadOnlyList<ReversiblePair<TestTask, TestExecutor, TestAssignmentPolicy>>>());

        var task = new TestTask("T1", "Task1");
        reverse.LeftFind(task).Should().BeEmpty();
    }
}

public class BunchSchedulingTaskTimeTests {
    [Fact]
    public void BunchSchedulingTaskTime_Properties_ShouldBeCorrect() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true, TimeSpan.FromHours(1));

        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            timeWindow, tasks, compilation);

        taskTime.DelayEnabled.Should().BeTrue();
        taskTime.AdvanceEnabled.Should().BeTrue();
        taskTime.OverMaxDelayEnabled.Should().BeTrue();
        taskTime.OverMaxAdvanceEnabled.Should().BeTrue();
        taskTime.WithRedundancy.Should().BeFalse();
    }
}

public class SlotBasedCapacityResultTests {
    [Fact]
    public void CapacityIntermediateValues_GetSlotConstraints_ShouldReturnNullForUnknownSlot() {
        var slots = new List<TimeRange>();
        var results = new Dictionary<TimeRange, SlotBasedCapacityResult<IProductionAction>>();
        var intermediate = new CapacityIntermediateValues<IProductionAction>(slots, results);

        var unknownSlot = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));
        intermediate.GetSlotConstraints(unknownSlot).Should().BeNull();
    }
}

public class BunchAggregationWithTimeTests {
    [Fact]
    public void BunchCompilationAggregationWithTime_ShouldHaveTaskTimeAndMakespan() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true, TimeSpan.FromHours(1));

        var agg = new BunchCompilationAggregationWithTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            timeWindow, tasks, executors);

        agg.TaskTime.Should().NotBeNull();
        agg.Makespan.Should().NotBeNull();
        agg.Compilation.Should().NotBeNull();
    }
}
