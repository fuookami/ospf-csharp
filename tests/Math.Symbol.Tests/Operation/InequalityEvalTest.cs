#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests;

public class InequalityEvalTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");

    [Fact]
    public void SatisfiedBy_Flt64_LT_True() {
        Comparison.LT.SatisfiedBy(new Flt64(1.0), new Flt64(2.0)).Should().BeTrue();
    }

    [Fact]
    public void SatisfiedBy_Flt64_LT_False() {
        Comparison.LT.SatisfiedBy(new Flt64(2.0), new Flt64(1.0)).Should().BeFalse();
    }

    [Fact]
    public void SatisfiedBy_Flt64_LE_Equal() {
        Comparison.LE.SatisfiedBy(new Flt64(2.0), new Flt64(2.0)).Should().BeTrue();
    }

    [Fact]
    public void SatisfiedBy_Flt64_EQ_True() {
        Comparison.EQ.SatisfiedBy(new Flt64(5.0), new Flt64(5.0)).Should().BeTrue();
    }

    [Fact]
    public void SatisfiedBy_Flt64_EQ_False() {
        Comparison.EQ.SatisfiedBy(new Flt64(5.0), new Flt64(6.0)).Should().BeFalse();
    }

    [Fact]
    public void SatisfiedBy_Flt64_GE() {
        Comparison.GE.SatisfiedBy(new Flt64(3.0), new Flt64(2.0)).Should().BeTrue();
        Comparison.GE.SatisfiedBy(new Flt64(2.0), new Flt64(2.0)).Should().BeTrue();
        Comparison.GE.SatisfiedBy(new Flt64(1.0), new Flt64(2.0)).Should().BeFalse();
    }

    [Fact]
    public void SatisfiedBy_Flt64_GT() {
        Comparison.GT.SatisfiedBy(new Flt64(3.0), new Flt64(2.0)).Should().BeTrue();
        Comparison.GT.SatisfiedBy(new Flt64(2.0), new Flt64(2.0)).Should().BeFalse();
    }

    [Fact]
    public void SatisfiedBy_Flt64_NE() {
        Comparison.NE.SatisfiedBy(new Flt64(1.0), new Flt64(2.0)).Should().BeTrue();
        Comparison.NE.SatisfiedBy(new Flt64(2.0), new Flt64(2.0)).Should().BeFalse();
    }

    [Fact]
    public void LinearInequality_IsSatisfied_Satisfied() {
        // x + y <= 10, x=3, y=4 => 7 <= 10 => true
        var lhs = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, _x),
                new LinearMonomial<Flt64>(Flt64.One, _y),
            },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(
            System.Array.Empty<LinearMonomial<Flt64>>(),
            new Flt64(10.0));
        var ineq = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(3.0), [_y] = new(4.0) };
        ineq.IsSatisfied(values).Should().BeTrue();
    }

    [Fact]
    public void LinearInequality_IsSatisfied_NotSatisfied() {
        // x + y <= 5, x=3, y=4 => 7 <= 5 => false
        var lhs = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, _x),
                new LinearMonomial<Flt64>(Flt64.One, _y),
            },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(
            System.Array.Empty<LinearMonomial<Flt64>>(),
            new Flt64(5.0));
        var ineq = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(3.0), [_y] = new(4.0) };
        ineq.IsSatisfied(values).Should().BeFalse();
    }

    [Fact]
    public void LinearInequality_IsSatisfied_MissingValue_SkipsMonomial() {
        // When x is missing, Evaluate skips it (returns constant 0).
        // 0 <= 5 is true.
        var lhs = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, _x) },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(
            System.Array.Empty<LinearMonomial<Flt64>>(),
            new Flt64(5.0));
        var ineq = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

        var values = new Dictionary<ISymbol, Flt64>();
        ineq.IsSatisfied(values).Should().BeTrue();
    }

    [Fact]
    public void QuadraticInequality_IsSatisfied() {
        // x^2 <= 10, x=3 => 9 <= 10 => true
        var lhs = new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Quadratic(Flt64.One, _x, _x) },
            Flt64.Zero);
        var rhs = new QuadraticPolynomial<Flt64>(
            System.Array.Empty<QuadraticMonomial<Flt64>>(),
            new Flt64(10.0));
        var ineq = new QuadraticInequalityOf<Flt64>(lhs, rhs, Comparison.LE);

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(3.0) };
        ineq.IsSatisfied(values).Should().BeTrue();
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
