// 路线编译模型测试 / Tests for the route compilation model.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路线编译模型 / The route compilation model.
/// </summary>
public sealed class RouteCompilationModelTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>主路径：两个客户可注册 / The main path registers: two customers.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Compilation().Register(Model()).IsOk.Should().BeTrue();

    /// <summary>
    /// **注册同时建好人工覆盖、客户覆盖、车队用量与路线成本四个符号**。
    /// **Registration builds all four symbols**: artificial coverage, customer coverage, fleet usage and route cost.
    /// </summary>
    [Fact]
    public void Register_ShouldBuildEverySymbol() {
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(Model());

        compilation.ArtificialCoverage.Should().NotBeNull();
        compilation.CustomerCoverage.Should().NotBeNull();
        compilation.CustomerCoverage!.Count.Should().Be(2);
        compilation.FleetUsage.Should().NotBeNull();
        compilation.FleetUsage!.Count.Should().Be(1);
        compilation.RouteCost.Should().NotBeNull();
    }

    /// <summary>路线成本符号初始为空 / The route-cost symbol starts empty.</summary>
    [Fact]
    public void Register_ShouldLeaveRouteCostEmpty() {
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(Model());

        compilation.RouteCost!.Polynomial.Monomials.Should().BeEmpty();
    }

    /// <summary>**重复注册幂等** / Repeated registration is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();

        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册时不能添加列** / Columns cannot be added before registration.</summary>
    [Fact]
    public void AddColumns_ShouldRejectBeforeRegistration() =>
        Compilation().AddColumns(0, new[] { RouteOf("c1") }, Model()).IsOk.Should().BeFalse();

    /// <summary>空列集是安全空操作 / An empty column batch is a safe no-op.</summary>
    [Fact]
    public void AddColumns_ShouldAcceptEmptyBatch() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        Result<IReadOnlyList<Route<FltX>>, Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            compilation.AddColumns(0, Array.Empty<Route<FltX>>(), model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeEmpty();
        compilation.X.Should().BeEmpty();
    }

    /// <summary>
    /// **一列同时进入成本、客户覆盖与车队用量三处**：漏任一处都会让模型内部不自洽而不报错。
    /// **One column enters cost, customer coverage and fleet usage together**: missing any one makes the model internally inconsistent
    /// without any error.
    /// </summary>
    [Fact]
    public void AddColumns_ShouldAccumulateCostCoverageAndUsage() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        compilation.AddColumns(0, new[] { RouteOf("c1") }, model).IsOk.Should().BeTrue();

        compilation.X.Should().HaveCount(1);
        compilation.X[0].Items.Should().HaveCount(1);
        compilation.RouteCost!.Polynomial.Monomials.Should().HaveCount(1);
        compilation.FleetUsage![0].Polynomial.Monomials.Should().HaveCount(1);
        compilation.CustomerCoverage![0].Polynomial.Monomials.Should().HaveCount(2, "a_0 + 覆盖 c1 的列 / a_0 plus the c1 column");
        compilation.CustomerCoverage[1].Polynomial.Monomials.Should().HaveCount(1, "只有 a_1，未覆盖 c2 / only a_1, no c2 column");
    }

    /// <summary>签名去重：同一条路线第二次不会被重复加入 / Signature dedup: the same route is not added twice.</summary>
    [Fact]
    public void AddColumns_ShouldDeduplicateBySignature() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        compilation.AddColumns(0, new[] { RouteOf("c1") }, model).IsOk.Should().BeTrue();
        Result<IReadOnlyList<Route<FltX>>, Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> second =
            compilation.AddColumns(1, new[] { RouteOf("c1") }, model);

        second.Value.Should().BeEmpty();
        compilation.X.Should().HaveCount(1, "第二轮没有可加的列 / the second round adds nothing");
    }

    /// <summary>路线变量可查 / A route's column variable is queryable.</summary>
    [Fact]
    public void ColumnVariable_ShouldResolveAfterAdding() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);
        compilation.AddColumns(0, new[] { RouteOf("c1") }, model);

        compilation.ColumnVariable(RouteOf("c1")).IsOk.Should().BeTrue();
    }

    /// <summary>**未知路线查询变量失败** / Querying an unknown route's variable fails.</summary>
    [Fact]
    public void ColumnVariable_ShouldRejectUnknownRoute() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        compilation.ColumnVariable(RouteOf("c2")).IsOk.Should().BeFalse();
    }

    /// <summary>未注册时切换 Phase II 失败 / Switching to Phase II before registration fails.</summary>
    [Fact]
    public void SwitchToPhaseTwo_ShouldRejectBeforeRegistration() =>
        Compilation().SwitchToPhaseTwo(Model()).IsOk.Should().BeFalse();

    /// <summary>路线变量值为空时提取为空 / Extraction over an empty pool yields nothing.</summary>
    [Fact]
    public void ExtractRouteValues_ShouldBeEmptyWithoutColumns() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        Result<IReadOnlyList<(Route<FltX> Route, Flt64 Value)>, Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> values =
            compilation.ExtractRouteValues(model);

        values.IsOk.Should().BeTrue();
        values.Value.Should().BeEmpty();
    }

    /// <summary>未求解时解式提取为空，而不是报错 / Extraction over an unsolved model is empty rather than an error.</summary>
    [Fact]
    public void ExtractSolution_ShouldBeEmptyWhenUnsolved() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);
        compilation.AddColumns(0, new[] { RouteOf("c1") }, model);

        compilation.ExtractSolution(model).Value.Should().BeEmpty();
    }

    /// <summary>空移除集是安全空操作 / An empty removal set is a safe no-op.</summary>
    [Fact]
    public void RemoveColumns_ShouldAcceptEmptyBatch() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<FltX> compilation = Compilation();
        compilation.Register(model);

        compilation.RemoveColumns(Array.Empty<Route<FltX>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>构造一个路线编译模型 / Build a route compilation model.</summary>
    private static RouteCompilation<FltX> Compilation() =>
        new(Instance(), FltXNetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("route_compilation");

    /// <summary>构造一个两客户、单车型的实例 / Build a two-customer, one-vehicle-type instance.</summary>
    private static VrptwInstance<FltX> Instance() =>
        VrptwInstance<FltX>.Invoke(
            "compilation",
            new Depot<FltX>(Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new Depot<FltX>(Node("depot-end"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value),
            new[] { CustomerOf("c1", "n1"), CustomerOf("c2", "n2") },
            new[] {
                VehicleType<FltX>.Invoke(new VehicleTypeId("truck"), Meters(100), Meters(10), 2).Value,
            },
            new TimeWindow<FltX>(
                new TimeRange(Origin, Origin.AddHours(12)), false, TimeSpan.FromMinutes(1)),
            VrptwUnits.Invoke(SIBaseUnits.Meter, SIBaseUnits.Meter, SIBaseUnits.Meter).Value,
            VrptwTolerances.Default).Value;

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<FltX> CustomerOf(string id, string nodeId) =>
        Customer<FltX>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1.0),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value,
            TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造一条只服务 c1 的路线 / Build a route serving c1 only.</summary>
    private static Route<FltX> RouteOf(string customerId) {
        var stops = new List<RouteStop<FltX>> {
            new(new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0.0)),
            new(new NetworkNodeId($"n{customerId[1..]}"), new CustomerId(customerId), Origin, Origin, Origin, Meters(1.0)),
            new(new NetworkNodeId("depot-end"), null, Origin, Origin, Origin, Meters(1.0)),
        };
        return Route<FltX>.Invoke(
            new VehicleTypeId("truck"),
            stops,
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId("a1"), new NetworkArcId("a2") }).Value;
    }

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<FltX>> { ["x"] = Meters(0.0), ["y"] = Meters(0.0) }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
