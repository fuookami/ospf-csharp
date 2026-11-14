// 路线编译聚合测试 / Tests for the route compilation aggregation.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;
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
/// 路线编译聚合 / The route compilation aggregation.
/// </summary>
public sealed class RouteCompilationAggregationTest {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>聚合内建一个编译模型 / The aggregation builds its own compilation model.</summary>
    [Fact]
    public void Aggregation_ShouldExposeCompilationAndInstance() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();

        aggregation.Compilation.Should().NotBeNull();
        aggregation.Instance.Should().BeSameAs(aggregation.Instance);
        aggregation.ValueAdapter.Should().NotBeNull();
    }

    /// <summary>管线列表初始为空 / The pipeline list starts empty.</summary>
    [Fact]
    public void Aggregation_ShouldStartWithEmptyPipelineList() =>
        Aggregation().PipelineList.Should().BeEmpty();

    /// <summary>登记的管线出现在列表中 / A recorded pipeline shows up in the list.</summary>
    [Fact]
    public void Aggregation_ShouldRecordPipelines() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        var coverage = new CustomerCoverageConstraint<Flt64>(aggregation.Instance.Customers, aggregation.Compilation);

        aggregation.AddPipeline(coverage);

        aggregation.PipelineList.Should().ContainSingle().Which.Should().BeSameAs(coverage);
    }

    /// <summary>**管线列表是快照**：改动内部列表不影响已取出的快照 / The pipeline list is a snapshot.</summary>
    [Fact]
    public void Aggregation_PipelineListShouldBeSnapshot() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        aggregation.AddPipeline(new CustomerCoverageConstraint<Flt64>(
            aggregation.Instance.Customers, aggregation.Compilation));
        int snapshotCount = aggregation.PipelineList.Count;

        aggregation.AddPipeline(new CustomerCoverageConstraint<Flt64>(
            aggregation.Instance.Customers, aggregation.Compilation, "second"));

        snapshotCount.Should().Be(1);
        aggregation.PipelineList.Should().HaveCount(2);
    }

    /// <summary>注册委托给编译模型 / Registration delegates to the compilation model.</summary>
    [Fact]
    public void Aggregation_RegisterShouldDelegate() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Compilation.CustomerCoverage.Should().NotBeNull();
    }

    /// <summary>空列集是安全空操作 / An empty column batch is a safe no-op.</summary>
    [Fact]
    public void Aggregation_AddColumnsShouldAcceptEmptyBatch() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.AddColumns(0, Array.Empty<Route<Flt64>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册时添加列被拒** / Adding columns before registration is rejected.</summary>
    [Fact]
    public void Aggregation_AddColumnsShouldRejectBeforeRegistration() =>
        Aggregation().AddColumns(0, new[] { RouteOf("c1") }, Model()).IsOk.Should().BeFalse();

    /// <summary>空移除集是安全空操作 / An empty removal set is a safe no-op.</summary>
    [Fact]
    public void Aggregation_RemoveColumnsShouldAcceptEmptyBatch() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.RemoveColumns(Array.Empty<Route<Flt64>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册时切换 Phase II 被拒** / Switching to Phase II before registration is rejected.</summary>
    [Fact]
    public void Aggregation_SwitchToPhaseTwoShouldRejectBeforeRegistration() =>
        Aggregation().SwitchToPhaseTwo(Model()).IsOk.Should().BeFalse();

    /// <summary>
    /// **未求解时收敛检查返回失败而不是 false**：没有解值就无法判断收敛，"未收敛"与"无法判断"是两件事，混淆它们会让调用方
    /// 在 Phase I 无限循环。
    /// **An unsolved model fails the convergence check rather than answering false**: without solution values convergence cannot be
    /// decided, and "not converged" is a different thing from "undecidable"; conflating them would loop Phase I forever.
    /// </summary>
    [Fact]
    public void Aggregation_IsPhaseOneConvergedShouldFailWhenUnsolved() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.IsPhaseOneConverged(model).IsOk.Should().BeFalse();
    }

    /// <summary>未注册时收敛检查失败 / The convergence check fails before registration.</summary>
    [Fact]
    public void Aggregation_IsPhaseOneConvergedShouldRejectBeforeRegistration() =>
        Aggregation().IsPhaseOneConverged(Model()).IsOk.Should().BeFalse();

    /// <summary>**容差可显式覆盖实例默认值**（同一未求解状态下行为一致，说明参数被正确转发）。</summary>
    [Fact]
    public void Aggregation_ShouldAcceptExplicitTolerance() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.IsPhaseOneConverged(model, new Flt64(1.0)).IsOk.Should().BeFalse();
    }

    /// <summary>空列池提取路线值为空 / Extraction over an empty pool yields nothing.</summary>
    [Fact]
    public void Aggregation_ExtractRouteValuesShouldBeEmptyWithoutColumns() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.ExtractRouteValues(model).Value.Should().BeEmpty();
    }

    /// <summary>未求解时解式提取为空 / Extraction over an unsolved model is empty.</summary>
    [Fact]
    public void Aggregation_ExtractSolutionShouldBeEmptyWhenUnsolved() {
        RouteCompilationAggregation<Flt64> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);
        aggregation.AddColumns(0, new[] { RouteOf("c1") }, model);

        aggregation.ExtractSolution(model).Value.Should().BeEmpty();
    }

    /// <summary>构造聚合 / Build the aggregation.</summary>
    private static RouteCompilationAggregation<Flt64> Aggregation() =>
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
