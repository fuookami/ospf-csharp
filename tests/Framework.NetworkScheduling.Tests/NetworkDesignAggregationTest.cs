// 网络建设聚合与上下文测试 / Tests for the design aggregation and context.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络建设聚合与上下文 / The design aggregation and context.
/// </summary>
public sealed class NetworkDesignAggregationTest {
    /// <summary>主路径：无设施无通道可注册 / The main path registers: no facilities, no services.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Aggregation().Register(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeTrue();

    /// <summary>建设费用表达式暴露出来 / The construction-cost expression is exposed.</summary>
    [Fact]
    public void Register_ShouldExposeConstructionCost() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation(facilities: true);
        aggregation.Register(new LinearMetaModel<Flt64>("design"));

        aggregation.ConstructionCost.Monomials.Should().NotBeEmpty();
    }

    /// <summary>启用表达式委托到建设组件 / Activation expressions delegate to the construction component.</summary>
    [Fact]
    public void Availability_ShouldDelegate() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation(facilities: true);
        aggregation.Register(new LinearMetaModel<Flt64>("design"));

        aggregation.Availability(new NetworkArcId("a1")).Constant.ToDouble().Should().BeApproximately(1.0, 1e-9);
        aggregation.FacilityAvailability(new NetworkNodeId("n1")).Monomials.Should().HaveCount(1);
    }

    /// <summary>
    /// **同选项重复注册幂等** / Re-registering the same model with the same options is idempotent.
    /// </summary>
    [Fact]
    public void Register_ShouldBeIdempotentForSameOptions() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkDesignAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**不同目标选项重复注册被拒** / Re-registering with different objective options is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectInconsistentOptions() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkDesignAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model, minimizeCost: false).IsOk.Should().BeFalse();
    }

    /// <summary>**不可跨模型复用** / No reuse across models.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(new LinearMetaModel<Flt64>("a")).IsOk.Should().BeTrue();
        aggregation.Register(new LinearMetaModel<Flt64>("b")).IsOk.Should().BeFalse();
    }

    /// <summary>注册后名义流已挂上 / The nominal flow is attached once registered.</summary>
    [Fact]
    public void Register_ShouldAttachNominalFlow() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("design"));

        aggregation.Flow.Problem.Should().BeSameAs(aggregation.Flow.Problem);
        aggregation.Flow.Flow(new FlowKey("c1", new NetworkArcId("a1"))).IsOk.Should().BeTrue();
    }

    /// <summary>**未注册时不能挂场景流** / A scenario flow cannot attach before registration.</summary>
    [Fact]
    public void AttachFlow_ShouldRejectBeforeRegistration() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation();
        var model = new LinearMetaModel<Flt64>("design");
        var scenario = new FlowContext<FltX>(Problem().Flow, FltXNetworkSchedulingSolverValueAdapter.Instance, "scenario");

        aggregation.AttachFlow(model, scenario).IsOk.Should().BeFalse();
    }

    /// <summary>**场景不能用别的模型** / A scenario cannot use a different model.</summary>
    [Fact]
    public void AttachFlow_ShouldRejectForeignModel() {
        NetworkDesignAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("design"));
        var scenario = new FlowContext<FltX>(Problem().Flow, FltXNetworkSchedulingSolverValueAdapter.Instance, "scenario");

        aggregation.AttachFlow(new LinearMetaModel<Flt64>("other"), scenario).IsOk.Should().BeFalse();
    }

    /// <summary>同一场景重复挂载幂等 / Re-attaching the same scenario is idempotent.</summary>
    [Fact]
    public void AttachFlow_ShouldBeIdempotent() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkDesignAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(model);
        var scenario = new FlowContext<FltX>(Problem().Flow, FltXNetworkSchedulingSolverValueAdapter.Instance, "scenario");

        aggregation.AttachFlow(model, scenario).IsOk.Should().BeTrue();
        aggregation.AttachFlow(model, scenario).IsOk.Should().BeTrue();
    }

    /// <summary>未注册模型提取解失败 / Extracting from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Aggregation().ExtractSolution(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();

    /// <summary>注册后未求解时提取解失败 / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkDesignAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(model);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>未注册模型提取建设解失败 / Extracting construction from an unregistered model fails.</summary>
    [Fact]
    public void ConstructionExtract_ShouldRejectUnregisteredModel() =>
        new NetworkConstruction<FltX>(
                Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance, "design")
            .ExtractSolution(new LinearMetaModel<Flt64>("design")).IsOk.Should().BeFalse();

    /// <summary>注册后未求解时提取建设解失败 / Construction extraction fails before solving.</summary>
    [Fact]
    public void ConstructionExtract_ShouldRejectUnsolvedModel() {
        var model = new LinearMetaModel<Flt64>("design");
        NetworkConstruction<FltX> construction = Construction();
        construction.Register(model);

        construction.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>上下文委托到聚合 / The context delegates to the aggregation.</summary>
    [Fact]
    public void Context_ShouldDelegate() {
        var context = new NetworkDesignContext<FltX>(Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance);
        var model = new LinearMetaModel<Flt64>("design");

        context.Register(model).IsOk.Should().BeTrue();
        context.Flow.Flow(new FlowKey("c1", new NetworkArcId("a1"))).IsOk.Should().BeTrue();
        context.AttachFlow(model, context.Flow).IsOk.Should().BeTrue();
    }

    /// <summary>构造一个建设聚合 / Build a design aggregation.</summary>
    private static NetworkDesignAggregation<FltX> Aggregation(bool facilities = false) =>
        new(
            Problem(facilities
                ? new[] { new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(5.0)) }
                : null),
            FltXNetworkSchedulingSolverValueAdapter.Instance,
            "design");

    /// <summary>构造一个建设组件 / Build a construction component.</summary>
    private static NetworkConstruction<FltX> Construction() =>
        new(
            Problem(new[] { new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(5.0)) }),
            FltXNetworkSchedulingSolverValueAdapter.Instance,
            "design");

    /// <summary>构造一个建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> Problem(IReadOnlyList<NetworkFacility<FltX>>? facilities = null) =>
        new(FlowProblem(), Array.Empty<NetworkService<FltX>>(), facilities);

    /// <summary>构造一个带单弧单商品的流问题 / Build a flow problem with one arc and one commodity.</summary>
    private static FlowProblem<FltX> FlowProblem() {
        NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            Cost(2.0), null, new NetworkArcId("a1")).Value;
        FlowArc<FltX> arc = FlowArc<FltX>.Invoke(
            networkArc, CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(10.0)).Value).Value;

        NetworkNode<FltX>[] nodes = {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, new[] { networkArc },
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: candidate => candidate.Id,
                    From: candidate => candidate.From,
                    To: candidate => candidate.To)).Value;

        var commodity = new CommodityFlow<FltX>(
            Domain.Flow.Model.FlowCommodity<FltX>.Invoke("c1").Value, SIBaseUnits.Meter);

        return new FlowProblem<FltX>(
            graph, new[] { arc }, new[] { commodity }, new FltX(0.0),
            costUnit: NoneUnit.Instance);
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>无量纲费用（与流的费用单位一致）/ A dimensionless cost matching the flow's cost unit.</summary>
    private static Quantity<FltX> Cost(double value) => new(new FltX(value), NoneUnit.Instance);
}
