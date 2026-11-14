// VRPTW 实例与路线校验测试 / Tests for VRPTW instance and route validation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// VRPTW 实例与路线校验 / VRPTW instance and route validation.
/// </summary>
public sealed class VrptwValidatorTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>合法实例构造成功 / A valid instance constructs.</summary>
    [Fact]
    public void Create_ShouldBuildValidInstance() =>
        InstanceOf().IsOk.Should().BeTrue();

    /// <summary>实例建立索引 / The instance builds its indexes.</summary>
    [Fact]
    public void Create_ShouldBuildIndexes() {
        VrptwInstance<FltX> instance = InstanceOf().Value;

        instance.CustomerById.Should().HaveCount(2);
        instance.CustomerByNodeId.Should().HaveCount(2);
        instance.VehicleTypeById.Should().HaveCount(1);
    }

    /// <summary>空名称被拒绝 / A blank name is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectBlankName() =>
        InstanceOf(name: " ").IsOk.Should().BeFalse();

    /// <summary>无客户被拒绝 / No customers is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectNoCustomers() =>
        InstanceOf(customers: Array.Empty<Customer<FltX>>()).IsOk.Should().BeFalse();

    /// <summary>无车辆类型被拒绝 / No vehicle types is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectNoVehicleTypes() =>
        InstanceOf(vehicleTypes: Array.Empty<VehicleType<FltX>>()).IsOk.Should().BeFalse();

    /// <summary>起止仓库同节点被拒绝 / Identical depot nodes are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectSameDepot() =>
        InstanceOf(
            startDepot: DepotOf("depot"),
            endDepot: DepotOf("depot")).IsOk.Should().BeFalse();

    /// <summary>重复客户标识被拒绝 / Duplicate customer identities are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectDuplicateCustomerId() =>
        InstanceOf(customers: new[]
        {
            CustomerOf("c1", "n1", 5.0),
            CustomerOf("c1", "n2", 5.0),
        }).IsOk.Should().BeFalse();

    /// <summary>重复车辆类型标识被拒绝 / Duplicate vehicle-type identities are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectDuplicateVehicleTypeId() =>
        InstanceOf(vehicleTypes: new[]
        {
            VehicleOf("truck", 100.0),
            VehicleOf("truck", 100.0),
        }).IsOk.Should().BeFalse();

    /// <summary>重复网络节点标识被拒绝 / Duplicate network node identities are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectDuplicateNodeId() =>
        InstanceOf(customers: new[]
        {
            CustomerOf("c1", "shared", 5.0),
            CustomerOf("c2", "shared", 5.0),
        }).IsOk.Should().BeFalse();

    /// <summary>客户节点与仓库节点重名被拒绝 / A customer sharing a depot node is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectCustomerOnDepotNode() =>
        InstanceOf(customers: new[] { CustomerOf("c1", "depot", 5.0) }).IsOk.Should().BeFalse();

    /// <summary>
    /// **坐标轴不一致被拒绝**：一个二维点与一个三维点之间距离无从定义。
    /// **Inconsistent coordinate axes are rejected**: distance between a 2-D and a 3-D point is undefined.
    /// </summary>
    [Fact]
    public void Create_ShouldRejectInconsistentAxes() =>
        InstanceOf(customers: new[] { CustomerOf("c1", "n1", 5.0, withZ: true) }).IsOk.Should().BeFalse();

    /// <summary>无坐标被拒绝 / Missing coordinates are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectMissingCoordinates() =>
        InstanceOf(customers: new[] { CustomerOfWithoutCoordinates("c1", "n1", 5.0) }).IsOk.Should().BeFalse();

    /// <summary>
    /// **服务时间窗超出业务时间轴被拒绝**：这会让排程无解，而求解器只会报"不可行"，不会指出是数据问题。
    /// **A service window outside the business timeline is rejected**: it makes the schedule infeasible, and the solver would
    /// only report "infeasible" without pointing at the data.
    /// </summary>
    [Fact]
    public void Create_ShouldRejectWindowOutsideTimeline() =>
        InstanceOf(customers: new[]
        {
            CustomerWithWindow("c1", "n1", 5.0, Origin.AddDays(-1), Origin),
        }).IsOk.Should().BeFalse();

    /// <summary>
    /// **需求超过所有车辆容量被拒绝**：数据本身不可解，应在构造期报出来。
    /// **A demand exceeding every vehicle capacity is rejected**: the data is unsolvable and should surface at construction.
    /// </summary>
    [Fact]
    public void Create_ShouldRejectOversizedDemand() =>
        InstanceOf(
            customers: new[] { CustomerOf("c1", "n1", 5000.0) },
            vehicleTypes: new[] { VehicleOf("truck", 100.0) }).IsOk.Should().BeFalse();

    /// <summary>重新校验已构造实例通过 / Revalidating a constructed instance passes.</summary>
    [Fact]
    public void Validate_ShouldPassForValidInstance() =>
        VrptwValidator.Validate(InstanceOf().Value).IsOk.Should().BeTrue();

    /// <summary>
    /// **只有校验器能造实例**：`VrptwInstance.Invoke` 与 `VrptwValidator.Create` 行为一致，内部旁路不对外公开。
    /// **Only the validator mints instances**: `VrptwInstance.Invoke` matches `VrptwValidator.Create`, and the internal bypass
    /// is not public.
    /// </summary>
    [Fact]
    public void Instance_InvokeShouldDelegateToValidator() =>
        VrptwInstance<FltX>.Invoke(
            "test", DepotOf("depot"), DepotOf("depot-end"),
            new[] { CustomerOf("c1", "n1", 5.0) },
            new[] { VehicleOf("truck", 100.0) },
            Timeline(), Units(), VrptwTolerances.Default).IsOk.Should().BeTrue();

    /// <summary>合法路线通过校验 / A valid route passes validation.</summary>
    [Fact]
    public void Route_ShouldValidateValidRoute() =>
        ValidateRouteOf(ValidStops()).IsOk.Should().BeTrue();

    /// <summary>首个停靠点不是起始仓库时失败 / A first stop that is not the start depot fails.</summary>
    [Fact]
    public void Route_ShouldRejectWrongFirstStop() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[0] = StopOf("depot-end", null, Origin, Origin, Origin, 0.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>最后停靠点不是结束仓库时失败 / A last stop that is not the end depot fails.</summary>
    [Fact]
    public void Route_ShouldRejectWrongLastStop() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[^1] = StopOf("n2", new CustomerId("c2"), Origin, Origin, Origin, 0.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>仓库停靠点带客户标识时失败 / A depot stop carrying a customer identity fails.</summary>
    [Fact]
    public void Route_ShouldRejectDepotWithCustomer() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[0] = StopOf("depot", new CustomerId("c1"), Origin, Origin, Origin, 0.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>客户重复访问时失败 / Visiting a customer twice fails.</summary>
    [Fact]
    public void Route_ShouldRejectRepeatedCustomer() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[2] = StopOf("n1", new CustomerId("c1"), Origin, Origin, Origin, 0.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>客户节点不匹配时失败 / A mismatched customer node fails.</summary>
    [Fact]
    public void Route_ShouldRejectNodeMismatch() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[1] = StopOf("n2", new CustomerId("c1"), Origin, Origin, Origin, 0.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>起始负载非零时失败 / A non-zero initial load fails.</summary>
    [Fact]
    public void Route_ShouldRejectNonZeroInitialLoad() {
        List<RouteStop<FltX>> stops = ValidStops();
        stops[0] = StopOf("depot", null, Origin, Origin, Origin, 5.0);

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **到达时刻与递推不符时失败**：这是校验器最主要的作用——重放一遍资源状态，暴露任何不一致。
    /// **A non-matching arrival time fails**: the main job here is replaying resource state to expose any inconsistency.
    /// </summary>
    [Fact]
    public void Route_ShouldRejectWrongArrival() {
        List<RouteStop<FltX>> stops = ValidStops();
        RouteStop<FltX> customer1 = stops[1];
        stops[1] = customer1 with { Arrival = customer1.Arrival.AddMinutes(7) };

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>累计负载与递推不符时失败 / A non-matching accumulated load fails.</summary>
    [Fact]
    public void Route_ShouldRejectWrongLoad() {
        List<RouteStop<FltX>> stops = ValidStops();
        RouteStop<FltX> customer1 = stops[1];
        stops[1] = customer1 with { AccumulatedLoad = Meters(99) };

        ValidateRouteOf(stops).IsOk.Should().BeFalse();
    }

    /// <summary>车辆类型不存在时失败 / A missing vehicle type fails.</summary>
    [Fact]
    public void Route_ShouldRejectUnknownVehicleType() {
        VrptwInstance<FltX> instance = InstanceOf().Value;
        Route<FltX> route = Route<FltX>.Invoke(
            new VehicleTypeId("ghost"), ValidStops(), Meters(10), Meters(20)).Value;

        Validate(instance, route).IsOk.Should().BeFalse();
    }

    /// <summary>例如路线总距离与重算不符时失败 / A route total distance that disagrees with the recomputation fails.</summary>
    [Fact]
    public void Route_ShouldRejectWrongTotalDistance() {
        VrptwInstance<FltX> instance = InstanceOf().Value;
        Route<FltX> route = Route<FltX>.Invoke(
            new VehicleTypeId("truck"), ValidStops(), Meters(999), Meters(20)).Value;

        Validate(instance, route).IsOk.Should().BeFalse();
    }

    /// <summary>构造合法实例 / Build a valid instance.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        VrptwInstance<FltX>, ErrorCode, Error<ErrorCode>> InstanceOf(
        string name = "test",
        Depot<FltX>? startDepot = null,
        Depot<FltX>? endDepot = null,
        IReadOnlyList<Customer<FltX>>? customers = null,
        IReadOnlyList<VehicleType<FltX>>? vehicleTypes = null) =>
        VrptwValidator.Create(
            name,
            startDepot ?? DepotOf("depot"),
            endDepot ?? DepotOf("depot-end"),
            customers ?? new[] { CustomerOf("c1", "n1", 5.0), CustomerOf("c2", "n2", 5.0) },
            vehicleTypes ?? new[] { VehicleOf("truck", 100.0) },
            Timeline(),
            Units(),
            VrptwTolerances.Default);

    /// <summary>校验一条路线 / Validate a route.</summary>
    private static Try Validate(VrptwInstance<FltX> instance, Route<FltX> route) =>
        RouteValidator.Validate(
            instance, route,
            new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter),
            new DistanceAsTravelTimeCalculator<FltX>(
                new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter), Timeline()),
            new DistanceArcCostCalculator<FltX>(SIBaseUnits.Meter, SIBaseUnits.Meter),
            new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter),
            FltXNetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>按停靠点构造并校验一条路线 / Build and validate a route from stops.</summary>
    private static Try ValidateRouteOf(IReadOnlyList<RouteStop<FltX>> stops) =>
        Validate(
            InstanceOf().Value,
            Route<FltX>.Invoke(new VehicleTypeId("truck"), stops, Meters(0), Meters(100)).Value);

    /// <summary>
    /// 构造一条资源状态完全自洽的路线 / Build a route whose resource state is fully self-consistent.
    /// </summary>
    /// <remarks>
    /// 所有节点重合于原点，因此每段距离与行驶时间都是 0；客户服务 10 分钟；客户时间窗覆盖整天。
    /// 这样到达时刻、离开时刻与累计负载都能手算核对，测试断言的才是递推本身而不是几何。
    /// All nodes coincide at the origin, so every arc distance and travel time is zero; each customer serves 10 minutes and
    /// windows span the day. Arrival, departure and load are then hand-checkable, so the assertions target the recurrence
    /// itself rather than the geometry.
    /// </remarks>
    private static List<RouteStop<FltX>> ValidStops() {
        DateTimeOffset arrive1 = Origin;
        DateTimeOffset depart1 = arrive1.AddMinutes(10);
        DateTimeOffset arrive2 = depart1;
        DateTimeOffset depart2 = arrive2.AddMinutes(10);
        DateTimeOffset arriveEnd = depart2;
        return new List<RouteStop<FltX>>
        {
            StopOf("depot", null, Origin, Origin, Origin, 0.0),
            StopOf("n1", new CustomerId("c1"), arrive1, arrive1, depart1, 5.0),
            StopOf("n2", new CustomerId("c2"), arrive2, arrive2, depart2, 10.0),
            StopOf("depot-end", null, arriveEnd, arriveEnd, arriveEnd, 10.0),
        };
    }

    /// <summary>构造停靠点 / Build a stop.</summary>
    private static RouteStop<FltX> StopOf(
        string nodeId,
        CustomerId? customerId,
        DateTimeOffset arrival,
        DateTimeOffset serviceStart,
        DateTimeOffset departure,
        double load) =>
        new(new NetworkNodeId(nodeId), customerId, arrival, serviceStart, departure, Meters(load));

    /// <summary>构造仓库 / Build a depot.</summary>
    private static Depot<FltX> DepotOf(string nodeId) =>
        new(Node(nodeId, false), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value);

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<FltX> CustomerOf(string id, string nodeId, double demand, bool withZ = false) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId, withZ), Meters(demand),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造无坐标客户 / Build a customer without coordinates.</summary>
    private static Customer<FltX> CustomerOfWithoutCoordinates(string id, string nodeId, double demand) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), NetworkNode<FltX>.Invoke(new NetworkNodeId(nodeId)).Value, Meters(demand),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造带指定时间窗的客户 / Build a customer with the given time window.</summary>
    private static Customer<FltX> CustomerWithWindow(
        string id, string nodeId, double demand, DateTimeOffset ready, DateTimeOffset due) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId, false), Meters(demand),
            ServiceTimeWindow.Invoke(ready, due).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造车辆类型 / Build a vehicle type.</summary>
    private static VehicleType<FltX> VehicleOf(string id, double capacity) =>
        VehicleType<FltX>.Invoke(new VehicleTypeId(id), Meters(capacity), Meters(100), 2).Value;

    /// <summary>构造带坐标的节点 / Build a node carrying coordinates.</summary>
    private static NetworkNode<FltX> Node(string id, bool withZ) {
        var attributes = new Dictionary<string, Quantity<FltX>>
        {
            ["x"] = Meters(0),
            ["y"] = Meters(0),
        };
        if (withZ) {
            attributes["z"] = Meters(1);
        }
        return NetworkNode<FltX>.Invoke(new NetworkNodeId(id), attributes).Value;
    }

    /// <summary>业务时间轴 / The business timeline.</summary>
    private static TimeWindow<FltX> Timeline() =>
        new(new TimeRange(Origin, Origin.AddHours(12)), false, TimeSpan.FromMinutes(1));

    /// <summary>单位口径 / The unit contract.</summary>
    private static VrptwUnits Units() =>
        VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value;

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
