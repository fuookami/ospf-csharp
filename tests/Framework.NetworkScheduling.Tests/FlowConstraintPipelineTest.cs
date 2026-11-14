// 网络流约束管线测试 / Tests for the flow constraint pipelines.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络流约束管线 / The flow constraint pipelines.
/// </summary>
public sealed class FlowConstraintPipelineTest {
    /// <summary>
    /// **容量上下界注册成两行独立约束**：合成一条区间约束会让上界与下界共用一份对偶，而最优解上通常只有一侧是紧的。
    /// **The capacity bounds register as two independent rows**: a merged interval constraint shares one dual between both sides,
    /// while at an optimum usually only one side is tight.
    /// </summary>
    [Fact]
    public void CapacityBound_ShouldRegisterTwoRows() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new CapacityBoundConstraint(Expression(), new Flt64(0.0), new Flt64(10.0), "cap");

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(2);
    }

    /// <summary>两行的名称分别带 lower 与 upper 后缀 / The two rows are suffixed lower and upper.</summary>
    [Fact]
    public void CapacityBound_ShouldNameBothRows() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new CapacityBoundConstraint(Expression(), new Flt64(0.0), new Flt64(10.0), "cap");

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Select(constraint => constraint.Name)
            .Should().BeEquivalentTo(new[] { "cap_lower", "cap_upper" });
    }

    /// <summary>两行归属同一个管线分组 / Both rows belong to the pipeline's group.</summary>
    [Fact]
    public void CapacityBound_ShouldGroupBothRows() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new CapacityBoundConstraint(Expression(), new Flt64(0.0), new Flt64(10.0), "cap");
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(2);
    }

    /// <summary>常数表达式也能注册 / A constant expression still registers.</summary>
    [Fact]
    public void CapacityBound_ShouldAcceptConstantExpression() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new CapacityBoundConstraint(Constant(3.0), new Flt64(0.0), new Flt64(10.0), "cap");

        pipeline.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Should().HaveCount(2);
    }

    /// <summary>
    /// **守恒约束注册为一行等式**：节点平衡是一行，不是两行。
    /// **The conservation constraint registers as a single equality row**: node balance is one row, not two.
    /// </summary>
    [Fact]
    public void Conservation_ShouldRegisterOneRow() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new FlowConservationConstraint(Expression(), new Flt64(5.0), "balance");

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(1);
        model.Constraints[0].Name.Should().Be("balance");
    }

    /// <summary>守恒约束归属自身分组 / The conservation row belongs to its own group.</summary>
    [Fact]
    public void Conservation_ShouldGroupItsRow() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new FlowConservationConstraint(Expression(), new Flt64(5.0), "balance");
        pipeline.Invoke(model);

        model.ConstraintsOfGroup(pipeline).Should().HaveCount(1);
    }

    /// <summary>负净供需（需求）也可注册 / A negative net balance (demand) registers fine.</summary>
    [Fact]
    public void Conservation_ShouldAcceptNegativeNet() {
        var model = new LinearMetaModel<Flt64>("test");
        var pipeline = new FlowConservationConstraint(Expression(), new Flt64(-5.0), "balance");

        pipeline.Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>两条管线可在同一模型共存且互不干扰 / Both pipelines coexist in one model.</summary>
    [Fact]
    public void Pipelines_ShouldCoexist() {
        var model = new LinearMetaModel<Flt64>("test");
        var capacity = new CapacityBoundConstraint(Expression(), new Flt64(0.0), new Flt64(10.0), "cap");
        var balance = new FlowConservationConstraint(Expression(), new Flt64(5.0), "balance");

        capacity.Invoke(model).IsOk.Should().BeTrue();
        balance.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Should().HaveCount(3);
        model.ConstraintsOfGroup(capacity).Should().HaveCount(2);
        model.ConstraintsOfGroup(balance).Should().HaveCount(1);
    }

    /// <summary>构造一个单变量表达式 / Build a single-variable expression.</summary>
    private static LinearPolynomial<Flt64> Expression() =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, new URealVar("x")) }, Flt64.Zero);

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));
}
