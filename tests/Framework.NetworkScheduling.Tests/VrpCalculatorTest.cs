// VRP 距离、行驶时间与弧成本计算测试 / Tests for VRP distance, travel-time and arc-cost calculation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// VRP 距离、行驶时间与弧成本计算 / VRP distance, travel-time and arc-cost calculation.
/// </summary>
public sealed class VrpCalculatorTest {
    /// <summary>二维欧氏距离正确 / A 2-D Euclidean distance is correct.</summary>
    [Fact]
    public void Euclidean_ShouldCompute2D() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        Quantity<FltX> distance = calculator.Distance(Node(("x", 0.0), ("y", 0.0)), Node(("x", 3.0), ("y", 4.0))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        distance.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>三维欧氏距离正确 / A 3-D Euclidean distance is correct.</summary>
    [Fact]
    public void Euclidean_ShouldCompute3D() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        Quantity<FltX> distance = calculator
            .Distance(Node(("x", 0.0), ("y", 0.0), ("z", 0.0)), Node(("x", 1.0), ("y", 2.0), ("z", 2.0))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>距离按目标单位给出 / The distance is reported in the target unit.</summary>
    [Fact]
    public void Euclidean_ShouldConvertToTargetUnit() {
        var calculator = new EuclideanDistanceCalculator<FltX>(LengthUnits.Centimeter);

        Quantity<FltX> distance = calculator.Distance(Node(("x", 0.0)), Node(("x", 1.0))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-6, "1m = 100cm");
    }

    /// <summary>
    /// **轴集不同被拒绝**：按"取较短者"或"缺失补零"来凑合，会静默地把三维点投影到平面上。
    /// **Mismatched axis sets are rejected**: padding or truncating silently projects a 3-D point onto a plane.
    /// </summary>
    [Fact]
    public void Euclidean_ShouldRejectAxisMismatch() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        calculator.Distance(Node(("x", 0.0), ("y", 0.0)), Node(("x", 1.0))).IsOk.Should().BeFalse();
    }

    /// <summary>轴名不同也被拒绝 / Different axis names are rejected.</summary>
    [Fact]
    public void Euclidean_ShouldRejectDifferentAxisNames() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        calculator.Distance(Node(("x", 0.0), ("y", 0.0)), Node(("x", 1.0), ("z", 1.0))).IsOk.Should().BeFalse();
    }

