#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchGeneration;

public class GraphTests {
    [Fact]
    public void Graph_PutNode_ShouldStoreNode() {
        var graph = new Graph();
        var node = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);

        graph.Put(node);

        graph.Nodes.Should().HaveCount(1);
        graph.GetNode(1UL).Should().Be(node);
    }

    [Fact]
    public void Graph_PutEdge_ShouldStoreEdge() {
        var graph = new Graph();
        var node1 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);
        var node2 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T2", "Task2"),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            2UL);

        graph.Put(node1);
        graph.Put(node2);
        graph.Put(node1, node2);

        graph.Connected(node1, node2).Should().BeTrue();
        graph.Connected(node2, node1).Should().BeFalse();
    }

    [Fact]
    public void Graph_GetNode_UnknownIndex_ShouldReturnNull() {
        var graph = new Graph();
        graph.GetNode(999UL).Should().BeNull();
    }

    [Fact]
    public void Graph_GetEdges_UnknownNode_ShouldReturnEmpty() {
        var graph = new Graph();
        var node = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);

        graph.GetEdges(node).Should().BeEmpty();
    }

    [Fact]
    public void Graph_Reverse_ShouldSwapEdges() {
        var graph = new Graph();
        var node1 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);
        var node2 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T2", "Task2"),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            2UL);

        graph.Put(node1);
        graph.Put(node2);
        graph.Put(node1, node2);

        Graph reversed = graph.Reverse();

        reversed.Connected(node2, node1).Should().BeTrue();
        reversed.Connected(node1, node2).Should().BeFalse();
    }

    [Fact]
    public void Graph_Reverse_RootNodeBecomesEndNode() {
        var graph = new Graph();
        var node = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);

        graph.Put(RootNode.Instance);
        graph.Put(node);
        graph.Put(RootNode.Instance, node);

        Graph reversed = graph.Reverse();

        // Root->Node becomes Node->End
        reversed.Connected(node, EndNode.Instance).Should().BeTrue();
    }
}

public class NodeTests {
    [Fact]
    public void RootNode_ShouldHaveMinTime() {
        RootNode.Instance.Time.Should().Be(DateTimeOffset.MinValue);
        RootNode.Instance.ToString().Should().Be("Root");
    }

    [Fact]
    public void EndNode_ShouldHaveMaxTime() {
        EndNode.Instance.Time.Should().Be(DateTimeOffset.MaxValue);
        EndNode.Instance.ToString().Should().Be("End");
    }

    [Fact]
    public void TaskNode_ShouldHaveCorrectProperties() {
        var task = new TestTask("T1", "Task1");
        var time = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var node = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(task, time, 42UL);

        node.Task.Should().Be(task);
        node.Time.Should().Be(time);
        node.Index.Should().Be(42UL);
    }
}

public class EdgeTests {
    [Fact]
    public void Edge_ShouldHaveFromAndTo() {
        var node1 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);
        var node2 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T2", "Task2"),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            2UL);
        var edge = new Edge(node1, node2);

        edge.From.Should().Be(node1);
        edge.To.Should().Be(node2);
    }

    [Fact]
    public void Edge_Equality_ShouldWork() {
        var node1 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T1", "Task1"),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1UL);
        var node2 = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            new TestTask("T2", "Task2"),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            2UL);
        var edge1 = new Edge(node1, node2);
        var edge2 = new Edge(node1, node2);

        edge1.Should().Be(edge2);
    }
}

public class LabelTests {
    [Fact]
    public void Label_Constructor_ShouldInitialize() {
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(10.0)) },
            new Flt64(10.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(5.0));

        label.Cost.CostSum.Should().Be(new Flt64(10.0));
        label.ShadowPrice.ToDouble().Should().Be(5.0);
        label.PrevLabel.Should().BeNull();
        label.Node.Should().BeNull();
        label.Task.Should().BeNull();
        label.Trace.Should().BeEmpty();
    }

    [Fact]
    public void Label_ReducedCost_ShouldBeCostMinusShadowPrice() {
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(10.0)) },
            new Flt64(10.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(3.0));

        label.ReducedCost.ToDouble().Should().Be(7.0);
    }

    [Fact]
    public void Label_IsBetterBunch_ShouldBeTrueWhenReducedCostNegative() {
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(2.0)) },
            new Flt64(2.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(5.0));

        label.IsBetterBunch.Should().BeTrue();
    }

    [Fact]
    public void Label_Trace_ShouldTrackTaskNodes() {
        var task = new TestTask("T1", "Task1");
        var node = new TaskNode<TestTask, TestExecutor, TestAssignmentPolicy>(
            task, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 42UL);
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(10.0)) },
            new Flt64(10.0));
        var prevLabel = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(0.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(0.0), prevLabel, node, task);

        label.Trace.Should().Contain(42UL);
    }

    [Fact]
    public void Label_Visited_ShouldReturnFalseForRootAndEnd() {
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(10.0)) },
            new Flt64(10.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(0.0));

        label.Visited(RootNode.Instance).Should().BeFalse();
        label.Visited(EndNode.Instance).Should().BeFalse();
    }

    [Fact]
    public void Label_GenerateBunch_ShouldReturnNullForNonEndNode() {
        var cost = new ImmutableCost<Flt64>(
            new[] { new CostItem<Flt64>("base", new Flt64(10.0)) },
            new Flt64(10.0));
        var label = new Label<TestTask, TestExecutor, TestAssignmentPolicy>(
            cost, new Flt64(0.0));

        var executor = new TestExecutor("E1", "Executor1");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(
            null, DateTimeOffset.Now);

        AbstractTaskBunch<TestTask, TestExecutor, TestAssignmentPolicy>? result = label.GenerateBunch(
            0, executor, usability,
            (e, last, tasks) => new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>(), new Flt64(0.0)));

        result.Should().BeNull();
    }
}

public class PlannedTaskBunchGeneratorTests {
    [Fact]
    public void PlannedTaskBunchGenerator_Constructor_ShouldInitialize() {
        var graph = new Graph();
        var tasks = new List<TestTask> { new("T1", "Task1") };
        var executors = new List<TestExecutor> { new("E1", "Executor1") };

        var generator = new PlannedTaskBunchGenerator<TestTask, TestExecutor, TestAssignmentPolicy>(
            graph, tasks, executors);

        generator.Graph.Should().Be(graph);
        generator.Tasks.Should().HaveCount(1);
        generator.Executors.Should().HaveCount(1);
    }
}

public class UnplannedTaskBunchGeneratorTests {
    [Fact]
    public void UnplannedTaskBunchGenerator_ShouldBeInstantiable() {
        var generator = new UnplannedTaskBunchGenerator();
        generator.Should().NotBeNull();
    }
}
