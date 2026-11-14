// 客户覆盖与车队数量约束管线测试 / Tests for the coverage and fleet-size pipelines.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 客户覆盖与车队数量约束管线 / The coverage and fleet-size constraint pipelines.
/// </summary>
public sealed class CoverageAndFleetConstraintsTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>**每个客户一条覆盖行** / One coverage row per customer.</summary>
    [Fact]
    public void CustomerCoverage_ShouldRegisterOneRowPerCustomer() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);

        Coverage(compilation).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count(constraint =>
                constraint.Name.StartsWith("customer_coverage_", StringComparison.Ordinal))
            .Should().Be(2);
    }

    /// <summary>覆盖行归属自身分组 / Coverage rows belong to the pipeline's group.</summary>
    [Fact]
    public void CustomerCoverage_ShouldGroupItsRows() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);
        CustomerCoverageConstraint<Flt64> pipeline = Coverage(compilation);
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(2);
    }

    /// <summary>覆盖行携带客户对偶键 / Coverage rows carry the customer dual key.</summary>
    [Fact]
    public void CustomerCoverage_ShouldCarryShadowPriceKey() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);
        Coverage(compilation).Invoke(model);

        model.Constraints.Where(constraint =>
                constraint.Name.StartsWith("customer_coverage_", StringComparison.Ordinal))
            .Select(constraint => constraint.Args)
            .Should().OnlyContain(args => args is CustomerCoverageShadowPriceKey);
    }

    /// <summary>**未注册的编译模型被拒** / An unregistered compilation model is rejected.</summary>
    [Fact]
    public void CustomerCoverage_ShouldRejectUnregisteredCompilation() =>
        Coverage(Compilation()).Invoke(Model()).IsOk.Should().BeFalse();

    /// <summary>
    /// **覆盖对偶是本次定价所访问客户之和，未命中按 0 计**。
    /// **The coverage dual is the sum over the visited customers, with a miss counting as zero.**
    /// </summary>
    [Fact]
    public void CustomerCoverage_Extractor_ShouldSumVisitedCustomers() {
        var map = new VrpShadowPriceMap();
        map.Put(new ShadowPrice(new CustomerCoverageShadowPriceKey(new CustomerId("c1")), new Flt64(3.0)));
        map.Put(new ShadowPrice(new CustomerCoverageShadowPriceKey(new CustomerId("c2")), new Flt64(4.0)));

        var args = new VrpShadowPriceArguments(
            new VehicleTypeId("truck"), new HashSet<CustomerId> { new("c1"), new("c2") });

        Coverage(Compilation()).Extractor()!(map, args).ToDouble().Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>未访问的客户不计入 / Customers that were not visited contribute nothing.</summary>
    [Fact]
    public void CustomerCoverage_Extractor_ShouldIgnoreUnvisitedCustomers() {
        var map = new VrpShadowPriceMap();
        map.Put(new ShadowPrice(new CustomerCoverageShadowPriceKey(new CustomerId("c2")), new Flt64(4.0)));

        var args = new VrpShadowPriceArguments(
            new VehicleTypeId("truck"), new HashSet<CustomerId> { new("c1") });

        Coverage(Compilation()).Extractor()!(map, args).ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**每个车型一条车队上界行** / One fleet upper-bound row per vehicle type.</summary>
    [Fact]
    public void FleetSize_ShouldRegisterOneRowPerVehicleType() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);

        Fleet(compilation).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count(constraint => constraint.Name.StartsWith("fleet_size_", StringComparison.Ordinal))
            .Should().Be(1);
    }

    /// <summary>车队行归属自身分组 / Fleet rows belong to the pipeline's group.</summary>
    [Fact]
    public void FleetSize_ShouldGroupItsRow() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);
        FleetSizeConstraint<Flt64> pipeline = Fleet(compilation);
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(1);
    }

    /// <summary>车队行携带车型对偶键 / The fleet row carries the fleet dual key.</summary>
    [Fact]
    public void FleetSize_ShouldCarryShadowPriceKey() {
        LinearMetaModel<Flt64> model = Model();
        RouteCompilation<Flt64> compilation = Compilation();
        compilation.Register(model);
        Fleet(compilation).Invoke(model);

        model.Constraints.Single(constraint =>
                constraint.Name.StartsWith("fleet_size_", StringComparison.Ordinal))
            .Args.Should().BeOfType<FleetSizeShadowPriceKey>();
    }

    /// <summary>**未注册的编译模型被拒** / An unregistered compilation model is rejected.</summary>
    [Fact]
    public void FleetSize_ShouldRejectUnregisteredCompilation() =>
        Fleet(Compilation()).Invoke(Model()).IsOk.Should().BeFalse();

    /// <summary>**车队对偶按有符号值直接取用，不做归一化** / The fleet dual is read signed, with no normalisation.</summary>
    [Fact]
    public void FleetSize_Extractor_ShouldReadSignedValue() {
        var map = new VrpShadowPriceMap();
        map.Put(new ShadowPrice(new FleetSizeShadowPriceKey(new VehicleTypeId("truck")), new Flt64(-2.5)));

        var args = new VrpShadowPriceArguments(
            new VehicleTypeId("truck"), new HashSet<CustomerId>());

        Fleet(Compilation()).Extractor()!(map, args).ToDouble().Should().BeApproximately(-2.5, 1e-9);
    }

    /// <summary>未登记的车型按 0 计 / An absent vehicle type counts as zero.</summary>
    [Fact]
    public void FleetSize_Extractor_ShouldReturnZeroWhenAbsent() {
        var map = new VrpShadowPriceMap();

        var args = new VrpShadowPriceArguments(
            new VehicleTypeId("ghost"), new HashSet<CustomerId>());

        Fleet(Compilation()).Extractor()!(map, args).ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>构造客户覆盖管线 / Build the coverage pipeline.</summary>
    private static CustomerCoverageConstraint<Flt64> Coverage(RouteCompilation<Flt64> compilation) =>
        new(Instance().Customers, compilation);

    /// <summary>构造车队数量管线 / Build the fleet-size pipeline.</summary>
    private static FleetSizeConstraint<Flt64> Fleet(RouteCompilation<Flt64> compilation) =>
        new(Instance().VehicleTypes, compilation);

    /// <summary>构造路线编译模型 / Build a route compilation model.</summary>
    private static RouteCompilation<Flt64> Compilation() =>
        new(Instance(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("route_compilation");

    /// <summary>构造面向 Flt64 的实例 / Build the Flt64 instance.</summary>
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

    /// <summary>构造面向 Flt64 的客户 / Build the Flt64 customer.</summary>
    private static Customer<Flt64> CustomerOf(string id, string nodeId) =>
        Customer<Flt64>.Invoke(
            new CustomerId(id), Node(nodeId), Meters(1.0),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value, TimeSpan.FromMinutes(10)).Value;

    /// <summary>构造面向 Flt64 的节点 / Build the Flt64 node.</summary>
    private static NetworkNode<Flt64> Node(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<Flt64>> { ["x"] = Meters(0.0), ["y"] = Meters(0.0) }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
