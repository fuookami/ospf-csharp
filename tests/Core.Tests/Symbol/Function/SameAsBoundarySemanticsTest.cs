#nullable enable

using System;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>相同状态比较端点契约 / Same-status comparison boundary contract.</summary>
public class SameAsBoundarySemanticsTest {
    [Theory]
    [InlineData(Comparison.LE, -1.0, 1)]
    [InlineData(Comparison.LE, 0.125, 1)]
    [InlineData(Comparison.LE, 0.1875, -1)]
    [InlineData(Comparison.LE, 0.25, 0)]
    [InlineData(Comparison.GE, 1.0, 1)]
    [InlineData(Comparison.GE, -0.125, 1)]
    [InlineData(Comparison.GE, -0.1875, -1)]
    [InlineData(Comparison.GE, -0.25, 0)]
    [InlineData(Comparison.EQ, 0.125, 1)]
    [InlineData(Comparison.EQ, -0.125, 1)]
    [InlineData(Comparison.EQ, 0.1875, -1)]
    [InlineData(Comparison.EQ, -0.1875, -1)]
    [InlineData(Comparison.EQ, 0.25, 0)]
    [InlineData(Comparison.EQ, -0.25, 0)]
    public void Evaluation_ShouldDistinguishSatisfiedGapAndViolatedBoundaries(
        Comparison comparison,
        double value,
        int expected) {
        var input = new RealVar("same-boundary-input");
        var zero = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) },
            Flt64.Zero
        );
        var function = new SameAsFunction(
            inequalities: new[] {
                zero.Le(Flt64.Zero),
                new LinearInequality<Flt64>(
                    Lhs: polynomial,
                    Rhs: zero,
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(0.125),
            constraint: false
        );

        Flt64? result = function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(value) });
        Assert.Equal(expected < 0 ? null : new Flt64(expected), result);
    }

    [Fact]
    public void Evaluation_ShouldHonorExplicitViolationBoundary() {
        var input = new RealVar("same-wider-gap-x");
        var zero = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) },
            Flt64.Zero
        );
        var function = new SameAsFunction(
            inequalities: new[] { zero.Le(Flt64.Zero), polynomial.Le(Flt64.Zero) },
            epsilon: new Flt64(0.125),
            constraint: false,
            strictBoundary: new Flt64(0.5)
        );

        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(0.25) }));
        Assert.Equal(Flt64.Zero, function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(0.5) }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void Evaluation_ShouldRejectNonFiniteOrUnrepresentableInputs(double inputValue) {
        var input = new RealVar("same-invalid-value-x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) },
            Flt64.Zero
        );
        var function = new SameAsFunction(new[] { polynomial.Le(Flt64.Zero) }, new Flt64(0.125));

        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(inputValue) }));
    }
}
