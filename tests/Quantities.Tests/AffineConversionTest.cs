#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class AffineConversionTest {
    [Fact]
    public void TestCelsiusToKelvin() {
        // 0 C = 273.15 K
        var q = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = q.To(TemperatureUnits.Kelvin);
        Assert.True(result.IsOk);
        Flt64 kelvin = result.Value.Value;
        Assert.True(System.Math.Abs(kelvin.ToDouble() - 273.15) < 0.01);
    }

    [Fact]
    public void TestKelvinToCelsius() {
        // 273.15 K = 0 C
        var q = new Quantity<Flt64>(new Flt64(273.15), TemperatureUnits.Kelvin);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = q.To(TemperatureUnits.Celsius);
        Assert.True(result.IsOk);
        Flt64 celsius = result.Value.Value;
        Assert.True(System.Math.Abs(celsius.ToDouble()) < 0.01);
    }

    [Fact]
    public void TestCelsiusToFahrenheit() {
        // 0 C = 32 F
        var q = new Quantity<Flt64>(new Flt64(0.0), TemperatureUnits.Celsius);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = q.To(TemperatureUnits.Fahrenheit);
        Assert.True(result.IsOk);
        Flt64 fahrenheit = result.Value.Value;
        Assert.True(System.Math.Abs(fahrenheit.ToDouble() - 32.0) < 0.1);
    }

    [Fact]
    public void TestFahrenheitToCelsius() {
        // 32 F = 0 C
        var q = new Quantity<Flt64>(new Flt64(32.0), TemperatureUnits.Fahrenheit);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = q.To(TemperatureUnits.Celsius);
        Assert.True(result.IsOk);
        Flt64 celsius = result.Value.Value;
        Assert.True(System.Math.Abs(celsius.ToDouble()) < 0.1);
    }

    [Fact]
    public void TestFahrenheitToKelvin() {
        // 212 F = 373.15 K
        var q = new Quantity<Flt64>(new Flt64(212.0), TemperatureUnits.Fahrenheit);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = q.To(TemperatureUnits.Kelvin);
        Assert.True(result.IsOk);
        Flt64 kelvin = result.Value.Value;
        Assert.True(System.Math.Abs(kelvin.ToDouble() - 373.15) < 0.1);
    }
}
