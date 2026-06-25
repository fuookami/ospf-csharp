#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Symbol;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Symbol;

public class SymbolQuantityIntegrationTest
{
    private static ISymbol Sym(string name) => new TestSymbol(name);
    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;

    [Fact]
    public void DimensionedSymbol_WithPolynomialQuantity()
    {
        var x = new DimensionedSymbol("x", "distance", Dimensions.Length, SIBaseUnits.Meter);
        var poly = new Quantity<LinearPolynomial<Flt64>>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1.0), x) },
                new Flt64(0)),
            SIBaseUnits.Meter);

        Assert.Equal(SIBaseUnits.Meter, poly.Unit);
        Assert.Equal(Dimensions.Length, poly.Unit.Quantity);
    }

    [Fact]
    public void Registry_InferDimension_ForPolynomialOps()
    {
        var registry = new SymbolDimensionRegistry();
        var distance = new DimensionedSymbol("d", "distance", Dimensions.Length, SIBaseUnits.Meter);
        var time = new DimensionedSymbol("t", "time", Dimensions.Time, SIBaseUnits.Second);
        registry.Register(distance);
        registry.Register(time);

        // Speed = distance / time
        var speedDim = registry.InferDimension(distance, time, Operation.Divide);
        Assert.True(speedDim.IsOk);
        Assert.Equal(Dimensions.Velocity, speedDim.Value);
    }

    [Fact]
    public void DimensionedSymbol_MultiplyWith_ProducesCorrectDimension()
    {
        var force = new DimensionedSymbol("F", "force", Dimensions.Force, null);
        var length = new DimensionedSymbol("L", "length", Dimensions.Length, SIBaseUnits.Meter);

        var energyDim = force.MultiplyWith(length);
        Assert.Equal(Dimensions.Energy, energyDim);
    }

    [Fact]
    public void SymbolDimensionRegistry_MultipleRegistrations()
    {
        var registry = new SymbolDimensionRegistry();
        var symbols = new DimensionedSymbol[20];
        for (int i = 0; i < 20; i++)
        {
            symbols[i] = new DimensionedSymbol($"x{i}", null, Dimensions.Length, SIBaseUnits.Meter);
            registry.Register(symbols[i]);
        }

        // Verify all registered
        foreach (var s in symbols)
        {
            Assert.True(registry.IsRegistered(s));
            Assert.NotNull(registry.GetDimension(s));
        }

        // Verify can unregister
        foreach (var s in symbols)
        {
            Assert.True(registry.Unregister(s));
        }

        Assert.False(registry.IsRegistered(symbols[0]));
    }
}
