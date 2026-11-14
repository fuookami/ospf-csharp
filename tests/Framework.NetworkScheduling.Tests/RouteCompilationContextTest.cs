// 路线编译上下文测试 / Tests for the route compilation context.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路线编译上下文 / The route compilation context.
/// </summary>
public sealed class RouteCompilationContextTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>初始阶段是 Phase I / The initial phase is Phase I.</summary>
    [Fact]
    public void Context_ShouldStartInPhaseOne() =>
        Context().Phase.Should().Be(PricingPhase.PhaseOne);

    /// <summary>**Phase I 管线列表是覆盖 → 车队 → Phase-I 目标** / The Phase-I list is coverage → fleet → Phase-I objective.</summary>
    [Fact]
    public void Context_ShouldStartWithPhaseOnePipelines() {
        RouteCompilationContext<Flt64> context = Context();

        context.PipelineList.Should().HaveCount(3);
        context.PipelineList[0].Should().BeOfType<CustomerCoverageConstraint<Flt64>>();
        context.PipelineList[1].Should().BeOfType<FleetSizeConstraint<Flt64>>();
        context.PipelineList[2].Should().BeOfType<PhaseOneMinimization<Flt64>>();
    }

    /// <summary>初始没有扩展贡献结果 / No contribution results initially.</summary>
    [Fact]
    public void Context_ShouldStartWithEmptyExtractedContributions() =>
        Context().ExtractedContributions.Should().BeEmpty();

    /// <summary>**注册同时落下聚合符号与全部管线约束** / Registration lands both the aggregation symbols and every pipeline row.</summary>
    [Fact]
    public void Context_RegisterShouldRegisterAggregationAndPipelines() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();

        context.Compilation.CustomerCoverage.Should().NotBeNull();
        model.Constraints.Should().Contain(constraint =>
            constraint.Name.StartsWith("customer_coverage_", StringComparison.Ordinal));
        model.Constraints.Should().Contain(constraint =>
            constraint.Name.StartsWith("fleet_size_", StringComparison.Ordinal));
    }

    /// <summary>重复注册保持幂等 / Repeated registration stays idempotent.</summary>
    [Fact]
    public void Context_RegisterShouldBeIdempotent() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>额外管线参与注册 / Extra pipelines take part in registration.</summary>
    [Fact]
    public void Context_ShouldRegisterExtraPipelines() {
        var extra = new NoopPipeline("extra");
        RouteCompilationContext<Flt64> withExtra = new(
            Instance(),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            new[] { extra });

        withExtra.PipelineList.Should().HaveCount(4);
        withExtra.PipelineList[3].Should().BeSameAs(extra);
        Context().PipelineList.Should().HaveCount(3);
    }

    /// <summary>空列集是安全空操作 / An empty column batch is a safe no-op.</summary>
    [Fact]
    public void Context_AddColumnsShouldAcceptEmptyBatch() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.AddColumns(0, Array.Empty<Route<Flt64>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册时加列被拒** / Adding columns before registration is rejected.</summary>
    [Fact]
    public void Context_AddColumnsShouldRejectBeforeRegistration() =>
        Context().AddColumns(0, new[] { RouteOf("c1") }, Model()).IsOk.Should().BeFalse();

    /// <summary>加列成功后列池与变量都增长 / After adding, both the pool and the variables grow.</summary>
    [Fact]
    public void Context_AddColumnsShouldGrowPoolAndVariables() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.AddColumns(0, new[] { RouteOf("c1") }, model).IsOk.Should().BeTrue();

        context.Compilation.ColumnPool.Size.Should().Be(1);
        context.Compilation.X.Should().HaveCount(1);
    }

    /// <summary>空移除集是安全空操作 / An empty removal set is a safe no-op.</summary>
    [Fact]
    public void Context_RemoveColumnsShouldAcceptEmptyBatch() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.RemoveColumns(Array.Empty<Route<Flt64>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>**未知路线在移除时被忽略，不报错** / Unknown routes are ignored during removal.</summary>
    [Fact]
    public void Context_RemoveColumnsShouldIgnoreUnknownRoutes() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);
        context.AddColumns(0, new[] { RouteOf("c1") }, model);

        context.RemoveColumns(new[] { RouteOf("c2") }, model).IsOk.Should().BeTrue();
        context.Compilation.ColumnPool.Size.Should().Be(1, "未知路线不产生移除 / an unknown route removes nothing");
    }

    /// <summary>**切换到 Phase II 后阶段与管线列表一起改变** / Switching to Phase II changes both phase and list.</summary>
    [Fact]
    public void Context_SwitchToPhaseTwoShouldSwapPipelines() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();

        context.Phase.Should().Be(PricingPhase.PhaseTwo);
        context.PipelineList.Should().HaveCount(3);
        context.PipelineList[2].Should().BeOfType<RouteCostMinimization<Flt64>>();
    }

    /// <summary>**阶段切换是单向的**：再次切换是空操作，不会退回 Phase I / The switch is one-way: repeating it is a no-op.</summary>
    [Fact]
    public void Context_SwitchToPhaseTwoShouldBeOneWay() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();
        context.SwitchToPhaseTwo(model).IsOk.Should().BeTrue();

        context.Phase.Should().Be(PricingPhase.PhaseTwo);
        context.PipelineList[2].Should().BeOfType<RouteCostMinimization<Flt64>>();
    }

    /// <summary>**未注册时切换 Phase II 被拒** / Switching to Phase II before registration is rejected.</summary>
    [Fact]
    public void Context_SwitchToPhaseTwoShouldRejectBeforeRegistration() =>
        Context().SwitchToPhaseTwo(Model()).IsOk.Should().BeFalse();

    /// <summary>**Phase II 加列后仍处于 Phase II** / Adding columns in Phase II keeps the phase.</summary>
    [Fact]
    public void Context_AddColumnsInPhaseTwoShouldKeepPhase() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);
        context.SwitchToPhaseTwo(model);

        context.AddColumns(1, new[] { RouteOf("c1") }, model).IsOk.Should().BeTrue();
        context.Phase.Should().Be(PricingPhase.PhaseTwo);
    }

    /// <summary>未求解时收敛检查失败 / The convergence check fails while unsolved.</summary>
    [Fact]
    public void Context_IsPhaseOneConvergedShouldFailWhenUnsolved() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.IsPhaseOneConverged(model).IsOk.Should().BeFalse();
    }

    /// <summary>空列池提取路线值为空 / Extraction over an empty pool yields nothing.</summary>
    [Fact]
    public void Context_ExtractRouteValuesShouldBeEmptyWithoutColumns() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.ExtractRouteValues(model).Value.Should().BeEmpty();
    }

    /// <summary>未求解时解式提取为空 / Extraction over an unsolved model is empty.</summary>
    [Fact]
    public void Context_ExtractSolutionShouldBeEmptyWhenUnsolved() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);
        context.AddColumns(0, new[] { RouteOf("c1") }, model);

        context.ExtractSolution(model).Value.Should().BeEmpty();
    }

    /// <summary>**提取影子价格在无扩展时成功且映射为空** / Shadow-price extraction succeeds and yields an empty map with no extensions.</summary>
    [Fact]
    public void Context_ExtractShadowPriceShouldSucceedWithoutExtensions() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        var map = new VrpShadowPriceMap();
        context.ExtractShadowPrice(map, model, MetaDualSolution.Empty).IsOk.Should().BeTrue();
        map.Map.Should().BeEmpty();
    }

    /// <summary>**定价对偶快照携带当前阶段** / The pricing-dual snapshot carries the current phase.</summary>
    [Fact]
    public void Context_ExtractPricingDualsShouldCarryPhase() {
        RouteCompilationContext<Flt64> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        PricingDuals duals = context.ExtractPricingDuals(model, MetaDualSolution.Empty).Value;
        duals.Phase.Should().Be(PricingPhase.PhaseOne);

        context.SwitchToPhaseTwo(model);
        context.ExtractPricingDuals(model, MetaDualSolution.Empty).Value.Phase.Should().Be(PricingPhase.PhaseTwo);
    }

    /// <summary>**重复的扩展名被拒绝**：两个扩展同名会让结果互相覆盖 / A duplicate contribution name is rejected.</summary>
    [Fact]
    public void Context_ExtractContributionValuesShouldRejectDuplicateNames() {
        RouteCompilationContext<Flt64> context = new(
            Instance(),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            null,
            new IRouteCompilationContribution<Flt64>[] { new NamedContribution("same"), new NamedContribution("same") });

        context.ExtractContributionValues(Model(), Array.Empty<(Route<Flt64>, Flt64)>()).IsOk.Should().BeFalse();
    }

    /// <summary>**空白扩展名被拒绝** / A blank contribution name is rejected.</summary>
    [Fact]
    public void Context_ExtractContributionValuesShouldRejectBlankNames() {
        RouteCompilationContext<Flt64> context = new(
            Instance(),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            null,
            new IRouteCompilationContribution<Flt64>[] { new NamedContribution("   ") });

        context.ExtractContributionValues(Model(), Array.Empty<(Route<Flt64>, Flt64)>()).IsOk.Should().BeFalse();
    }

    /// <summary>**异构结果按扩展名收集** / Heterogeneous results are collected by contribution name.</summary>
    [Fact]
    public void Context_ExtractContributionValuesShouldCollectByName() {
        RouteCompilationContext<Flt64> context = new(
            Instance(),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            null,
            new IRouteCompilationContribution<Flt64>[] { new NamedContribution("alpha"), new NamedContribution("beta") });

        IReadOnlyDictionary<string, object?> values =
            context.ExtractContributionValues(Model(), Array.Empty<(Route<Flt64>, Flt64)>()).Value;

        values.Keys.Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }

    /// <summary>构造上下文 / Build the context.</summary>
    private static RouteCompilationContext<Flt64> Context() =>
        new(Instance(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("route_compilation");

    /// <summary>不做任何事的管线桩 / A pipeline stub that does nothing.</summary>
    private sealed class NoopPipeline : ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> {
        private readonly string _name;

        internal NoopPipeline(string name) => _name = name;

        /// <inheritdoc/>
        public string Name => _name;

        /// <inheritdoc/>
        public Try Invoke(LinearMetaModel<Flt64> model) => Results.Ok(Results.SuccessInstance);
    }

    /// <summary>只带名称的最小贡献 / A minimal contribution carrying only a name.</summary>
    private sealed class NamedContribution : IRouteCompilationContribution<Flt64> {
        private readonly string _name;

        internal NamedContribution(string name) => _name = name;

        /// <inheritdoc/>
        public string Name => _name;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, Flt64> Coefficients(ResourceArc<VehicleTypeId> arc) =>
            new Dictionary<string, Flt64>();
    }

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
    private static Route<Flt64> RouteOf(string customerId) =>
        Route<Flt64>.Invoke(
            new VehicleTypeId("truck"),
            new[] {
                new RouteStop<Flt64>(new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0.0)),
                new RouteStop<Flt64>(
                    new NetworkNodeId($"n{customerId[1..]}"), new CustomerId(customerId),
                    Origin, Origin, Origin, Meters(1.0)),
                new RouteStop<Flt64>(new NetworkNodeId("depot-end"), null, Origin, Origin, Origin, Meters(1.0)),
            },
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId("a1"), new NetworkArcId("a2") }).Value;

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<Flt64> Node(string id) =>
        NetworkNode<Flt64>.Invoke(
            new NetworkNodeId(id),
            new Dictionary<string, Quantity<Flt64>> { ["x"] = Meters(0.0), ["y"] = Meters(0.0) }).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
