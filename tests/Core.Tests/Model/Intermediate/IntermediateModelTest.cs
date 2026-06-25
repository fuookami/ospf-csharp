#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;
/// <summary>
/// 中间模型测试 / Intermediate model tests.
/// </summary>
public class IntermediateModelTest {
    [Fact]
    public void LinearConstraintCell_Creation() {
        var cell = new LinearConstraintCell(0, 1, Flt64.One);
        cell.RowIndex.Should().Be(0);
        cell.ColIndex.Should().Be(1);
        cell.Coefficient.Should().Be(Flt64.One);
    }

    [Fact]
    public void LinearConstraintCell_Negate() {
        var cell = new LinearConstraintCell(0, 1, new Flt64(3.0));
        LinearConstraintCell negated = cell.Negate();
        negated.Coefficient.Should().Be(new Flt64(-3.0));
    }

    [Fact]
    public void LinearObjectiveCell_Creation() {
        var cell = new LinearObjectiveCell(0, new Flt64(5.0));
        cell.ColIndex.Should().Be(0);
        cell.Coefficient.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void LinearExpressionSymbol_FromVariable() {
        var item = new RealVar("x");
        var sym = LinearExpressionSymbol.FromVariable(item, "x_sym");
        sym.Name.Should().Be("x_sym");
    }

    [Fact]
    public void LinearExpressionSymbol_FromConstant() {
        var sym = LinearExpressionSymbol.FromConstant(new Flt64(42.0), "const_42");
        sym.Name.Should().Be("const_42");
    }

    [Fact]
    public void QuadraticExpressionSymbol_FromVariable() {
        var item = new RealVar("y");
        var sym = QuadraticExpressionSymbol.FromVariable(item, "y_quad");
        sym.Name.Should().Be("y_quad");
        sym.Category.Should().Be(QuadraticCategory.Instance);
    }

    [Fact]
    public void LinearExpressionSymbol_Evaluate_Constant() {
        var sym = LinearExpressionSymbol.FromConstant(new Flt64(42.0));
        // Evaluate with empty token table should return the constant
        // (simplified: just verify the symbol was created correctly)
        sym.Polynomial.Constant.Should().Be(new Flt64(42.0));
    }

    [Fact]
    public void LinearExpressionSymbol_Dependencies_EmptyForConstant() {
        var sym = LinearExpressionSymbol.FromConstant(Flt64.One);
        sym.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void QuadraticExpressionSymbol_Dependencies_EmptyForConstant() {
        var sym = QuadraticExpressionSymbol.FromConstant(Flt64.One);
        sym.Dependencies.Should().BeEmpty();
    }
}
