#nullable enable

using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Symbol;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Symbol;

public class SymbolDimensionTest {
    [Fact]
    public void DimensionedSymbol_CanAddTo_SameDimension() {
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var y = new DimensionedSymbol("y", null, Dimensions.Length, SIBaseUnits.Meter);
        Assert.True(x.CanAddTo(y));
    }

    [Fact]
    public void DimensionedSymbol_CanAddTo_DifferentDimension() {
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var t = new DimensionedSymbol("t", null, Dimensions.Time, SIBaseUnits.Second);
        Assert.False(x.CanAddTo(t));
    }

    [Fact]
    public void DimensionedSymbol_MultiplyWith() {
        var l = new DimensionedSymbol("L", null, Dimensions.Length, SIBaseUnits.Meter);
        var l2 = new DimensionedSymbol("L2", null, Dimensions.Length, SIBaseUnits.Meter);
        DerivedQuantity area = l.MultiplyWith(l2);
        Assert.Equal(Dimensions.Area, area);
    }

    [Fact]
    public void DimensionedSymbol_DivideBy() {
        var d = new DimensionedSymbol("d", null, Dimensions.Length, SIBaseUnits.Meter);
        var t = new DimensionedSymbol("t", null, Dimensions.Time, SIBaseUnits.Second);
        DerivedQuantity speed = d.DivideBy(t);
        Assert.Equal(Dimensions.Velocity, speed);
    }

    [Fact]
    public void SymbolDimensionRegistry_RegisterAndGet() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", "distance", Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(x);

        Assert.True(registry.IsRegistered(x));
        DimensionedSymbol? dim = registry.GetDimension(x);
        Assert.NotNull(dim);
        Assert.Equal(Dimensions.Length, dim!.Quantity);
    }

    [Fact]
    public void SymbolDimensionRegistry_Unregister() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(x);
        Assert.True(registry.Unregister(x));
        Assert.False(registry.IsRegistered(x));
    }

    [Fact]
    public void SymbolDimensionRegistry_Clear() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(x);
        registry.Clear();
        Assert.False(registry.IsRegistered(x));
    }

    [Fact]
    public void SymbolDimensionRegistry_ValidateAddSubDimension_Same() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var y = new DimensionedSymbol("y", null, Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(x);
        registry.Register(y);

        Result<Utils.Functional.Unit, ErrorCode, Error<ErrorCode>> result = registry.ValidateAddSubDimension(new[] { x, y });
        Assert.True(result.IsOk);
    }

    [Fact]
    public void SymbolDimensionRegistry_ValidateAddSubDimension_Mismatch() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var t = new DimensionedSymbol("t", null, Dimensions.Time, SIBaseUnits.Second);
        registry.Register(x);
        registry.Register(t);

        Result<Utils.Functional.Unit, ErrorCode, Error<ErrorCode>> result = registry.ValidateAddSubDimension(new[] { x, t });
        Assert.True(result.IsFailed);
    }

    [Fact]
    public void SymbolDimensionRegistry_InferDimension_Add() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var y = new DimensionedSymbol("y", null, Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(x);
        registry.Register(y);

        Result<DerivedQuantity, ErrorCode, Error<ErrorCode>> result = registry.InferDimension(x, y, Operation.Add);
        Assert.True(result.IsOk);
        Assert.Equal(Dimensions.Length, result.Value);
    }

    [Fact]
    public void SymbolDimensionRegistry_InferDimension_Multiply() {
        var registry = new SymbolDimensionRegistry();
        var l = new DimensionedSymbol("L", null, Dimensions.Length, SIBaseUnits.Meter);
        var w = new DimensionedSymbol("W", null, Dimensions.Length, SIBaseUnits.Meter);
        registry.Register(l);
        registry.Register(w);

        Result<DerivedQuantity, ErrorCode, Error<ErrorCode>> result = registry.InferDimension(l, w, Operation.Multiply);
        Assert.True(result.IsOk);
        // Length * Length has same fundamental quantities as Area
        Assert.Equal(Dimensions.Area.DimensionSymbol(), result.Value.DimensionSymbol());
    }

    [Fact]
    public void SymbolDimensionRegistry_InferDimension_Divide() {
        var registry = new SymbolDimensionRegistry();
        var d = new DimensionedSymbol("d", null, Dimensions.Length, SIBaseUnits.Meter);
        var t = new DimensionedSymbol("t", null, Dimensions.Time, SIBaseUnits.Second);
        registry.Register(d);
        registry.Register(t);

        Result<DerivedQuantity, ErrorCode, Error<ErrorCode>> result = registry.InferDimension(d, t, Operation.Divide);
        Assert.True(result.IsOk);
        Assert.Equal(Dimensions.Velocity, result.Value);
    }

    [Fact]
    public void SymbolDimensionRegistry_InferDimension_AddMismatch() {
        var registry = new SymbolDimensionRegistry();
        var x = new DimensionedSymbol("x", null, Dimensions.Length, SIBaseUnits.Meter);
        var t = new DimensionedSymbol("t", null, Dimensions.Time, SIBaseUnits.Second);
        registry.Register(x);
        registry.Register(t);

        Result<DerivedQuantity, ErrorCode, Error<ErrorCode>> result = registry.InferDimension(x, t, Operation.Add);
        Assert.True(result.IsFailed);
    }
}
