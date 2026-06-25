#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;
// ===== Test helper types =====

public class CapTestExecutor : Executor {
    public CapTestExecutor(string id, string name) : base(id, name) { }
}

public class TestProductionAction : IProductionAction {
    public TestProductionAction(string id, string name, CapTestExecutor executor, bool discrete = false, TimeSpan? batchDuration = null) {
        Id = id;
        Name = name;
        Executor = executor;
        Discrete = discrete;
        BatchDuration = batchDuration;
    }

    public string Id { get; }
    public string Name { get; }
    public Executor Executor { get; }
    public bool Discrete { get; }
    public TimeSpan? BatchDuration { get; }

    public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => fromDouble(10.0);
    public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 100;
    public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => new Flt64(1.0);
}

// ===== Tests =====

public class ProductionActionTests {
    [Fact]
    public void TestProductionActionProperties() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor, discrete: true, batchDuration: TimeSpan.FromHours(1));

        action.Id.Should().Be("a1");
        action.Name.Should().Be("Action1");
        ((IProductionAction)action).DisplayName.Should().Be("Action1");
        action.Executor.Should().Be(executor);
        action.Discrete.Should().BeTrue();
        action.BatchDuration.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void TestProductionActionUnitCost() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);

        Flt64 cost = action.UnitCost(DateTimeOffset.Now, d => new Flt64(d));
        cost.Should().Be(new Flt64(10.0));
    }

    [Fact]
    public void TestProductionActionSolverValue() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);

        Flt64 solverCost = ((IProductionAction)action).UnitCostSolverValue(DateTimeOffset.Now);
        solverCost.Should().Be(new Flt64(10.0));
    }
}

public class ActionAllocationTests {
    [Fact]
    public void TestActionAllocationCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slot = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));

        var allocation = new ActionAllocation<TestProductionAction>(
            action, slot, 0, 5, TimeSpan.FromHours(4), Order: 1);

        allocation.Action.Should().Be(action);
        allocation.Slot.Should().Be(slot);
        allocation.SlotIndex.Should().Be(0);
        allocation.Amount.Should().Be(5);
        allocation.Duration.Should().Be(TimeSpan.FromHours(4));
        allocation.Order.Should().Be(1);
    }

    [Fact]
    public void TestActionAllocationDefaultOrder() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slot = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));

        var allocation = new ActionAllocation<TestProductionAction>(
            action, slot, 0, 5, TimeSpan.FromHours(4));

        allocation.Order.Should().Be(0);
    }
}

public class ExecutorCapacityResultTests {
    [Fact]
    public void TestExecutorCapacityResultCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var slot = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));

        var result = new ExecutorCapacityResult(executor, slot, 0, TimeSpan.FromHours(6));

        result.Executor.Should().Be(executor);
        result.Slot.Should().Be(slot);
        result.SlotIndex.Should().Be(0);
        result.TotalDuration.Should().Be(TimeSpan.FromHours(6));
    }
}

public class CapacitySchedulingSolutionTests {
    [Fact]
    public void TestSolutionCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slot = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));

        var allocations = new List<ActionAllocation<TestProductionAction>>
        {
            new(action, slot, 0, 5, TimeSpan.FromHours(4))
        };
        var capacities = new List<ExecutorCapacityResult>
        {
            new(executor, slot, 0, TimeSpan.FromHours(6))
        };

        var solution = new CapacitySchedulingSolution<TestProductionAction>(
            new[] { action }, allocations, capacities);

        solution.Actions.Should().HaveCount(1);
        solution.ActionAllocations.Should().HaveCount(1);
        solution.ExecutorCapacities.Should().HaveCount(1);
    }

    [Fact]
    public void TestSolutionAllocationsBySlot() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slot1 = new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8));
        var slot2 = new TimeRange(DateTimeOffset.Now.AddHours(8), DateTimeOffset.Now.AddHours(16));

        var allocations = new List<ActionAllocation<TestProductionAction>>
        {
            new(action, slot1, 0, 5, TimeSpan.FromHours(4)),
            new(action, slot2, 1, 3, TimeSpan.FromHours(2))
        };

        var solution = new CapacitySchedulingSolution<TestProductionAction>(
            new[] { action }, allocations, Array.Empty<ExecutorCapacityResult>());

        solution.AllocationsInSlot(slot1).Should().HaveCount(1);
        solution.AllocationsInSlot(slot2).Should().HaveCount(1);
        solution.AllocationsForAction(action).Should().HaveCount(2);
    }
}

