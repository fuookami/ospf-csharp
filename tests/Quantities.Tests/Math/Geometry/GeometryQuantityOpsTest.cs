#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryQuantityOpsTest
{
    private static Quantity<Flt64> Q(double v, PhysicalUnit? unit = null) =>
        new(new Flt64(v), unit ?? SIBaseUnits.Meter);

    [Fact]
    public void QuantityProduct_TwoQuantities()
    {
        var a = Q(3);
        var b = Q(4);
        var result = QuantityOps.QuantityProduct(a, b);
        Assert.Equal(new Flt64(12.0), result.Value);
    }

    [Fact]
    public void QuantityProduct_QuantityAndScalar()
    {
        var a = Q(3);
        var result = QuantityOps.QuantityProduct(a, new Flt64(4.0));
        Assert.Equal(new Flt64(12.0), result.Value);
    }

    [Fact]
    public void QuantityZeroOf()
    {
        var q = Q(42);
        var zero = QuantityOps.QuantityZeroOf(q);
        Assert.Equal(new Flt64(0), zero.Value);
        Assert.Equal(q.Unit, zero.Unit);
    }

    [Fact]
    public void PlusSafe_SameUnit()
    {
        var a = Q(3);
        var b = Q(4);
        var result = QuantityOps.PlusSafe(a, b);
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void MinusSafe_SameUnit()
    {
        var a = Q(10);
        var b = Q(3);
        var result = QuantityOps.MinusSafe(a, b);
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void OrdSafe_LessThan()
    {
        var a = Q(3);
        var b = Q(5);
        var result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Less>(result.Value);
    }

    [Fact]
    public void OrdSafe_Equal()
    {
        var a = Q(5);
        var b = Q(5);
        var result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Equal>(result.Value);
    }

    [Fact]
    public void OrdSafe_GreaterThan()
    {
        var a = Q(10);
        var b = Q(5);
        var result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Greater>(result.Value);
    }

    [Fact]
    public void MaxSafe()
    {
        var a = Q(3);
        var b = Q(7);
        var result = QuantityOps.MaxSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void MinSafe()
    {
        var a = Q(3);
        var b = Q(7);
        var result = QuantityOps.MinSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(3.0), result.Value.Value);
    }

    [Fact]
    public void ClampSafe()
    {
        var value = Q(15);
        var lb = Q(0);
        var ub = Q(10);
        var result = QuantityOps.ClampSafe(value, lb, ub, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(10.0), result.Value.Value);
    }

    [Fact]
    public void ContainsInRangeSafe_Inside()
    {
        var result = QuantityOps.ContainsInRangeSafe(Q(5), Q(0), Q(10), true, true, "x");
        Assert.True(result.IsOk);
        Assert.True(result.Value);
    }

    [Fact]
    public void ContainsInRangeSafe_Outside()
    {
        var result = QuantityOps.ContainsInRangeSafe(Q(15), Q(0), Q(10), true, true, "x");
        Assert.True(result.IsOk);
        Assert.False(result.Value);
    }
}
