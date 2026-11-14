// 管线列表生成器测试 / Tests for the pipeline list generator.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 管线列表生成器 / The pipeline list generator.
/// </summary>
public sealed class PipelineListGeneratorTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>**Phase I 固定三段：覆盖、车队、Phase-I 目标** / Phase I is three segments: coverage, fleet, Phase-I objective.</summary>
    [Fact]
    public void PhaseOne_ShouldProduceThreeSegments() {
        RouteCompilation<Flt64> compilation = Compilation();
        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> pipelines =
            PipelineListGenerator.PhaseOne(Instance(), compilation);

        pipelines.Should().HaveCount(3);
        pipelines[0].Should().BeOfType<CustomerCoverageConstraint<Flt64>>();
        pipelines[1].Should().BeOfType<FleetSizeConstraint<Flt64>>();
        pipelines[2].Should().BeOfType<PhaseOneMinimization<Flt64>>();
    }

    /// <summary>**Phase II 固定三段：覆盖、车队、路线成本目标** / Phase II is three segments: coverage, fleet, route-cost objective.</summary>
    [Fact]
    public void PhaseTwo_ShouldProduceThreeSegments() {
        RouteCompilation<Flt64> compilation = Compilation();
        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> pipelines =
            PipelineListGenerator.PhaseTwo(Instance(), compilation);

        pipelines.Should().HaveCount(3);
        pipelines[0].Should().BeOfType<CustomerCoverageConstraint<Flt64>>();
        pipelines[1].Should().BeOfType<FleetSizeConstraint<Flt64>>();
        pipelines[2].Should().BeOfType<RouteCostMinimization<Flt64>>();
    }

    /// <summary>
    /// **两条最小化目标互斥**：同一份列表里绝不会同时出现 Phase-I 与 Phase-II 目标。
    /// **The two minimisation objectives are mutually exclusive**: no list ever contains both.
    /// </summary>
    [Fact]
    public void Phases_ShouldNeverMixMinimizationObjectives() {
        RouteCompilation<Flt64> compilation = Compilation();

        PipelineListGenerator.PhaseOne(Instance(), compilation)
            .Should().NotContain(pipeline => pipeline is RouteCostMinimization<Flt64>);
        PipelineListGenerator.PhaseTwo(Instance(), compilation)
            .Should().NotContain(pipeline => pipeline is PhaseOneMinimization<Flt64>);
    }

    /// <summary>额外管线追加在末尾 / Extra pipelines are appended at the end.</summary>
    [Fact]
    public void PhaseOne_ShouldAppendExtraPipelines() {
        RouteCompilation<Flt64> compilation = Compilation();
        var extra = new CustomerCoverageConstraint<Flt64>(Instance().Customers, compilation, "extra");

        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> pipelines =
            PipelineListGenerator.PhaseOne(Instance(), compilation, new[] { extra });

        pipelines.Should().HaveCount(4);
        pipelines[3].Should().BeSameAs(extra);
    }

    /// <summary>额外管线为 null 时安全 / A null extra list is safe.</summary>
    [Fact]
    public void PhaseTwo_ShouldAcceptNullExtras() =>
        PipelineListGenerator.PhaseTwo(Instance(), Compilation(), null).Should().HaveCount(3);

    /// <summary>可注入自定义覆盖约束 / A custom coverage constraint can be injected.</summary>
    [Fact]
    public void PhaseOne_ShouldHonourInjectedConstraints() {
        RouteCompilation<Flt64> compilation = Compilation();
        var coverage = new CustomerCoverageConstraint<Flt64>(Instance().Customers, compilation, "custom_coverage");
        var fleet = new FleetSizeConstraint<Flt64>(Instance().VehicleTypes, compilation, "custom_fleet");

        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> pipelines =
            PipelineListGenerator.PhaseOne(
                Instance(), compilation, null, coverage, fleet);

        pipelines[0].Should().BeSameAs(coverage);
        pipelines[1].Should().BeSameAs(fleet);
    }

    /// <summary>**影子价格键转换命中时返回该键** / A matching shadow-price key casts to itself.</summary>
    [Fact]
    public void ShadowPriceKeyOf_ShouldReturnMatchingKey() {
        var key = new FleetSizeShadowPriceKey(new VehicleTypeId("truck"));

        ShadowPriceKeyCastSupport.ShadowPriceKeyOf<FleetSizeShadowPriceKey>(key).Should().BeSameAs(key);
    }

    /// <summary>**类型不符时返回 null，不抛异常** / A mismatched type yields null instead of throwing.</summary>
    [Fact]
    public void ShadowPriceKeyOf_ShouldReturnNullOnMismatch() {
        var key = new CustomerCoverageShadowPriceKey(new CustomerId("c1"));

        ShadowPriceKeyCastSupport.ShadowPriceKeyOf<FleetSizeShadowPriceKey>(key).Should().BeNull();
        ShadowPriceKeyCastSupport.ShadowPriceKeyOf<FleetSizeShadowPriceKey>(null).Should().BeNull();
        ShadowPriceKeyCastSupport.ShadowPriceKeyOf<FleetSizeShadowPriceKey>("not a key").Should().BeNull();
    }

    /// <summary>构造路线编译模型 / Build a route compilation model.</summary>
    private static RouteCompilation<Flt64> Compilation() =>
        new(Instance(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

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

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<Flt64> Node(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<Flt64>> { ["x"] = Meters(0.0), ["y"] = Meters(0.0) }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