public class CapacityColumnTests {
    [Fact]
    public void TestCapacityColumnCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        column.Executor.Should().Be(executor);
        column.SlotIndex.Should().Be(0);
        column.Order.Should().Be(0);
        column.ColumnCost.Should().Be(new Flt64(50.0));
    }

    [Fact]
    public void TestCapacityColumnAmountFor() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action1 = new TestProductionAction("a1", "Action1", executor);
        var action2 = new TestProductionAction("a2", "Action2", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action1, 5 }, { action2, 3 } };

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(80.0));

        column.AmountFor(action1).Should().Be(5);
        column.AmountFor(action2).Should().Be(3);
        column.TotalAmount.Should().Be(8);
        column.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void TestCapacityColumnEmpty() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var allocations = new Dictionary<TestProductionAction, ulong>();

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, Flt64.Zero);

        column.IsEmpty.Should().BeTrue();
        column.TotalAmount.Should().Be(0);
    }
}

public class CapacityColumnAggregationTests {
    [Fact]
    public void TestAddColumns() {
        var aggregation = new CapacityColumnAggregation<CapTestExecutor, TestProductionAction>();
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        IReadOnlyList<CapacityColumn<CapTestExecutor, TestProductionAction>> added = aggregation.AddColumns(0, new[] { column });

        added.Should().HaveCount(1);
        aggregation.Columns.Should().HaveCount(1);
        aggregation.ColumnsIteration.Should().HaveCount(1);
    }

    [Fact]
    public void TestAddColumnsDeduplication() {
        var aggregation = new CapacityColumnAggregation<CapTestExecutor, TestProductionAction>();
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };

        var column1 = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));
        var column2 = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        IReadOnlyList<CapacityColumn<CapTestExecutor, TestProductionAction>> added1 = aggregation.AddColumns(0, new[] { column1 });
        IReadOnlyList<CapacityColumn<CapTestExecutor, TestProductionAction>> added2 = aggregation.AddColumns(0, new[] { column2 });

        added1.Should().HaveCount(1);
        added2.Should().HaveCount(0); // Deduplicated
        aggregation.Columns.Should().HaveCount(1);
    }

    [Fact]
    public void TestRemoveColumn() {
        var aggregation = new CapacityColumnAggregation<CapTestExecutor, TestProductionAction>();
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        aggregation.AddColumns(0, new[] { column });
        aggregation.Columns.Should().HaveCount(1);

        aggregation.RemoveColumn(column);
        aggregation.Columns.Should().HaveCount(0);
        aggregation.RemovedColumns.Should().HaveCount(1);
    }

    [Fact]
    public void TestClear() {
        var aggregation = new CapacityColumnAggregation<CapTestExecutor, TestProductionAction>();
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };

        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        aggregation.AddColumns(0, new[] { column });
        aggregation.Clear();

        aggregation.Columns.Should().BeEmpty();
        aggregation.ColumnsIteration.Should().BeEmpty();
        aggregation.RemovedColumns.Should().BeEmpty();
    }
}

