#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.ValueRange;

public class ValueWrapperTest {
    [Fact]
    public void Value_CreatesNormalWrapper() {
        var v = new ValueWrapper<Flt64>.Value(new Flt64(5.0));
        Assert.False(v.IsInfinity);
        Assert.False(v.IsNegativeInfinity);
        Assert.False(v.IsInfinityOrNegativeInfinity);
        Assert.Equal(new Flt64(5.0), v.Unwrap());
    }

    [Fact]
    public void Infinity_IsPositiveInfinity() {
        var inf = new ValueWrapper<Flt64>.Infinity();
        Assert.True(inf.IsInfinity);
        Assert.False(inf.IsNegativeInfinity);
        Assert.True(inf.IsInfinityOrNegativeInfinity);
    }

    [Fact]
    public void NegativeInfinity_IsNegativeInfinity() {
        var negInf = new ValueWrapper<Flt64>.NegativeInfinity();
        Assert.False(negInf.IsInfinity);
        Assert.True(negInf.IsNegativeInfinity);
        Assert.True(negInf.IsInfinityOrNegativeInfinity);
    }

    [Fact]
    public void Of_PositiveInfinity_ReturnsInfinity() {
        INumericConstants<Flt64> c = NumericConstantsRegistry.For<Flt64>();
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueWrapper<Flt64>.Of(c.PositiveInfinity!.Value);
        Assert.True(result.IsOk);
        Assert.IsType<ValueWrapper<Flt64>.Infinity>(result.Value);
    }

    [Fact]
    public void Of_NegativeInfinity_ReturnsNegativeInfinity() {
        INumericConstants<Flt64> c = NumericConstantsRegistry.For<Flt64>();
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueWrapper<Flt64>.Of(c.NegativeInfinity!.Value);
        Assert.True(result.IsOk);
        Assert.IsType<ValueWrapper<Flt64>.NegativeInfinity>(result.Value);
    }

    [Fact]
    public void Of_NaN_ReturnsFailed() {
        INumericConstants<Flt64> c = NumericConstantsRegistry.For<Flt64>();
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueWrapper<Flt64>.Of(c.NaN!.Value);
        Assert.True(result.IsFailed);
    }

    [Fact]
    public void Of_NormalValue_ReturnsValue() {
        Result<ValueWrapper<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueWrapper<Flt64>.Of(new Flt64(42.0));
        Assert.True(result.IsOk);
        Assert.IsType<ValueWrapper<Flt64>.Value>(result.Value);
        Assert.Equal(new Flt64(42.0), result.Value.Unwrap());
    }

