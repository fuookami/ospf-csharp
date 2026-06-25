#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class UnitTest {
    [Fact]
    public void TestSIBaseUnits() {
        Assert.Equal("meter", SIBaseUnits.Meter.Name);
        Assert.Equal("m", SIBaseUnits.Meter.Symbol);
        Assert.Equal("second", SIBaseUnits.Second.Name);
        Assert.Equal("s", SIBaseUnits.Second.Symbol);
        Assert.Equal("kilogram", SIBaseUnits.Kilogram.Name);
        Assert.Equal("kg", SIBaseUnits.Kilogram.Symbol);
    }

    [Fact]
    public void TestUnitDimension() {
        Assert.True(SIBaseUnits.Meter.Quantity.Equals(Dimensions.Length));
        Assert.True(SIBaseUnits.Second.Quantity.Equals(Dimensions.Time));
        Assert.True(SIBaseUnits.Kilogram.Quantity.Equals(Dimensions.Mass));
    }

    [Fact]
    public void TestLinearConversion() {
        // 1 km = 1000 m
        Scale? kmToM = LengthUnits.Kilometer.To(SIBaseUnits.Meter);
        Assert.NotNull(kmToM);
    }

    [Fact]
    public void TestUnitMultiply() {
        PhysicalUnit mPerS = SIBaseUnits.Meter.Divide(SIBaseUnits.Second);
        Assert.NotNull(mPerS);
    }

    [Fact]
    public void TestUnitPow() {
        PhysicalUnit m2 = SIBaseUnits.Meter.Pow(2);
        Assert.NotNull(m2);
    }

    [Fact]
    public void TestDimensionMismatch() {
        Scale? result = SIBaseUnits.Meter.To(SIBaseUnits.Second);
        Assert.Null(result);
    }

    [Fact]
    public void TestNoneUnit() {
        Assert.Null(NoneUnit.Instance.Name);
        Assert.Null(NoneUnit.Instance.Symbol);
        Assert.True(NoneUnit.Instance.Quantity.IsNone());
    }
}
