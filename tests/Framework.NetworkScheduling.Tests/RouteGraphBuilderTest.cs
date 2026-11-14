// 定价图构建测试 / Tests for pricing-graph construction.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 定价图构建 / Pricing-graph construction.
/// </summary>
public sealed class RouteGraphBuilderTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>合法构建得到图 / A valid build yields a graph.</summary>
    [Fact]
    public void Build_ShouldProduceGraph() =>
        Build().IsOk.Should().BeTrue();

    /// <summary>
    /// **节点顺序固定为「起始仓库 → 客户 → 结束仓库」**：定价状态与位集索引都依赖这个顺序。
    /// **The node order is fixed to「start depot → customers → end depot」**: pricing state and bitset indices depend on it.
    /// </summary>
    [Fact]
    public void Build_ShouldOrderNodes() {
        PricingGraph<FltX> graph = Build().Value;

        graph.StartDepotIndex.Should().Be(0);
        graph.EndDepotIndex.Should().Be(graph.Nodes.Count - 1);
        graph.Nodes[0].IsDepot.Should().BeTrue();
        graph.Nodes[^1].IsDepot.Should().BeTrue();
        graph.Nodes.Skip(1).Take(graph.Nodes.Count - 2).Should().OnlyContain(node => !node.IsDepot);
    }

    /// <summary>客户节点带实例客户索引 / Customer nodes carry their instance customer index.</summary>
    [Fact]
    public void Build_ShouldIndexCustomers() {
        PricingGraph<FltX> graph = Build().Value;

        graph.Nodes.Where(node => !node.IsDepot).Select(node => node.CustomerIndex)
            .Should().Equal(new[] { 0, 1 });
        graph.CustomerCount.Should().Be(2);
    }

    /// <summary>节点标识映射覆盖全部节点 / The identity map covers every node.</summary>
    [Fact]
    public void Build_ShouldMapNodeIds() {
        PricingGraph<FltX> graph = Build().Value;

        graph.NodeIndexMap.Should().HaveCount(graph.Nodes.Count);
        graph.NodeIndexMap[new NetworkNodeId("depot")].Should().Be(0);
    }

    /// <summary>
    /// **不能回到起始仓库、不能从结束仓库出发**：这两条排除是 elementary 路线的基本约束。
    /// **No arc returns to the start depot or leaves the end depot**: basic elementary-route constraints.
    /// </summary>
    [Fact]
    public void Build_ShouldExcludeInvalidArcs() {
        PricingGraph<FltX> graph = Build().Value;

        graph.Arcs.Should().NotContain(arc => arc.ToNodeId.Equals(new NetworkNodeId("depot")));
        graph.Arcs.Should().NotContain(arc => arc.FromNodeId.Equals(new NetworkNodeId("depot-end")));
    }

    /// <summary>自环弧被排除 / Self-loop arcs are excluded.</summary>
    [Fact]
    public void Build_ShouldExcludeSelfLoops() =>
        Build().Value.Arcs.Should().NotContain(arc => arc.FromIndex == arc.ToIndex);

    /// <summary>出弧缓存按索引返回 / Outgoing arcs are returned per index.</summary>
    [Fact]
    public void Build_ShouldGroupOutgoing() {
        PricingGraph<FltX> graph = Build().Value;

        IReadOnlyList<PricingArc<FltX>> outgoing = graph.OutgoingFrom(graph.StartDepotIndex);

        outgoing.Should().NotBeEmpty();
        outgoing.Should().OnlyContain(arc => arc.FromIndex == graph.StartDepotIndex);
    }

    /// <summary>出弧缓存重复调用结果一致 / Repeated outgoing calls agree.</summary>
    [Fact]
    public void Build_ShouldCacheOutgoing() {
        PricingGraph<FltX> graph = Build().Value;

        graph.OutgoingFrom(0).Should().BeSameAs(graph.OutgoingFrom(0));
    }

    /// <summary>车辆容量被保留 / The vehicle capacity is carried over.</summary>
    [Fact]
    public void Build_ShouldCarryCapacity() =>
        Build().Value.VehicleCapacity.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);

    /// <summary>
    /// **起始仓库出发的弧含固定成本**：固定成本只在起点计入一次，否则按弧重复累加会让每条路线贵上若干倍。
    /// **Arcs leaving the start depot include the fixed cost**: it is charged once at the origin, and per-arc accumulation
    /// would make every route several times too expensive.
    /// </summary>
    [Fact]
    public void Build_ShouldChargeFixedCostAtOriginOnly() {
        PricingGraph<FltX> graph = Build().Value;

        PricingArc<FltX> fromDepot = graph.Arcs.First(arc => arc.FromIndex == graph.StartDepotIndex);
        PricingArc<FltX> fromCustomer = graph.Arcs.First(arc =>
            !graph.Nodes[arc.FromIndex].IsDepot);

        fromDepot.ObjectiveCost.ToFlt64().ToDouble().Should().BeApproximately(10.0 + 0.0, 1e-9);
        fromCustomer.ObjectiveCost.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **第一阶段把目标成本压成零**：只用对偶判断"是否存在负列"，这是两阶段单纯形在定价侧的对应做法。
    /// **Phase one collapses the objective cost to zero**: it decides "is there a negative column" from duals alone, mirroring
    /// two-phase simplex on the pricing side.
    /// </summary>
    [Fact]
    public void Build_PhaseOneShouldZeroObjective() {
        PricingGraph<FltX> graph = Build(PricingPhase.PhaseOne, duals: null).Value;

        graph.Arcs.First(arc => arc.FromIndex == graph.StartDepotIndex)
            .ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>第二阶段用真实目标成本 / Phase two uses the real objective cost.</summary>
    [Fact]
    public void Build_PhaseTwoShouldUseObjective() {
        PricingGraph<FltX> graph = Build(PricingPhase.PhaseTwo, duals: null).Value;

        graph.Arcs.First(arc => arc.FromIndex == graph.StartDepotIndex)
            .ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>客户对偶降低终点为客户的弧 reduced cost / A customer dual lowers the reduced cost of arcs reaching it.</summary>
    [Fact]
    public void Build_ShouldSubtractCustomerDual() {
        var duals = new PricingDuals(
            PricingPhase.PhaseTwo,
            new Dictionary<CustomerId, Flt64> { [new CustomerId("c1")] = new Flt64(4.0) },
            new Dictionary<VehicleTypeId, Flt64>());

        PricingGraph<FltX> graph = Build(duals: duals).Value;
        PricingArc<FltX> toCustomer1 = graph.Arcs
            .First(arc => arc.ToNodeId.Equals(new NetworkNodeId("n1")) && !arc.FromNodeId.Equals(new NetworkNodeId("depot")));

        toCustomer1.ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(-4.0, 1e-9);
    }

    /// <summary>车队对偶只在起点弧生效 / The fleet dual applies only to arcs leaving the start depot.</summary>
    [Fact]
    public void Build_ShouldSubtractFleetDualAtOrigin() {
        var duals = new PricingDuals(
            PricingPhase.PhaseTwo,
            new Dictionary<CustomerId, Flt64>(),
            new Dictionary<VehicleTypeId, Flt64> { [new VehicleTypeId("truck")] = new Flt64(-3.0) });

        PricingGraph<FltX> graph = Build(duals: duals).Value;

        graph.Arcs.First(arc => arc.FromIndex == graph.StartDepotIndex)
            .ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(13.0, 1e-9, "10 - (-3)");
    }

    /// <summary>终点为仓库的弧不扣客户对偶 / Arcs ending at a depot take no customer dual.</summary>
    [Fact]
    public void Build_ShouldNotChargeCustomerDualAtDepot() {
        var duals = new PricingDuals(
            PricingPhase.PhaseTwo,
            new Dictionary<CustomerId, Flt64>
            {
                [new CustomerId("c1")] = new Flt64(4.0),
                [new CustomerId("c2")] = new Flt64(4.0),
            },
            new Dictionary<VehicleTypeId, Flt64>());

        PricingGraph<FltX> graph = Build(duals: duals).Value;

        foreach (PricingArc<FltX> arc in graph.Arcs.Where(arc => arc.ToIndex == graph.EndDepotIndex)) {
            arc.ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(
                arc.ObjectiveCost.ToFlt64().ToDouble(), 1e-9,
                "终点为仓库的弧不扣客户对偶，reduced cost 等于目标成本 / arcs ending at a depot take no customer dual, so "
                + "the reduced cost equals the objective cost");
        }
    }

    /// <summary>缺失扩展行对偶时构建失败 / A missing extra-row dual fails the build.</summary>
    [Fact]
    public void Build_ShouldRejectMissingExtraDual() {
        var builder = new RouteGraphBuilder<FltX>(
            Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance,
            DistanceCalculator(), TravelTimeCalculator(), ArcCostCalculator(),
            coefficientProviders: new[] { new ConstantCoefficientProvider(new FltX(2.0)) });

        builder.Build(new VehicleTypeId("truck"), PhaseOneDuals()).IsOk.Should().BeFalse();
    }

    /// <summary>扩展行对偶存在时构建成功 / The build succeeds once the extra dual exists.</summary>
    [Fact]
    public void Build_ShouldAcceptExtraDual() {
        var duals = new PricingDuals(
            PricingPhase.PhaseOne,
            new Dictionary<CustomerId, Flt64>(),
            new Dictionary<VehicleTypeId, Flt64>(),
            new Dictionary<string, Flt64> { ["row"] = new Flt64(1.0) });
        var builder = new RouteGraphBuilder<FltX>(
            Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance,
            DistanceCalculator(), TravelTimeCalculator(), ArcCostCalculator(),
            coefficientProviders: new[] { new ConstantCoefficientProvider(new FltX(2.0)) });

        builder.Build(new VehicleTypeId("truck"), duals).IsOk.Should().BeTrue();
    }

    /// <summary>车辆类型不存在时失败 / A missing vehicle type fails the build.</summary>
    [Fact]
    public void Build_ShouldRejectUnknownVehicleType() =>
        new RouteGraphBuilder<FltX>(
                Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance,
                DistanceCalculator(), TravelTimeCalculator(), ArcCostCalculator())
            .Build(new VehicleTypeId("ghost"), PhaseOneDuals()).IsOk.Should().BeFalse();

    /// <summary>
    /// **被遮罩禁止的客户不进图**：它连同所有相关弧一起消失，定价自然不会生成含该客户的列。
    /// **Mask-forbidden customers are excluded from the graph**: they vanish along with their arcs, so pricing cannot produce a
    /// column containing them.
    /// </summary>
    [Fact]
    public void Build_ShouldExcludeMaskForbiddenCustomers() {
        BranchMask<VehicleTypeId> mask = BranchMask<VehicleTypeId>.Invoke(
            new NetworkNodeId("depot"), new NetworkNodeId("depot-end"),
            forbiddenNodes: new Dictionary<VehicleTypeId, IReadOnlySet<NetworkNodeId>>
            {
                [new VehicleTypeId("truck")] = new HashSet<NetworkNodeId> { new("n1") },
            }).Value;

        PricingGraph<FltX> graph = Build(branchMask: mask).Value;

        graph.NodeIndexMap.Should().NotContainKey(new NetworkNodeId("n1"));
        graph.Arcs.Should().NotContain(arc => arc.ToNodeId.Equals(new NetworkNodeId("n1")));
    }

    /// <summary>资源图包含实例外节点时失败 / A resource graph with a foreign node fails the build.</summary>
    [Fact]
    public void Build_ShouldRejectForeignResourceNode() {
        ResourceGraph<VehicleTypeId> resourceGraph = ResourceGraph<VehicleTypeId>.Invoke(
            new[] { new NetworkNodeId("ghost") },
            Array.Empty<ResourceArc<VehicleTypeId>>()).Value;

        new RouteGraphBuilder<FltX>(
                Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance,
                DistanceCalculator(), TravelTimeCalculator(), ArcCostCalculator(),
                resourceGraph: resourceGraph)
            .Build(new VehicleTypeId("truck"), PhaseOneDuals()).IsOk.Should().BeFalse();
    }

    /// <summary>构造一张定价图 / Build a pricing graph.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        PricingGraph<FltX>, ErrorCode, Error<ErrorCode>> Build(
        PricingPhase phase = PricingPhase.PhaseOne,
        PricingDuals? duals = null,
        BranchMask<VehicleTypeId>? branchMask = null) {
        PricingDuals effective = duals ?? (phase == PricingPhase.PhaseOne
            ? PhaseOneDuals()
            : new PricingDuals(
                PricingPhase.PhaseTwo,
                new Dictionary<CustomerId, Flt64>(),
                new Dictionary<VehicleTypeId, Flt64>()));
        return new RouteGraphBuilder<FltX>(
                Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance,
                DistanceCalculator(), TravelTimeCalculator(), ArcCostCalculator())
            .Build(new VehicleTypeId("truck"), effective, branchMask);
    }

    /// <summary>第一阶段空对偶 / Empty phase-one duals.</summary>
    private static PricingDuals PhaseOneDuals() => new(
        PricingPhase.PhaseOne,
        new Dictionary<CustomerId, Flt64>(),
        new Dictionary<VehicleTypeId, Flt64>());

    /// <summary>距离计算器 / The distance calculator.</summary>
    private static IDistanceCalculator<FltX> DistanceCalculator() =>
        new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

    /// <summary>行驶时间计算器 / The travel-time calculator.</summary>
    private static ITravelTimeCalculator<FltX> TravelTimeCalculator() =>
        new DistanceAsTravelTimeCalculator<FltX>(
            new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter), Timeline());

    /// <summary>弧成本计算器 / The arc-cost calculator.</summary>
    private static IArcCostCalculator<FltX> ArcCostCalculator() =>
        new DistanceArcCostCalculator<FltX>(SIBaseUnits.Meter, SIBaseUnits.Meter);

    /// <summary>
    /// 构造一个两客户实例，所有节点重合于原点（距离与行驶时间恒为零）。
    /// Build a two-customer instance with every node at the origin, so distances and travel times are always zero.
    /// </summary>
    private static VrptwInstance<FltX> Instance() =>
        VrptwInstance<FltX>.Invoke(
            "graph",
            new Depot<FltX>(Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new Depot<FltX>(Node("depot-end"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new[] { CustomerOf("c1", "n1"), CustomerOf("c2", "n2") },
            new[] { VehicleType<FltX>.Invoke(new VehicleTypeId("truck"), Meters(100), Meters(10), 2).Value },
            Timeline(),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default).Value;

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<FltX> CustomerOf(string id, string nodeId) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造带坐标的节点 / Build a node carrying coordinates.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<FltX>> { ["x"] = Meters(0), ["y"] = Meters(0) }).Value;

    /// <summary>业务时间轴 / The business timeline.</summary>
    private static TimeWindow<FltX> Timeline() =>
        new(new TimeRange(Origin, Origin.AddHours(12)), false, TimeSpan.FromMinutes(1));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>恒定系数提供者替身 / A constant-coefficient provider stand-in.</summary>
    private sealed class ConstantCoefficientProvider(FltX value) : IRouteArcCoefficientProvider<FltX> {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, FltX> Coefficients(ResourceArc<VehicleTypeId> arc) =>
            new Dictionary<string, FltX> { ["row"] = value };
    }
}
