// 网络流问题数据层测试 / Tests for the flow-problem data layer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络流问题数据层 / The flow-problem data layer.
/// </summary>
public sealed class FlowProblemTest {
    /// <summary>每条弧生成一行 Resource / One resource row per arc.</summary>
    [Fact]
    public void Capacities_ShouldProduceOneRowPerArc() =>
        Problem(arcs: 2, commodities: 1).Capacities().Value.Should().HaveCount(2);

    /// <summary>行标识形如 arc:&lt;弧标识&gt; / Row identity is arc:&lt;arc id&gt;.</summary>
    [Fact]
    public void Capacities_ShouldNameRows() =>
        Problem(arcs: 1, commodities: 1).Capacities().Value[0].Id.Should().Be("arc:a1");

    /// <summary>默认消耗是 1 个商品单位 / The default consumption is one commodity unit.</summary>
    [Fact]
    public void Capacities_ShouldDefaultToOneCommodityUnit() {
        FlowCapacity<FltX> row = Problem(arcs: 1, commodities: 1).Capacities().Value[0];

        row.Usage.Should().HaveCount(1);
        row.Usage.Values.Single().Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>
    /// **量纲不同且无显式覆盖时直接失败**：把"吨"和"立方米"硬加能通过数值检查，物理上毫无意义。
    /// **Mismatched dimensions without an explicit override fail**: adding tonnes to cubic metres passes numeric checks and is
    /// physically meaningless.
    /// </summary>
    [Fact]
    public void Capacities_ShouldRejectCrossDimensionalDefault() =>
        Problem(arcs: 1, commodities: 1, commodityUnit: SIBaseUnits.Second).Capacities().IsOk.Should().BeFalse();

    /// <summary>显式覆盖在量纲兼容时被采用 / An explicit override is used when dimensions are compatible.</summary>
    [Fact]
    public void Capacities_ShouldUseExplicitOverride() {
        var key = new FlowKey("c1", new NetworkArcId("a1"));
        FlowProblem<FltX> problem = Problem(
            arcs: 1,
            commodities: 1,
            capacityUsage: new Dictionary<FlowKey, Quantity<FltX>>
            {
                [key] = new Quantity<FltX>(new FltX(2.5), SIBaseUnits.Meter),
            });

        problem.Capacities().Value[0].Usage[key].Value.ToFlt64().ToDouble()
            .Should().BeApproximately(2.5, 1e-9);
    }

    /// <summary>
    /// **`allowedArcs` 把商品从该弧的资源行里排除**：不该出现在这条弧上的商品若仍计入容量，会凭空占掉额度。
    /// **`allowedArcs` excludes a commodity from that arc's resource row**: a commodity that may not use the arc but still counts
    /// against its capacity would consume quota out of nowhere.
    /// </summary>
    [Fact]
    public void Capacities_ShouldRespectAllowedArcs() {
        FlowProblem<FltX> problem = Problem(
            arcs: 1,
            commodities: 1,
            allowedArcs: new HashSet<NetworkArcId> { new("other") });

        problem.Capacities().Value[0].Usage.Should().BeEmpty();
    }

    /// <summary>额外资源行追加在弧行之后 / Extra resource rows come after the arc rows.</summary>
    [Fact]
    public void Capacities_ShouldAppendExplicitResources() {
        var resource = new FlowCapacity<FltX>(
            "throughput", Bounds(0.0, 10.0),
            new Dictionary<FlowKey, Quantity<FltX>>
            {
                [new FlowKey("c1", new NetworkArcId("a1"))] = Meters(1.0),
            });

        FlowProblem<FltX> problem = Problem(arcs: 1, commodities: 1, resources: new[] { resource });

        IReadOnlyList<FlowCapacity<FltX>> rows = problem.Capacities().Value;
        rows.Should().HaveCount(2);
        rows[^1].Id.Should().Be("throughput");
    }

    /// <summary>多商品时每弧行含多条用量 / With several commodities each arc row has several usages.</summary>
    [Fact]
    public void Capacities_ShouldIncludeEveryCommodity() =>
        Problem(arcs: 1, commodities: 3).Capacities().Value[0].Usage.Should().HaveCount(3);

    /// <summary>问题快照不随后续改动变化 / The problem snapshot does not follow later mutation.</summary>
    [Fact]
    public void Problem_ShouldSnapshotCollections() {
        var resources = new List<FlowCapacity<FltX>>();
        FlowProblem<FltX> problem = Problem(arcs: 1, commodities: 1, resources: resources);

        resources.Add(new FlowCapacity<FltX>("late", Bounds(0.0, 1.0)));

        problem.Resources.Should().BeEmpty();
    }

    /// <summary>商品配置快照不随后续改动变化 / The commodity options snapshot does not follow later mutation.</summary>
    [Fact]
    public void CommodityFlow_ShouldSnapshotMaps() {
        var costs = new Dictionary<NetworkArcId, Quantity<FltX>>();
        var options = new CommodityFlow<FltX>(Commodity("coal"), SIBaseUnits.Meter, costs: costs);

        costs[new NetworkArcId("a1")] = Meters(1.0);

        options.Costs.Should().BeEmpty();
    }

    /// <summary>
    /// **整数步长与"整单不可拆分"无关**：它只表示流量必须是步长的整数倍。
    /// **An integer step says nothing about unsplittable orders**: it only means the flow must be a multiple of the step.
    /// </summary>
    [Fact]
    public void CommodityFlow_ShouldKeepIntegerStep() {
        var options = new CommodityFlow<FltX>(
            Commodity("coal"), SIBaseUnits.Meter, integerStep: Meters(2.0));

        options.IntegerStep!.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>连续流没有步长 / Continuous flow has no step.</summary>
    [Fact]
    public void CommodityFlow_ShouldAllowContinuousFlow() =>
        new CommodityFlow<FltX>(Commodity("coal"), SIBaseUnits.Meter).IntegerStep.Should().BeNull();

    /// <summary>构造一个流问题 / Build a flow problem.</summary>
    private static FlowProblem<FltX> Problem(
        int arcs,
        int commodities,
        PhysicalUnit? commodityUnit = null,
        IReadOnlyDictionary<FlowKey, Quantity<FltX>>? capacityUsage = null,
        IReadOnlyList<FlowCapacity<FltX>>? resources = null,
        IReadOnlySet<NetworkArcId>? allowedArcs = null) {
        var flowArcs = new List<Domain.Flow.Model.FlowArc<FltX>>();
        var networkArcs = new List<NetworkArc<FltX>>();
        for (int index = 0; index < arcs; index++) {
            NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
                new NetworkNodeId("n0"), new NetworkNodeId("n1"), Meters(1.0),
                null, new NetworkArcId($"a{index + 1}")).Value;
            networkArcs.Add(networkArc);
            flowArcs.Add(Domain.Flow.Model.FlowArc<FltX>.Invoke(networkArc, Bounds(0.0, 100.0)).Value);
        }

        var options = new List<CommodityFlow<FltX>>();
        for (int index = 0; index < commodities; index++) {
            options.Add(new CommodityFlow<FltX>(
                Commodity($"c{index + 1}"),
                commodityUnit ?? SIBaseUnits.Meter,
                allowedArcs: allowedArcs));
        }

        var nodes = new List<NetworkNode<FltX>> {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, networkArcs,
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: arc => arc.Id,
                    From: arc => arc.From,
                    To: arc => arc.To)).Value;

        return new FlowProblem<FltX>(
            graph, flowArcs, options, new FltX(0.0),
            capacityUsage: capacityUsage, resources: resources);
    }

    /// <summary>构造一个商品 / Build a commodity.</summary>
    private static Domain.Flow.Model.FlowCommodity<FltX> Commodity(string id) =>
        Domain.Flow.Model.FlowCommodity<FltX>.Invoke(id).Value;

    /// <summary>构造容量上下界 / Build capacity bounds.</summary>
    private static CapacityBounds<FltX> Bounds(double lower, double upper) =>
        CapacityBounds<FltX>.Invoke(Meters(lower), Meters(upper)).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
