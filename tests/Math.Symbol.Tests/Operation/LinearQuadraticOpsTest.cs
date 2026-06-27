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

public class LinearQuadraticOpsTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");
    private readonly ISymbol _z = new TestSymbol("z");

    [Fact]
    public void CombineLinearTerms_MergesLikeTerms() {
        // 2x + 3x + y = 5x + y
        var p = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), _x),
                new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                new LinearMonomial<Flt64>(new Flt64(1.0), _y),
            },
            Flt64.Zero);

        LinearPolynomial<Flt64> result = p.CombineLinearTerms();
        result.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void CombineLinearTerms_RemovesZeroCoefficients() {
        // 2x - 2x + y = y
        var p = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), _x),
                new LinearMonomial<Flt64>(new Flt64(-2.0), _x),
                new LinearMonomial<Flt64>(new Flt64(1.0), _y),
            },
            Flt64.Zero);

        LinearPolynomial<Flt64> result = p.CombineLinearTerms();
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Symbol.Should().Be(_y);
    }

    [Fact]
    public void EvaluateLinear_WithAllValues() {
        // 3x + 2y + 1, x=2, y=3 => 6 + 6 + 1 = 13
        var p = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                new LinearMonomial<Flt64>(new Flt64(2.0), _y),
            },
            new Flt64(1.0));

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
        Flt64? result = p.EvaluateLinear(values);
        result.Should().Be(new Flt64(13.0));
    }

    [Fact]
    public void EvaluateLinear_MissingValue_ReturnsNull() {
        var p = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(3.0), _x) },
            Flt64.Zero);

        var values = new Dictionary<ISymbol, Flt64>();
        Flt64? result = p.EvaluateLinear(values);
        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateLinearOrdered_ReturnsOk() {
        // 2x + 3y, order=[x,y], values=[5,7] => 10 + 21 = 31
        var p = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), _x),
                new LinearMonomial<Flt64>(new Flt64(3.0), _y),
            },
            Flt64.Zero);

        var result = p.EvaluateLinearOrdered(
            new[] { _x, _y },
            new[] { new Flt64(5.0), new Flt64(7.0) });
        result.IsOk.Should().BeTrue();
        result.Value.Should().Be(new Flt64(31.0));
    }

    [Fact]
    public void PartialEvaluateLinear_SubstitutesKnownValues() {
        // 2x + 3y + 5, fix x=10 => 3y + 25
        var p = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(2.0), _x),
                new LinearMonomial<Flt64>(new Flt64(3.0), _y),
            },
            new Flt64(5.0));

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(10.0) };
        LinearPolynomial<Flt64> result = p.PartialEvaluateLinear(values);
        result.Constant.Should().Be(new Flt64(25.0));
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Symbol.Should().Be(_y);
    }

    [Fact]
    public void CombineQuadraticTerms_MergesLikeTerms() {
        // 2xy + 3xy = 5xy
        var p = new QuadraticPolynomial<Flt64>(
            new[] {
                QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _y),
                QuadraticMonomial<Flt64>.Quadratic(new Flt64(3.0), _x, _y),
            },
            Flt64.Zero);

        QuadraticPolynomial<Flt64> result = p.CombineQuadraticTerms();
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void EvaluateQuadratic_WithAllValues() {
        // x^2 + 2xy + 3, x=2, y=3 => 4 + 12 + 3 = 19
        var p = new QuadraticPolynomial<Flt64>(
            new[] {
                QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _x),
                QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _y),
            },
            new Flt64(3.0));

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
        Flt64? result = p.EvaluateQuadratic(values);
        result.Should().Be(new Flt64(19.0));
    }

    [Fact]
    public void EvaluateQuadraticOrdered_ReturnsOk() {
        // xy + 5, order=[x,y], values=[3,4] => 12 + 5 = 17
        var p = new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _y) },
            new Flt64(5.0));

        var result = p.EvaluateQuadraticOrdered(
            new[] { _x, _y },
            new[] { new Flt64(3.0), new Flt64(4.0) });
        result.IsOk.Should().BeTrue();
        result.Value.Should().Be(new Flt64(17.0));
    }

    [Fact]
    public void PartialEvaluateQuadratic_SubstitutesKnownValues() {
        // xy + 2x + 3, fix y=5 => 5x + 2x + 3 = 7x + 3
        var p = new QuadraticPolynomial<Flt64>(
            new[] {
                QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _y),
                QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), _x),
            },
            new Flt64(3.0));

        var values = new Dictionary<ISymbol, Flt64> { [_y] = new(5.0) };
        QuadraticPolynomial<Flt64> result = p.PartialEvaluateQuadratic(values);
        // After substituting y=5: 5x + 2x + 3 = 7x + 3 (after combine)
        result.Constant.Should().Be(new Flt64(3.0));
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(7.0));
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
