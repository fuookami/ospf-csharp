// 网络流模型测试 / Tests for the flow models.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络流模型 / The flow models.
/// </summary>
public sealed class FlowModelTest {
    /// <summary>由网络弧与容量构造流弧 / A flow arc is built from a network arc and capacity.</summary>
    [Fact]
    public void Arc_ShouldConstructFromNetworkArc() =>
        FlowArc<FltX>.Invoke(NetworkArc(), Bounds(0.0, 10.0)).IsOk.Should().BeTrue();

    /// <summary>便捷构造透传网络弧校验 / The convenience factory forwards network-arc validation.</summary>
    [Fact]
    public void Arc_ShouldForwardNetworkArcValidation() =>
        FlowArc<FltX>.Invoke(
            new NetworkNodeId("n1"), new NetworkNodeId("n2"), Meters(1.0), Bounds(0.0, 10.0),
            id: new NetworkArcId(" ")).IsOk.Should().BeFalse();

    /// <summary>流弧暴露网络弧的起点终点 / The flow arc exposes the network arc's endpoints.</summary>
    [Fact]
    public void Arc_ShouldExposeEndpoints() {
        FlowArc<FltX> arc = FlowArc<FltX>.Invoke(NetworkArc(), Bounds(0.0, 10.0)).Value;

        arc.From.Value.Should().Be("n1");
        arc.To.Value.Should().Be("n2");
        arc.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>平行弧可用显式标识区分 / Parallel arcs are distinguished by explicit identity.</summary>
    [Fact]
    public void Arc_ShouldDistinguishParallelArcs() {
        FlowArc<FltX> first = FlowArc<FltX>.Invoke(
            new NetworkNodeId("n1"), new NetworkNodeId("n2"), Meters(1.0), Bounds(0.0, 10.0),
            id: new NetworkArcId("a1")).Value;
        FlowArc<FltX> second = FlowArc<FltX>.Invoke(
            new NetworkNodeId("n1"), new NetworkNodeId("n2"), Meters(1.0), Bounds(0.0, 10.0),
            id: new NetworkArcId("a2")).Value;

        first.NetworkArc.Id.Should().NotBe(second.NetworkArc.Id);
    }

    /// <summary>供需绑定保留净量 / A supply-demand binding retains its net quantity.</summary>
    [Fact]
    public void SupplyDemand_ShouldRetainNet() {
        SupplyDemand<FltX> binding = SupplyDemand<FltX>.Invoke(
            new NetworkNodeId("n1"), Meters(5.0)).Value;

        binding.Net.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        binding.NodeId.Value.Should().Be("n1");
    }

    /// <summary>
    /// **空白节点标识被拒绝**：它会通过后续所有字典查找，把供需静静挂在不存在的节点上，守恒约束少一条。
    /// **A blank node identity is rejected**: it passes every later dictionary lookup, silently attaching the balance to a node
    /// that does not exist and dropping a conservation constraint.
    /// </summary>
    [Fact]
    public void SupplyDemand_ShouldRejectBlankNodeId() =>
        SupplyDemand<FltX>.Invoke(new NetworkNodeId(" "), Meters(5.0)).IsOk.Should().BeFalse();

    /// <summary>负净量表示需求 / A negative net means demand.</summary>
    [Fact]
    public void SupplyDemand_ShouldAcceptNegative() =>
        SupplyDemand<FltX>.Invoke(new NetworkNodeId("n1"), Meters(-3.0)).IsOk.Should().BeTrue();

    /// <summary>流节点按商品查询供需 / A flow node looks balances up by commodity.</summary>
    [Fact]
    public void Node_ShouldLookUpBalance() {
        FlowNode<FltX> node = FlowNode<FltX>.Invoke(
            NetworkNode(),
            new Dictionary<string, NodeBalance<FltX>> { ["coal"] = Balance(5.0) }).Value;

        node.BalanceOf("coal").Should().NotBeNull();
        node.BalanceOf("ore").Should().BeNull();
    }

    /// <summary>流节点标识来自网络节点 / The flow node identity comes from the network node.</summary>
    [Fact]
    public void Node_ShouldExposeNetworkNodeId() =>
        FlowNode<FltX>.Invoke(NetworkNode()).Value.Id.Value.Should().Be("n1");

    /// <summary>空供需表是合法输入 / An empty balance map is legal.</summary>
    [Fact]
    public void Node_ShouldAcceptNoBalances() =>
        FlowNode<FltX>.Invoke(NetworkNode()).Value.Balances.Should().BeEmpty();

    /// <summary>商品保留供需列表 / The commodity retains its balance list.</summary>
    [Fact]
    public void Commodity_ShouldRetainSupplyDemands() {
        FlowCommodity<FltX> commodity = FlowCommodity<FltX>.Invoke(
            "coal",
            new[] {
                SupplyDemand<FltX>.Invoke(new NetworkNodeId("n1"), Meters(5.0)).Value,
                SupplyDemand<FltX>.Invoke(new NetworkNodeId("n2"), Meters(-5.0)).Value,
            }).Value;

        commodity.SupplyDemands.Should().HaveCount(2);
    }

    /// <summary>空白商品标识被拒绝 / A blank commodity identity is rejected.</summary>
    [Fact]
    public void Commodity_ShouldRejectBlankId() =>
        FlowCommodity<FltX>.Invoke(" ").IsOk.Should().BeFalse();

    /// <summary>
    /// **同一节点的供需不得重复**：重复条目会让守恒约束被加两次或相抵，净供需悄悄翻倍或归零。
    /// **One node must not carry two balances**: a duplicate adds the conservation constraint twice or nets it to zero, silently
    /// doubling or cancelling the net supply.
    /// </summary>
    [Fact]
    public void Commodity_ShouldRejectDuplicateNode() =>
        FlowCommodity<FltX>.Invoke(
            "coal",
            new[] {
                SupplyDemand<FltX>.Invoke(new NetworkNodeId("n1"), Meters(5.0)).Value,
                SupplyDemand<FltX>.Invoke(new NetworkNodeId("n1"), Meters(-5.0)).Value,
            }).IsOk.Should().BeFalse();

    /// <summary>无供需的商品合法 / A commodity without balances is legal.</summary>
    [Fact]
    public void Commodity_ShouldAcceptNoSupplyDemands() =>
        FlowCommodity<FltX>.Invoke("coal").Value.SupplyDemands.Should().BeEmpty();

    /// <summary>构造网络弧 / Build a network arc.</summary>
    private static NetworkArc<FltX> NetworkArc() =>
        NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n1"), new NetworkNodeId("n2"), Meters(1.0),
            null, new NetworkArcId("a1")).Value;

    /// <summary>构造容量上下界 / Build capacity bounds.</summary>
    private static CapacityBounds<FltX> Bounds(double lower, double upper) =>
        CapacityBounds<FltX>.Invoke(Meters(lower), Meters(upper)).Value;

    /// <summary>构造节点供需 / Build a node balance.</summary>
    private static NodeBalance<FltX> Balance(double net) =>
        NodeBalance<FltX>.Invoke(Meters(net)).Value;

    /// <summary>构造网络节点 / Build a network node.</summary>
    private static NetworkNode<FltX> NetworkNode() =>
        NetworkNode<FltX>.Invoke(
            new NetworkNodeId("n1"),
            new Dictionary<string, Quantity<FltX>>()).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
