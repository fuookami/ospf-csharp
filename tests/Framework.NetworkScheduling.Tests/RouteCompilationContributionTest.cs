// 路线扩展主问题贡献测试 / Tests for route-compilation contributions.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路线扩展主问题贡献 / Route-compilation contributions.
/// </summary>
public sealed class RouteCompilationContributionTest {
    /// <summary>
    /// **所有生命周期钩子都有可用的空默认实现**：扩展只需覆写真正关心的一两个。
    /// **Every lifecycle hook has a usable empty default**: an extension overrides only the ones it cares about.
    /// </summary>
    [Fact]
    public void Contribution_ShouldProvideWorkingDefaults() {
        IRouteCompilationContribution<Flt64> contribution = new MinimalContribution();

        contribution.Register(Compilation(), Model()).IsOk.Should().BeTrue();
        contribution.AddColumns(Array.Empty<RouteCompilationColumn<Flt64>>(), Model()).IsOk.Should().BeTrue();
        contribution.RemoveColumns(Array.Empty<RouteCompilationColumn<Flt64>>(), Model()).IsOk.Should().BeTrue();
        contribution.RefreshShadowPrices(Model(), EmptyDuals()).IsOk.Should().BeTrue();
        contribution.Extract(Model(), Array.Empty<(Route<Flt64>, Flt64)>()).IsOk.Should().BeTrue();
    }

    /// <summary>默认提取结果为空值而不是错误 / The default extraction yields null rather than an error.</summary>
    [Fact]
    public void Contribution_DefaultExtractShouldBeNull() {
        // 默认接口成员只能在接口类型的实例上调用 / default interface members are only callable through an interface-typed instance
        IRouteCompilationContribution<Flt64> contribution = new MinimalContribution();
        contribution.Extract(Model(), Array.Empty<(Route<Flt64>, Flt64)>()).Value.Should().BeNull();
    }

    /// <summary>默认对偶快照为空表 / The default dual snapshot is empty.</summary>
    [Fact]
    public void Contribution_DefaultShadowPricesShouldBeEmpty() {
        IRouteCompilationContribution<Flt64> contribution = new MinimalContribution();
        contribution.RefreshShadowPrices(Model(), EmptyDuals()).Value.Should().BeEmpty();
    }

    /// <summary>**名称即提取结果键**，由扩展自行给出 / The name doubles as the extraction-result key.</summary>
    [Fact]
    public void Contribution_ShouldExposeStableName() =>
        new MinimalContribution().Name.Should().Be("minimal");

