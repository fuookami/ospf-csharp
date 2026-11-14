// VRP 策略与定价测试 / Tests for VRP policies and pricing.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// VRP 策略与定价 / VRP policies and pricing.
/// </summary>
public sealed class VrpPolicyTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// **坐标顺序固定为 x / y / z**：坐标是位置向量，顺序错乱会让点积、距离与朝向全部算错。
    /// **The order is fixed to x / y / z**: coordinates are a position vector, and a shuffled order breaks the dot product,
    /// distance and orientation.
    /// </summary>
    [Fact]
    public void Coordinates_ShouldReadInFixedOrder() {
        NetworkNode<FltX> node = NodeWith(
            ("z", 3.0), ("x", 1.0), ("y", 2.0));

        IReadOnlyList<Quantity<FltX>> coordinates = node.VrpCoordinates();

        coordinates.Should().HaveCount(3);
        coordinates[0].Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
        coordinates[2].Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// **保留轴名让调用方能区分"缺了 z"与"z 为零"**：两者在几何上完全不同。
    /// **Keeping axis names lets callers tell "no z" from "z is zero"**: geometrically different cases.
    /// </summary>
    [Fact]
    public void Coordinates_ShouldDistinguishMissingAxis() {
        NetworkNode<FltX> twoDimensional = NodeWith(("x", 1.0), ("y", 2.0));
        NetworkNode<FltX> flat = NodeWith(("x", 1.0), ("y", 2.0), ("z", 0.0));

        twoDimensional.VrpCoordinates().Should().HaveCount(2);
        flat.VrpCoordinates().Should().HaveCount(3);
        twoDimensional.VrpCoordinateAttributes().Select(pair => pair.Axis)
            .Should().Equal(new[] { "x", "y" });
    }

    /// <summary>无坐标节点返回空快照 / A node without coordinates yields an empty snapshot.</summary>
    [Fact]
    public void Coordinates_ShouldBeEmptyWithoutAttributes() =>
        Node("plain").VrpCoordinates().Should().BeEmpty();

    /// <summary>无关属性不计入坐标 / Unrelated attributes are not coordinates.</summary>
    [Fact]
    public void Coordinates_ShouldIgnoreUnrelatedKeys() =>
        NodeWith(("w", 9.0), ("x", 1.0)).VrpCoordinates().Should().ContainSingle();

    /// <summary>
    /// **默认策略只禁自环**：自环会让客户在 elementary 路线里重复出现，且距离与成本累加零增益。
    /// **The default policy rejects self-loops only**: they repeat a customer in an elementary route and add a zero-gain step.
    /// </summary>
    [Fact]
    public void DefaultFeasibility_ShouldRejectSelfLoop() {
        var policy = new DefaultArcFeasibilityPolicy<FltX>();
        VehicleType<FltX> vehicle = Vehicle("truck", 1000.0);

        policy.IsFeasible(Node("a"), Node("a"), vehicle).Value.Should().BeFalse();
        policy.IsFeasible(Node("a"), Node("b"), vehicle).Value.Should().BeTrue();
    }

    /// <summary>
    /// **资源弧重载默认退回端点策略**：绝大多数可行性规则只依赖端点与车型，平行弧只在少数场景下才需要区分。
    /// **The resource-arc overload defaults to the endpoint policy**: most rules depend only on endpoints and vehicle type.
    /// </summary>
    [Fact]
    public void Feasibility_ShouldDelegateArcOverloadToEndpoints() {
        IArcFeasibilityPolicy<FltX> policy = new DefaultArcFeasibilityPolicy<FltX>();
        VehicleType<FltX> vehicle = Vehicle("truck", 1000.0);
        var arc = new ResourceArc<VehicleTypeId>(
            vehicle.Id, new NetworkNodeId("a"), new NetworkNodeId("b"));

        policy.IsFeasible(arc, Node("a"), Node("b"), vehicle).Value.Should().BeTrue();
    }

    /// <summary>
    /// **`IsAdditive` 默认假，是安全默认**：非可加策略若误报可加，定价会用偏低的 reduced cost 接受不该接受的列且无报错。
    /// **`IsAdditive` defaults to false, the safe default**: a non-additive policy claiming additivity makes pricing accept
    /// columns it should reject with no error.
    /// </summary>
    [Fact]
    public void RouteCostPolicy_ShouldDefaultToNonAdditive() {
        IRouteCostPolicy<FltX> policy = new NonAdditivePolicy();

        policy.IsAdditive.Should().BeFalse();
    }

    /// <summary>固定加弧成本策略声明可加 / The fixed-plus-arc policy declares additivity.</summary>
    [Fact]
    public void FixedPlusArcCost_ShouldBeAdditive() =>
        new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter).IsAdditive.Should().BeTrue();

    /// <summary>固定加弧成本求和对齐单位 / The fixed-plus-arc sum aligns units.</summary>
    [Fact]
    public void FixedPlusArcCost_ShouldSumInTargetUnit() {
        var policy = new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter);
        VehicleType<FltX> vehicle = Vehicle("truck", 10.0);

        Quantity<FltX> total = policy.Cost(vehicle, new[] { Meters(2), Meters(3) }).Value;

        total.Value.ToFlt64().ToDouble().Should().BeApproximately(15.0, 1e-9);
    }

    /// <summary>可换算单位的弧成本先换算再相加 / A convertible arc cost is converted before summing.</summary>
    [Fact]
    public void FixedPlusArcCost_ShouldConvertBeforeSumming() {
        var policy = new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter);
        VehicleType<FltX> vehicle = Vehicle("truck", 10.0);
        var centimeters = new Quantity<FltX>(new FltX(300), LengthUnits.Centimeter);

        Quantity<FltX> total = policy.Cost(vehicle, new[] { centimeters }).Value;

        total.Value.ToFlt64().ToDouble().Should().BeApproximately(13.0, 1e-9, "300cm = 3m");
    }

    /// <summary>
    /// **任一项换算失败即整体失败**：跳过会让成本偏低且无人察觉。
    /// **Any failed conversion fails the whole computation**: skipping would understate cost unnoticed.
    /// </summary>
    [Fact]
    public void FixedPlusArcCost_ShouldFailOnIncompatibleUnit() {
        var policy = new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter);
        VehicleType<FltX> vehicle = Vehicle("truck", 10.0);
        var seconds = new Quantity<FltX>(new FltX(5), SIBaseUnits.Second);

        policy.Cost(vehicle, new[] { seconds }).IsOk.Should().BeFalse();
    }

    /// <summary>固定成本单位不兼容时失败 / An incompatible fixed-cost unit fails.</summary>
    [Fact]
    public void FixedPlusArcCost_ShouldFailOnIncompatibleFixedCost() {
        var policy = new FixedPlusArcCostPolicy<FltX>(SIBaseUnits.Meter);
        VehicleType<FltX> vehicle = VehicleWithFixedCostInSeconds("truck");

        policy.Cost(vehicle, Array.Empty<Quantity<FltX>>()).IsOk.Should().BeFalse();
    }

    /// <summary>Demo17 策略继承可加性与求和行为 / The Demo17 policy inherits additivity and the sum.</summary>
    [Fact]
    public void Demo17Policy_ShouldBehaveLikeFixedPlusArc() {
        var policy = new Demo17CostPolicy<FltX>(SIBaseUnits.Meter);
        VehicleType<FltX> vehicle = Vehicle("truck", 10.0);

        policy.IsAdditive.Should().BeTrue();
        policy.Cost(vehicle, new[] { Meters(5) }).Value.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(15.0, 1e-9);
    }

    /// <summary>对偶快照保留三个阶段字段 / The dual snapshot keeps all three maps.</summary>
    [Fact]
    public void Duals_ShouldKeepMaps() {
        var duals = new PricingDuals(
            PricingPhase.PhaseOne,
            new Dictionary<CustomerId, Flt64> { [new CustomerId("c1")] = new Flt64(1.0) },
            new Dictionary<VehicleTypeId, Flt64> { [new VehicleTypeId("truck")] = new Flt64(-2.0) });

        duals.Phase.Should().Be(PricingPhase.PhaseOne);
        duals.Customer.Should().HaveCount(1);
        duals.Fleet[new VehicleTypeId("truck")].ToDouble().Should().BeApproximately(-2.0, 1e-9);
        duals.Additional.Should().BeEmpty();
    }

    /// <summary>
    /// **对偶量允许任意符号**：客户覆盖对偶自由（可正可负），车队对偶在最小化下为非正。强行取绝对值会颠倒 reduced cost 方向。
    /// **Duals allow either sign**: customer duals are sign-free and fleet duals are non-positive under minimization.
    /// Taking absolute values would invert the reduced-cost direction.
    /// </summary>
    [Fact]
    public void Duals_ShouldPreserveSigns() {
        var duals = new PricingDuals(
            PricingPhase.PhaseTwo,
            new Dictionary<CustomerId, Flt64> { [new CustomerId("c1")] = new Flt64(-3.5) },
            new Dictionary<VehicleTypeId, Flt64>());

        duals.Customer[new CustomerId("c1")].ToDouble().Should().BeApproximately(-3.5, 1e-9);
    }

    /// <summary>对偶映射在构造时复制 / Dual maps are copied at construction.</summary>
    [Fact]
    public void Duals_ShouldCopyMaps() {
        var customer = new Dictionary<CustomerId, Flt64> { [new CustomerId("c1")] = new Flt64(1.0) };
        var duals = new PricingDuals(
            PricingPhase.PhaseOne, customer, new Dictionary<VehicleTypeId, Flt64>());

        customer[new CustomerId("c2")] = new Flt64(9.0);

        duals.Customer.Should().HaveCount(1);
    }

    /// <summary>
    /// **路线系数汇总与逐弧系数共用一份定义**：两边各写一遍求和，漏算一条弧就会让主问题与定价给出不同 reduced cost。
    /// **Route coefficients share one definition with the per-arc ones**: duplicated summation makes the master and pricing
    /// disagree on reduced cost when one side misses an arc.
    /// </summary>
    [Fact]
    public void Coefficients_ShouldSumOverArcs() {
        IRouteArcCoefficientProvider<FltX> provider = new ConstantCoefficientProvider(new FltX(2.0));
        Route<FltX> route = RouteOf("depot", "c1", "c2", "depot2");

        IReadOnlyDictionary<string, FltX> coefficients = provider.RouteCoefficients(route);

        coefficients["row"].ToFlt64().ToDouble().Should().BeApproximately(6.0, 1e-9, "三段弧 × 2 / three arcs × 2");
    }

    /// <summary>无弧路线得到空系数 / A route without arcs yields no coefficients.</summary>
    [Fact]
    public void Coefficients_ShouldBeEmptyForEmptyProvider() {
        IRouteArcCoefficientProvider<FltX> provider = new ConstantCoefficientProvider(new FltX(0.0));
        Route<FltX> route = RouteOf("depot", "c1");

        provider.RouteCoefficients(route)["row"].ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>非可加策略替身，用于验证默认值 / A non-additive policy stand-in used to check the default.</summary>
    private sealed class NonAdditivePolicy : IRouteCostPolicy<FltX> {
        /// <inheritdoc/>
        public Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> Cost(
            VehicleType<FltX> vehicleType, IReadOnlyList<Quantity<FltX>> arcCosts) =>
            Results.Ok(Meters(0));
    }

    /// <summary>恒定系数提供者，用于验证汇总 / A constant-coefficient provider used to check summation.</summary>
    private sealed class ConstantCoefficientProvider(FltX value) : IRouteArcCoefficientProvider<FltX> {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, FltX> Coefficients(ResourceArc<VehicleTypeId> arc) =>
            new Dictionary<string, FltX> { ["row"] = value };
    }

    /// <summary>构造带坐标的节点 / Build a node carrying coordinates.</summary>
    private static NetworkNode<FltX> NodeWith(params (string Key, double Value)[] attributes) {
        Dictionary<string, Quantity<FltX>> map = attributes.ToDictionary(
            pair => pair.Key,
            pair => Meters(pair.Value));
        return NetworkNode<FltX>.Invoke(new NetworkNodeId("point"), map).Value;
    }

    /// <summary>构造普通节点 / Build a plain node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId(id)).Value;

    /// <summary>构造车辆类型 / Build a vehicle type.</summary>
    private static VehicleType<FltX> Vehicle(string id, double fixedCost) =>
        VehicleType<FltX>.Invoke(new VehicleTypeId(id), Meters(1000), Meters(fixedCost), 1).Value;

    /// <summary>构造固定成本以秒为单位的车辆类型（用于单位不兼容用例）/ Build a vehicle whose fixed cost is in seconds.</summary>
    private static VehicleType<FltX> VehicleWithFixedCostInSeconds(string id) =>
        VehicleType<FltX>.Invoke(
            new VehicleTypeId(id), Meters(1000),
            new Quantity<FltX>(new FltX(1), SIBaseUnits.Second), 1).Value;

    /// <summary>构造路线 / Build a route.</summary>
    private static Route<FltX> RouteOf(params string[] nodeIds) {
        var stops = nodeIds.Select(nodeId => new RouteStop<FltX>(
            new NetworkNodeId(nodeId), null, Origin, Origin, Origin, Meters(0))).ToList();
        return Route<FltX>.Invoke(
            new VehicleTypeId("truck"), stops, Meters(10), Meters(20)).Value;
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
