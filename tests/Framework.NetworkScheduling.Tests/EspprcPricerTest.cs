// ESPPRC 定价器测试 / Tests for the ESPPRC pricer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// ESPPRC 定价器 / The ESPPRC pricer.
/// </summary>
public sealed class EspprcPricerTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **弧 reduced cost 全非负时穷尽且不产生列**：这是"不存在负列"的正常情形。
    /// **All-non-negative arc reduced costs exhaust without producing columns**: the ordinary "no negative column" case.
    /// </summary>
    [Fact]
    public void Price_ShouldExhaustWithoutNegativeColumn() {
        PricingResult<FltX> result = Price(NoNegativeColumnGraph()).Value;

        result.Routes.Should().BeEmpty();
        result.ExactPricingComplete.Should().BeTrue();
        result.Interrupted.Should().BeFalse();
    }

    /// <summary>
    /// **精确定价完成时 min reduced cost 不为负**：这是"不存在负列"的证据，主问题据此可以停止定价。
    /// **Exact completion implies a non-negative min reduced cost**: evidence that no negative column exists, which is what lets
    /// the master stop pricing.
    /// </summary>
    [Fact]
    public void Price_ExactCompletionShouldHaveNoNegativeColumn() {
        PricingResult<FltX> result = Price(NoNegativeColumnGraph()).Value;

        result.ExactPricingComplete.Should().BeTrue();
        result.MinReducedCost.ToFlt64().ToDouble().Should().BeGreaterThanOrEqualTo(0.0);
    }

    /// <summary>
    /// **中断不能宣称精确定价完成**：截断只限制列数，绝不等于"没有更多负列"。
    /// **An interruption cannot claim exact completion**: truncation only bounds the column count, never means "no more negative
    /// columns".
    /// </summary>
    [Fact]
    public void Price_InterruptionShouldNotClaimCompletion() {
        PricingResult<FltX> result = Price(NoNegativeColumnGraph(), checker: AlwaysStopChecker.Instance).Value;

        result.Interrupted.Should().BeTrue();
        result.ExactPricingComplete.Should().BeFalse();
    }

    /// <summary>单客户线路可被定价出来 / A single-customer route is priced out.</summary>
    [Fact]
    public void Price_ShouldProduceRoute() {
        PricingResult<FltX> result = Price(GraphOf(OneCustomer())).Value;

        result.Routes.Should().NotBeEmpty();
        result.Routes[0].Stops.Should().HaveCount(3);
        result.Routes[0].Stops[1].CustomerId!.Value.Should().Be("c1");
    }

    /// <summary>路线首尾都是仓库 / A route starts and ends at depots.</summary>
    [Fact]
    public void Price_ShouldStartAndEndAtDepots() {
        Route<FltX> route = Price(GraphOf(OneCustomer())).Value.Routes[0];

        route.Stops[0].CustomerId.Should().BeNull();
        route.Stops[^1].CustomerId.Should().BeNull();
        route.Stops[0].NodeId.Value.Should().Be("depot");
        route.Stops[^1].NodeId.Value.Should().Be("depot-end");
    }

    /// <summary>累积负载单调递增 / The accumulated load is monotone.</summary>
    [Fact]
    public void Price_ShouldAccumulateLoad() {
        Route<FltX> route = Price(GraphOf(OneCustomer())).Value.Routes[0];

        route.Stops[1].AccumulatedLoad.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>到达时刻不早于 ready time / Arrival is never before the ready time.</summary>
    [Fact]
    public void Price_ShouldRespectReadyTime() {
        Route<FltX> route = Price(GraphOf(OneCustomer())).Value.Routes[0];

        route.Stops[1].Arrival.Should().BeOnOrAfter(Origin);
        route.Stops[1].ServiceStart.Should().BeOnOrAfter(route.Stops[1].Arrival);
        route.Stops[1].Departure.Should().BeOnOrAfter(route.Stops[1].ServiceStart);
    }

    /// <summary>列数上限限制返回条数 / The column limit bounds the returned count.</summary>
    [Fact]
    public void Price_ShouldRespectColumnLimit() {
        PricingResult<FltX> result = Price(GraphOf(ThreeCustomers()), maxColumns: 1).Value;

        result.Routes.Should().HaveCount(1);
    }

    /// <summary>
    /// **遮罩禁止的客户不会出现在任何列里**：分支定界的一致性依赖这一点。
    /// **A mask-forbidden customer appears in no column**: branch-and-bound consistency depends on it.
    /// </summary>
    [Fact]
    public void Price_ShouldRespectBranchMask() {
        BranchMask<VehicleTypeId> mask = InvokeMask(new NetworkNodeId("n1"));
        PricingResult<FltX> result = Price(GraphOf(ThreeCustomers(), mask)).Value;

        result.Routes.Should().NotBeEmpty();
        foreach (Route<FltX> route in result.Routes) {
            route.Stops.Where(stop => stop.CustomerId is { Value: "c1" }).Should().BeEmpty();
        }
    }

    /// <summary>路线成本被写入每条列 / The route cost is recorded on every column.</summary>
    [Fact]
    public void Price_ShouldRecordRouteCost() {
        Route<FltX> route = Price(GraphOf(OneCustomer())).Value.Routes[0];

        route.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9, "固定成本 10");
    }

    /// <summary>弧标识链与停靠点数一致 / The arc-identity chain matches the stop count.</summary>
    [Fact]
    public void Price_ShouldRecordArcIds() {
        Route<FltX> route = Price(GraphOf(OneCustomer())).Value.Routes[0];

        route.ArcIds.Should().HaveCount(route.Stops.Count - 1);
    }

    /// <summary>车辆类型不存在时定价失败 / A missing vehicle type fails pricing.</summary>
    [Fact]
    public void Price_ShouldRejectUnknownVehicleType() {
        var request = new PricingRequest<FltX>(
            Instance: Instance(ThreeCustomers()),
            Duals: PhaseOneDuals(),
            BranchMask: null,
            PricingTolerance: new Flt64(1e-6),
            VehicleTypeId: new VehicleTypeId("ghost"),
            MaxColumnsPerPricing: 10);

        new EspprcPricer<FltX>(Instance(ThreeCustomers()), FltXNetworkSchedulingSolverValueAdapter.Instance)
            .Price(GraphOf(ThreeCustomers()), request).IsOk.Should().BeFalse();
    }

    /// <summary>执行一次定价 / Run pricing.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        PricingResult<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Price(
        PricingGraph<FltX> graph,
        IPricingInterruptionChecker? checker = null,
        int maxColumns = 10) =>
        new EspprcPricer<FltX>(Instance(ThreeCustomers()), FltXNetworkSchedulingSolverValueAdapter.Instance)
            .Price(graph, new PricingRequest<FltX>(
                Instance: Instance(ThreeCustomers()),
                Duals: PhaseOneDuals(),
                BranchMask: null,
                PricingTolerance: new Flt64(1e-6),
                VehicleTypeId: new VehicleTypeId("truck"),
                MaxColumnsPerPricing: maxColumns,
                InterruptionChecker: checker));

    /// <summary>第一阶段空对偶 / Empty phase-one duals.</summary>
    private static PricingDuals PhaseOneDuals() => new(
        PricingPhase.PhaseOne,
        new Dictionary<CustomerId, Flt64>(),
        new Dictionary<VehicleTypeId, Flt64>());

    /// <summary>
    /// 构造定价图。节点全部重合于原点，距离与行驶时间恒为零，因此断言针对的是资源递推本身而不是几何。
    /// Build a pricing graph with every node at the origin, so distances and travel times are always zero and assertions target
    /// the resource recurrence rather than the geometry.
    /// </summary>
    private static PricingGraph<FltX> GraphOf(
        bool[] present,
        BranchMask<VehicleTypeId>? mask = null,
        double arcReducedCost = -1.0) {
        var nodes = new List<PricingNode<FltX>>();
        var map = new Dictionary<NetworkNodeId, int>();
        var zero = new FltX(0.0);
        nodes.Add(new PricingNode<FltX>(
            new NetworkNodeId("depot"), true, -1, zero, new FltX(600.0), zero, zero));
        map[new NetworkNodeId("depot")] = 0;
        int customerIndex = 0;
        for (int index = 0; index < present.Length; index++) {
            if (!present[index]) {
                continue;
            }
            string id = $"c{index + 1}";
            string nodeId = $"n{index + 1}";
            if (mask is not null && !mask.AllowsNode(new VehicleTypeId("truck"), new NetworkNodeId(nodeId))) {
                customerIndex += 1;
                continue;
            }
            nodes.Add(new PricingNode<FltX>(
                new NetworkNodeId(nodeId), false, customerIndex, zero, new FltX(600.0), zero, new FltX(1.0)));
            map[new NetworkNodeId(nodeId)] = nodes.Count - 1;
            customerIndex += 1;
        }
        nodes.Add(new PricingNode<FltX>(
            new NetworkNodeId("depot-end"), true, -1, zero, new FltX(600.0), zero, zero));
        map[new NetworkNodeId("depot-end")] = nodes.Count - 1;

        var arcs = new List<PricingArc<FltX>>();
        for (int from = 0; from < nodes.Count; from++) {
            if (nodes[from].NodeId.Value == "depot-end") {
                continue;
            }
            for (int to = 0; to < nodes.Count; to++) {
                if (from == to || nodes[to].NodeId.Value == "depot") {
                    continue;
                }
                arcs.Add(new PricingArc<FltX>(
                    from, to, new FltX(0.0), new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
                    new FltX(arcReducedCost), zero, zero,
                    nodes[from].NodeId, nodes[to].NodeId,
                    new NetworkArcId($"a{from}-{to}")));
            }
        }
        return new PricingGraph<FltX>(
            nodes, arcs, map, customerIndex, 0, nodes.Count - 1, new FltX(100.0));
    }

    /// <summary>
    /// 弧 reduced cost 统一为负，保证只要存在合法路线就会产生负列，测试因此不依赖几何。
    /// Every arc carries a negative reduced cost, so any feasible route yields a negative column and the tests do not depend on
    /// geometry.
    /// </summary>
    private static PricingGraph<FltX> NoNegativeColumnGraph() =>
        GraphOf(ThreeCustomers(), arcReducedCost: 0.0);

    /// <summary>一个客户的图 / One customer.</summary>
    private static bool[] OneCustomer() => new[] { true, false, false };

    /// <summary>三个客户的图 / Three customers.</summary>
    private static bool[] ThreeCustomers() => new[] { true, true, true };

    /// <summary>构造分支遮罩 / Build a branch mask.</summary>
    private static BranchMask<VehicleTypeId> InvokeMask(NetworkNodeId forbidden) =>
        BranchMask<VehicleTypeId>.Invoke(
            new NetworkNodeId("depot"), new NetworkNodeId("depot-end"),
            forbiddenNodes: new Dictionary<VehicleTypeId, IReadOnlySet<NetworkNodeId>>
            {
                [new VehicleTypeId("truck")] = new HashSet<NetworkNodeId> { forbidden },
            }).Value;

    /// <summary>构造 VRPTW 实例 / Build a VRPTW instance.</summary>
    private static VrptwInstance<FltX> Instance(bool[] present) =>
        VrptwInstance<FltX>.Invoke(
            "pricer",
            new Depot<FltX>(Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new Depot<FltX>(Node("depot-end"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            Enumerable.Range(0, present.Length).Where(index => present[index])
                .Select(index => CustomerOf($"c{index + 1}", $"n{index + 1}")).ToList(),
            new[] { VehicleType<FltX>.Invoke(new VehicleTypeId("truck"), Meters(100), Meters(10), 2).Value },
            Timeline(),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default).Value;

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<FltX> CustomerOf(string id, string nodeId) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<FltX>> { ["x"] = Meters(0), ["y"] = Meters(0) }).Value;

    /// <summary>业务时间轴 / The business timeline.</summary>
    private static TimeWindow<FltX> Timeline() =>
        new(new TimeRange(Origin, Origin.AddHours(12)), false, TimeSpan.FromMinutes(1));

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>
    /// 立即中断的检查器，用于验证"中断不能宣称精确定价完成"。
    /// A checker that interrupts immediately, used to verify that an interruption cannot claim exact completion.
    /// </summary>
    private sealed class AlwaysStopChecker : IPricingInterruptionChecker {
        /// <summary>共享实例 / Shared instance.</summary>
        public static AlwaysStopChecker Instance { get; } = new AlwaysStopChecker();

        /// <inheritdoc/>
        public bool ShouldStop() => true;
    }
}