    /// <summary>
    /// **单列视图的两种系数表示内容一致**：领域值与 solver 值指向同一组行键。
    /// **The two coefficient representations cover the same row keys**: domain values and solver values align.
    /// </summary>
    [Fact]
    public void Column_ShouldKeepBothCoefficientRepresentations() {
        var column = new RouteCompilationColumn<Flt64>(
            Route(),
            Expression(),
            new Dictionary<string, Flt64> { ["row_a"] = new Flt64(2.0), ["row_b"] = new Flt64(3.0) },
            new Dictionary<string, Flt64> { ["row_a"] = new Flt64(2.0), ["row_b"] = new Flt64(3.0) });

        column.Coefficients.Keys.Should().BeEquivalentTo(column.SolverCoefficients.Keys);
        column.Coefficients["row_b"].ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>单列视图对传入字典做快照 / The column view snapshots the dictionaries it is given.</summary>
    [Fact]
    public void Column_ShouldSnapshotCoefficients() {
        var domain = new Dictionary<string, Flt64> { ["row_a"] = new Flt64(1.0) };
        var solver = new Dictionary<string, Flt64> { ["row_a"] = new Flt64(1.0) };
        var column = new RouteCompilationColumn<Flt64>(Route(), Expression(), domain, solver);

        domain["row_b"] = new Flt64(9.0);
        solver["row_b"] = new Flt64(9.0);

        column.Coefficients.Should().HaveCount(1);
        column.SolverCoefficients.Should().HaveCount(1);
    }

    /// <summary>单列视图保留路线与列变量 / The column view keeps the route and column variable.</summary>
    [Fact]
    public void Column_ShouldKeepRouteAndVariable() {
        Route<Flt64> route = Route();
        LinearPolynomial<Flt64> variable = Expression();
        var column = new RouteCompilationColumn<Flt64>(
            route, variable, new Dictionary<string, Flt64>(), new Dictionary<string, Flt64>());

        column.Route.Should().BeSameAs(route);
        column.Variable.Should().BeSameAs(variable);
    }

    /// <summary>构造路线编译模型 / Build a route compilation model.</summary>
    private static RouteCompilation<Flt64> Compilation() =>
        new(
            VrptwInstance<Flt64>.Invoke(
                "contribution",
                new Depot<Flt64>(
                    Node("depot"),
                    Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure.ServiceTimeWindow
                        .Invoke(Origin, Origin.AddHours(12)).Value),
                new Depot<Flt64>(
                    Node("depot-end"),
                    Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure.ServiceTimeWindow
                        .Invoke(Origin, Origin.AddHours(12)).Value),
                new[] { CustomerOf("c1", "n1") },
                new[] {
                    VehicleType<Flt64>.Invoke(new VehicleTypeId("truck"), Meters(100), Meters(10), 2).Value,
                },
                new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
                    new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                        Origin, Origin.AddHours(12)),
                    false,
                    TimeSpan.FromMinutes(1)),
                VrptwUnits.Invoke(Quantities.Unit.SIBaseUnits.Meter, Quantities.Unit.SIBaseUnits.Meter, Quantities.Unit.SIBaseUnits.Meter).Value,
                VrptwTolerances.Default).Value,
            Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造客户 / Build a customer.</summary>
    private static Customer<Flt64> CustomerOf(string id, string nodeId) =>
        Customer<Flt64>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1.0),
            Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure.ServiceTimeWindow
                .Invoke(Origin, Origin.AddHours(12)).Value,
            TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<Flt64> Node(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantities.Quantity.Quantity<Flt64>> {
                ["x"] = Meters(0.0),
                ["y"] = Meters(0.0),
            }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantities.Quantity.Quantity<Flt64> Meters(double value) =>
        new(new Flt64(value), Quantities.Unit.SIBaseUnits.Meter);

    /// <summary>业务时间原点 / The business time origin.</summary>
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("contribution");

    /// <summary>空的 solver 对偶解 / An empty solver dual solution.</summary>
    private static MetaDualSolution EmptyDuals() => MetaDualSolution.Empty;

    /// <summary>构造一个只有名称的最小贡献 / Build a minimal contribution carrying only a name.</summary>
    private sealed class MinimalContribution : IRouteCompilationContribution<Flt64> {
        /// <inheritdoc/>
        public string Name => "minimal";

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, Flt64> Coefficients(ResourceArc<VehicleTypeId> arc) =>
            new Dictionary<string, Flt64>();
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression() =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, new Core.Variable.URealVar("x")) }, Flt64.Zero);

    /// <summary>构造一条合法的三停靠路线（起点仓 → 客户 → 终点仓）/ Build a valid three-stop route.</summary>
    private static Route<Flt64> Route() =>
        Route<Flt64>.Invoke(
            new VehicleTypeId("truck"),
            new[] {
                new RouteStop<Flt64>(
                    new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0.0)),
                new RouteStop<Flt64>(
                    new NetworkNodeId("n1"), new CustomerId("c1"), Origin, Origin, Origin, Meters(1.0)),
                new RouteStop<Flt64>(
                    new NetworkNodeId("depot-end"), null, Origin, Origin, Origin, Meters(1.0)),
            },
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId("a1"), new NetworkArcId("a2") }).Value;
}
