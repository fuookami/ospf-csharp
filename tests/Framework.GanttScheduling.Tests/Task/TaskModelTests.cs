#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Error;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Task;

public class TaskModelTests {
    [Fact]
    public void GanttSchedulingCapabilityError_WithCapability_ShouldContainMessage() {
        var error = GanttSchedulingCapabilityError.Of("GPU");
        error.Code.Should().Be(ErrorCode.IllegalArgument);
        error.Message.Should().Contain("GPU");
    }

    [Fact]
    public void GanttSchedulingCapabilityError_Unsupported_ShouldHaveDefaultMessage() {
        var error = GanttSchedulingCapabilityError.Unsupported();
        error.Code.Should().Be(ErrorCode.IllegalArgument);
        error.Message.Should().Contain("Capability not supported");
    }

    [Fact]
    public void GanttSchedulingLifecycleError_Default_ShouldHaveDefaultMessage() {
        var error = new GanttSchedulingLifecycleError();
        error.Code.Should().Be(ErrorCode.ApplicationError);
        error.Message.Should().Contain("lifecycle");
    }

    [Fact]
    public void GanttSchedulingSolvingError_WithDetail_ShouldContainDetail() {
        var error = new GanttSchedulingSolvingError("infeasible policy");
        error.Code.Should().Be(ErrorCode.ApplicationFailed);
        error.Message.Should().Be("infeasible policy");
    }

    [Fact]
    public void GanttSchedulingValidationError_WithDetail_ShouldContainDetail() {
        var error = new GanttSchedulingValidationError("bad input");
        error.Code.Should().Be(ErrorCode.IllegalArgument);
        error.Message.Should().Be("bad input");
    }

    [Fact]
    public void TaskType_ShouldBeBasedOnType() {
        var tt = new TaskType(typeof(string));
        tt.TypeName.Should().Be("System.String");
    }

    [Fact]
    public void TaskKey_ShouldHaveIdAndType() {
        var key = new TaskKey("T1", new TaskType(typeof(string)));
        key.Id.Should().Be("T1");
        key.Type.TypeName.Should().Be("System.String");
    }

    [Fact]
    public void TaskKey_Equality_ShouldWork() {
        var k1 = new TaskKey("T1", new TaskType(typeof(string)));
        var k2 = new TaskKey("T1", new TaskType(typeof(string)));
        k1.Should().Be(k2);
    }

