// 网络流聚合测试 / Tests for the flow aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络流聚合 / The flow aggregation.
/// </summary>
public sealed class FlowAggregationTest {
    /// <summary>主路径：一个商品、一条弧可注册 / The main path registers: one commodity, one arc.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Aggregation().Register(new LinearMetaModel<Flt64>("flow")).IsOk.Should().BeTrue();

    /// <summary>注册后流量表达式可查 / The flow expression is queryable after registration.</summary>
    [Fact]
    public void Register_ShouldExposeFlowExpression() {
        FlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("flow"));

        aggregation.Flow(new FlowKey("c1", new NetworkArcId("a1"))).IsOk.Should().BeTrue();
    }

    /// <summary>注册后资源表达式可查 / The resource expression is queryable after registration.</summary>
    [Fact]
    public void Register_ShouldExposeLoadExpression() {
        FlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("flow"));

        aggregation.Load("arc:a1").IsOk.Should().BeTrue();
    }

    /// <summary>未注册的键查询失败 / Querying an unregistered key fails.</summary>
    [Fact]
    public void Expressions_ShouldRejectUnknownKey() {
        FlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("flow"));

        aggregation.Flow(new FlowKey("ghost", new NetworkArcId("a1"))).IsOk.Should().BeFalse();
        aggregation.Load("ghost").IsOk.Should().BeFalse();
    }

    /// <summary>注册后费用表达式非空 / The cost expression is non-empty after registration.</summary>
    [Fact]
    public void Register_ShouldBuildCost() {
        FlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("flow"));

        aggregation.Cost.Monomials.Should().NotBeEmpty();
    }

    /// <summary>不注册目标时费用表达式仍构建 / The cost expression is still built without an objective.</summary>
    [Fact]
    public void Register_ShouldAllowSkippingObjective() {
        FlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(new LinearMetaModel<Flt64>("flow"), minimizeCost: false).IsOk.Should().BeTrue();
        aggregation.Cost.Monomials.Should().NotBeEmpty();
    }

    /// <summary>
    /// **同一模型用相同选项重复注册是幂等的**。
    /// **Re-registering the same model with the same options is idempotent.**
    /// </summary>
    [Fact]
    public void Register_ShouldBeIdempotentForSameOptions() {
        var model = new LinearMetaModel<Flt64>("flow");
        FlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **同一模型用不同选项重复注册必须失败**：两层约束会叠成较紧者，可行域被悄悄收窄。
    /// **Re-registering the same model with different options must fail**: two layers of constraints stack as the tighter one and
    /// silently shrink the feasible region.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectInconsistentOptions() {
        var model = new LinearMetaModel<Flt64>("flow");
        FlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model, minimizeCost: false).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **不可跨模型复用**：注册态绑定在构建它的那个模型上。
    /// **No reuse across models**: registration state is bound to the model that built it.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        FlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(new LinearMetaModel<Flt64>("a")).IsOk.Should().BeTrue();
        aggregation.Register(new LinearMetaModel<Flt64>("b")).IsOk.Should().BeFalse();
    }

    /// <summary>未注册模型提取解应失败 / Extracting from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Aggregation().ExtractSolution(new LinearMetaModel<Flt64>("flow")).IsOk.Should().BeFalse();

    /// <summary>注册后未求解时提取解失败 / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        var model = new LinearMetaModel<Flt64>("flow");
        FlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(model);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **弧与图不一致时拒绝注册**：拓扑与声明不符时，守恒约束会按错误的邻接关系建立。
    /// **A mismatch between arcs and graph rejects registration**: with topology disagreeing with the declaration, conservation rows
    /// get built on the wrong adjacency.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectArcGraphMismatch() {
        FlowProblem<FltX> problem = Problem(graphArcId: "other");
        var aggregation = new FlowAggregation<FltX>(
            problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "flow");

        aggregation.Register(new LinearMetaModel<Flt64>("flow")).IsOk.Should().BeFalse();
    }

    /// <summary>空商品集可注册 / An empty commodity set registers.</summary>
    [Fact]
    public void Register_ShouldAcceptNoCommodities() {
        FlowProblem<FltX> problem = Problem(commodities: 0);
        var aggregation = new FlowAggregation<FltX>(
            problem, FltXNetworkSchedulingSolverValueAdapter.Instance, "flow");

        aggregation.Register(new LinearMetaModel<Flt64>("flow")).IsOk.Should().BeTrue();
    }

    /// <summary>构造一个可注册的聚合 / Build a registrable aggregation.</summary>
    private static FlowAggregation<FltX> Aggregation() =>
        new(Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance, "flow");

    /// <summary>构造一个流问题 / Build a flow problem.</summary>
    private static FlowProblem<FltX> Problem(int commodities = 1, string graphArcId = "a1") {
        NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"), Meters(1.0), null, new NetworkArcId("a1")).Value;
        Domain.Flow.Model.FlowArc<FltX> flowArc =
            Domain.Flow.Model.FlowArc<FltX>.Invoke(networkArc, Bounds(0.0, 100.0)).Value;

        NetworkArc<FltX> graphArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"), Meters(1.0), null, new NetworkArcId(graphArcId)).Value;

        var nodes = new List<NetworkNode<FltX>> {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, new[] { graphArc },
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: arc => arc.Id,
                    From: arc => arc.From,
                    To: arc => arc.To)).Value;

        var options = new List<CommodityFlow<FltX>>();
        for (int index = 0; index < commodities; index++) {
            options.Add(new CommodityFlow<FltX>(Commodity($"c{index + 1}"), SIBaseUnits.Meter));
        }

        return new FlowProblem<FltX>(
            graph, new[] { flowArc }, options, new FltX(0.0), costUnit: SIBaseUnits.Meter);
    }

    /// <summary>构造一个商品 / Build a commodity.</summary>
    private static Domain.Flow.Model.FlowCommodity<FltX> Commodity(string id) =>
        Domain.Flow.Model.FlowCommodity<FltX>.Invoke(id).Value;

    /// <summary>构造容量上下界 / Build capacity bounds.</summary>
    private static CapacityBounds<FltX> Bounds(double lower, double upper) =>
        CapacityBounds<FltX>.Invoke(Meters(lower), Meters(upper)).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