public class CapacityCompilationTests {
    [Fact]
    public void TestCapacityCompilationCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        compilation.Executors.Should().HaveCount(1);
        compilation.Actions.Should().HaveCount(1);
        compilation.Slots.Should().HaveCount(1);
    }

    [Fact]
    public void TestCapacityCompilationExtractSolution() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        Result<CapacitySchedulingSolution<TestProductionAction>, ErrorCode, Error<ErrorCode>> result = compilation.ExtractSolution(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class CapacityOrderCompilationTests {
    [Fact]
    public void TestCapacityOrderCompilationCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new CapacityOrderCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow, maxOrderPerSlot: 3);

        compilation.Executors.Should().HaveCount(1);
        compilation.MaxOrderPerSlot.Should().Be(3);
    }

    [Fact]
    public void TestCapacityOrderCompilationExtractSolution() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new CapacityOrderCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow, maxOrderPerSlot: 3);

        Result<CapacitySchedulingSolution<TestProductionAction>, ErrorCode, Error<ErrorCode>> result = compilation.ExtractSolution(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class IterativeCapacityCompilationTests {
    [Fact]
    public void TestIterativeCapacityCompilationCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new IterativeCapacityCompilation<CapTestExecutor, TestProductionAction>(
            new[] { executor }, new[] { action }, slots, timeWindow);

        compilation.Executors.Should().HaveCount(1);
        compilation.ColumnsByExecutor.Should().HaveCount(1);
    }

    [Fact]
    public void TestAddColumns() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new IterativeCapacityCompilation<CapTestExecutor, TestProductionAction>(
            new[] { executor }, new[] { action }, slots, timeWindow);

        var allocations = new Dictionary<TestProductionAction, ulong> { { action, 5 } };
        var column = new CapacityColumn<CapTestExecutor, TestProductionAction>(
            executor, 0, 0, allocations, new Flt64(50.0));

        Result<IReadOnlyList<CapacityColumn<CapTestExecutor, TestProductionAction>>, ErrorCode, Error<ErrorCode>> result = compilation.AddColumns(0, new[] { column });
        result.IsOk.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public void TestExtractSolution() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var compilation = new IterativeCapacityCompilation<CapTestExecutor, TestProductionAction>(
            new[] { executor }, new[] { action }, slots, timeWindow);

        Result<CapacitySchedulingSolution<TestProductionAction>, ErrorCode, Error<ErrorCode>> result = compilation.ExtractSolution(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class CapacitySchedulingAggregationTests {
    [Fact]
    public void TestAggregationCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var aggregation = new CapacitySchedulingAggregation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        aggregation.ActionCount.Should().Be(1);
        aggregation.SlotCount.Should().Be(1);
        aggregation.ExecutorCount.Should().Be(1);
    }

    [Fact]
    public void TestActionsForExecutor() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action1 = new TestProductionAction("a1", "Action1", executor);
        var action2 = new TestProductionAction("a2", "Action2", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));

        var aggregation = new CapacitySchedulingAggregation<TestProductionAction>(
            new[] { action1, action2 }, slots, timeWindow);

        aggregation.ActionsForExecutor("e1").Should().HaveCount(2);
        aggregation.ActionsForExecutor("e2").Should().BeEmpty();
    }
}

public class CapacityCostMinimizationTests {
    [Fact]
    public void TestCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        var minimization = new CapacityCostMinimization<TestProductionAction>(
            compilation, new[] { action }, slots, timeWindow);

        minimization.Name.Should().Be("capacity_cost_minimization");
        minimization.Lazy.Should().BeFalse();
    }

    [Fact]
    public void TestInvoke() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        var minimization = new CapacityCostMinimization<TestProductionAction>(
            compilation, new[] { action }, slots, timeWindow);

        Result<Success, ErrorCode, Error<ErrorCode>> result = minimization.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class ExecutorCapacityConstraintTests {
    [Fact]
    public void TestCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        var constraint = new ExecutorCapacityConstraint<TestProductionAction>(
            compilation, slots, timeWindow);

        constraint.Name.Should().Be("executor_capacity");
        constraint.Lazy.Should().BeFalse();
    }

    [Fact]
    public void TestInvoke() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow);

        var constraint = new ExecutorCapacityConstraint<TestProductionAction>(
            compilation, slots, timeWindow);

        Result<Success, ErrorCode, Error<ErrorCode>> result = constraint.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}

public class OrderConstraintTests {
    [Fact]
    public void TestCreation() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityOrderCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow, maxOrderPerSlot: 3);

        var constraint = new OrderConstraint<TestProductionAction>(
            compilation, new[] { action }, slots, 3);

        constraint.Name.Should().Be("order");
        constraint.Lazy.Should().BeFalse();
    }

    [Fact]
    public void TestInvoke() {
        var executor = new CapTestExecutor("e1", "Executor1");
        var action = new TestProductionAction("a1", "Action1", executor);
        var slots = new List<TimeRange>
        {
            new(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(8))
        };
        var timeWindow = new TimeWindow<Flt64>(
            new TimeRange(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24)),
            false, TimeSpan.FromHours(8));
        var compilation = new CapacityOrderCompilation<TestProductionAction>(
            new[] { action }, slots, timeWindow, maxOrderPerSlot: 3);

        var constraint = new OrderConstraint<TestProductionAction>(
            compilation, new[] { action }, slots, 3);

        Result<Success, ErrorCode, Error<ErrorCode>> result = constraint.Invoke(new object());
        result.IsOk.Should().BeTrue();
    }
}
