// 广义流模型测试 / Tests for the generalized-flow models.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 广义流模型 / The generalized-flow models.
/// </summary>
public sealed class GeneralizedFlowModelTest {
    /// <summary>合法弧可构造 / A legitimate arc is constructed.</summary>
    [Fact]
    public void Arc_ShouldConstruct() =>
        Arc(yield: new Quantity<FltX>(new FltX(0.8), NoneUnit.Instance)).IsOk.Should().BeTrue();

    /// <summary>默认产率为 1 / The default yield is one.</summary>
    [Fact]
    public void Arc_ShouldDefaultYieldToOne() {
        GeneralizedFlowArc<FltX> arc = Arc().Value;

        arc.ProductionYield.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
        arc.ProductionYield.Unit.Should().BeSameAs(NoneUnit.Instance);
    }

    /// <summary>
    /// **带量纲的产率被拒绝**：那意味着单位换算被混进了业务损耗，后续成本归集会差一整个量纲的因子。
    /// **A dimensional yield is rejected**: it means unit conversion has been folded into business loss, leaving later cost
    /// attribution off by a whole dimension.
    /// </summary>
    [Fact]
    public void Arc_ShouldRejectDimensionalYield() =>
        Arc(yield: new Quantity<FltX>(new FltX(0.8), SIBaseUnits.Meter)).IsOk.Should().BeFalse();

    /// <summary>
    /// **仿射单位的产率被拒绝**：仿射单位的乘除没有物理意义。
    /// **An affine-unit yield is rejected**: multiplication and division of affine units is physically meaningless.
    /// </summary>
    [Fact]
    public void Arc_ShouldRejectAffineYield() =>
        Arc(yield: new Quantity<FltX>(new FltX(0.5), TemperatureUnits.Celsius)).IsOk.Should().BeFalse();

    /// <summary>
    /// **单位换算与业务产率是两件事**：两者都被保留，不会被折算成一个总系数。
    /// **Unit conversion and business yield are two things**: both are retained and never folded into one overall factor.
    /// </summary>
    [Fact]
    public void Arc_ShouldKeepConversionAndYieldSeparate() {
        var conversion = new Quantity<FltX>(new FltX(0.907), NoneUnit.Instance);
        GeneralizedFlowArc<FltX> arc = Arc(yield: new Quantity<FltX>(new FltX(0.8), NoneUnit.Instance), conversion: conversion).Value;

        arc.ProductionYield.Value.ToFlt64().ToDouble().Should().BeApproximately(0.8, 1e-9);
        arc.InputToOutputFactor!.Value.ToFlt64().ToDouble().Should().BeApproximately(0.907, 1e-9);
    }

    /// <summary>节点净供需与单位匹配时可构造 / A node with matching balance dimension is constructed.</summary>
    [Fact]
    public void Node_ShouldConstruct() =>
        Node("n1", new FltX(5.0)).IsOk.Should().BeTrue();

    /// <summary>
    /// **净供需量纲与节点单位不一致时被拒绝**：否则节点平衡约束两边量纲不同，求解器给出的解在量纲上毫无意义。
    /// **A net supply whose dimension differs from the node unit is rejected**: otherwise the balance constraint compares different
    /// dimensions and the solver's answer is dimensionally meaningless.
    /// </summary>
    [Fact]
    public void Node_ShouldRejectMismatchedDimension() =>
        Node("n1", new FltX(5.0), unit: SIBaseUnits.Second).IsOk.Should().BeFalse();

    /// <summary>节点标识来自网络节点 / The node identity comes from the network node.</summary>
    [Fact]
    public void Node_ShouldExposeNetworkNodeId() =>
        Node("n1", new FltX(5.0)).Value.Id.Value.Should().Be("n1");

    /// <summary>问题保留节点与弧的快照 / The problem snapshots nodes and arcs.</summary>
    [Fact]
    public void Problem_ShouldSnapshotCollections() {
        var nodes = new List<GeneralizedFlowNode<FltX>> { Node("n1", new FltX(1.0)).Value };
        GeneralizedFlowProblem<FltX> problem = ProblemOf(nodes, Array.Empty<GeneralizedFlowArc<FltX>>());

        nodes.Add(Node("n2", new FltX(2.0)).Value);

        problem.Nodes.Should().HaveCount(1);
    }

    /// <summary>问题默认费用单位为无量纲 / The problem's cost unit defaults to dimensionless.</summary>
    [Fact]
    public void Problem_ShouldDefaultCostUnit() =>
        ProblemOf(Array.Empty<GeneralizedFlowNode<FltX>>(), Array.Empty<GeneralizedFlowArc<FltX>>())
            .CostUnit.Should().BeSameAs(NoneUnit.Instance);

    /// <summary>默认容差合法 / The default tolerance is valid.</summary>
    [Fact]
    public void Tolerance_ShouldBeValidByDefault() =>
        new GeneralizedFlowSolverTolerance().IsValid().Should().BeTrue();

