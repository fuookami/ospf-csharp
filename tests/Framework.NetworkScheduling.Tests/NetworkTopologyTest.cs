// 拓扑算法测试 / Tests for the topology algorithms.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 拓扑算法 / Topology algorithms.
/// </summary>
public sealed class NetworkTopologyTest {
    /// <summary>多源可达覆盖下游节点 / Multi-source reachability covers downstream nodes.</summary>
    [Fact]
    public void Reachable_ShouldCoverDownstream() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 1.0) });

        IReadOnlySet<NetworkNodeId> reachable = topology
            .Reachable(new[] { new NetworkNodeId("a") }).Value;

        reachable.Select(id => id.Value).Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    /// <summary>多源可达合并起点 / Multi-source reachability merges sources.</summary>
    [Fact]
    public void Reachable_ShouldMergeSources() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "c", 1.0), Arc("e2", "b", "c", 1.0) });

        IReadOnlySet<NetworkNodeId> reachable = topology
            .Reachable(new[] { new NetworkNodeId("a"), new NetworkNodeId("b") }).Value;

        reachable.Should().HaveCount(3);
    }

    /// <summary>
    /// **起点不存在时报错而不是返回空集**：返回空集会让调用方以为"这个起点没有可达节点"，而真实原因是拼写错误。
    /// **A missing source is an error rather than an empty set**: an empty set would read as "this source reaches nothing",
    /// when the real cause is a typo.
    /// </summary>
    [Fact]
    public void Reachable_ShouldRejectMissingSource() =>
        TopologyOf(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>())
            .Reachable(new[] { new NetworkNodeId("ghost") }).IsOk.Should().BeFalse();

    /// <summary>可达性尊重节点过滤 / Reachability respects the node filter.</summary>
    [Fact]
    public void Reachable_ShouldRespectNodeFilter() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 1.0) });

        IReadOnlySet<NetworkNodeId> reachable = topology
            .Reachable(new[] { new NetworkNodeId("a") }, allowedNode: id => id.Value != "b").Value;

        reachable.Select(id => id.Value).Should().BeEquivalentTo(new[] { "a" });
    }

    /// <summary>无权见证给出有序弧 / The unweighted witness yields ordered arcs.</summary>
    [Fact]
    public void Witness_ShouldReturnArcs() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 1.0) });

        IReadOnlyList<NetworkArc<FltX>>? path = topology
            .Witness(new NetworkNodeId("a"), new NetworkNodeId("c")).Value;

        path.Should().NotBeNull();
        path!.Select(arc => arc.Id.Value).Should().Equal(new[] { "e1", "e2" });
    }

    /// <summary>不可达时见证为 null / The witness is null when unreachable.</summary>
    [Fact]
    public void Witness_ShouldReturnNullWhenUnreachable() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0) });

        topology.Witness(new NetworkNodeId("b"), new NetworkNodeId("a")).Value.Should().BeNull();
    }

    /// <summary>起点等于终点时路径为空而非 null / Source equals target yields an empty path, not null.</summary>
    [Fact]
    public void Witness_ShouldReturnEmptyForSameEndpoint() =>
        TopologyOf(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>())
            .Witness(new NetworkNodeId("a"), new NetworkNodeId("a")).Value.Should().BeEmpty();

    /// <summary>端点不存在时报错 / A missing endpoint is an error.</summary>
    [Fact]
    public void Witness_ShouldRejectMissingEndpoint() =>
        TopologyOf(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>())
            .Witness(new NetworkNodeId("a"), new NetworkNodeId("ghost")).IsOk.Should().BeFalse();

    /// <summary>有向无环图给出拓扑序 / A DAG yields a topological order.</summary>
    [Fact]
    public void TopologicalOrder_ShouldOrderDag() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 1.0) });

        IReadOnlyList<NetworkNodeId>? order = topology.TopologicalOrder();

        order.Should().NotBeNull();
        order!.Select(id => id.Value).Should().Equal(new[] { "a", "b", "c" });
    }

    /// <summary>
    /// **有环时返回 null 而不是部分顺序**：部分顺序看起来像一个合法答案，下游无法区分它与完整排序。
    /// **A cycle yields null rather than a partial order**: a partial order looks like a legitimate answer and downstream
    /// cannot tell it apart from a complete one.
    /// </summary>
    [Fact]
    public void TopologicalOrder_ShouldReturnNullForCycle() =>
        TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "a", 1.0) }).TopologicalOrder().Should().BeNull();

    /// <summary>无环图无环 / A DAG has no cycle.</summary>
    [Fact]
    public void HasCycle_ShouldBeFalseForDag() =>
        TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0) }).HasCycle().Should().BeFalse();

    /// <summary>环图有环 / A cyclic graph has a cycle.</summary>
    [Fact]
    public void HasCycle_ShouldBeTrueForCycle() =>
        TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "a", 1.0) }).HasCycle().Should().BeTrue();

    /// <summary>强连通分量把环聚为一片 / SCCs group a cycle into one component.</summary>
    [Fact]
    public void Scc_ShouldGroupCycle() {
        IReadOnlyList<IReadOnlySet<NetworkNodeId>> components = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "a", 1.0), Arc("e3", "b", "c", 1.0) })
            .StronglyConnectedComponents();

        components.Should().HaveCount(2);
        components.Should().Contain(component => component.Count == 2);
    }

    /// <summary>无环图的每个节点各成一个分量 / A DAG gives one component per node.</summary>
    [Fact]
    public void Scc_ShouldSplitDag() =>
        TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0) }).StronglyConnectedComponents().Should().HaveCount(2);

    /// <summary>
    /// **长链不栈溢出**：递归实现在数万节点上会崩，因此实现是迭代式。这里用 5000 节点链验证。
    /// **A long chain must not overflow the stack**: a recursive implementation dies at tens of thousands of nodes, so the
    /// implementation is iterative. A 5000-node chain checks it.
    /// </summary>
    [Fact]
    public void Scc_ShouldHandleLongChain() {
        var nodes = Enumerable.Range(0, 5000).Select(index => Node($"n{index}")).ToList();
        var arcs = Enumerable.Range(0, 4999)
            .Select(index => Arc($"e{index}", $"n{index}", $"n{index + 1}", 1.0))
            .ToList();

        IReadOnlyList<IReadOnlySet<NetworkNodeId>> components = TopologyOf(nodes, arcs).StronglyConnectedComponents();

        components.Should().HaveCount(5000);
    }

    /// <summary>最短路找到路径与成本 / The shortest path finds a path and cost.</summary>
    [Fact]
    public void ShortestPath_ShouldFindPath() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = ShortestPathOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 2.0) },
            "a", "c").Value;

        result.Status.Should().Be(ShortestPathStatus.Found);
        result.Arcs.Select(arc => arc.Id.Value).Should().Equal(new[] { "e1", "e2" });
        result.Cost!.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>最短路选成本更低的那条 / The shortest path picks the cheaper route.</summary>
    [Fact]
    public void ShortestPath_ShouldPickCheaper() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = ShortestPathOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[]
            {
                Arc("direct", "a", "c", 10.0),
                Arc("via1", "a", "b", 1.0),
                Arc("via2", "b", "c", 1.0),
            },
            "a", "c").Value;

        result.Cost!.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>不可达时终态为 Unreachable / An unreachable target reports Unreachable.</summary>
    [Fact]
    public void ShortestPath_ShouldReportUnreachable() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = ShortestPathOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0) },
            "b", "a").Value;

        result.Status.Should().Be(ShortestPathStatus.Unreachable);
        result.Cost.Should().BeNull();
    }

    /// <summary>
    /// **能影响目标的负环报 NegativeCycle**：负环会让最短路无下界。
    /// **A negative cycle affecting the target reports NegativeCycle**: it leaves the shortest path unbounded below.
    /// </summary>
    [Fact]
    public void ShortestPath_ShouldDetectNegativeCycle() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = ShortestPathOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[]
            {
                Arc("e1", "a", "b", 1.0),
                Arc("e2", "b", "c", -3.0),
                Arc("e3", "c", "b", 1.0),
            },
            "a", "c").Value;

        result.Status.Should().Be(ShortestPathStatus.NegativeCycle);
    }

    /// <summary>
    /// **与目标无关的负环不该毒化整趟查询**：只报"能影响目标"的负环。
    /// **A negative cycle unrelated to the target must not poison the query**: only cycles that can affect the target are
    /// reported.
    /// </summary>
    [Fact]
    public void ShortestPath_ShouldIgnoreIrrelevantNegativeCycle() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = ShortestPathOf(
            new[] { Node("a"), Node("b"), Node("x"), Node("y") },
            new[]
            {
                Arc("main", "a", "b", 1.0),
                Arc("loop1", "x", "y", -1.0),
                Arc("loop2", "y", "x", -1.0),
            },
            "a", "b").Value;

        result.Status.Should().Be(ShortestPathStatus.Found);
    }

    /// <summary>端点不存在时报错 / A missing endpoint is an error.</summary>
    [Fact]
    public void ShortestPath_ShouldRejectMissingEndpoint() =>
        TopologyOf(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>())
            .ShortestPath(
                new NetworkNodeId("a"), new NetworkNodeId("ghost"),
                new FltX(0), SIBaseUnits.Meter, arc => Results.Ok(arc.Cost))
            .IsOk.Should().BeFalse();

    /// <summary>节点被过滤掉时报告不可达 / A filtered-out node reports unreachable.</summary>
    [Fact]
    public void ShortestPath_ShouldRespectNodeFilter() {
        ShortestPathResult<NetworkArc<FltX>, FltX> result = TopologyOf(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 1.0) })
            .ShortestPath(
                new NetworkNodeId("a"), new NetworkNodeId("c"),
                new FltX(0), SIBaseUnits.Meter, arc => Results.Ok(arc.Cost),
                allowedNode: id => id.Value != "b").Value;

        result.Status.Should().Be(ShortestPathStatus.Unreachable);
    }

    /// <summary>过滤视图只含允许弧 / The filtered view keeps only allowed arcs.</summary>
    [Fact]
    public void Filtered_ShouldDropArcs() {
        NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> topology = TopologyOf(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "a", "b", 2.0) });

        topology.Filtered(allowedArc: arc => arc.Id.Value == "e1").Graph.Arcs.Should().ContainSingle();
    }

    /// <summary>构造拓扑 / Build a topology.</summary>
    private static NetworkTopology<NetworkNode<FltX>, NetworkArc<FltX>> TopologyOf(
        IReadOnlyList<NetworkNode<FltX>> nodes,
        IReadOnlyList<NetworkArc<FltX>> arcs) =>
        new(NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(nodes, arcs).Value);

    /// <summary>执行最短路 / Run a shortest path.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        ShortestPathResult<NetworkArc<FltX>, FltX>, ErrorCode, Error<ErrorCode>> ShortestPathOf(
        IReadOnlyList<NetworkNode<FltX>> nodes,
        IReadOnlyList<NetworkArc<FltX>> arcs,
        string source,
        string target) =>
        TopologyOf(nodes, arcs).ShortestPath(
            new NetworkNodeId(source), new NetworkNodeId(target),
            new FltX(0), SIBaseUnits.Meter, arc => Results.Ok(arc.Cost));

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId(id)).Value;

    /// <summary>构造弧 / Build an arc.</summary>
    private static NetworkArc<FltX> Arc(string id, string from, string to, double cost) =>
        NetworkArc<FltX>.Invoke(
            new NetworkNodeId(from), new NetworkNodeId(to), Meters(cost),
            id: new NetworkArcId(id)).Value;

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
