// 路径编译上下文测试 / Tests for the path compilation context.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路径编译上下文 / The path compilation context.
/// </summary>
public sealed class PathCompilationContextTest {
    /// <summary>**上下文不新增状态：聚合是同一个实例** / The context adds no state: one aggregation instance.</summary>
    [Fact]
    public void Context_ShouldDelegateToSingleAggregation() {
        PathCompilationContext<Flt64, string> context = Context();

        context.Aggregation.Problem.Should().BeSameAs(context.Problem);
        context.Aggregation.Name.Should().Be(context.Name);
        context.Name.Should().Be("path_compilation");
    }

    /// <summary>注册委托给聚合 / Registration delegates to the aggregation.</summary>
    [Fact]
    public void Context_RegisterShouldDelegate() {
        PathCompilationContext<Flt64, string> context = Context();
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.Pipelines.Should().HaveCount(2);
        context.Aggregation.Columns.Should().ContainSingle();
    }

    /// <summary>**重复注册同一模型幂等** / Repeated registration is idempotent.</summary>
    [Fact]
    public void Context_RegisterShouldBeIdempotent() {
        PathCompilationContext<Flt64, string> context = Context();
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.Pipelines.Should().HaveCount(2);
    }

    /// <summary>**跨模型复用被拒** / Reuse across models is rejected.</summary>
    [Fact]
    public void Context_RegisterShouldRejectSecondModel() {
        PathCompilationContext<Flt64, string> context = Context();
        context.Register(Model()).IsOk.Should().BeTrue();

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时加列被拒** / Adding columns before registration is rejected.</summary>
    [Fact]
    public void Context_AddColumnsShouldRejectBeforeRegistration() =>
        Context().AddColumns(new[] { Column("col_b", 2.0) }, Model()).IsOk.Should().BeFalse();

    /// <summary>加列委托给聚合 / Column addition delegates to the aggregation.</summary>
    [Fact]
    public void Context_AddColumnsShouldDelegate() {
        PathCompilationContext<Flt64, string> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.AddColumns(new[] { Column("col_b", 2.0) }, model).IsOk.Should().BeTrue();
        context.Aggregation.Columns.Should().HaveCount(2);
    }

    /// <summary>**未注册时移除被拒** / Removal before registration is rejected.</summary>
    [Fact]
    public void Context_RemoveColumnsShouldRejectBeforeRegistration() =>
        Context().RemoveColumns(new HashSet<string> { "col_a" }, Model()).IsOk.Should().BeFalse();

    /// <summary>移除委托给聚合 / Column removal delegates to the aggregation.</summary>
    [Fact]
    public void Context_RemoveColumnsShouldDelegate() {
        PathCompilationContext<Flt64, string> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.RemoveColumns(new HashSet<string> { "col_a" }, model).IsOk.Should().BeTrue();
        context.Aggregation.Columns.Should().BeEmpty();
    }

    /// <summary>**未求解时提取失败** / Extraction on an unsolved model fails.</summary>
    [Fact]
    public void Context_ExtractSolutionShouldFailWhenUnsolved() {
        PathCompilationContext<Flt64, string> context = Context();
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时提取被拒** / Extraction before registration is rejected.</summary>
    [Fact]
    public void Context_ExtractSolutionShouldRejectBeforeRegistration() =>
        Context().ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>构造上下文 / Build the context.</summary>
    private static PathCompilationContext<Flt64, string> Context() =>
        new(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造问题 / Build the problem.</summary>
    private static PathCompilationProblem<Flt64, string> Problem() =>
        new(
            new[] { Row("r1"), Row("r2") },
            new[] { Column("col_a", 3.0) },
            SIBaseUnits.Meter);

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("path_compilation");

    /// <summary>构造一行 / Build a row.</summary>
    private static PathCompilationRow<Flt64> Row(string id) =>
        new(id, SIBaseUnits.Meter, Meters(1.0), Meters(1.0));

    /// <summary>构造一列 / Build a column.</summary>
    private static PathColumn<Flt64, string> Column(string id, double cost) =>
        new(id, "payload", Meters(cost),
            new Dictionary<string, Quantity<Flt64>> { ["r1"] = Meters(1.0) });

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);

}
