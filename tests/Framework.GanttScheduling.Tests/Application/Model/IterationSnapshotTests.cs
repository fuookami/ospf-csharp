#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model.Bunch;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model.Task;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Bunch;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Task;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using System;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Application.Model;
/// <summary>
/// FltX solver 值适配器，将 Flt64 转换为 FltX / FltX solver value adapter converting Flt64 to FltX.
/// </summary>
internal sealed class FltXSolverValueAdapter : ISchedulingSolverValueAdapter<FltX> {
    public Flt64 ToFlt64(FltX value) => value.ToFlt64();
    public FltX FromFlt64(Flt64 value) {
        double d = value.ToDouble();
        if (double.IsPositiveInfinity(d)) {
            return new FltX(decimal.MaxValue);
        }

        if (double.IsNegativeInfinity(d)) {
            return new FltX(decimal.MinValue);
        }

        if (d > (double)decimal.MaxValue) {
            return new FltX(decimal.MaxValue);
        }

        if (d < (double)decimal.MinValue) {
            return new FltX(decimal.MinValue);
        }

        return value.ToFltX();
    }
    public FltX RoundSolution(Flt64 value) => FromFlt64(value);
    public ulong FloorToUInt64(Flt64 value) => (ulong)global::System.Math.Round(value.ToDouble());
    public Flt64 FloorValue(Flt64 value) => new(global::System.Math.Floor(value.ToDouble()));
}

/// <summary>
/// 迭代快照测试辅助任务类型 / Iteration snapshot test helper task type.
/// </summary>
internal sealed class SnapshotTask : IIterativeAbstractTask<Executor, AssignmentPolicy<Executor>> {
    public int Index => 0;
    public string Id => "task";
    public string Name => "task";
    public long Iteration => 0L;
    public AssignmentPolicy<Executor>? AssignmentPolicy => null;
    public Executor? Executor => null;
    public bool? PartialEq(IAbstractTask<Executor, AssignmentPolicy<Executor>> rhs) => ReferenceEquals(this, rhs);
}

/// <summary>
/// 迭代快照测试辅助普通任务类型 / Iteration snapshot test helper plain task type.
/// </summary>
internal sealed class SnapshotPlainTask : IAbstractTask<Executor, AssignmentPolicy<Executor>> {
    public int Index => 0;
    public string Id => "task";
    public string Name => "task";
    public AssignmentPolicy<Executor>? AssignmentPolicy => null;
    public Executor? Executor => null;
    public bool? PartialEq(IAbstractTask<Executor, AssignmentPolicy<Executor>> rhs) => ReferenceEquals(this, rhs);
}

public class IterationSnapshotTests {
    private readonly ISchedulingSolverValueAdapter<FltX> _adapter = new FltXSolverValueAdapter();

    // ===== Direct port from Kotlin IterationSnapshotTest =====

    [Fact]
    public void TaskIterationSnapshot_ShouldExposeQuantityFltXObjectives() {
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();

        iteration.RefreshLpObj(new Flt64(80.0));
        iteration.RefreshIpObj(new Flt64(100.0));

        IterationSnapshot<FltX> snapshot = iteration.Snapshot(_adapter);

        snapshot.BestObjective.Value.Should().Be(new FltX("100.0"));
        snapshot.BestLpObjective.Value.Should().Be(new FltX("80.0"));
        snapshot.LowerBound.Value.Should().Be(new FltX("0.0"));
        snapshot.UpperBound.Should().NotBeNull();
        snapshot.UpperBound!.Value.Should().Be(new FltX("100.0"));
        snapshot.SlowLpImprovementStep.Should().NotBeNull();
    }

    [Fact]
    public void BunchIterationSnapshot_ShouldExposeQuantityFltXObjectives() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();

        iteration.RefreshLpObj(new Flt64(60.0));
        iteration.RefreshIpObj(new Flt64(75.0));

        IterationSnapshot<FltX> snapshot = iteration.Snapshot(_adapter);

