#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Quantities.Tests;

public class QuantityValueExtensionsTest {
    // ---- ToFlt64 ----

    [Fact]
    public void TestToFlt64FromFlt64() {
        var q = new Quantity<Flt64>(new Flt64(42.5), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.ToFlt64();
        Assert.Equal(new Flt64(42.5), result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    [Fact]
    public void TestToFlt64FromInt64() {
        var q = new Quantity<Int64>(new Int64(42), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.ToFlt64();
        Assert.Equal(new Flt64(42.0), result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    // ---- ToFltXQuantity ----

    [Fact]
    public void TestToFltXFromFlt64() {
        var q = new Quantity<Flt64>(new Flt64(42.5), SIBaseUnits.Meter);
        Quantity<FltX> result = q.ToFltXQuantity();
        Assert.Equal(new FltX(42.5), result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    // ---- ToInt64 ----

    [Fact]
    public void TestToInt64FromFlt64() {
        var q = new Quantity<Flt64>(new Flt64(42.7), SIBaseUnits.Meter);
        Quantity<Int64> result = q.ToInt64();
        Assert.Equal(new Int64(42), result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    // ---- Floor / Ceil / Round ----

    [Fact]
    public void TestFloor() {
        var q = new Quantity<Flt64>(new Flt64(3.7), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.Floor();
        Assert.Equal(new Flt64(3.0), result.Value);
    }

    [Fact]
    public void TestCeil() {
        var q = new Quantity<Flt64>(new Flt64(3.2), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.Ceil();
        Assert.Equal(new Flt64(4.0), result.Value);
    }

    [Fact]
    public void TestRound() {
        var q = new Quantity<Flt64>(new Flt64(3.5), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.Round();
        Assert.Equal(new Flt64(4.0), result.Value);
    }

    [Fact]
    public void TestFloorNegative() {
        var q = new Quantity<Flt64>(new Flt64(-3.7), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.Floor();
        Assert.Equal(new Flt64(-4.0), result.Value);
    }

    [Fact]
    public void TestFloorFltX() {
        var q = new Quantity<FltX>(new FltX(3.7), SIBaseUnits.Meter);
        Quantity<FltX> result = q.Floor();
        Assert.Equal(new FltX(3), result.Value);
    }

    [Fact]
    public void TestCeilFltX() {
        var q = new Quantity<FltX>(new FltX(3.2), SIBaseUnits.Meter);
        Quantity<FltX> result = q.Ceil();
        Assert.Equal(new FltX(4), result.Value);
    }

    // ---- MapValue ----

    [Fact]
    public void TestMapValue() {
        var q = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Quantity<Flt64> result = q.MapValue(v => v * new Flt64(2.0));
        Assert.Equal(new Flt64(200.0), result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    [Fact]
    public void TestMapValueTypeChange() {
        var q = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Quantity<double> result = q.MapValue(v => v.ToDouble());
        Assert.Equal(100.0, result.Value);
        Assert.Equal(SIBaseUnits.Meter, result.Unit);
    }

    // ---- TryMapValue ----

    [Fact]
    public void TestTryMapValueSuccess() {
        var q = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
        Quantity<Int64>? result = q.TryMapValue<Flt64, Int64>(v => new Int64((long)v.ToDouble()));
        Assert.NotNull(result);
        Quantity<Int64> rq = result!;
        Assert.Equal(new Int64(100), rq.Value);
        Assert.Equal(SIBaseUnits.Meter, rq.Unit);
    }

    [Fact]
    public void TestTryMapValueReturnsNull() {
        var q = new Quantity<Flt64>(new Flt64(-1.0), SIBaseUnits.Meter);
        // TryMapValue for struct returns null when the function returns null
        Quantity<string>? result = q.TryMapValueRef<Flt64, string>(v => v.ToDouble() > 0 ? v.ToString() : null);
        Assert.Null(result);
    }
}