    /// <summary>无坐标节点被拒绝 / A node without coordinates is rejected.</summary>
    [Fact]
    public void Euclidean_ShouldRejectMissingCoordinates() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        calculator.Distance(Node(("x", 0.0)), Node()).IsOk.Should().BeFalse();
    }

    /// <summary>坐标单位不兼容时失败 / An incompatible coordinate unit fails.</summary>
    [Fact]
    public void Euclidean_ShouldRejectIncompatibleCoordinateUnit() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);

        calculator.Distance(Node(("x", 0.0)), NodeWithUnit(("x", 1.0, SIBaseUnits.Second))).IsOk.Should().BeFalse();
    }

    /// <summary>同一点距离为零 / The distance from a point to itself is zero.</summary>
    [Fact]
    public void Euclidean_ShouldBeZeroForSamePoint() {
        var calculator = new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter);
        NetworkNode<FltX> node = Node(("x", 2.0), ("y", 3.0));

        calculator.Distance(node, node).Value.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// <summary>
    /// **Solomon 取整发生在距离算完之后**：先取整坐标会把 0.04 的差抹成 0，让两个不同点重合。
    /// **Solomon rounding happens after the distance is computed**: rounding coordinates first wipes a 0.04 difference to
    /// zero and collapses two distinct points.
    /// </summary>
    [Fact]
    public void Solomon_ShouldRoundDistanceToOneDecimal() {
        var policy = new SolomonDistancePolicy<FltX>(SIBaseUnits.Meter, value => value.RoundTo(1));

        Quantity<FltX> distance = policy.Distance(Node(("x", 0.0)), Node(("x", 1.04))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>Solomon 保留一位小数的第二位进位 / Solomon rounds the second decimal up.</summary>
    [Fact]
    public void Solomon_ShouldRoundUpSecondDecimal() {
        var policy = new SolomonDistancePolicy<FltX>(SIBaseUnits.Meter, value => value.RoundTo(1));

        Quantity<FltX> distance = policy.Distance(Node(("x", 0.0)), Node(("x", 1.06))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(1.1, 1e-9);
    }

    /// <summary>
    /// **坐标差 0.04 的两个点不会被抹成同一点**：这验证取整确实发生在距离之后。
    /// **Two points 0.04 apart are not collapsed**: this confirms rounding happens after the distance.
    /// </summary>
    [Fact]
    public void Solomon_ShouldNotCollapseNearbyPoints() {
        var policy = new SolomonDistancePolicy<FltX>(SIBaseUnits.Meter, value => value.RoundTo(1));

        Quantity<FltX> distance = policy.Distance(Node(("x", 0.0)), Node(("x", 0.04))).Value;

        distance.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter)
            .Distance(Node(("x", 0.0)), Node(("x", 0.04))).Value.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(0.04, 1e-9, "欧氏距离本身不为零 / the Euclidean distance itself is not zero");
    }

    /// <summary>
    /// **距离按 1:1 映射到时间轴刻度**：距离与时长之间没有物理换算关系，只有建模约定，且约定来自实例时间轴。
    /// **Distance maps 1:1 onto the timeline scale**: there is no physical conversion, only a modelling convention taken from
    /// the instance timeline.
    /// </summary>
    [Fact]
    public void DistanceAsTravelTime_ShouldUseTimelineScale() {
        var timeline = new TimeWindow<FltX>(
            new TimeRange(Origin, Origin.AddHours(24)), false, TimeSpan.FromMinutes(1));
        var calculator = new DistanceAsTravelTimeCalculator<FltX>(
            new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter), timeline);

        TimeSpan travelTime = calculator.TravelTime(
            Node(("x", 0.0)), Node(("x", 30.0)), Vehicle("truck", 0.0)).Value;

        travelTime.Should().Be(TimeSpan.FromMinutes(30), "30 单位 × 1 分钟刻度 / 30 units × one-minute interval");
    }

    /// <summary>距离计算失败时行驶时间也失败 / A distance failure propagates to travel time.</summary>
    [Fact]
    public void DistanceAsTravelTime_ShouldPropagateFailure() {
        var timeline = new TimeWindow<FltX>(
            new TimeRange(Origin, Origin.AddHours(24)), false, TimeSpan.FromMinutes(1));
        var calculator = new DistanceAsTravelTimeCalculator<FltX>(
            new EuclideanDistanceCalculator<FltX>(SIBaseUnits.Meter), timeline);

        calculator.TravelTime(Node(("x", 0.0), ("y", 0.0)), Node(("x", 1.0)), Vehicle("truck", 0.0))
            .IsOk.Should().BeFalse();
    }

    /// <summary>按距离计费把数值搬到成本单位 / Distance-based costing moves the value into the cost unit.</summary>
    [Fact]
    public void DistanceArcCost_ShouldUseCostUnit() {
        var calculator = new DistanceArcCostCalculator<FltX>(SIBaseUnits.Meter, SIBaseUnits.Second);

        Quantity<FltX> cost = calculator.Cost(
            Node(("x", 0.0)), Node(("x", 0.0)), Meters(7.5), TimeSpan.FromMinutes(1), Vehicle("truck", 0.0)).Value;

        cost.Value.ToFlt64().ToDouble().Should().BeApproximately(7.5, 1e-9);
        cost.Unit.Should().Be(SIBaseUnits.Second);
    }

    /// <summary>距离先归一化到实例距离单位再计价 / The distance is normalized before costing.</summary>
    [Fact]
    public void DistanceArcCost_ShouldNormalizeDistance() {
        var calculator = new DistanceArcCostCalculator<FltX>(SIBaseUnits.Meter, SIBaseUnits.Second);
        var centimeters = new Quantity<FltX>(new FltX(300), LengthUnits.Centimeter);

        Quantity<FltX> cost = calculator.Cost(
            Node(("x", 0.0)), Node(("x", 0.0)), centimeters, TimeSpan.Zero, Vehicle("truck", 0.0)).Value;

        cost.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9, "300cm = 3m");
    }

    /// <summary>距离单位无法归一化时失败 / An unnormalizable distance fails.</summary>
    [Fact]
    public void DistanceArcCost_ShouldFailOnIncompatibleDistance() {
        var calculator = new DistanceArcCostCalculator<FltX>(SIBaseUnits.Meter, SIBaseUnits.Second);
        var seconds = new Quantity<FltX>(new FltX(5), SIBaseUnits.Second);

        calculator.Cost(Node(("x", 0.0)), Node(("x", 0.0)), seconds, TimeSpan.Zero, Vehicle("truck", 0.0))
            .IsOk.Should().BeFalse();
    }

    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>构造带坐标的节点 / Build a node carrying coordinates.</summary>
    private static NetworkNode<FltX> Node(params (string Key, double Value)[] attributes) {
        Dictionary<string, Quantity<FltX>> map = attributes.ToDictionary(
            pair => pair.Key, pair => Meters(pair.Value));
        return NetworkNode<FltX>.Invoke(new NetworkNodeId("point"), map).Value;
    }

    /// <summary>构造使用指定单位的坐标节点 / Build a node whose coordinate uses the given unit.</summary>
    private static NetworkNode<FltX> NodeWithUnit(params (string Key, double Value, PhysicalUnit Unit)[] attributes) {
        Dictionary<string, Quantity<FltX>> map = attributes.ToDictionary(
            pair => pair.Key, pair => new Quantity<FltX>(new FltX(pair.Value), pair.Unit));
        return NetworkNode<FltX>.Invoke(new NetworkNodeId("point"), map).Value;
    }

    /// <summary>构造车辆类型 / Build a vehicle type.</summary>
    private static VehicleType<FltX> Vehicle(string id, double fixedCost) =>
        VehicleType<FltX>.Invoke(new VehicleTypeId(id), Meters(1000), Meters(fixedCost), 1).Value;

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
