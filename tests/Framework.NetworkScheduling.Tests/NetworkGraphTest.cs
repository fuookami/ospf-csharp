// 网络图测试 / Tests for the immutable network graph.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络图 / The immutable network graph.
/// </summary>
public sealed class NetworkGraphTest {
    /// <summary>合法图构造成功 / A valid graph constructs.</summary>
    [Fact]
    public void Invoke_ShouldBuildGraph() =>
        Graph(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e1", "a", "b", 1.0) }).IsOk.Should().BeTrue();

    /// <summary>空图合法 / An empty graph is legal.</summary>
    [Fact]
    public void Invoke_ShouldAcceptEmpty() =>
        Graph(Array.Empty<NetworkNode<FltX>>(), Array.Empty<NetworkArc<FltX>>()).IsOk.Should().BeTrue();

    /// <summary>重复节点标识被拒绝 / Duplicate node identities are rejected.</summary>
    [Fact]
    public void Invoke_ShouldRejectDuplicateNodeId() =>
        Graph(new[] { Node("a"), Node("a") }, Array.Empty<NetworkArc<FltX>>()).IsOk.Should().BeFalse();

    /// <summary>
    /// 空节点标识被拒绝。**必须绕过 `NetworkNode.Invoke` 的校验**才能构造出空标识的节点——用默认的 `NodeId` 提取器会让
    /// `NetworkNode` 自己先拒绝，测不到 `NetworkGraph` 这一层。因此这里改用自定义访问器直接从字段取值。
    /// A blank node identity is rejected. **`NetworkNode.Invoke`'s own validation must be bypassed** to build a node with a
    /// blank identity — the default `NodeId` selector would let `NetworkNode` reject it first and never exercise
    /// `NetworkGraph`. A custom accessor reading the field directly is used instead.
    /// </summary>
    [Fact]
    public void Invoke_ShouldRejectBlankNodeId() =>
        NetworkGraph<NetworkNodeId, NetworkNodeId>.Invoke(
            new[] { new NetworkNodeId("a"), new NetworkNodeId(" ") },
            Array.Empty<NetworkNodeId>(),
            new NetworkGraphAccess<NetworkNodeId, NetworkNodeId>(
                NodeId: node => node,
                ArcId: arc => new NetworkArcId(arc.Value),
                From: arc => arc,
                To: arc => arc)).IsOk.Should().BeFalse();

    /// <summary>重复弧标识被拒绝 / Duplicate arc identities are rejected.</summary>
    [Fact]
    public void Invoke_ShouldRejectDuplicateArcId() =>
        Graph(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e", "a", "b", 1.0), Arc("e", "a", "b", 2.0) }).IsOk.Should().BeFalse();

    /// <summary>
    /// **弧引用缺失节点被拒绝**：只查标识唯一性不够，一条指向不存在节点的弧会让后续遍历反复尝试解析节点。
    /// **An arc referencing a missing node is rejected**: uniqueness alone is not enough, and such an arc makes later
    /// traversals keep trying to resolve an absent node.
    /// </summary>
    [Fact]
    public void Invoke_ShouldRejectMissingNodeReference() =>
        Graph(new[] { Node("a") }, new[] { Arc("e", "a", "ghost", 1.0) }).IsOk.Should().BeFalse();

    /// <summary>节点索引可查 / The node index is queryable.</summary>
    [Fact]
    public void IndexOf_ShouldFindNode() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            Graph(new[] { Node("a"), Node("b") }, Array.Empty<NetworkArc<FltX>>()).Value;

        graph.IndexOf(new NetworkNodeId("a")).Should().Be(0);
        graph.IndexOf(new NetworkNodeId("b")).Should().Be(1);
    }

    /// <summary>不存在的节点索引为 null / A missing node index is null.</summary>
    [Fact]
    public void IndexOf_ShouldReturnNullForMissing() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            Graph(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>()).Value;

        graph.IndexOf(new NetworkNodeId("ghost")).Should().BeNull();
    }

    /// <summary>按标识查询节点 / Nodes are queryable by identity.</summary>
    [Fact]
    public void Node_ShouldLookUp() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            Graph(new[] { Node("a") }, Array.Empty<NetworkArc<FltX>>()).Value;

