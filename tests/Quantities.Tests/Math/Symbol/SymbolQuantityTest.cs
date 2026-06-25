#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Symbol;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Symbol;

public class SymbolQuantityTest
{
    private static ISymbol Sym(string name) => new TestSymbol(name);

    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;

    [Fact]
    public void LinearPolynomialQuantity_PlusSafe()
    {
        var x = Sym("x");
        var y = Sym("y");
        var a = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);
        var b = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2.0), y) },
                new Flt64(0)),
            SIBaseUnits.Meter);

        var result = a.PlusSafe(b);
        Assert.True(result.IsOk);
    }

    [Fact]
    public void LinearPolynomialQuantity_MinusSafe()
    {
        var x = Sym("x");
        var a = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(5.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);
        var b = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);

        var result = a.MinusSafe(b);
        Assert.True(result.IsOk);
    }

    [Fact]
    public void LinearPolynomialQuantity_PlusSafe_DimensionMismatch()
    {
        var x = Sym("x");
        var a = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);
        var b = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2.0), x) },
                new Flt64(0)),
            SIBaseUnits.Second);

        var result = a.PlusSafe(b);
        Assert.True(result.IsFailed);
    }

    [Fact]
    public void LinearPolynomialQuantity_Times()
    {
        var x = Sym("x");
        var q = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(3.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);

        var result = q.Times(new Flt64(2.0));
        Assert.Equal(new Flt64(6.0), result.Value.Monomials[0].Coefficient);
    }

    [Fact]
    public void LinearPolynomialQuantity_Evaluate()
    {
        var x = Sym("x");
        var q = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(100.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);

        var values = new System.Collections.Generic.Dictionary<ISymbol, Flt64> { [x] = new Flt64(5.0) };
        var result = q.Evaluate(values);
        Assert.NotNull(result);
        Assert.Equal(new Flt64(500.0), result!.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }
}
