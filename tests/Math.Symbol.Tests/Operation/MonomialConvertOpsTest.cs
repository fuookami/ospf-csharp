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

public class MonomialConvertOpsTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");

    [Fact]
    public void LinearMonomial_ToQuadraticMonomial() {
        var m = new LinearMonomial<Flt64>(new Flt64(3.0), _x);
        QuadraticMonomial<Flt64> q = m.ToQuadraticMonomialFromLinear();
        q.Coefficient.Should().Be(new Flt64(3.0));
        q.Symbol1.Should().Be(_x);
        q.Symbol2.Should().BeNull();
        q.IsQuadratic.Should().BeFalse();
    }

    [Fact]
    public void LinearMonomial_ToCanonicalMonomial() {
        var m = new LinearMonomial<Flt64>(new Flt64(3.0), _x);
        CanonicalMonomial<Flt64> c = m.ToCanonicalMonomialFromLinear();
        c.Coefficient.Should().Be(new Flt64(3.0));
        c.Powers.Should().ContainKey(_x);
        c.Powers[_x].Should().Be(1);
    }

    [Fact]
    public void QuadraticMonomial_ToLinearMonomialOrNull_Linear() {
        var m = QuadraticMonomial<Flt64>.Linear(new Flt64(3.0), _x);
        LinearMonomial<Flt64>? result = m.ToLinearMonomialOrNull();
        result.Should().NotBeNull();
        result!.Coefficient.Should().Be(new Flt64(3.0));
        result.Symbol.Should().Be(_x);
    }

    [Fact]
    public void QuadraticMonomial_ToLinearMonomialOrNull_Quadratic_ReturnsNull() {
        var m = QuadraticMonomial<Flt64>.Quadratic(new Flt64(3.0), _x, _y);
        LinearMonomial<Flt64>? result = m.ToLinearMonomialOrNull();
        result.Should().BeNull();
    }

    [Fact]
    public void CanonicalMonomial_ToLinearMonomialOrNull_Degree1() {
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int> { [_x] = 1 });
        LinearMonomial<Flt64>? result = m.ToLinearMonomialOrNull();
        result.Should().NotBeNull();
        result!.Coefficient.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void CanonicalMonomial_ToLinearMonomialOrNull_Degree2_ReturnsNull() {
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int> { [_x] = 2 });
        LinearMonomial<Flt64>? result = m.ToLinearMonomialOrNull();
        result.Should().BeNull();
    }

    [Fact]
    public void CanonicalMonomial_ToQuadraticMonomialOrNull_Degree2_Square() {
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int> { [_x] = 2 });
        QuadraticMonomial<Flt64>? result = m.ToQuadraticMonomialOrNull();
        result.Should().NotBeNull();
        result!.IsQuadratic.Should().BeTrue();
        result.Symbol1.Should().Be(_x);
        result.Symbol2.Should().Be(_x);
    }

    [Fact]
    public void CanonicalMonomial_ToQuadraticMonomialOrNull_Degree2_Cross() {
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int> { [_x] = 1, [_y] = 1 });
        QuadraticMonomial<Flt64>? result = m.ToQuadraticMonomialOrNull();
        result.Should().NotBeNull();
        result!.IsQuadratic.Should().BeTrue();
    }

    [Fact]
    public void CanonicalMonomial_ToQuadraticMonomialOrNull_Degree3_ReturnsNull() {
        var m = new CanonicalMonomial<Flt64>(new Flt64(5.0), new Dictionary<ISymbol, int> { [_x] = 3 });
        QuadraticMonomial<Flt64>? result = m.ToQuadraticMonomialOrNull();
        result.Should().BeNull();
    }

    [Fact]
    public void SubtractLinear_WorksCorrectly() {
        // (3x + 5) - (x + 2) = 2x + 3
        var a = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(3.0), _x) },
            new Flt64(5.0));
        var b = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(1.0), _x) },
            new Flt64(2.0));

        LinearPolynomial<Flt64> result = a.SubtractLinear(b);
        result.Constant.Should().Be(new Flt64(3.0));
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(2.0));
    }

    [Fact]
    public void SubtractCanonical_WorksCorrectly() {
        // (2x^2 + 5) - (x^2 + 3) = x^2 + 2
        var a = new CanonicalPolynomial<Flt64>(
            new[] { new CanonicalMonomial<Flt64>(new Flt64(2.0), new Dictionary<ISymbol, int> { [_x] = 2 }) },
            new Flt64(5.0));
        var b = new CanonicalPolynomial<Flt64>(
            new[] { new CanonicalMonomial<Flt64>(new Flt64(1.0), new Dictionary<ISymbol, int> { [_x] = 2 }) },
            new Flt64(3.0));

        CanonicalPolynomial<Flt64> result = a.SubtractCanonical(b);
        result.Constant.Should().Be(new Flt64(2.0));
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(1.0));
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
