// 最小化目标管线测试 / Tests for the minimisation objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 最小化目标管线 / The minimisation objectives.
/// </summary>
public sealed class MinimizationObjectivesTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>**Phase-I 目标可注册** / The Phase-I objective registers.</summary>
    [Fact]
    public void PhaseOne_ShouldRegisterMinimumObjective() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);

        PhaseOne(compilation).Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>**Phase-I 目标第二次注册同样成功**：目标项可叠加，不会与已有约束冲突。</summary>
    [Fact]
    public void PhaseOne_ShouldAllowRepeatRegistration() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);

        PhaseOne(compilation).Invoke(model).IsOk.Should().BeTrue();
        PhaseOne(compilation).Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册的编译模型被拒** / An unregistered compilation model is rejected.</summary>
    [Fact]
    public void PhaseOne_ShouldRejectUnregisteredCompilation() =>
        PhaseOne(Compilation()).Invoke(Model()).IsOk.Should().BeFalse();

    /// <summary>Phase-I 目标不提供影子价格提取器 / The Phase-I objective exposes no shadow-price extractor.</summary>
    [Fact]
    public void PhaseOne_ShouldExposeNoExtractor() =>
        PhaseOne(Compilation()).Extractor().Should().BeNull();

    /// <summary>
    /// **Phase-II 目标是加入过列之后的最小化路线成本**。
    /// **The Phase-II objective minimises route cost after columns have been added.**
    /// </summary>
    [Fact]
    public void RouteCost_ShouldRegisterMinimumObjectiveAfterColumns() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);
        compilation.AddColumns(0, new[] { RouteOf("c1") }, model);

        RouteCost(compilation).Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册的编译模型被拒** / An unregistered compilation model is rejected.</summary>
    [Fact]
    public void RouteCost_ShouldRejectUnregisteredCompilation() =>
        RouteCost(Compilation()).Invoke(Model()).IsOk.Should().BeFalse();

    /// <summary>Phase-II 目标不提供影子价格提取器 / The Phase-II objective exposes no shadow-price extractor.</summary>
    [Fact]
    public void RouteCost_ShouldExposeNoExtractor() =>
        RouteCost(Compilation()).Extractor().Should().BeNull();

    /// <summary>构造 Phase-I 管线 / Build the Phase-I pipeline.</summary>
    private static PhaseOneMinimization<Flt64> PhaseOne(RouteCompilation<Flt64> compilation) => new(compilation);

    /// <summary>构造 Phase-II 管线 / Build the Phase-II pipeline.</summary>
    private static RouteCostMinimization<Flt64> RouteCost(RouteCompilation<Flt64> compilation) => new(compilation);

    /// <summary>构造路线编译模型 / Build a route compilation model.</summary>
    private static RouteCompilation<Flt64> Compilation() =>
        new(Instance(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("route_compilation");

    /// <summary>构造一个两客户、单车型的实例 / Build a two-customer, one-vehicle-type instance.</summary>
    private static VrptwInstance<Flt64> Instance() =>
        VrptwInstance<Flt64>.Invoke(
            "compilation",
            new Depot<Flt64>(Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new Depot<Flt64>(Node("depot-end"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new[] { CustomerOf("c1", "n1"), CustomerOf("c2", "n2") },
            new[] { VehicleType<Flt64>.Invoke(new VehicleTypeId("truck"), Meters(100), Meters(10), 2).Value },
            new TimeWindow<Flt64>(new TimeRange(Origin, Origin.AddHours(12)), false, TimeSpan.FromMinutes(1)),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default).Value;

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<Flt64> CustomerOf(string id, string nodeId) =>
        Customer<Flt64>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1.0),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造一条只服务给定客户的路线 / Build a route serving the given customer only.</summary>
    private static Route<Flt64> RouteOf(string customerId) {
        var stops = new List<RouteStop<Flt64>> {
            new(new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0.0)),
            new(new NetworkNodeId($"n{customerId[1..]}"), new CustomerId(customerId), Origin, Origin, Origin, Meters(1.0)),
            new(new NetworkNodeId("depot-end"), null, Origin, Origin, Origin, Meters(1.0)),
        };
        return Route<Flt64>.Invoke(
            new VehicleTypeId("truck"),
            stops,
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId("a1"), new NetworkArcId("a2") }).Value;
    }

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<Flt64> Node(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<Flt64>> { ["x"] = Meters(0.0), ["y"] = Meters(0.0) }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
