#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class ValueRangeQuantityExtensionsTest {
    [Fact]
    public void TestLowerBoundQuantity() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(
            new Flt64(1.0), new Flt64(5.0));
        Assert.True(range.IsOk);

        var q = new Quantity<ValueRange<Flt64>>(range.Value, SIBaseUnits.Meter);
        Quantity<Flt64> lower = q.LowerBoundQuantity();
        Assert.Equal(new Flt64(1.0), lower.Value);
        Assert.Equal(SIBaseUnits.Meter, lower.Unit);
    }

    [Fact]
    public void TestUpperBoundQuantity() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(
            new Flt64(1.0), new Flt64(5.0));
        Assert.True(range.IsOk);

        var q = new Quantity<ValueRange<Flt64>>(range.Value, SIBaseUnits.Meter);
        Quantity<Flt64> upper = q.UpperBoundQuantity();
        Assert.Equal(new Flt64(5.0), upper.Value);
        Assert.Equal(SIBaseUnits.Meter, upper.Unit);
    }

    [Fact]
    public void TestDiffOrNull() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(
            new Flt64(1.0), new Flt64(5.0));
        Assert.True(range.IsOk);

        var q = new Quantity<ValueRange<Flt64>>(range.Value, SIBaseUnits.Meter);
        Quantity<ValueWrapper<Flt64>>? diff = q.DiffOrNull();
        Assert.NotNull(diff);
        Quantity<ValueWrapper<Flt64>> diffQ = diff!;
        Assert.Equal(SIBaseUnits.Meter, diffQ.Unit);
    }

    [Fact]
    public void TestDiff() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(
            new Flt64(1.0), new Flt64(5.0));
        Assert.True(range.IsOk);

        var q = new Quantity<ValueRange<Flt64>>(range.Value, SIBaseUnits.Meter);
        Result<Quantity<ValueWrapper<Flt64>>, ErrorCode, Error<ErrorCode>> diff = q.Diff();
        Assert.True(diff.IsOk);
        Assert.Equal(SIBaseUnits.Meter, diff.Value.Unit);
    }

    [Fact]
    public void TestBoundValue() {
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> wrapper = ValueWrapper<Flt64>.Of(new Flt64(42.0));
        Assert.True(wrapper.IsOk);

        var bound = new Bound<Flt64>(wrapper.Value, new Interval.Closed());
        var q = new Quantity<Bound<Flt64>>(bound, SIBaseUnits.Meter);
        Quantity<Flt64> value = q.BoundValue();
        Assert.Equal(new Flt64(42.0), value.Value);
        Assert.Equal(SIBaseUnits.Meter, value.Unit);
    }

    [Fact]
    public void TestUnwrap() {
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> wrapper = ValueWrapper<Flt64>.Of(new Flt64(42.0));
        Assert.True(wrapper.IsOk);

        var q = new Quantity<ValueWrapper<Flt64>>(wrapper.Value, SIBaseUnits.Meter);
        Quantity<Flt64> unwrapped = q.Unwrap();
        Assert.Equal(new Flt64(42.0), unwrapped.Value);
        Assert.Equal(SIBaseUnits.Meter, unwrapped.Unit);
    }

    [Fact]
    public void TestUnwrapOrNullWithValue() {
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> wrapper = ValueWrapper<Flt64>.Of(new Flt64(42.0));
        Assert.True(wrapper.IsOk);

        var q = new Quantity<ValueWrapper<Flt64>>(wrapper.Value, SIBaseUnits.Meter);
        Quantity<Flt64>? unwrapped = q.UnwrapOrNull();
        Assert.NotNull(unwrapped);
        Quantity<Flt64> uq = unwrapped!;
        Assert.Equal(new Flt64(42.0), uq.Value);
        Assert.Equal(SIBaseUnits.Meter, uq.Unit);
    }
}
