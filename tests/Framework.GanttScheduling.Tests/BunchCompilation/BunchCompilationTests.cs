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

    /// <summary>任务时间窗 / The task's time window.</summary>
    /// <remarks>
    /// 构造时可给，之后也可设。完工时间的判据需要**不同任务在不同时刻结束**——全部相同就分不出
    /// "取了最大值"和"取了任意一个"。
    ///
    /// Settable at construction and afterwards. The makespan criteria need **tasks ending at different times** — with all equal there is no telling "took the maximum" from
    /// "took any one of them".
    /// </remarks>
    public TimeRange? Time { get; set; }
    /// <summary>分配的执行者 / The assigned executor.</summary>
    /// <remarks>
    /// 默认 `null`（与接口默认一致）。迭代加列的判据需要它能被设上——**列的执行器是跨轮冲突与 `xor` 约束的依据**，
    /// 没有它就没法判"这一列属于哪个执行器"。
    ///
    /// Defaults to `null` (matching the interface default). The iterative add-columns criteria need it settable — **a column's executor drives the cross-round conflicts and the
    /// `xor` constraints**, and without it there is no way to tell which executor a column belongs to.
    /// </remarks>
    public TestExecutor? Executor { get; set; }

    /// <summary>可用执行器 / The enabled executors.</summary>
    /// <remarks>
    /// 接口默认返回**空集**，而空集会把每个组合都固定为 0——于是 `taskCompilation == 1` 立刻不可行。
    /// 默认保持空集（既有判据依赖"空集 = 全禁止"），需要可行的判据自己填上。
    ///
    /// The interface default is an **empty** set, and an empty set pins every combination to 0 — which makes `taskCompilation == 1` immediately infeasible. The default stays empty
    /// (existing criteria rely on "empty = all forbidden"); criteria that need feasibility fill it in themselves.
    /// </remarks>
    public ISet<TestExecutor> EnabledExecutorSet { get; } = new HashSet<TestExecutor>();

    /// <inheritdoc/>
    public ISet<TestExecutor> EnabledExecutors => EnabledExecutorSet;

    /// <summary>是否允许取消 / Whether the task may be cancelled.</summary>
    /// <remarks>
    /// 接口默认 `false`，与"默认不可取消"一致。取消成本判据需要它能被设上：不允许取消的任务 `y == 0`，
    /// 计不进取消成本——这条分支没有可点的开关就永远测不到。
    ///
    /// The interface defaults to `false`, matching "not cancellable by default". The cancellation-cost criteria need it settable: a task that cannot cancel has `y == 0` and cannot
    /// be charged, and that branch is unreachable without a switch.
    /// </remarks>
    public bool CancelEnabled { get; set; }

    /// <summary>是否允许提前 / Whether advance is enabled.</summary>
    /// <remarks>
    /// 接口默认 `false`。开始时间变量的判据需要它能被设上：**不允许提前时 `est` 被钉在时间窗开始**，
    /// 允许时下界才放开——两种情况下 `est` 的边界完全不同，没有开关就测不到"放开"那一半。
    ///
    /// The interface defaults to `false`. The start-time-variable criteria need it settable: **with advance disabled `est` is pinned to the window start**, and only when advance is
    /// allowed does the lower bound relax — the two cases give completely different bounds, and without the switch the "relaxed" half is unreachable.
    /// </remarks>
    public bool AdvanceEnabled { get; set; }

    /// <summary>是否允许延迟 / Whether delay is enabled.</summary>
    /// <remarks>同上，作用于 `est` 的上界 / Same as above, acting on the upper bound of `est`.</remarks>
    public bool DelayEnabled { get; set; }

    /// <summary>最晚结束时间 / The last allowed end time.</summary>
    /// <remarks>
    /// 接口默认 `null`。准时判据需要它能被设上：`on_last_end_time` 只在**任务允许延迟且给了最晚结束时间**时才是一个真指示，
    /// 否则是常量零——没有这个开关那条分支永远测不到。
    ///
    /// The interface defaults to `null`. The on-time criteria need it settable: `on_last_end_time` is a real indicator only when **the task may delay and a last end time is
    /// given**, otherwise constant zero — without this switch that branch is unreachable.
    /// </remarks>
    public DateTimeOffset? LastEndTime { get; set; }

    /// <summary>最大允许延迟 / The maximum allowed delay.</summary>
    /// <remarks>
    /// 接口默认 `null`（= 没有上限）。超最大延迟的判据需要它可设：没有上限就没有"超限"，而那正是要测的一条分支。
    ///
    /// The interface defaults to `null` (= no limit). The over-max delay criteria need it settable: without a limit there is no violation, and that is exactly one branch to pin.
    /// </remarks>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>最大允许提前 / The maximum allowed advance.</summary>
    /// <remarks>同上，作用于 `over_max_advance_time` / Same as above, acting on `over_max_advance_time`.</remarks>
    public TimeSpan? MaxAdvance { get; set; }

    /// <summary>最早结束时间 / The earliest allowed end time.</summary>
    /// <remarks>同上，作用于 `on_earliest_end_time` / Same as above, acting on `on_earliest_end_time`.</remarks>
    public DateTimeOffset? EarliestEndTime { get; set; }

    /// <summary>排定时间 / The scheduled time.</summary>
    /// <remarks>
    /// 接口默认 `null`。延迟/提前两条目标都要求**有排定时间**（偏移量相对什么？），所以这个开关必须能设，
    /// 否则"没有基准就被排除"这条判据永远测不到。
    ///
    /// The interface defaults to `null`. Both the delay and advance objectives require **a scheduled time** (deviation relative to what?), so this must be settable — otherwise
    /// "no reference means excluded" is unreachable.
    /// </remarks>
    public TimeRange? ScheduledTime { get; set; }

    public int Index { get; private set; }
    public void SetIndexed() => Index = ManualIndexed.Impl.NextIndex(GetType());

    // 报错信息里要能认出是哪个任务；默认的 `ToString` 只给类型名，等于什么都没说。
    // Error messages must identify which task; the default `ToString` yields only the type name, which says nothing.
    public override string ToString() => Id;
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
    public void BunchCompilation_Register_ShouldRejectANonModelAndBuildTheColumnsOnAModel() {
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);

        // 这条判据原来是"注册到 `new object` 也返回 Ok"——那正是这里删掉的静默成功。
        // 接线之后它的意义变成:非模型仍然失败,注册到模型才建列。
        // This criterion used to be "registering a `new object` also returns Ok" — exactly the silent success removed in. Once wired it means: a non-model still
        // fails, and only registering on a model builds the columns.
        Result<Success, ErrorCode, Error<ErrorCode>> result = compilation.Register(new object());
        result.IsOk.Should().BeFalse("a non-model must be rejected");

        var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.Cancellation.Should().ContainKey((Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.IAbstractTask<TestExecutor, TestAssignmentPolicy>)tasks[0]);
        compilation.TaskCompilationTable.Should().NotBeEmpty("`task_compilation_t = y[t]` is what the deviation masks read");
        compilation.ExecutorCompilation.Should().ContainKey(executors[0]);
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
