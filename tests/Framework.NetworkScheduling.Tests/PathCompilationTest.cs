// 路径编译模型与行管线测试 / Tests for the path compilation model and row pipeline.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Service;
using Fuookami.Ospf.Math.Algebr‌a.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路径编译模型与行管线 / The path compilation model and row pipeline.
/// </summary>
public sealed class PathCompilationTest {
    /// <summary>**每个行标识建一个行符号** / One row symbol per row identity.</summary>
    [Fact]
    public void Compilation_ShouldBuildOneSymbolPerRow() {
        var compilation = new PathCompilation(new[] { "r1", "r2" }, "pc");

        compilation.Rows.Should().HaveCount(2);
        compilation.Rows["r1"].Name.Should().Be("pc_row_r1");
        compilation.Variables.Should().BeEmpty();
    }

    /// <summary>**添加一列同时进变量表与所有行** / Adding a column enters both the variable map and every row.</summary>
    [Fact]
    public void Compilation_AddShouldUpdateVariablesAndRows() {
        var compilation = new PathCompilation(new[] { "r1", "r2" }, "pc");

        compilation.Add("col_a", new Dictionary<string, Flt64> { ["r1"] = new Flt64(2.0), ["r2"] = Flt64.Zero }, "v_a");

        compilation.Variables.Should().HaveCount(1);
        compilation.Variables["col_a"].Should().NotBeNull();
        compilation.Rows["r1"].Polynomial.Monomials.Should().HaveCount(1);
        compilation.Rows["r2"].Polynomial.Monomials.Should().HaveCount(1, "零系数也落一个单项 / a zero coefficient still lands one term");
    }