    /// <summary>负容差不合法 / A negative tolerance is invalid.</summary>
    [Fact]
    public void Tolerance_ShouldRejectNegative() =>
        new GeneralizedFlowSolverTolerance(new Flt64(-1.0), new Flt64(1e-7)).IsValid().Should().BeFalse();

    /// <summary>
    /// **容差随比较值的规模放大**：只用绝对容差时，大数值残差永远超差、小数值残差永远合格。
    /// **The tolerance scales with the magnitude of the compared values**: with an absolute tolerance alone, large-value residuals
    /// always fail and small-value ones always pass.
    /// </summary>
    [Fact]
    public void Tolerance_ShouldScaleWithMagnitude() {
        var tolerance = new GeneralizedFlowSolverTolerance(new Flt64(1e-7), new Flt64(1e-7));

        Flt64 small = tolerance.ForValues(new[] { new Flt64(1.0) })!.Value;
        Flt64 large = tolerance.ForValues(new[] { new Flt64(1e6) })!.Value;

        small.ToDouble().Should().BeLessThan(large.ToDouble());
        large.ToDouble().Should().BeApproximately(1e-7 + 1e-7 * 1e6, 1e-12);
    }

    /// <summary>非有限值不给容差 / A non-finite value yields no tolerance.</summary>
    [Fact]
    public void Tolerance_ShouldRejectNonFiniteValues() =>
        new GeneralizedFlowSolverTolerance().ForValues(new[] { new Flt64(double.PositiveInfinity) })
            .Should().BeNull();

    /// <summary>
    /// **节点结果保留 `Residual`**：省略它之后，平衡算错时解里只剩一对看起来都正常的数字。
    /// **The node result keeps `Residual`**: omit it and a mis-computed balance leaves only a pair of plausible-looking numbers.
    /// </summary>
    [Fact]
    public void NodeResult_ShouldCarryResidual() {
        var zero = new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter);
        var result = new GeneralizedFlowNodeResult<FltX>(zero, zero, zero, zero, zero);

        result.Residual.Should().BeSameAs(zero);
    }

    /// <summary>表达式视图按投入／产出分别查询 / The expression view queries inputs and outputs separately.</summary>
    [Fact]
    public void Expressions_ShouldQuerySeparately() {
        var arcId = new NetworkArcId("a1");
        var input = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        var expressions = new GeneralizedFlowExpressions(
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>> { [arcId] = input },
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>>());

        expressions.Input(arcId).IsOk.Should().BeTrue();
        expressions.Output(arcId).IsOk.Should().BeFalse();
    }

    /// <summary>未注册的弧查询失败 / Querying an unregistered arc fails.</summary>
    [Fact]
    public void Expressions_ShouldRejectUnregisteredArc() {
        var expressions = new GeneralizedFlowExpressions(
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>>(),
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>>());

        expressions.Input(new NetworkArcId("ghost")).IsOk.Should().BeFalse();
        expressions.Output(new NetworkArcId("ghost")).IsOk.Should().BeFalse();
    }

    /// <summary>构造一条广义流弧 / Build a generalized-flow arc.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        GeneralizedFlowArc<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Arc(
        Quantity<FltX>? yield = null,
        Quantity<FltX>? conversion = null) =>
        GeneralizedFlowArc<FltX>.Invoke(
            NetworkArc<FltX>.Invoke(
                new NetworkNodeId("n1"), new NetworkNodeId("n2"),
                new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
                new Dictionary<string, Quantity<FltX>>()).Value,
            CapacityBounds<FltX>.Invoke(
                new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX(10.0), SIBaseUnits.Meter)).Value,
            yield,
            conversion);

    /// <summary>构造一个广义流节点 / Build a generalized-flow node.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        GeneralizedFlowNode<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Node(
        string id,
        FltX supply,
        PhysicalUnit? unit = null) =>
        GeneralizedFlowNode<FltX>.Invoke(
            NetworkNode<FltX>.Invoke(
                new NetworkNodeId(id),
                new Dictionary<string, Quantity<FltX>>()).Value,
            unit ?? SIBaseUnits.Meter,
            new Quantity<FltX>(supply, SIBaseUnits.Meter));

    /// <summary>构造一个广义流问题 / Build a generalized-flow problem.</summary>
    private static GeneralizedFlowProblem<FltX> ProblemOf(
        IReadOnlyList<GeneralizedFlowNode<FltX>> nodes,
        IReadOnlyList<GeneralizedFlowArc<FltX>> arcs) =>
        new(
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                new List<NetworkNode<FltX>>(),
                new List<NetworkArc<FltX>>(),
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: arc => arc.Id,
                    From: arc => arc.From,
                    To: arc => arc.To)).Value,
            nodes,
            arcs,
            new FltX(0.0));
}
