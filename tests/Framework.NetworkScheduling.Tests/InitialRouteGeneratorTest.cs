// 初始路线生成器测试 / Tests for the initial route generator.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 初始路线生成器 / The initial route generator.
/// </summary>
public sealed class InitialRouteGeneratorTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>每个客户生成一条单客户路线 / One single-customer route per customer.</summary>
    [Fact]
    public void Generate_ShouldProduceOneRoutePerCustomer() {
        IReadOnlyList<Route<FltX>> routes = Generate(Customers(3));

        routes.Should().HaveCount(3);
    }

    /// <summary>每个客户 × 每种车辆类型各一条 / One route per customer × vehicle type.</summary>
    [Fact]
    public void Generate_ShouldCrossCustomersWithVehicleTypes() {
        IReadOnlyList<Route<FltX>> routes = Generate(Customers(2), vehicles: 2);

        routes.Should().HaveCount(4);
    }

    /// <summary>路线形态为「仓库 → 客户 → 仓库」/ The route shape is depot → customer → depot.</summary>
    [Fact]
    public void Generate_ShouldProduceThreeStopRoutes() {
        Route<FltX> route = Generate(Customers(1))[0];

        route.Stops.Should().HaveCount(3);
        route.Stops[0].NodeId.Value.Should().Be("depot");
        route.Stops[1].CustomerId.Should().NotBeNull();
        route.Stops[^1].NodeId.Value.Should().Be("depot-end");
    }

    /// <summary>弧标识链与停靠点一致 / The arc-identity chain matches the stops.</summary>
    [Fact]
    public void Generate_ShouldRecordArcIds() {
        Route<FltX> route = Generate(Customers(1))[0];

        route.ArcIds.Should().HaveCount(2);
    }

    /// <summary>累积负载等于客户需求 / The accumulated load equals the customer demand.</summary>
    [Fact]
    public void Generate_ShouldAccumulateLoad() {
        Route<FltX> route = Generate(Customers(1))[0];

        route.Stops[1].AccumulatedLoad.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>容量恰好等于需求时可生成 / A demand exactly at capacity still produces a route.</summary>
    [Fact]
    public void Generate_ShouldAcceptDemandAtCapacity() {
        IReadOnlyList<Route<FltX>> routes = Generate(new[] { "c1" }, demand: 100.0);

        routes.Should().HaveCount(1);
    }

    /// <summary>
    /// **不可行实例在构造期就被拒绝，根本到不了生成器**：超容量需求、超出排程时间窗的客户时间窗、空客户集，都由
    /// `VrptwValidator` 在 `VrptwInstance.Invoke` 里拦下。因此生成器内部的时间窗与容量检查是**防御性**的——它们保护的是
    /// 公开 API 之外的构造路径，不是这里能触发的分支。
    /// **An infeasible instance is rejected at construction and never reaches the generator**: over-capacity demand, customer
    /// windows outside the scheduling timeline, and empty customer sets are all caught by `VrptwValidator` inside
    /// `VrptwInstance.Invoke`. The generator's own time-window and capacity checks are therefore **defensive** — they guard
    /// construction paths outside the public API, not branches reachable here.
    /// </summary>
    [Fact]
    public void Generate_ShouldRequireValidatedInstance() {
        InstanceResult(new[] { "c1" }, demand: 500.0).IsOk.Should().BeFalse("超容量需求 / over-capacity demand");
        InstanceResult(
            new[] { "c1" },
            customerWindow: (Origin.AddHours(20), Origin.AddHours(21))).IsOk.Should()
            .BeFalse("客户时间窗超出排程时间窗 / customer window outside the scheduling timeline");
        InstanceResult(Array.Empty<string>()).IsOk.Should().BeFalse("空客户集 / empty customer set");
    }

    /// <summary>
    /// **遮罩禁止的客户不生成路线**：它不能出现在任何初始列里。
    /// **Mask-forbidden customers produce no route**: they must not appear in any initial column.
    /// </summary>
    [Fact]
    public void Generate_ShouldExcludeMaskForbiddenCustomer() {
        BranchMask<VehicleTypeId> mask = BranchMask<VehicleTypeId>.Invoke(
            new NetworkNodeId("depot"), new NetworkNodeId("depot-end"),
            forbiddenNodes: new Dictionary<VehicleTypeId, IReadOnlySet<NetworkNodeId>>
            {
                [new VehicleTypeId("truck-0")] = new HashSet<NetworkNodeId> { new("n1") },
            }).Value;

        IReadOnlyList<Route<FltX>> routes = Generate(Customers(2), branchMask: mask);

        routes.Should().HaveCount(1);
        routes[0].Stops[1].CustomerId!.Value.Should().Be("c2");
    }

    /// <summary>
    /// **所有生成的路线都通过独立复核**：这是"初始可行列"这个词的全部含义。
    /// **Every generated route passes independent validation**: that is the whole meaning of "initial feasible column".
    /// </summary>
    [Fact]
    public void Generate_ShouldProduceValidatedRoutes() {
        VrptwInstance<FltX> instance = Instance(Customers(3), vehicles: 1);

        foreach (Route<FltX> route in Generate(Customers(3))) {
            Domain.Vrp.Service.RouteValidator.Validate(
                instance, route, DistanceCalculator(), TravelTimeCalculator(),
                ArcCostCalculator(), RouteCostPolicy(), FltXNetworkSchedulingSolverValueAdapter.Instance)
                .IsOk.Should().BeTrue();
        }
    }

    /// <summary>执行一次生成 / Run one generation.</summary>
    private static IReadOnlyList<Route<FltX>> Generate(
        string[] customers,
        int vehicles = 1,
        double demand = 1.0,
        (DateTimeOffset From, DateTimeOffset To)? customerWindow = null,
        BranchMask<VehicleTypeId>? branchMask = null) =>
        new InitialRouteGenerator<FltX>(
                Instance(customers, vehicles, demand, customerWindow),
                FltXNetworkSchedulingSolverValueAdapter.Instance,
                DistanceCalculator(),
                TravelTimeCalculator(),
                ArcCostCalculator(),
                RouteCostPolicy())
            .Generate(branchMask);

    /// <summary>构造客户标识 / Build customer identities.</summary>
    private static string[] Customers(int count) =>
        Enumerable.Range(1, count).Select(index => $"c{index}").ToArray();

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

    /// <summary>路线成本策略 / The route-cost policy.</summary>
    private static IRouteCostPolicy<FltX> RouteCostPolicy() =>
        new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter);

    /// <summary>
    /// 构造实例。所有节点重合于原点，距离与行驶时间恒为零，因此断言针对递推本身而不是几何。
    /// Build an instance with every node at the origin, so distances and travel times are always zero and assertions target the
    /// recurrence rather than the geometry.
    /// </summary>
    private static VrptwInstance<FltX> Instance(
        string[] customers,
        int vehicles = 1,
        double demand = 1.0,
        (DateTimeOffset From, DateTimeOffset To)? customerWindow = null) =>
        InstanceResult(customers, vehicles, demand, customerWindow).Value;

    /// <summary>构造实例并返回未解包的结果 / Build an instance and return the unwrapped result.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        VrptwInstance<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> InstanceResult(
        string[] customers,
        int vehicles = 1,
        double demand = 1.0,
        (DateTimeOffset From, DateTimeOffset To)? customerWindow = null) {
        (DateTimeOffset From, DateTimeOffset To) window =
            customerWindow ?? (Origin, Origin.AddHours(12));
        var vehicleTypes = new List<VehicleType<FltX>>();
        for (int index = 0; index < vehicles; index++) {
            vehicleTypes.Add(VehicleType<FltX>.Invoke(
                new VehicleTypeId($"truck-{index}"), Meters(100), Meters(10), 2).Value);
        }

        return VrptwInstance<FltX>.Invoke(
            "initial",
            new Depot<FltX>(Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new Depot<FltX>(Node("depot-end"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            customers.Select(id => CustomerOf(id, $"n{id[1..]}", demand, window)).ToList(),
            vehicleTypes,
            Timeline(),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default);
    }

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<FltX> CustomerOf(
        string id,
        string nodeId,
        double demand,
        (DateTimeOffset From, DateTimeOffset To) window) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(demand),
            ServiceTimeWindow.Invoke(window.From, window.To).Value, TimeSpan.FromMinutes(10)).Value;

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
}
