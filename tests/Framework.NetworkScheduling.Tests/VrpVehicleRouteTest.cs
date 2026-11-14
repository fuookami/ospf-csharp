// VRPTW 单位口径、容差、车辆类型与路线测试 / Tests for VRPTW units, tolerances, vehicle types and routes.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// VRPTW 单位口径、容差、车辆类型与路线 / VRPTW units, tolerances, vehicle types and routes.
/// </summary>
public sealed class VrpVehicleRouteTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>合法单位口径通过 / A valid unit contract passes.</summary>
    [Fact]
    public void Units_ShouldAcceptLengthDistance() =>
        VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Kilogram, SIBaseUnits.Meter).IsOk.Should().BeTrue();

    /// <summary>
    /// **距离必须使用长度量纲**：距离要参与三角不等式与路径累加，用错了量纲不会报错，只会给出错误的最优解。
    /// **Distance must have length dimension**: it participates in the triangle inequality, and a wrong dimension throws
    /// nothing — it merely yields a wrong optimum.
    /// </summary>
    [Fact]
    public void Units_ShouldRejectNonLengthDistance() =>
        VrptwUnits.Invoke(SIBaseUnits.Second, SIBaseUnits.Kilogram, SIBaseUnits.Meter).IsOk.Should().BeFalse();

    /// <summary>
    /// **仿射单位不能作负载**：摄氏温度带 273.15 偏移，累加没有意义。用现成的 `TemperatureUnits.Celsius` 验证。
    /// **An affine unit cannot serve as load**: Celsius carries a 273.15 offset and accumulation is meaningless. Verified with
    /// the existing `TemperatureUnits.Celsius`.
    /// </summary>
    [Fact]
    public void Units_ShouldRejectAffineLoad() =>
        VrptwUnits.Invoke(SIBaseUnits.Meter, TemperatureUnits.Celsius, SIBaseUnits.Meter).IsOk.Should().BeFalse();

    /// <summary>仿射单位不能作成本 / An affine unit cannot serve as cost.</summary>
    [Fact]
    public void Units_ShouldRejectAffineCost() =>
        VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Kilogram, TemperatureUnits.Celsius).IsOk.Should().BeFalse();

    /// <summary>默认容差合同可用 / The default tolerance contract is available.</summary>
    [Fact]
    public void Tolerances_ShouldExposeDefault() {
        VrptwTolerances.Default.Feasibility.ToDouble().Should().BeApproximately(1e-7, 1e-12);
        VrptwTolerances.Default.RelativeGap.ToDouble().Should().BeApproximately(1e-4, 1e-9);
    }

    /// <summary>无参构造等同默认 / Constructing with no arguments equals the defaults.</summary>
    [Fact]
    public void Tolerances_ShouldDefaultAllArguments() {
        VrptwTolerances tolerances = VrptwTolerances.Invoke().Value;

        tolerances.Pricing.ToDouble().Should().BeApproximately(
            VrptwTolerances.Default.Pricing.ToDouble(), 1e-15);
    }

    /// <summary>
    /// **零容差合法**：表示"要求精确相等"，是一种有意的严格设定。
    /// **A zero tolerance is legal**: it means "require exact equality", a deliberate strict setting.
    /// </summary>
    [Fact]
    public void Tolerances_ShouldAcceptZero() =>
        VrptwTolerances.Invoke(feasibility: new Flt64(0.0)).IsOk.Should().BeTrue();

    /// <summary>
    /// **负容差非法**：负容差会让所有比较反向，静默地把算法变成不做任何判定。
    /// **A negative tolerance is illegal**: it inverts every comparison and silently turns the algorithm into one that
    /// decides nothing.
    /// </summary>
    [Fact]
    public void Tolerances_ShouldRejectNegative() =>
        VrptwTolerances.Invoke(pricing: new Flt64(-1e-8)).IsOk.Should().BeFalse();

    /// <summary>非有限容差非法 / A non-finite tolerance is illegal.</summary>
    [Fact]
    public void Tolerances_ShouldRejectNan() =>
        VrptwTolerances.Invoke(integrality: new Flt64(double.NaN)).IsOk.Should().BeFalse();

    /// <summary>合法车辆类型构造成功 / A valid vehicle type constructs.</summary>
    [Fact]
    public void VehicleType_ShouldBuild() =>
        VehicleOf("truck", 100.0, 10.0, 3).IsOk.Should().BeTrue();

    /// <summary>车辆类型保留字段 / A vehicle type keeps its fields.</summary>
    [Fact]
    public void VehicleType_ShouldKeepFields() {
        VehicleType<FltX> vehicle = VehicleOf("truck", 100.0, 10.0, 3).Value;

        vehicle.Id.Value.Should().Be("truck");
        vehicle.Amount.Should().Be(3);
        vehicle.Capacity.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);
    }

    /// <summary>
    /// **零容量被拒绝**：零容量车装不下任何有需求的客户，却会占用车队名额并支付固定成本，最优解里就会出现"派了一辆空车"。
    /// **Zero capacity is rejected**: such a vehicle carries no demand yet consumes a fleet slot and its fixed cost, so the
    /// optimum contains "a dispatched empty vehicle".
    /// </summary>
    [Fact]
    public void VehicleType_ShouldRejectZeroCapacity() =>
        VehicleOf("truck", 0.0, 10.0, 3).IsOk.Should().BeFalse();

    /// <summary>负容量被拒绝 / A negative capacity is rejected.</summary>
    [Fact]
    public void VehicleType_ShouldRejectNegativeCapacity() =>
        VehicleOf("truck", -1.0, 10.0, 3).IsOk.Should().BeFalse();

    /// <summary>
    /// **零固定成本合法**：自有车辆没有启用费用。
    /// **A zero fixed cost is legal**: owned vehicles have no activation fee.
    /// </summary>
    [Fact]
    public void VehicleType_ShouldAcceptZeroFixedCost() =>
        VehicleOf("truck", 100.0, 0.0, 3).IsOk.Should().BeTrue();

    /// <summary>负固定成本被拒绝 / A negative fixed cost is rejected.</summary>
    [Fact]
    public void VehicleType_ShouldRejectNegativeFixedCost() =>
        VehicleOf("truck", 100.0, -1.0, 3).IsOk.Should().BeFalse();

    /// <summary>
    /// 零可用数量被拒绝：等于把该车型从车队删掉，应直接不写它。
    /// A zero available amount is rejected: it equals removing the type, which should simply be omitted.
    /// </summary>
    [Fact]
    public void VehicleType_ShouldRejectZeroAmount() =>
        VehicleOf("truck", 100.0, 10.0, 0).IsOk.Should().BeFalse();

    /// <summary>空车辆类型标识被拒绝 / A blank vehicle-type identity is rejected.</summary>
    [Fact]
    public void VehicleType_ShouldRejectBlankId() =>
        VehicleOf(" ", 100.0, 10.0, 3).IsOk.Should().BeFalse();

    /// <summary>合法路线构造成功 / A valid route constructs.</summary>
    [Fact]
    public void Route_ShouldBuild() =>
        RouteOf(Stops("depot", "c1", "depot2"), 10.0, 20.0).IsOk.Should().BeTrue();

    /// <summary>单个停靠点的路线被拒绝 / A single-stop route is rejected.</summary>
    [Fact]
    public void Route_ShouldRejectSingleStop() =>
        RouteOf(Stops("depot"), 0.0, 0.0).IsOk.Should().BeFalse();

    /// <summary>
    /// **距离必须是长度量纲**：用错量纲不会抛异常，只会给出错误的最优解。
    /// **Distance must have length dimension**: a wrong dimension throws nothing and merely yields a wrong optimum.
    /// </summary>
    [Fact]
    public void Route_ShouldRejectNonLengthDistance() =>
        Route<FltX>.Invoke(
            new VehicleTypeId("truck"),
            Stops("depot", "c1"),
            new Quantity<FltX>(new FltX(10), SIBaseUnits.Second),
            Meters(20)).IsOk.Should().BeFalse();

    /// <summary>负距离被拒绝 / A negative distance is rejected.</summary>
    [Fact]
    public void Route_ShouldRejectNegativeDistance() =>
        RouteOf(Stops("depot", "c1"), -1.0, 20.0).IsOk.Should().BeFalse();

    /// <summary>负成本被拒绝 / A negative cost is rejected.</summary>
    [Fact]
    public void Route_ShouldRejectNegativeCost() =>
        RouteOf(Stops("depot", "c1"), 10.0, -1.0).IsOk.Should().BeFalse();

    /// <summary>默认弧标识按端点派生 / Default arc identities are derived from endpoints.</summary>
    [Fact]
    public void Route_ShouldDeriveArcIds() {
        Route<FltX> route = RouteOf(Stops("depot", "c1", "depot2"), 10.0, 20.0).Value;

        route.ArcIds.Should().HaveCount(2);
        route.ArcIds[0].Should().Be(ResourceArcs.DefaultId(new NetworkNodeId("depot"), new NetworkNodeId("c1")));
    }

    /// <summary>弧标识数量不符被拒绝 / A mismatched arc-identity count is rejected.</summary>
    [Fact]
    public void Route_ShouldRejectArcIdCountMismatch() =>
        Route<FltX>.Invoke(
            new VehicleTypeId("truck"),
            Stops("depot", "c1", "depot2"),
            Meters(10),
            Meters(20),
            new[] { new NetworkArcId("only-one") }).IsOk.Should().BeFalse();

    /// <summary>空弧标识被拒绝 / A blank arc identity is rejected.</summary>
    [Fact]
    public void Route_ShouldRejectBlankArcId() =>
        Route<FltX>.Invoke(
            new VehicleTypeId("truck"),
            Stops("depot", "c1", "depot2"),
            Meters(10),
            Meters(20),
            new[] { new NetworkArcId(" "), new NetworkArcId("ok") }).IsOk.Should().BeFalse();

    /// <summary>
    /// **签名编码有序节点与稳定弧标识**：同端点的平行弧必须形成不同签名，否则会被误判为重复列。
    /// **The signature encodes ordered nodes and stable arc identities**: parallel arcs between the same endpoints must yield
    /// different signatures or they are misjudged as duplicate columns.
    /// </summary>
    [Fact]
    public void Route_ShouldDistinguishParallelArcs() {
        List<RouteStop<FltX>> stops = Stops("depot", "c1", "depot2");
        Route<FltX> first = Route<FltX>.Invoke(
            new VehicleTypeId("truck"), stops, Meters(10), Meters(20),
            new[] { new NetworkArcId("a1"), new NetworkArcId("a2") }).Value;
        Route<FltX> second = Route<FltX>.Invoke(
            new VehicleTypeId("truck"), stops, Meters(10), Meters(20),
            new[] { new NetworkArcId("b1"), new NetworkArcId("b2") }).Value;

        first.Signature.Should().NotBe(second.Signature);
    }

    /// <summary>相同路线的签名相等 / Identical routes share a signature.</summary>
    [Fact]
    public void Route_ShouldShareSignatureWhenIdentical() {
        Route<FltX> first = RouteOf(Stops("depot", "c1", "depot2"), 10.0, 20.0).Value;
        Route<FltX> second = RouteOf(Stops("depot", "c1", "depot2"), 10.0, 20.0).Value;

        first.Signature.Should().Be(second.Signature);
    }

    /// <summary>
    /// **签名带长度前缀**：`("ab","c")` 与 `("a","bc")` 不该拼出同一个签名。
    /// **The signature is length-prefixed**: `("ab","c")` and `("a","bc")` must not produce the same one.
    /// </summary>
    [Fact]
    public void RouteSignature_ShouldNotCollideOnConcatenation() {
        Route<FltX> first = RouteOf(Stops("ab", "c"), 10.0, 20.0).Value;
        Route<FltX> second = RouteOf(Stops("a", "bc"), 10.0, 20.0).Value;

        first.Signature.Should().NotBe(second.Signature);
    }

    /// <summary>不同车辆类型产生不同签名 / Different vehicle types yield different signatures.</summary>
    [Fact]
    public void RouteSignature_ShouldIncludeVehicleType() {
        List<RouteStop<FltX>> stops = Stops("depot", "c1");
        Route<FltX> truck = Route<FltX>.Invoke(
            new VehicleTypeId("truck"), stops, Meters(10), Meters(20)).Value;
        Route<FltX> van = Route<FltX>.Invoke(
            new VehicleTypeId("van"), stops, Meters(10), Meters(20)).Value;

        truck.Signature.Should().NotBe(van.Signature);
    }

    /// <summary>解保留路线与汇总指标 / A solution keeps its routes and aggregates.</summary>
    [Fact]
    public void Solution_ShouldKeepAggregates() {
        Route<FltX> route = RouteOf(Stops("depot", "c1", "depot2"), 10.0, 20.0).Value;
        var solution = new VrptwSolution<FltX>(new[] { route }, Meters(10), Meters(20));

        solution.Routes.Should().ContainSingle();
        solution.TotalDistance.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>空解合法 / An empty solution is legal.</summary>
    [Fact]
    public void Solution_ShouldAcceptEmpty() {
        var solution = new VrptwSolution<FltX>(Array.Empty<Route<FltX>>(), Meters(0), Meters(0));

        solution.Routes.Should().BeEmpty();
    }

    /// <summary>构造车辆类型 / Build a vehicle type.</summary>
    private static Result<VehicleType<FltX>, ErrorCode, Error<ErrorCode>> VehicleOf(
        string id, double capacity, double fixedCost, int amount) =>
        VehicleType<FltX>.Invoke(
            new VehicleTypeId(id), Meters(capacity), Meters(fixedCost), amount);

    /// <summary>构造路线 / Build a route.</summary>
    private static Result<Route<FltX>, ErrorCode, Error<ErrorCode>> RouteOf(
        IReadOnlyList<RouteStop<FltX>> stops, double distance, double cost) =>
        Route<FltX>.Invoke(new VehicleTypeId("truck"), stops, Meters(distance), Meters(cost));

    /// <summary>构造停靠点序列 / Build a stop sequence.</summary>
    private static List<RouteStop<FltX>> Stops(params string[] nodeIds) =>
        nodeIds.Select(nodeId => new RouteStop<FltX>(
            new NetworkNodeId(nodeId), null, Origin, Origin, Origin, Meters(0))).ToList();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
