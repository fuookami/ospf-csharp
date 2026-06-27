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

public class CanonicalOpsTest {
    private readonly ISymbol _x = new TestSymbol("x");
    private readonly ISymbol _y = new TestSymbol("y");

    [Fact]
    public void CombineCanonicalTerms_MergesLikeTerms() {
        // 2*x^2 + 3*x^2 = 5*x^2
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(2.0), new Dictionary<ISymbol, int> { [_x] = 2 }),
                new CanonicalMonomial<Flt64>(new Flt64(3.0), new Dictionary<ISymbol, int> { [_x] = 2 }),
            },
            Flt64.Zero);

        CanonicalPolynomial<Flt64> result = p.CombineCanonicalTerms();
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void EvaluateCanonical_WithAllValues() {
        // 2*x^2*y + 5, x=2, y=3 => 2*4*3 + 5 = 29
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(2.0),
                    new Dictionary<ISymbol, int> { [_x] = 2, [_y] = 1 }),
            },
            new Flt64(5.0));

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
        Flt64? result = p.EvaluateCanonical(values);
        result.Should().Be(new Flt64(29.0));
    }

    [Fact]
    public void EvaluateCanonical_MissingValue_ReturnsConstant() {
        // When monomial symbols are missing, skipped; result is just the constant.
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(1.0),
                    new Dictionary<ISymbol, int> { [_x] = 2 }),
            },
            new Flt64(5.0));

        var values = new Dictionary<ISymbol, Flt64>();
        Flt64? result = p.EvaluateCanonical(values);
        result.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void EvaluateCanonicalOrdered_ReturnsOk() {
        // x^2 + y, order=[x,y], values=[3,10] => 9 + 10 = 19
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(1.0), new Dictionary<ISymbol, int> { [_x] = 2 }),
                new CanonicalMonomial<Flt64>(new Flt64(1.0), new Dictionary<ISymbol, int> { [_y] = 1 }),
            },
            Flt64.Zero);

        var result = p.EvaluateCanonicalOrdered(
            new[] { _x, _y },
            new[] { new Flt64(3.0), new Flt64(10.0) });
        result.IsOk.Should().BeTrue();
        result.Value.Should().Be(new Flt64(19.0));
    }

    [Fact]
    public void EvaluateCanonicalOrdered_DuplicateSymbols_ReturnsFailed() {
        var p = new CanonicalPolynomial<Flt64>(
            new[] { new CanonicalMonomial<Flt64>(new Flt64(1.0), new Dictionary<ISymbol, int> { [_x] = 1 }) },
            Flt64.Zero);

        var result = p.EvaluateCanonicalOrdered(
            new[] { _x, _x },
            new[] { new Flt64(1.0), new Flt64(2.0) });
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void PartialEvaluateCanonical_SubstitutesKnownValues() {
        // x^2*y + 3, fix x=2 => 4*y + 3
        var p = new CanonicalPolynomial<Flt64>(
            new[] {
                new CanonicalMonomial<Flt64>(new Flt64(1.0),
                    new Dictionary<ISymbol, int> { [_x] = 2, [_y] = 1 }),
            },
            new Flt64(3.0));

        var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0) };
        CanonicalPolynomial<Flt64> result = p.PartialEvaluateCanonical(values);
        result.Constant.Should().Be(new Flt64(3.0));
        result.Monomials.Should().HaveCount(1);
        result.Monomials[0].Coefficient.Should().Be(new Flt64(4.0));
    }

    [Fact]
    public void ComputeNonNegativeRingPower_ZeroPower_ReturnsOne() {
        Flt64 result = CanonicalOps.ComputeNonNegativeRingPower(new Flt64(5.0), 0, Flt64.One);
        result.Should().Be(Flt64.One);
    }

    [Fact]
    public void ComputeNonNegativeRingPower_OnePower_ReturnsValue() {
        Flt64 result = CanonicalOps.ComputeNonNegativeRingPower(new Flt64(5.0), 1, Flt64.One);
        result.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void ComputeNonNegativeRingPower_Square() {
        Flt64 result = CanonicalOps.ComputeNonNegativeRingPower(new Flt64(3.0), 2, Flt64.One);
        result.Should().Be(new Flt64(9.0));
    }

    [Fact]
    public void ComputeNonNegativeRingPower_Cube() {
        Flt64 result = CanonicalOps.ComputeNonNegativeRingPower(new Flt64(2.0), 3, Flt64.One);
        result.Should().Be(new Flt64(8.0));
    }

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
}