        snapshot.BestObjective.Value.Should().Be(new FltX("75.0"));
        snapshot.BestLpObjective.Value.Should().Be(new FltX("60.0"));
        snapshot.LowerBound.Value.Should().Be(new FltX("0.0"));
        snapshot.UpperBound.Should().NotBeNull();
        snapshot.UpperBound!.Value.Should().Be(new FltX("75.0"));
        snapshot.SlowLpImprovementStep.Should().NotBeNull();
    }

    // ===== Extended iteration tests =====

    [Fact]
    public void BunchIteration_InitialState_ShouldHaveZeroIteration() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.Iteration.Should().Be(0UL);
    }

    [Fact]
    public void BunchIteration_Increment_ShouldIncreaseIteration() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.Increment();
        iteration.Iteration.Should().Be(1UL);
        iteration.Increment();
        iteration.Iteration.Should().Be(2UL);
    }

    [Fact]
    public void BunchIteration_Decrement_ShouldDecreaseIteration() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.Increment();
        iteration.Increment();
        iteration.Decrement();
        iteration.Iteration.Should().Be(1UL);
    }

    [Fact]
    public void BunchIteration_RefreshLpObj_FirstCall_ShouldReturnTrue() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        bool improved = iteration.RefreshLpObj(new Flt64(100.0));
        improved.Should().BeTrue();
    }

    [Fact]
    public void BunchIteration_RefreshLpObj_WorseValue_ShouldReturnFalse() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshLpObj(new Flt64(100.0));
        bool improved = iteration.RefreshLpObj(new Flt64(150.0));
        improved.Should().BeFalse();
    }

    [Fact]
    public void BunchIteration_RefreshIpObj_FirstCall_ShouldReturnTrue() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        bool improved = iteration.RefreshIpObj(new Flt64(200.0));
        improved.Should().BeTrue();
    }

    [Fact]
    public void BunchIteration_RefreshIpObj_SameValue_ShouldNotBeImprovement() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshIpObj(new Flt64(200.0));
        // Same value within 0.01 threshold → increments slow count
        bool improved = iteration.RefreshIpObj(new Flt64(200.0));
        improved.Should().BeFalse();
    }

    [Fact]
    public void BunchIteration_HalveStep_ShouldAffectConvergenceThreshold() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        // After first LP refresh, the slow improvement step is initialized
        iteration.RefreshLpObj(new Flt64(1000.0));
        // Halve the step
        iteration.HalveStep();
        // Should still work without error
        iteration.RefreshLpObj(new Flt64(900.0));
    }

    [Fact]
    public void BunchIteration_OptimalRate_ShouldBeBetween0And1() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshIpObj(new Flt64(100.0));
        Flt64 rate = iteration.OptimalRate;
        (rate >= Flt64.Zero).Should().BeTrue();
        (rate <= Flt64.One).Should().BeTrue();
    }

    [Fact]
    public void BunchIteration_IsImprovementSlow_ShouldStartFalse() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.IsImprovementSlow.Should().BeFalse();
    }

    [Fact]
    public void BunchIteration_Snapshot_UpperBoundNull_WhenNoIpObj() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshLpObj(new Flt64(80.0));
        // No IP obj set → upper bound should be null
        IterationSnapshot<FltX> snapshot = iteration.Snapshot(_adapter);
        snapshot.UpperBound.Should().BeNull();
    }

    [Fact]
    public void BunchIteration_Snapshot_SlowLpImprovementStepNull_WhenInfinity() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        // No LP refresh → slowLpImprovementStep is infinity → null in snapshot
        IterationSnapshot<FltX> snapshot = iteration.Snapshot(_adapter);
        snapshot.SlowLpImprovementStep.Should().BeNull();
    }

    [Fact]
    public void BunchIteration_ToString_ShouldReturnIterationCount() {
        var iteration = new BunchIteration<SnapshotPlainTask, Executor, AssignmentPolicy<Executor>>();
        iteration.ToString().Should().Be("0");
        iteration.Increment();
        iteration.ToString().Should().Be("1");
    }

    [Fact]
    public void TaskIteration_InitialState_ShouldHaveZeroIteration() {
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();
        iteration.Iteration.Should().Be(0UL);
    }

    [Fact]
    public void TaskIteration_IncrementDecrement_ShouldWork() {
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();
        iteration.Increment();
        iteration.Increment();
        iteration.Increment();
        iteration.Decrement();
        iteration.Iteration.Should().Be(2UL);
    }

    [Fact]
    public void TaskIteration_RefreshIpObj_BetterValue_ShouldReturnTrue() {
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshIpObj(new Flt64(200.0));
        bool improved = iteration.RefreshIpObj(new Flt64(100.0));
        improved.Should().BeTrue();
    }

    [Fact]
    public void TaskIteration_RunTime_ShouldBePositive() {
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RunTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void TaskIteration_Snapshot_Flt64Adapter_ShouldRoundTrip() {
        ISchedulingSolverValueAdapter<Flt64> flt64Adapter = SchedulingSolverValueAdapter.Flt64;
        var iteration = new TaskIteration<SnapshotTask, Executor, AssignmentPolicy<Executor>>();
        iteration.RefreshLpObj(new Flt64(50.0));
        iteration.RefreshIpObj(new Flt64(80.0));

        IterationSnapshot<Flt64> snapshot = iteration.Snapshot(flt64Adapter);

        snapshot.BestObjective.Value.Should().Be(new Flt64(80.0));
        snapshot.BestLpObjective.Value.Should().Be(new Flt64(50.0));
        snapshot.IsImprovementSlow.Should().BeFalse();
    }

    // ===== Configuration / Policy type tests =====

    [Fact]
    public void BunchBranchAndPriceConfiguration_DefaultValues_ShouldHaveExpectedDefaults() {
        var config = new BunchBranchAndPriceAlgorithm<
            GanttSchedulingShadowPriceMap<Executor, AssignmentPolicy<Executor>>,
            IGanttSchedulingShadowPriceArguments<Executor, AssignmentPolicy<Executor>>,
            AbstractTaskBunch<IAbstractTask<Executor, AssignmentPolicy<Executor>>, Executor, AssignmentPolicy<Executor>>,
            IAbstractTask<Executor, AssignmentPolicy<Executor>>,
            Executor,
            AssignmentPolicy<Executor>>.Configuration();

        config.BadReducedAmount.Should().Be(20UL);
        config.MaximumColumnAmount.Should().Be(50000UL);
        config.MinimumColumnAmountPerExecutor.Should().Be(0UL);
        config.ResolvedTimeLimit.Should().Be(TimeSpan.FromSeconds(30000));
    }

    [Fact]
    public void TaskBranchAndPriceConfiguration_DefaultValues_ShouldHaveExpectedDefaults() {
        var config = new TaskBranchAndPriceAlgorithm<
            GanttSchedulingShadowPriceMap<Executor, AssignmentPolicy<Executor>>,
            IGanttSchedulingShadowPriceArguments<Executor, AssignmentPolicy<Executor>>,
            IIterativeAbstractTask<Executor, AssignmentPolicy<Executor>>,
            IAbstractTask<Executor, AssignmentPolicy<Executor>>,
            Executor,
            AssignmentPolicy<Executor>>.Configuration();

        config.MaxBadReducedAmount.Should().Be(20UL);
        config.MaximumColumnAmount.Should().Be(50000UL);
        config.Solver.Should().BeNull();
        config.ResolvedTimeLimit.Should().Be(TimeSpan.FromSeconds(30000));
    }
}
