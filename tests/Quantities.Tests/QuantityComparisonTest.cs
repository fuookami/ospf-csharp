#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Quantities.Tests;

public class QuantityComparisonTest {
    // ---- AreEqual / AreNotEqual ----

    [Fact]
    public void TestAreEqualSameUnit() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Assert.True(a.AreEqual(b));
        Assert.False(a.AreNotEqual(b));
    }

    [Fact]
    public void TestAreNotEqualSameUnit() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
        Assert.False(a.AreEqual(b));
        Assert.True(a.AreNotEqual(b));
    }

    [Fact]
    public void TestAreEqualDifferentUnitsSameDimension() {
        // 1 km = 1000 m
        var km = new Quantity<Flt64>(new Flt64(1.0), LengthUnits.Kilometer);
        var m = new Quantity<Flt64>(new Flt64(1000.0), SIBaseUnits.Meter);
        Assert.True(km.AreEqual(m));
    }

    [Fact]
    public void TestAreNotEqualDifferentUnitsSameDimension() {
        var km = new Quantity<Flt64>(new Flt64(1.0), LengthUnits.Kilometer);
        var m = new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter);
        Assert.False(km.AreEqual(m));
    }

    [Fact]
    public void TestAreEqualDimensionMismatch() {
        var m = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var s = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Second);
        Assert.False(m.AreEqual(s));
    }

    // ---- Affine comparison (temperature) ----

    [Fact]
    public void TestAreEqualAffineCelsiusKelvin() {
        // 0 C = 273.15 K
        var celsius = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        var kelvin = new Quantity<Flt64>(new Flt64(273.15), TemperatureUnits.Kelvin);
        Assert.True(celsius.AreEqual(kelvin));
    }

    [Fact]
    public void TestAreEqualAffineFahrenheitCelsius() {
        // 32 F = 0 C
        var fahrenheit = new Quantity<Flt64>(new Flt64(32.0), TemperatureUnits.Fahrenheit);
        var celsius = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        Assert.True(fahrenheit.AreEqual(celsius));
    }

    [Fact]
    public void TestAreEqualAffineFahrenheitKelvin() {
        // 212 F = 373.15 K
        var fahrenheit = new Quantity<Flt64>(new Flt64(212.0), TemperatureUnits.Fahrenheit);
        var kelvin = new Quantity<Flt64>(new Flt64(373.15), TemperatureUnits.Kelvin);
        Assert.True(fahrenheit.AreEqual(kelvin));
    }

    // ---- PartialCompare ----

    [Fact]
    public void TestPartialCompareSameUnit() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
        Order? result = a.PartialCompare(b);
        Assert.IsType<Order.Less>(result);
    }

    [Fact]
    public void TestPartialCompareEqual() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Order? result = a.PartialCompare(b);
        Assert.IsType<Order.Equal>(result);
    }

    [Fact]
    public void TestPartialCompareGreater() {
        var a = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Order? result = a.PartialCompare(b);
        Assert.IsType<Order.Greater>(result);
    }

    [Fact]
    public void TestPartialCompareDimensionMismatch() {
        var m = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var s = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Second);
        Assert.Null(m.PartialCompare(s));
    }

    [Fact]
    public void TestPartialCompareDifferentUnitsSameDimension() {
        // 1 km > 500 m
        var km = new Quantity<Flt64>(new Flt64(1.0), LengthUnits.Kilometer);
        var m = new Quantity<Flt64>(new Flt64(500.0), SIBaseUnits.Meter);
        Order? result = km.PartialCompare(m);
        Assert.IsType<Order.Greater>(result);
    }

    // ---- LessThan / GreaterThan / LessThanOrEqual / GreaterThanOrEqual ----

    [Fact]
    public void TestLessThan() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
        Assert.True(a.LessThan(b));
        Assert.False(b.LessThan(a));
    }

    [Fact]
    public void TestGreaterThan() {
        var a = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Assert.True(a.GreaterThan(b));
        Assert.False(b.GreaterThan(a));
    }

    [Fact]
    public void TestLessThanOrEqual() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Assert.True(a.LessThanOrEqual(b));
    }

    [Fact]
    public void TestGreaterThanOrEqual() {
        var a = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var b = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Assert.True(a.GreaterThanOrEqual(b));
    }

    [Fact]
    public void TestLessThanDimensionMismatch() {
        var m = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        var s = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Second);
        Assert.Null(m.LessThan(s));
    }

    // ---- Affine ordering ----

    [Fact]
    public void TestAffineCelsiusLessThanKelvin() {
        // 0 C < 100 C = 373.15 K
        var celsius0 = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        var kelvin373 = new Quantity<Flt64>(new Flt64(373.15), TemperatureUnits.Kelvin);
        Assert.True(celsius0.LessThan(kelvin373));
    }

    [Fact]
    public void TestAffineFahrenheitGreaterThanCelsius() {
        // 212 F > 0 C
        var fahrenheit = new Quantity<Flt64>(new Flt64(212.0), TemperatureUnits.Fahrenheit);
        var celsius = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        Assert.True(fahrenheit.GreaterThan(celsius));
    }

    // ---- Int64 comparison ----

    [Fact]
    public void TestInt64Comparison() {
        var a = new Quantity<Int64>(new Int64(100), SIBaseUnits.Meter);
        var b = new Quantity<Int64>(new Int64(200), SIBaseUnits.Meter);
        Assert.True(a.LessThan(b));
        Assert.True(b.GreaterThan(a));
        Assert.True(a.AreEqual(a));
    }
}