    [Fact]
    public void Plus_ValueAndValue_ReturnsSum() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(3.0));
        var b = new ValueWrapper<Flt64>.Value(new Flt64(4.0));
        ValueWrapper<Flt64>? result = a.Plus(b);
        Assert.NotNull(result);
        Assert.IsType<ValueWrapper<Flt64>.Value>(result);
        Assert.Equal(new Flt64(7.0), result!.Unwrap());
    }

    [Fact]
    public void Plus_ValueAndInfinity_ReturnsInfinity() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(3.0));
        var b = new ValueWrapper<Flt64>.Infinity();
        ValueWrapper<Flt64>? result = a.Plus(b);
        Assert.IsType<ValueWrapper<Flt64>.Infinity>(result);
    }

    [Fact]
    public void Plus_InfinityAndNegativeInfinity_ReturnsNull() {
        var a = new ValueWrapper<Flt64>.Infinity();
        var b = new ValueWrapper<Flt64>.NegativeInfinity();
        ValueWrapper<Flt64>? result = a.Plus(b);
        Assert.Null(result);
    }

    [Fact]
    public void Minus_ValueAndValue_ReturnsDifference() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(10.0));
        var b = new ValueWrapper<Flt64>.Value(new Flt64(3.0));
        ValueWrapper<Flt64>? result = a.Minus(b);
        Assert.NotNull(result);
        Assert.Equal(new Flt64(7.0), result!.Unwrap());
    }

    [Fact]
    public void Times_ValueAndValue_ReturnsProduct() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(3.0));
        var b = new ValueWrapper<Flt64>.Value(new Flt64(4.0));
        ValueWrapper<Flt64>? result = a.Times(b);
        Assert.NotNull(result);
        Assert.Equal(new Flt64(12.0), result!.Unwrap());
    }

    [Fact]
    public void Times_ValueAndInfinity_SignDependent() {
        var pos = new ValueWrapper<Flt64>.Value(new Flt64(2.0));
        var neg = new ValueWrapper<Flt64>.Value(new Flt64(-3.0));
        var zero = new ValueWrapper<Flt64>.Value(new Flt64(0.0));
        var inf = new ValueWrapper<Flt64>.Infinity();

        Assert.IsType<ValueWrapper<Flt64>.Infinity>(pos.Times(inf));
        Assert.IsType<ValueWrapper<Flt64>.NegativeInfinity>(neg.Times(inf));
        Assert.IsType<ValueWrapper<Flt64>.Value>(zero.Times(inf));
    }

    [Fact]
    public void Div_ValueAndValue_ReturnsQuotient() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(12.0));
        var b = new ValueWrapper<Flt64>.Value(new Flt64(4.0));
        ValueWrapper<Flt64>? result = a.Div(b);
        Assert.NotNull(result);
        Assert.Equal(new Flt64(3.0), result!.Unwrap());
    }

    [Fact]
    public void UnaryMinus_Value_ReturnsNegated() {
        var v = new ValueWrapper<Flt64>.Value(new Flt64(5.0));
        ValueWrapper<Flt64> result = -v;
        Assert.IsType<ValueWrapper<Flt64>.Value>(result);
        Assert.Equal(new Flt64(-5.0), result.Unwrap());
    }

    [Fact]
    public void UnaryMinus_Infinity_ReturnsNegativeInfinity() {
        var inf = new ValueWrapper<Flt64>.Infinity();
        ValueWrapper<Flt64> result = -inf;
        Assert.IsType<ValueWrapper<Flt64>.NegativeInfinity>(result);
    }

    [Fact]
    public void UnaryMinus_NegativeInfinity_ReturnsInfinity() {
        var negInf = new ValueWrapper<Flt64>.NegativeInfinity();
        ValueWrapper<Flt64> result = -negInf;
        Assert.IsType<ValueWrapper<Flt64>.Infinity>(result);
    }

    [Fact]
    public void PartialOrd_ValueVsInfinity_ValueIsLess() {
        var v = new ValueWrapper<Flt64>.Value(new Flt64(100.0));
        var inf = new ValueWrapper<Flt64>.Infinity();
        Assert.IsType<Order.Less>(v.PartialOrd(inf));
    }

    [Fact]
    public void PartialOrd_InfinityVsValue_InfinityIsGreater() {
        var inf = new ValueWrapper<Flt64>.Infinity();
        var v = new ValueWrapper<Flt64>.Value(new Flt64(100.0));
        Assert.IsType<Order.Greater>(inf.PartialOrd(v));
    }

    [Fact]
    public void PartialOrd_TwoValues_ComparesCorrectly() {
        var a = new ValueWrapper<Flt64>.Value(new Flt64(3.0));
        var b = new ValueWrapper<Flt64>.Value(new Flt64(5.0));
        Assert.IsType<Order.Less>(a.PartialOrd(b));
        Assert.IsType<Order.Greater>(b.PartialOrd(a));
        Assert.IsType<Order.Equal>(a.PartialOrd(a));
    }

    [Fact]
    public void Copy_Value_CreatesIndependentCopy() {
        var original = new ValueWrapper<Flt64>.Value(new Flt64(42.0));
        ValueWrapper<Flt64> copy = original.Copy();
        Assert.Equal(original, copy);
        Assert.NotSame(original, copy);
    }
}
