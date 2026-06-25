#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests;

public class Flt64QuickDslTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");

    [Fact]
    public void LinearPolynomial_Empty() {
        LinearPolynomial<Flt64> p = Flt64QuickDsl.LinearPolynomial();
        p.Monomials.Should().BeEmpty();
        p.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public void LinearPolynomial_FromSymbol() {
        LinearPolynomial<Flt64> p = Flt64QuickDsl.LinearPolynomial(_x);
        p.Monomials.Should().HaveCount(1);
        p.Monomials[0].Coefficient.Should().Be(Flt64.One);
        p.Monomials[0].Symbol.Should().Be(_x);
    }

    [Fact]
    public void LinearPolynomial_FromConstant() {
        LinearPolynomial<Flt64> p = Flt64QuickDsl.LinearPolynomial(new Flt64(5.0));
        p.Monomials.Should().BeEmpty();
        p.Constant.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void QuadraticPolynomial_Empty() {
        QuadraticPolynomial<Flt64> p = Flt64QuickDsl.QuadraticPolynomial();
        p.Monomials.Should().BeEmpty();
        p.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public void Sum() {
        LinearPolynomial<Flt64> p = Flt64QuickDsl.Sum(new[] { _x, _y });
        p.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void SumVars() {
        (string, ISymbol)[] items = new[] { ("a", _x), ("b", _y) };
        LinearPolynomial<Flt64> p = Flt64QuickDsl.SumVars(items, item => item.Item2);
        p.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void Qsum() {
        QuadraticPolynomial<Flt64> p = Flt64QuickDsl.Qsum(new[] { _x, _y });
        p.Monomials.Should().HaveCount(2);
        foreach (QuadraticMonomial<Flt64> m in p.Monomials) {
            m.IsQuadratic.Should().BeFalse(); // linear terms in quadratic
        }
    }

    [Fact]
    public void Flt64QuickOps_Multiply_Flt64_Symbol() {
        LinearMonomial<Flt64> m = Flt64QuickOps.Multiply(new Flt64(3.0), _x);
        m.Coefficient.Should().Be(new Flt64(3.0));
        m.Symbol.Should().Be(_x);
    }

    [Fact]
    public void Flt64QuickOps_Multiply_LinearMonomial_Symbol() {
        var lm = new LinearMonomial<Flt64>(new Flt64(2.0), _x);
        QuadraticMonomial<Flt64> qm = Flt64QuickOps.Multiply(lm, _y);
        qm.IsQuadratic.Should().BeTrue();
        qm.Coefficient.Should().Be(new Flt64(2.0));
    }

    [Fact]
    public void Flt64QuickOps_Divide() {
        var lm = new LinearMonomial<Flt64>(new Flt64(6.0), _x);
        LinearMonomial<Flt64> result = Flt64QuickOps.Divide(lm, new Flt64(2.0));
        result.Coefficient.Should().Be(new Flt64(3.0));
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
