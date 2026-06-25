#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryQuantityOpsTest {
    private static Quantity<Flt64> Q(double v, PhysicalUnit? unit = null) =>
        new(new Flt64(v), unit ?? SIBaseUnits.Meter);

    [Fact]
    public void QuantityProduct_TwoQuantities() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> b = Q(4);
        Quantity<Flt64> result = QuantityOps.QuantityProduct(a, b);
        Assert.Equal(new Flt64(12.0), result.Value);
    }

    [Fact]
    public void QuantityProduct_QuantityAndScalar() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> result = QuantityOps.QuantityProduct(a, new Flt64(4.0));
        Assert.Equal(new Flt64(12.0), result.Value);
    }

    [Fact]
    public void QuantityZeroOf() {
        Quantity<Flt64> q = Q(42);
        Quantity<Flt64> zero = QuantityOps.QuantityZeroOf(q);
        Assert.Equal(new Flt64(0), zero.Value);
        Assert.Equal(q.Unit, zero.Unit);
    }

    [Fact]
    public void PlusSafe_SameUnit() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> b = Q(4);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = QuantityOps.PlusSafe(a, b);
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void MinusSafe_SameUnit() {
        Quantity<Flt64> a = Q(10);
        Quantity<Flt64> b = Q(3);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = QuantityOps.MinusSafe(a, b);
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void OrdSafe_LessThan() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> b = Q(5);
        Result<Order, ErrorCode, Error<ErrorCode>> result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Less>(result.Value);
    }

    [Fact]
    public void OrdSafe_Equal() {
        Quantity<Flt64> a = Q(5);
        Quantity<Flt64> b = Q(5);
        Result<Order, ErrorCode, Error<ErrorCode>> result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Equal>(result.Value);
    }

    [Fact]
    public void OrdSafe_GreaterThan() {
        Quantity<Flt64> a = Q(10);
        Quantity<Flt64> b = Q(5);
        Result<Order, ErrorCode, Error<ErrorCode>> result = QuantityOps.OrdSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.IsType<Fuookami.Ospf.Utils.Functional.Order.Greater>(result.Value);
    }

    [Fact]
    public void MaxSafe() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> b = Q(7);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = QuantityOps.MaxSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(7.0), result.Value.Value);
    }

    [Fact]
    public void MinSafe() {
        Quantity<Flt64> a = Q(3);
        Quantity<Flt64> b = Q(7);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = QuantityOps.MinSafe(a, b, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(3.0), result.Value.Value);
    }

    [Fact]
    public void ClampSafe() {
        Quantity<Flt64> value = Q(15);
        Quantity<Flt64> lb = Q(0);
        Quantity<Flt64> ub = Q(10);
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> result = QuantityOps.ClampSafe(value, lb, ub, "x");
        Assert.True(result.IsOk);
        Assert.Equal(new Flt64(10.0), result.Value.Value);
    }

    [Fact]
    public void ContainsInRangeSafe_Inside() {
        Result<bool, ErrorCode, Error<ErrorCode>> result = QuantityOps.ContainsInRangeSafe(Q(5), Q(0), Q(10), true, true, "x");
        Assert.True(result.IsOk);
        Assert.True(result.Value);
    }

    [Fact]
    public void ContainsInRangeSafe_Outside() {
        Result<bool, ErrorCode, Error<ErrorCode>> result = QuantityOps.ContainsInRangeSafe(Q(15), Q(0), Q(10), true, true, "x");
        Assert.True(result.IsOk);
        Assert.False(result.Value);
    }
}
