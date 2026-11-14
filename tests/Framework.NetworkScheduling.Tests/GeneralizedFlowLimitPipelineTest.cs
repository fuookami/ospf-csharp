// 广义流约束管线测试 / Tests for the generalized-flow constraint pipelines.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 广义流约束管线 / The generalized-flow constraint pipelines.
/// </summary>
public sealed class GeneralizedFlowLimitPipelineTest {
    /// <summary>
    /// **每条弧注册两条约束（下界与上界）**：容量作用在起点投入上。
    /// **Each arc registers two constraints (lower and upper)**: capacity applies to the origin input.
    /// </summary>
    [Fact]
    public void InputCapacity_ShouldRegisterTwoRowsPerArc() {
        var model = new LinearMetaModel<Flt64>("gf");

        Pipeline(arcs: 1).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2);
    }

    /// <summary>多弧时行数成倍 / The row count doubles with more arcs.</summary>
    [Fact]
    public void InputCapacity_ShouldScaleWithArcs() {
        var model = new LinearMetaModel<Flt64>("gf");

        Pipeline(arcs: 3).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(6);
    }

    /// <summary>行名带弧标识与上下界后缀 / Row names carry the arc identity and bound suffix.</summary>
    [Fact]
    public void InputCapacity_ShouldNameRows() {
        var model = new LinearMetaModel<Flt64>("gf");

        Pipeline(arcs: 1).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Select(constraint => constraint.Name)
            .Should().BeEquivalentTo(new[] { "cap_a1_input_lower", "cap_a1_input_upper" });
    }

    /// <summary>两行归属同一管线分组 / Both rows belong to the pipeline's group.</summary>
    [Fact]
    public void InputCapacity_ShouldGroupRows() {
        var model = new LinearMetaModel<Flt64>("gf");
        GeneralizedFlowInputCapacityPipeline pipeline = Pipeline(arcs: 1);
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(2);
    }

    /// <summary>
    /// **容量缺失时直接失败**：缺一条上界就等于让该弧可以无限投入。
    /// **A missing capacity fails outright**: an absent upper bound lets that arc take unlimited input.
    /// </summary>
    [Fact]
    public void InputCapacity_ShouldRejectMissingBound() {
        var pipeline = new GeneralizedFlowInputCapacityPipeline(
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>> { [new("a1")] = Expression() },
            new Dictionary<NetworkArcId, (Flt64 Lower, Flt64 Upper)>(),
            "cap");

        pipeline.Invoke(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();
    }

    /// <summary>空输入集注册成功且不产生约束 / An empty input set registers without rows.</summary>
    [Fact]
    public void InputCapacity_ShouldAcceptNoArcs() {
        var model = new LinearMetaModel<Flt64>("gf");
        var pipeline = new GeneralizedFlowInputCapacityPipeline(
            new Dictionary<NetworkArcId, LinearPolynomial<Flt64>>(),
            new Dictionary<NetworkArcId, (Flt64 Lower, Flt64 Upper)>(),
            "cap");

        pipeline.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Should().BeEmpty();
    }

    /// <summary>**每个节点注册一条广义平衡等式** / Each node registers one generalized balance equality.</summary>
    [Fact]
    public void NodeBalance_ShouldRegisterOneRowPerNode() {
        var model = new LinearMetaModel<Flt64>("gf");

        Balance(nodes: 2).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2);
        model.Constraints.Should().OnlyContain(constraint => constraint.Name.StartsWith("eq_balance_"));
    }

    /// <summary>平衡行归属自身分组 / Balance rows belong to their own group.</summary>
    [Fact]
    public void NodeBalance_ShouldGroupRows() {
        var model = new LinearMetaModel<Flt64>("gf");
        GeneralizedFlowNodeBalancePipeline pipeline = Balance(nodes: 1);
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(1);
    }

    /// <summary>**节点供需缺失时直接失败** / A missing node balance fails outright.</summary>
    [Fact]
    public void NodeBalance_ShouldRejectMissingSupply() {
        var pipeline = new GeneralizedFlowNodeBalancePipeline(
            new Dictionary<NetworkNodeId, LinearPolynomial<Flt64>> { [new("n0")] = Expression() },
            new Dictionary<NetworkNodeId, Flt64>(),
            "eq");

        pipeline.Invoke(new LinearMetaModel<Flt64>("gf")).IsOk.Should().BeFalse();
    }

    /// <summary>负净供给（需求节点）可注册 / A negative net supply (demand node) registers.</summary>
    [Fact]
    public void NodeBalance_ShouldAcceptNegativeSupply() {
        var model = new LinearMetaModel<Flt64>("gf");
        var pipeline = new GeneralizedFlowNodeBalancePipeline(
            new Dictionary<NetworkNodeId, LinearPolynomial<Flt64>> { [new("n0")] = Expression() },
            new Dictionary<NetworkNodeId, Flt64> { [new("n0")] = new Flt64(-4.0) },
            "eq");

        pipeline.Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>构造容量管线 / Build a capacity pipeline.</summary>
    private static GeneralizedFlowInputCapacityPipeline Pipeline(int arcs) {
        var inputs = new Dictionary<NetworkArcId, LinearPolynomial<Flt64>>();
        var bounds = new Dictionary<NetworkArcId, (Flt64 Lower, Flt64 Upper)>();
        for (int index = 0; index < arcs; index++) {
            var arcId = new NetworkArcId($"a{index + 1}");
            inputs[arcId] = Expression();
            bounds[arcId] = (Flt64.Zero, new Flt64(10.0));
        }
        return new GeneralizedFlowInputCapacityPipeline(inputs, bounds, "cap");
    }

    /// <summary>构造平衡管线 / Build a balance pipeline.</summary>
    private static GeneralizedFlowNodeBalancePipeline Balance(int nodes) {
        var balances = new Dictionary<NetworkNodeId, LinearPolynomial<Flt64>>();
        var supplies = new Dictionary<NetworkNodeId, Flt64>();
        for (int index = 0; index < nodes; index++) {
            var nodeId = new NetworkNodeId($"n{index}");
            balances[nodeId] = Expression();
            supplies[nodeId] = new Flt64(index);
        }
        return new GeneralizedFlowNodeBalancePipeline(balances, supplies, "eq");
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression() =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, new URealVar("x")) }, Flt64.Zero);
}