    [Fact]
    public void AssignmentPolicy_Full_ShouldBeTrueWhenBothSet() {
        var executor = new Executor("E1", "Executor1");
        var time = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1));
        var policy = new AssignmentPolicy<Executor>(executor, time);
        policy.Full.Should().BeTrue();
        policy.Empty.Should().BeFalse();
    }

    [Fact]
    public void AssignmentPolicy_Empty_ShouldBeTrueWhenNeitherSet() {
        var policy = new AssignmentPolicy<Executor>();
        policy.Full.Should().BeFalse();
        policy.Empty.Should().BeTrue();
    }

    [Fact]
    public void ExecutorChange_ShouldRecordFromAndTo() {
        var e1 = new Executor("E1", "Executor1");
        var e2 = new Executor("E2", "Executor2");
        var change = new ExecutorChange<Executor>(e1, e2);
        change.From.Should().Be(e1);
        change.To.Should().Be(e2);
    }

    [Fact]
    public void CostItem_Valid_ShouldBeTrueWhenCostValueSet() {
        var item = new CostItem<Flt64>("test", new Flt64(10.0));
        item.Valid.Should().BeTrue();
    }

    [Fact]
    public void CostItem_Invalid_ShouldBeFalseWhenCostValueNull() {
        var item = new CostItem<Flt64>("test");
        item.Valid.Should().BeFalse();
    }

    [Fact]
    public void ImmutableCost_ShouldHaveItemsAndSum() {
        var items = new List<CostItem<Flt64>>
        {
            new("a", new Flt64(5.0)),
            new("b", new Flt64(3.0))
        };
        var cost = new ImmutableCost<Flt64>(items, new Flt64(8.0));
        ((ICost<Flt64>)cost).Valid.Should().BeTrue();
        cost.Items.Should().HaveCount(2);
    }

    [Fact]
    public void ImmutableCost_Copy_ShouldCreateIndependentCopy() {
        var items = new List<CostItem<Flt64>> { new("a", new Flt64(5.0)) };
        var cost = new ImmutableCost<Flt64>(items, new Flt64(5.0));
        ICost<Flt64> copy = cost.Copy();
        copy.Should().NotBeSameAs(cost);
    }

    [Fact]
    public void MutableCost_Add_ShouldAccumulate() {
        var cost = new MutableCost<Flt64>();
        cost.Add(new CostItem<Flt64>("a", new Flt64(5.0)));
        cost.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Executor_ShouldHaveIdAndName() {
        var executor = new Executor("E1", "TestExecutor");
        executor.Id.Should().Be("E1");
        executor.Name.Should().Be("TestExecutor");
        executor.ActualId.Should().Be("E1");
        executor.DisplayName.Should().Be("TestExecutor");
    }

    [Fact]
    public void ExecutorInitialUsability_On_ShouldBeTrueWhenLastTaskNotNull() {
        var usability = new ExecutorInitialUsability<string, Executor, AssignmentPolicy<Executor>>(
            "someTask", DateTimeOffset.Now);
        usability.On.Should().BeTrue();
    }

    [Fact]
    public void ExecutorInitialUsability_Off_ShouldBeFalseWhenLastTaskNull() {
        var usability = new ExecutorInitialUsability<string, Executor, AssignmentPolicy<Executor>>(
            null, DateTimeOffset.Now);
        usability.On.Should().BeFalse();
    }

    [Fact]
    public void TaskStatus_Enum_ShouldHaveAllValues() {
        Enum.GetValues<TaskStatus>().Should().Contain(TaskStatus.NotAdvance);
        Enum.GetValues<TaskStatus>().Should().Contain(TaskStatus.NotDelay);
        Enum.GetValues<TaskStatus>().Should().Contain(TaskStatus.NotCancel);
        Enum.GetValues<TaskStatus>().Should().Contain(TaskStatus.Parallelable);
        Enum.GetValues<TaskStatus>().Should().Contain(TaskStatus.Divisible);
    }

    [Fact]
    public void SingleStepTaskPlan_ShouldHaveProperties() {
        var executors = new HashSet<Executor> { new("E1", "Ex1") };
        var status = new HashSet<TaskStatus> { TaskStatus.NotDelay };
        var plan = new SingleStepTaskPlan<Executor>("P1", "Plan1", executors, status);
        plan.Id.Should().Be("P1");
        plan.Name.Should().Be("Plan1");
        plan.EnabledExecutors.Should().HaveCount(1);
        plan.Status.Should().Contain(TaskStatus.NotDelay);
    }

    [Fact]
    public void SingleStepTaskPlan_CancelEnabled_ShouldBeTrueWhenNotCancel() {
        var plan = new SingleStepTaskPlan<Executor>("P1", "Plan1",
            new HashSet<Executor>(), new HashSet<TaskStatus>());
        ((IAbstractTaskPlan<Executor>)plan).CancelEnabled.Should().BeTrue();
    }

    [Fact]
    public void StepRelation_ShouldHaveAndAndOr() {
        Enum.GetValues<StepRelation>().Should().Contain(StepRelation.And);
        Enum.GetValues<StepRelation>().Should().Contain(StepRelation.Or);
    }

    [Fact]
    public void SchedulingSolverValueAdapter_Flt64_ShouldRoundTrip() {
        ISchedulingSolverValueAdapter<Flt64> adapter = SchedulingSolverValueAdapter.Flt64;
        var value = new Flt64(3.7);
        adapter.ToFlt64(value).Should().Be(value);
        adapter.FromFlt64(value).Should().Be(value);
    }

    [Fact]
    public void SchedulingSolverValueAdapter_Flt64_FloorToUInt64() {
        ISchedulingSolverValueAdapter<Flt64> adapter = SchedulingSolverValueAdapter.Flt64;
        adapter.FloorToUInt64(new Flt64(3.7)).Should().Be(4UL); // rounds
    }

    [Fact]
    public void TimeRange_Duration_ShouldBeEndMinusStart() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var range = new TimeRange(start, end);
        range.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void TimeRange_WithIntersection_ShouldDetectOverlap() {
        var r1 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero));
        var r2 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 6, 0, 0, TimeSpan.Zero));
        r1.WithIntersection(r2).Should().BeTrue();
    }

    [Fact]
    public void TimeRange_IntersectionWith_ShouldReturnOverlap() {
        var r1 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero));
        var r2 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 6, 0, 0, TimeSpan.Zero));
        TimeRange? intersection = r1.IntersectionWith(r2);
        intersection.Should().NotBeNull();
        intersection!.Start.Should().Be(new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero));
        intersection.End.Should().Be(new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TimeRange_Contains_ShouldCheckInstant() {
        var range = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero));
        range.Contains(new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        range.Contains(new DateTimeOffset(2024, 1, 1, 5, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void TimeRange_Merge_ShouldMergeOverlapping() {
        var r1 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero));
        var r2 = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
        IReadOnlyList<TimeRange> merged = TimeRange.Merge(new[] { r1, r2 });
        merged.Should().HaveCount(1);
        merged[0].Start.Should().Be(r1.Start);
        merged[0].End.Should().Be(r2.End);
    }

    [Fact]
    public void TimeWindow_TimeSlots_ShouldDivideByInterval() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero);
        var window = new TimeWindow<Flt64>(
            new TimeRange(start, end), true, TimeSpan.FromHours(1));
        window.TimeSlots.Should().HaveCount(4);
    }

    [Fact]
    public void ResourceCapacity_ShouldHaveProperties() {
        var capacity = new ResourceCapacity<Flt64>(
            new TimeRange(), Flt64.Zero, new Flt64(100.0));
        IAbstractResourceCapacity<Flt64> iface = capacity;
        capacity.LowerBound.Should().Be(Flt64.Zero);
        capacity.UpperBound.Should().Be(new Flt64(100.0));
        iface.LessEnabled.Should().BeFalse();
        iface.OverEnabled.Should().BeFalse();
    }

    [Fact]
    public void ResourceCapacity_SolverBounds_ShouldConvert() {
        var capacity = new ResourceCapacity<Flt64>(
            new TimeRange(), new Flt64(10.0), new Flt64(100.0),
            LessQuantity: new Flt64(5.0), OverQuantity: new Flt64(10.0));
        IAbstractResourceCapacity<Flt64> iface = capacity;
        iface.SolverLowerBound().Should().Be(new Flt64(10.0));
        iface.SolverUpperBound().Should().Be(new Flt64(100.0));
        iface.SolverLessQuantity().Should().Be(new Flt64(5.0));
        iface.SolverOverQuantity().Should().Be(new Flt64(10.0));
        iface.SolverValueRangeLower().Should().Be(new Flt64(5.0));
        iface.SolverValueRangeUpper().Should().Be(new Flt64(110.0));
    }

    [Fact]
    public void DurationRange_ShouldHaveLbAndUb() {
        var range = new DurationRange(TimeSpan.FromHours(1), TimeSpan.FromHours(8));
        range.Lb.Should().Be(TimeSpan.FromHours(1));
        range.Ub.Should().Be(TimeSpan.FromHours(8));
    }
}
