// 路径编译聚合测试 / Tests for the path compilation aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路径编译聚合 / The path compilation aggregation.
/// </summary>
public sealed class PathCompilationAggregationTest {
    /// <summary>**注册落下变量、行符号与行约束** / Registration lands variables, row symbols and row constraints.</summary>
    [Fact]
    public void Register_ShouldLandVariablesAndRows() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();

        aggregation.Register(model).IsOk.Should().BeTrue();

        aggregation.Pipelines.Should().HaveCount(2);
        model.Constraints.Should().HaveCount(2, "两行都是等式 / both rows are equalities");
    }

    /// <summary>初始列被自动注册 / The initial columns are registered automatically.</summary>
    [Fact]
    public void Register_ShouldAddInitialColumns() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();

        aggregation.Register(model).IsOk.Should().BeTrue();

        aggregation.Columns.Should().ContainSingle();
        aggregation.Compilation.Variables.Should().ContainSingle();
    }

    /// <summary>**重复注册同一模型幂等** / Repeated registration on the same model is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotentOnSameModel() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Pipelines.Should().HaveCount(2);
    }

    /// <summary>**跨模型复用被拒** / Reuse across models is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        aggregation.Register(Model()).IsOk.Should().BeTrue();

        aggregation.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**重复行标识被拒** / Duplicate row identities are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectDuplicateRows() {
        var rows = new List<PathCompilationRow<Flt64>> { Row("r1"), Row("r1") };
        var aggregation = new PathCompilationAggregation<Flt64, string>(
            new PathCompilationProblem<Flt64, string>(rows, Array.Empty<PathColumn<Flt64, string>>(), SIBaseUnits.Meter),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            "pc");

        aggregation.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**无标识行被拒** / Unnamed rows are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectUnnamedRows() {
        var rows = new List<PathCompilationRow<Flt64>> { new("  ", SIBaseUnits.Meter, Meters(1.0), Meters(1.0)) };
        var aggregation = new PathCompilationAggregation<Flt64, string>(
            new PathCompilationProblem<Flt64, string>(rows, Array.Empty<PathColumn<Flt64, string>>(), SIBaseUnits.Meter),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            "pc");

        aggregation.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**无边界行被拒** / Unbounded rows are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectUnboundedRows() {
        var rows = new List<PathCompilationRow<Flt64>> { new("r1", SIBaseUnits.Meter) };
        var aggregation = new PathCompilationAggregation<Flt64, string>(
            new PathCompilationProblem<Flt64, string>(rows, Array.Empty<PathColumn<Flt64, string>>(), SIBaseUnits.Meter),
            Flt64NetworkSchedulingSolverValueAdapter.Instance,
            "pc");

        aggregation.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**规范化把费用与行系数都换算到 solver 边界** / Normalisation converts both cost and row coefficients.</summary>
    [Fact]
    public void Compile_ShouldNormalizeCostAndCoefficients() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();

        CompiledPathColumn<Flt64, string> compiled = aggregation.Compile(Column("col_a", 3.0)).Value;

        compiled.Cost.ToDouble().Should().BeApproximately(3.0, 1e-9);
        compiled.Coefficients["r1"].ToDouble().Should().BeApproximately(1.0, 1e-9);
        compiled.Coefficients["r2"].ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**列引用未知行时被拒** / A column referencing an unknown row is rejected.</summary>
    [Fact]
    public void Compile_ShouldRejectUnknownRowReference() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        var column = new PathColumn<Flt64, string>(
            "col_a", "payload", Meters(1.0),
            new Dictionary<string, Quantity<Flt64>> { ["r_ghost"] = Meters(1.0) });

        aggregation.Compile(column).IsOk.Should().BeFalse();
    }

    /// <summary>**空白列标识被拒** / A blank column identity is rejected.</summary>
    [Fact]
    public void Compile_ShouldRejectBlankIdentity() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        var column = new PathColumn<Flt64, string>(
            "   ", "payload", Meters(1.0),
            new Dictionary<string, Quantity<Flt64>> { ["r1"] = Meters(1.0) });

        aggregation.Compile(column).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时加列被拒** / Adding columns before registration is rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectBeforeRegistration() =>
        Aggregation().AddColumns(new[] { Column("col_b", 2.0) }, Model()).IsOk.Should().BeFalse();

    /// <summary>**新列同时进变量表、行符号与目标** / A new column enters variables, row symbols and the objective.</summary>
    [Fact]
    public void AddColumns_ShouldLandColumnEverywhere() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.AddColumns(new[] { Column("col_b", 2.0) }, model).IsOk.Should().BeTrue();

        aggregation.Columns.Should().HaveCount(2);
        aggregation.Compilation.Variables.Should().HaveCount(2);
    }

    /// <summary>**同标识同系数去重，不重复加列** / An identical identity with identical coefficients deduplicates.</summary>
    [Fact]
    public void AddColumns_ShouldDeduplicateIdenticalColumn() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        Result<IReadOnlyList<PathColumn<Flt64, string>>, ErrorCode, Error<ErrorCode>> added =
            aggregation.AddColumns(new[] { Column("col_a", 3.0) }, model);

        added.IsOk.Should().BeTrue();
        added.Value.Should().BeEmpty("已存在同标识同系数 / an identical column already exists");
        aggregation.Columns.Should().ContainSingle();
    }

    /// <summary>**同标识不同费用被拒** / The same identity with a different cost is rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectConflictingCost() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.AddColumns(new[] { Column("col_a", 9.0) }, model).IsOk.Should().BeFalse();
    }

    /// <summary>**同标识不同载荷被拒** / The same identity with a different payload is rejected.</summary>
    [Fact]
    public void AddColumns_ShouldRejectConflictingPayload() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        var conflicting = new PathColumn<Flt64, string>(
            "col_a", "other-payload", Meters(3.0),
            new Dictionary<string, Quantity<Flt64>> { ["r1"] = Meters(1.0) });

        aggregation.AddColumns(new[] { conflicting }, model).IsOk.Should().BeFalse();
    }

    /// <summary>空批次是安全空操作 / An empty batch is a safe no-op.</summary>
    [Fact]
    public void AddColumns_ShouldAcceptEmptyBatch() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.AddColumns(Array.Empty<PathColumn<Flt64, string>>(), model).IsOk.Should().BeTrue();
    }

    /// <summary>**移除列同时清掉变量、行单项与目标项** / Removing a column clears variable, row terms and objective.</summary>
    [Fact]
    public void RemoveColumns_ShouldClearEverywhere() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.RemoveColumns(new HashSet<string> { "col_a" }, model).IsOk.Should().BeTrue();

        aggregation.Columns.Should().BeEmpty();
        aggregation.Compilation.Variables.Should().BeEmpty();
        aggregation.Compilation.Rows["r1"].Polynomial.Monomials.Should().BeEmpty();
    }

    /// <summary>**移除未知列是安全空操作** / Removing an unknown column is a safe no-op.</summary>
    [Fact]
    public void RemoveColumns_ShouldIgnoreUnknownColumn() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.RemoveColumns(new HashSet<string> { "col_ghost" }, model).IsOk.Should().BeTrue();
        aggregation.Columns.Should().ContainSingle();
    }

    /// <summary>**未注册时移除被拒** / Removal before registration is rejected.</summary>
    [Fact]
    public void RemoveColumns_ShouldRejectBeforeRegistration() =>
        Aggregation().RemoveColumns(new HashSet<string> { "col_a" }, Model()).IsOk.Should().BeFalse();

    /// <summary>**未求解时提取解失败** / Extraction on an unsolved model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldFailWhenUnsolved() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时提取被拒** / Extraction before registration is rejected.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectBeforeRegistration() =>
        Aggregation().ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>**刷新对偶需要声明全部行**：模型里多出未声明的行时被拒 / Extra undeclared rows are rejected.</summary>
    [Fact]
    public void RefreshShadowPrices_ShouldRejectExtraRows() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);
        model.AddConstraint(
            new Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                Array.Empty<Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One)
            .Le(new Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                Array.Empty<Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One)),
            null,
            name: "undeclared");

        aggregation.RefreshShadowPrices(model, MetaDualSolution.Empty, "scope").IsOk.Should().BeFalse();
    }

    /// <summary>**空白定价范围被拒** / A blank pricing scope is rejected.</summary>
    [Fact]
    public void RefreshShadowPrices_ShouldRejectBlankScope() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.RefreshShadowPrices(model, MetaDualSolution.Empty, "  ").IsOk.Should().BeFalse();
    }

    /// <summary>**缺少行对偶时刷新失败** / Missing row duals fail the refresh.</summary>
    [Fact]
    public void RefreshShadowPrices_ShouldFailWithoutDuals() {
        PathCompilationAggregation<Flt64, string> aggregation = Aggregation();
        LinearMetaModel<Flt64> model = Model();
        aggregation.Register(model);

        aggregation.RefreshShadowPrices(model, MetaDualSolution.Empty, "scope").IsOk.Should().BeFalse();
    }

    /// <summary>构造聚合 / Build the aggregation.</summary>
    private static PathCompilationAggregation<Flt64, string> Aggregation() =>
        new(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance, "pc");

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
            new Dictionary<string, Quantity<Flt64>> { [id == "col_a" ? "r1" : "r1"] = Meters(1.0) });

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
