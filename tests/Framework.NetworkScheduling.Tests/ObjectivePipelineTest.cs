// 目标管线测试 / Tests for the objective pipelines.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 目标管线 / The objective pipelines.
/// </summary>
public sealed class ObjectivePipelineTest {
    /// <summary>运行费用目标注册成功 / The operating-cost objective registers.</summary>
    [Fact]
    public void MinCostFlow_ShouldRegisterObjective() {
        var model = new LinearMetaModel<Flt64>("test");

        new MinCostFlowObjective(Expression(), "op_cost").Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>广义流费用目标注册成功 / The generalized-flow cost objective registers.</summary>
    [Fact]
    public void GeneralizedFlowCost_ShouldRegisterObjective() {
        var model = new LinearMetaModel<Flt64>("test");

        new GeneralizedFlowCostObjective(Expression(), "gf_cost").Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>建设费用目标注册成功 / The construction-cost objective registers.</summary>
    [Fact]
    public void ConstructionCost_ShouldRegisterObjective() {
        var model = new LinearMetaModel<Flt64>("test");

        new NetworkStructureConstructionCostObjective(Expression, "build_cost").Invoke(model).IsOk
            .Should().BeTrue();
    }

    /// <summary>
    /// **建设费用表达式在调用时才求值**：构造期表达式还不存在，提前抓取会注册一个空目标。
    /// **The construction-cost expression is evaluated only at invoke time**: it does not exist yet when the objective is
    /// constructed, and capturing it early would register an empty objective.
    /// </summary>
    [Fact]
    public void ConstructionCost_ShouldEvaluateLazily() {
        var model = new LinearMetaModel<Flt64>("test");
        int evaluations = 0;
        var objective = new NetworkStructureConstructionCostObjective(
            () => {
                evaluations += 1;
                return Expression();
            },
            "build_cost");

        evaluations.Should().Be(0, "构造期不应求值 / it must not evaluate at construction time");
        objective.Invoke(model);
        evaluations.Should().Be(1);
    }

    /// <summary>
    /// **每次注册都重新求值**：调用方可能在两次注册之间重建了费用表达式。
    /// **Every registration re-evaluates**: the caller may have rebuilt the cost expression between registrations.
    /// </summary>
    [Fact]
    public void ConstructionCost_ShouldReevaluatePerInvoke() {
        int evaluations = 0;
        var objective = new NetworkStructureConstructionCostObjective(
            () => {
                evaluations += 1;
                return Expression();
            },
            "build_cost");

        objective.Invoke(new LinearMetaModel<Flt64>("a"));
        objective.Invoke(new LinearMetaModel<Flt64>("b"));

        evaluations.Should().Be(2);
    }

    /// <summary>目标名称与管线名称一致 / The objective name matches the pipeline name.</summary>
    [Fact]
    public void Objectives_ShouldExposeName() {
        new MinCostFlowObjective(Expression(), "op_cost").Name.Should().Be("op_cost");
        new GeneralizedFlowCostObjective(Expression(), "gf_cost").Name.Should().Be("gf_cost");
        new NetworkStructureConstructionCostObjective(Expression, "build_cost").Name.Should().Be("build_cost");
    }

    /// <summary>常数表达式也能注册为目标 / A constant expression registers as an objective.</summary>
    [Fact]
    public void Objectives_ShouldAcceptConstantExpression() {
        var model = new LinearMetaModel<Flt64>("test");
        var cost = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(7.0));

        new MinCostFlowObjective(cost, "op_cost").Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression() =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, new URealVar("x")) }, Flt64.Zero);
}