        graph.Node(new NetworkNodeId("a")).Should().NotBeNull();
    }

    /// <summary>按标识查询弧 / Arcs are queryable by identity.</summary>
    [Fact]
    public void Arc_ShouldLookUp() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            Graph(new[] { Node("a"), Node("b") }, new[] { Arc("e", "a", "b", 1.0) }).Value;

        graph.Arc(new NetworkArcId("e")).Should().NotBeNull();
    }

    /// <summary>出弧按起点分组 / Outgoing arcs are grouped by origin.</summary>
    [Fact]
    public void Outgoing_ShouldGroupByFrom() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "a", "c", 2.0), Arc("e3", "b", "c", 3.0) }).Value;

        graph.Outgoing(new NetworkNodeId("a")).Should().HaveCount(2);
        graph.Outgoing(new NetworkNodeId("b")).Should().ContainSingle();
        graph.Outgoing(new NetworkNodeId("c")).Should().BeEmpty();
    }

    /// <summary>入弧按终点分组 / Incoming arcs are grouped by destination.</summary>
    [Fact]
    public void Incoming_ShouldGroupByTo() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "c", 1.0), Arc("e2", "b", "c", 2.0) }).Value;

        graph.Incoming(new NetworkNodeId("c")).Should().HaveCount(2);
    }

    /// <summary>过滤弧后保留原标识 / Filtering arcs preserves their identities.</summary>
    [Fact]
    public void Filtered_ShouldPreserveArcIds() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 2.0) }).Value;

        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> filtered =
            graph.Filtered(arc => arc.Id.Value == "e2");

        filtered.Arcs.Should().ContainSingle();
        filtered.ArcIds.Should().ContainSingle().Which.Value.Should().Be("e2");
    }

    /// <summary>过滤节点时关联弧一并移除 / Filtering nodes also drops incident arcs.</summary>
    [Fact]
    public void FilteredNodes_ShouldDropIncidentArcs() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "b", 1.0), Arc("e2", "b", "c", 2.0) }).Value;

        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> filtered =
            graph.FilteredNodes(node => node.Id.Value != "b");

        filtered.Nodes.Should().HaveCount(2);
        filtered.Arcs.Should().BeEmpty("两条弧都关联被删节点 b / both arcs touch the removed node b");
    }

    /// <summary>过滤节点后标识列表与节点列表对齐 / Identity list stays aligned with the node list.</summary>
    [Fact]
    public void FilteredNodes_ShouldKeepIdsAligned() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b"), Node("c") },
            new[] { Arc("e1", "a", "c", 1.0) }).Value;

        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> filtered =
            graph.FilteredNodes(node => node.Id.Value != "b");

        filtered.Nodes.Should().HaveCount(2);
        filtered.NodeIds.Should().HaveCount(2);
        filtered.Arcs.Should().ContainSingle("a→c 两端都保留 / a→c keeps both endpoints");
    }

    /// <summary>
    /// **反向视图互换起点与终点，但载荷与标识不变**：这是 `Reverse` 的全部效果。
    /// **A reversed view swaps origin and destination while payloads and identities stay unchanged**: the entire effect.
    /// </summary>
    [Fact]
    public void Reverse_ShouldSwapEndpoints() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e", "a", "b", 1.0) }).Value;

        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> reversed = graph.Reverse();
        NetworkArc<FltX> arc = graph.Arc(new NetworkArcId("e"))!;

        reversed.From(arc).Value.Should().Be("b");
        reversed.To(arc).Value.Should().Be("a");
        reversed.ArcId(arc).Value.Should().Be("e", "标识不变 / the identity is unchanged");
    }

    /// </// <summary>反向视图的出弧变成原图的入弧 / Outgoing arcs in the reversed view are the original incoming arcs.</summary>
    [Fact]
    public void Reverse_ShouldFlipAdjacency() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e", "a", "b", 1.0) }).Value;

        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> reversed = graph.Reverse();

        reversed.Outgoing(new NetworkNodeId("b")).Should().ContainSingle();
        reversed.Outgoing(new NetworkNodeId("a")).Should().BeEmpty();
    }

    /// <summary>双重反转回到原方向 / Reversing twice restores the original direction.</summary>
    [Fact]
    public void Reverse_ShouldBeInvolution() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph = Graph(
            new[] { Node("a"), Node("b") },
            new[] { Arc("e", "a", "b", 1.0) }).Value;

        NetworkArc<FltX> arc = graph.Arc(new NetworkArcId("e"))!;
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> twice = graph.Reverse().Reverse();

        twice.From(arc).Value.Should().Be("a");
    }

    /// <summary>节点标识快照可查 / The node identity snapshot is queryable.</summary>
    [Fact]
    public void NodeIds_ShouldMatchNodes() {
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            Graph(new[] { Node("a"), Node("b") }, Array.Empty<NetworkArc<FltX>>()).Value;

        graph.Nodes.Should().HaveCount(2);
        graph.NodeIds.Select(id => id.Value).Should().Equal(new[] { "a", "b" });
    }

    /// <summary>构造图 / Build a graph.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>,
        Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Graph(
        IReadOnlyList<NetworkNode<FltX>> nodes,
        IReadOnlyList<NetworkArc<FltX>> arcs) =>
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(nodes, arcs);

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
