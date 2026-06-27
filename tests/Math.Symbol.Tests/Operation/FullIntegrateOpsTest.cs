#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests;

public class FullIntegrateOpsTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");

    [Fact]
    public void IntegrateLinear_BasicPolynomial() {
        // integral(2x + 3) dx = x^2 + 3x + C
        var p = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(2.0), _x) },
            new Flt64(3.0));

        QuadraticPolynomial<Flt64> result = p.IntegrateLinear(_x, Flt64.Zero);
        // Should have x^2 term (coefficient 1) and 3x term
        result.Monomials.Should().HaveCount(2);
        result.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public void IntegrateLinear_CrossVariable() {
        // integral(2y) dx = 2yx + C
        var p = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(2.0), _y) },
            Flt64.Zero);

        QuadraticPolynomial<Flt64> result = p.IntegrateLinear(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].IsQuadratic.Should().BeTrue();
    }

    [Fact]
    public void IntegrateQuadraticMonomial_xSquared() {
        // integral(3x^2) dx = x^3 + C
        var m = QuadraticMonomial<Flt64>.Quadratic(new Flt64(3.0), _x, _x);
        CanonicalPolynomial<Flt64> result = m.IntegrateQuadraticMonomial(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Degree.Should().Be(3);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(1.0));
    }

    [Fact]
    public void IntegrateQuadraticMonomial_xy() {
        // integral(2xy) dx = yx^2 + C
        var m = QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _y);
        CanonicalPolynomial<Flt64> result = m.IntegrateQuadraticMonomial(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        // {x:2, y:1} => total degree 3
        result.Monomials[0].Degree.Should().Be(3);
    }

    [Fact]
    public void IntegrateCanonicalMonomial_xCubed() {
        // integral(x^3) dx = x^4/4 + C
        var m = new CanonicalMonomial<Flt64>(Flt64.One, new Dictionary<ISymbol, int> { [_x] = 3 });
        CanonicalPolynomial<Flt64> result = m.IntegrateCanonicalMonomial(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Degree.Should().Be(4);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(0.25));
    }

    [Fact]
    public void IntegrateCanonicalMonomial_xSquared() {
        // integral(x^2) dx = x^3/3 + C
        var m = new CanonicalMonomial<Flt64>(Flt64.One, new Dictionary<ISymbol, int> { [_x] = 2 });
        CanonicalPolynomial<Flt64> result = m.IntegrateCanonicalMonomial(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Degree.Should().Be(3);
        // coefficient should be 1/3: verify by multiplying by 3
        Flt64 coeffTimes3 = result.Monomials[0].Coefficient.Times(new Flt64(3.0));
        coeffTimes3.Eq(Flt64.One).Should().BeTrue();
    }

    [Fact]
    public void IntegrateCanonicalMonomial_Constant() {
        // integral(5) dx = 5x + C
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int>());
        CanonicalPolynomial<Flt64> result = m.IntegrateCanonicalMonomial(_x, Flt64.Zero);
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Degree.Should().Be(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void IntegrateCanonical_Polynomial() {
        // integral(2x^2 + 3x + 1) dx = 2x^3/3 + 3x^2/2 + x + C
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(2.0), new Dictionary<ISymbol, int> { [_x] = 2 }),
                new CanonicalMonomial<Flt64>(new Flt64(3.0), new Dictionary<ISymbol, int> { [_x] = 1 }),
            },
            new Flt64(1.0));

        CanonicalPolynomial<Flt64> result = p.IntegrateCanonical(_x, Flt64.Zero);
        // Should have x^3, x^2, x terms
        result.Monomials.Should().HaveCount(3);
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
