// 广义流聚合测试 / Tests for the generalized-flow aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 广义流聚合 / The generalized-flow aggregation.
/// </summary>
public sealed class GeneralizedFlowAggregationTest {
    /// <summary>主路径：一条同量纲弧可注册 / The main path registers: one same-dimension arc.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Aggregation().Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeTrue();

    /// <summary>注册后弧投入表达式可查 / The arc input expression is queryable after registration.</summary>
    [Fact]
    public void Register_ShouldExposeInput() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("gf"));

        aggregation.Input(new NetworkArcId("a1")).IsOk.Should().BeTrue();
    }

    /// <summary>注册后弧产出表达式可查 / The arc output expression is queryable after registration.</summary>
    [Fact]
    public void Register_ShouldExposeOutput() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("gf"));

        aggregation.Output(new NetworkArcId("a1")).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **产出表达式是投入按产出系数缩放的结果，不是独立变量**：产率为 0.5 时产出系数是 0.5。
    /// **The output expression is the input scaled by the output coefficient, not an independent variable**: with yield 0.5 the
    /// coefficient is 0.5.
    /// </summary>
    [Fact]
    public void Register_ShouldScaleOutputByYield() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation(yield: 0.5);
        aggregation.Register(new LinearMetaModel<Flt64>("gf"));

        LinearPolynomial<Flt64> output = aggregation.Output(new NetworkArcId("a1")).Value;
        LinearPolynomial<Flt64> input = aggregation.Input(new NetworkArcId("a1")).Value;

        output.Monomials.Should().HaveCount(1);
        output.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(0.5, 1e-9);
        output.Monomials[0].Symbol.Should().BeSameAs(input.Monomials[0].Symbol);
    }

    /// <summary>未注册时访问表达式失败 / Accessing expressions before registration fails.</summary>
    [Fact]
    public void Expressions_ShouldRejectBeforeRegistration() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Input(new NetworkArcId("a1")).IsOk.Should().BeFalse();
        aggregation.Output(new NetworkArcId("a1")).IsOk.Should().BeFalse();
    }

    /// <summary>注册后费用表达式非空 / The cost expression is non-empty after registration.</summary>
    [Fact]
    public void Register_ShouldBuildCost() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(new LinearMetaModel<Flt64>("gf"));

        aggregation.Cost.Monomials.Should().NotBeEmpty();
    }

    /// <summary>
    /// **同选项重复注册幂等** / Re-registering the same model with the same options is idempotent.
    /// </summary>
    [Fact]
    public void Register_ShouldBeIdempotentForSameOptions() {
        var model = new LinearMetaModel<Flt64>("gf");
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**不同目标选项重复注册被拒** / Re-registering with different objective options is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectInconsistentOptions() {
        var model = new LinearMetaModel<Flt64>("gf");
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model, minimizeCost: false).IsOk.Should().BeFalse();
    }

    /// <summary>**不可跨模型复用** / No reuse across models.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(new LinearMetaModel<Flt64>("a")).IsOk.Should().BeTrue();
        aggregation.Register(new LinearMetaModel<Flt64>("b")).IsOk.Should().BeFalse();
    }

    /// <summary>空白模型名被拒 / A blank model name is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectBlankName() =>
        new GeneralizedFlowAggregation<FltX>(
                Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance, " ")
            .Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();

    /// <summary>**负容差被拒** / A negative tolerance is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectNegativeTolerance() =>
        new GeneralizedFlowAggregation<FltX>(
                Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance, "gf",
                solverTolerance: new GeneralizedFlowSolverTolerance(new Flt64(-1.0), new Flt64(1e-7)))
            .Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();

    /// <summary>
    /// **跨量纲弧缺少转换系数时被拒**：把立方米当成吨会给出单位上完全错误的平衡，而模型照样解得出可行解。
    /// **A cross-dimensional arc without a conversion factor is rejected**: treating cubic metres as tonnes yields a dimensionally
    /// wrong balance that still solves to a feasible-looking answer.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectCrossDimensionalArcWithoutFactor() =>
        new GeneralizedFlowAggregation<FltX>(
                Problem(toUnit: SIBaseUnits.Second), FltXNetworkSchedulingSolverValueAdapter.Instance, "gf")
            .Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();

    /// <summary>跨量纲弧给出转换系数后可注册 / A cross-dimensional arc registers once a conversion factor is given.</summary>
    [Fact]
    public void Register_ShouldAcceptCrossDimensionalArcWithFactor() =>
        new GeneralizedFlowAggregation<FltX>(
                Problem(toUnit: SIBaseUnits.Second, conversion: SecondsPerMeter()),
                FltXNetworkSchedulingSolverValueAdapter.Instance, "gf")
            .Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeTrue();

    /// <summary>**仿射单位的节点被拒** / A node with an affine unit is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectAffineNodeUnit() =>
        new GeneralizedFlowAggregation<FltX>(
                Problem(nodeUnit: TemperatureUnits.Celsius), FltXNetworkSchedulingSolverValueAdapter.Instance, "gf")
            .Register(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();

    /// <summary>未注册模型提取解失败 / Extracting from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Aggregation().ExtractSolution(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();

    /// <summary>注册后未求解时提取解失败 / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        var model = new LinearMetaModel<Flt64>("gf");
        GeneralizedFlowAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(model);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>上下文委托到聚合 / The context delegates to the aggregation.</summary>
    [Fact]
    public void Context_ShouldDelegate() {
        var context = new GeneralizedFlowContext<FltX>(Problem(), FltXNetworkSchedulingSolverValueAdapter.Instance);
        var model = new LinearMetaModel<Flt64>("gf");

        context.Register(model).IsOk.Should().BeTrue();
        context.Input(new NetworkArcId("a1")).IsOk.Should().BeTrue();
        context.Cost.Monomials.Should().NotBeEmpty();
    }

    /// <summary>构造一个可注册的聚合 / Build a registrable aggregation.</summary>
    private static GeneralizedFlowAggregation<FltX> Aggregation(double yield = 1.0) =>
        new(Problem(yield: yield), FltXNetworkSchedulingSolverValueAdapter.Instance, "gf");

    /// <summary>每秒每米的比例单位 / The "seconds per metre" ratio unit.</summary>
    private static Quantity<FltX> SecondsPerMeter() => new(new FltX(1.0), SIBaseUnits.Second.Divide(SIBaseUnits.Meter));

    /// <summary>构造一个广义流问题 / Build a generalized-flow problem.</summary>
    private static GeneralizedFlowProblem<FltX> Problem(
        double yield = 1.0,
        PhysicalUnit? nodeUnit = null,
        PhysicalUnit? toUnit = null,
        Quantity<FltX>? conversion = null) {
        PhysicalUnit fromUnit = nodeUnit ?? SIBaseUnits.Meter;
        PhysicalUnit destination = toUnit ?? fromUnit;

        NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            new Quantity<FltX>(new FltX(2.0), NoneUnit.Instance), null, new NetworkArcId("a1")).Value;
        GeneralizedFlowArc<FltX> arc = GeneralizedFlowArc<FltX>.Invoke(
            networkArc,
            CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(100.0)).Value,
            new Quantity<FltX>(new FltX(yield), NoneUnit.Instance),
            conversion).Value;

        GeneralizedFlowNode<FltX>[] nodes = new[] {
            GeneralizedFlowNode<FltX>.Invoke(Node("n0"), fromUnit, new Quantity<FltX>(new FltX(5.0), fromUnit)).Value,
            GeneralizedFlowNode<FltX>.Invoke(Node("n1"), destination, new Quantity<FltX>(new FltX(-5.0), destination)).Value,
        };

        GeneralNetworkGraph(networkArc, out NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph);

        return new GeneralizedFlowProblem<FltX>(
            graph, nodes, new[] { arc }, new FltX(0.0), costUnit: SIBaseUnits.Meter);
    }

    /// <summary>构造网络图 / Build the network graph.</summary>
    private static void GeneralNetworkGraph(
        NetworkArc<FltX> arc,
        out NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph) =>
        graph = NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
            new[] { Node("n0"), Node("n1") }, new[] { arc },
            new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                NodeId: node => node.Id,
                ArcId: candidate => candidate.Id,
                From: candidate => candidate.From,
                To: candidate => candidate.To)).Value;

    /// <summary>构造网络节点 / Build a network node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId(id), new Dictionary<string, Quantity<FltX>>()).Value;

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