    /// <summary>**行系数缺失按零处理，不抛异常** / A missing row coefficient counts as zero.</summary>
    [Fact]
    public void Compilation_AddShouldDefaultMissingRowToZero() {
        var compilation = new PathCompilation(new[] { "r1", "r2" }, "pc");

        compilation.Add("col_a", new Dictionary<string, Flt64> { ["r1"] = new Flt64(3.0) }, "v_a");

        compilation.Rows["r2"].Polynomial.Monomials.Should().ContainSingle()
            .Which.Coefficient.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**两列叠加在同一行符号里，而不是互相覆盖** / Two columns accumulate in the same row symbol.</summary>
    [Fact]
    public void Compilation_AddShouldAccumulateColumns() {
        var compilation = new PathCompilation(new[] { "r1" }, "pc");

        compilation.Add("col_a", new Dictionary<string, Flt64> { ["r1"] = new Flt64(2.0) }, "v_a");
        compilation.Add("col_b", new Dictionary<string, Flt64> { ["r1"] = new Flt64(3.0) }, "v_b");

        compilation.Rows["r1"].Polynomial.Monomials.Should().HaveCount(2);
        compilation.Variables.Should().HaveCount(2);
    }

    /// <summary>**移除一列只删它的单项，其它列与常数保留** / Removing a column drops only its terms.</summary>
    [Fact]
    public void Compilation_RemoveShouldDropOnlyThatColumn() {
        var compilation = new PathCompilation(new[] { "r1" }, "pc");
        compilation.Add("col_a", new Dictionary<string, Flt64> { ["r1"] = new Flt64(2.0) }, "v_a");
        compilation.Add("col_b", new Dictionary<string, Flt64> { ["r1"] = new Flt64(3.0) }, "v_b");

        compilation.Remove("col_a");

        compilation.Variables.Should().HaveCount(1);
        compilation.Rows["r1"].Polynomial.Monomials.Should().ContainSingle();
        compilation.Rows["r1"].Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>**移除未知列是安全空操作** / Removing an unknown column is a safe no-op.</summary>
    [Fact]
    public void Compilation_RemoveShouldIgnoreUnknownColumn() {
        var compilation = new PathCompilation(new[] { "r1" }, "pc");
        compilation.Add("col_a", new Dictionary<string, Flt64> { ["r1"] = new Flt64(2.0) }, "v_a");

        compilation.Remove("col_ghost");

        compilation.Variables.Should().HaveCount(1);
        compilation.Rows["r1"].Polynomial.Monomials.Should().ContainSingle();
    }

    /// <summary>**行的上下界相等时注册一条等式** / Equal bounds register one equality.</summary>
    [Fact]
    public void Rows_ShouldRegisterEqualityWhenBoundsMatch() {
        LinearMetaModel<Flt64> model = Model();
        var pipeline = new PathCompilationRows(
            "r1", Symbol("r1"), new Flt64(1.0), new Flt64(1.0));

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        pipeline.Constraints.Should().HaveCount(1, "等式只有一行 / an equality has one row");
        pipeline.Constraints[0].Name.Should().Be("pc_row_r1_eq");
    }

    /// <summary>**上下界不同时注册两条不等式** / Different bounds register two inequalities.</summary>
    [Fact]
    public void Rows_ShouldRegisterTwoInequalitiesWhenBoundsDiffer() {
        LinearMetaModel<Flt64> model = Model();
        var pipeline = new PathCompilationRows(
            "r1", Symbol("r1"), new Flt64(1.0), new Flt64(2.0));

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        pipeline.Constraints.Should().HaveCount(2);
        pipeline.Constraints.Select(constraint => constraint.Name)
            .Should().BeEquivalentTo(new[] { "pc_row_r1_lower", "pc_row_r1_upper" });
    }

    /// <summary>**只有上界时注册一条上界** / Only an upper bound registers one row.</summary>
    [Fact]
    public void Rows_ShouldRegisterOnlyUpperBound() {
        LinearMetaModel<Flt64> model = Model();
        var pipeline = new PathCompilationRows("r1", Symbol("r1"), null, new Flt64(2.0));

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        pipeline.Constraints.Should().ContainSingle().Which.Name.Should().Be("pc_row_r1_upper");
    }

    /// <summary>**只有下界时注册一条下界** / Only a lower bound registers one row.</summary>
    [Fact]
    public void Rows_ShouldRegisterOnlyLowerBound() {
        LinearMetaModel<Flt64> model = Model();
        var pipeline = new PathCompilationRows("r1", Symbol("r1"), new Flt64(1.0), null);

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        pipeline.Constraints.Should().ContainSingle().Which.Name.Should().Be("pc_row_r1_lower");
    }

    /// <summary>**两侧都无界时什么都不注册** / No bounds register nothing.</summary>
    [Fact]
    public void Rows_ShouldRegisterNothingWithoutBounds() {
        LinearMetaModel<Flt64> model = Model();
        var pipeline = new PathCompilationRows("r1", Symbol("r1"), null, null);

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        pipeline.Constraints.Should().BeEmpty();
        model.Constraints.Should().BeEmpty();
    }

    /// <summary>**问题快照对传入集合做拷贝** / The problem snapshot copies the collections it is given.</summary>
    [Fact]
    public void Problem_ShouldSnapshotRowsAndColumns() {
        var rows = new List<PathCompilationRow<Flt64>> { Row("r1") };
        var columns = new List<PathColumn<Flt64, string>>();
        var problem = new PathCompilationProblem<Flt64, string>(rows, columns, SIBaseUnits.Meter);

        rows.Add(Row("r2"));

        problem.Rows.Should().ContainSingle();
        problem.CostUnit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>**列的系数字典被快照** / A column snapshots its coefficient dictionary.</summary>
    [Fact]
    public void Column_ShouldSnapshotCoefficients() {
        var coefficients = new Dictionary<string, Quantity<Flt64>> { ["r1"] = Meters(1.0) };
        var column = new PathColumn<Flt64, string>(
            "col_a", "payload", Meters(2.0), coefficients);

        coefficients["r2"] = Meters(9.0);

        column.Coefficients.Should().ContainSingle();
        column.Id.Should().Be("col_a");
        column.Payload.Should().Be("payload");
    }

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("path_compilation");

    /// <summary>构造一个空行符号 / Build an empty row symbol.</summary>
    private static LinearExpressionSymbol Symbol(string id) =>
        new(
            new MutableLinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            name: $"pc_row_{id}");

    /// <summary>构造一行 / Build a row.</summary>
    private static PathCompilationRow<Flt64> Row(string id) =>
        new(id, SIBaseUnits.Meter, Meters(1.0), Meters(1.0));

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
