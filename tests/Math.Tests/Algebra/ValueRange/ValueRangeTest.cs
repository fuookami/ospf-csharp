#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.ValueRange;

public class ValueRangeTest {
    [Fact]
    public void Of_SingleValue_CreatesFixedRange() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueRange<Flt64>.Of(new Flt64(5.0));
        Assert.True(result.IsOk);
        ValueRange<Flt64> range = result.Value;
        Assert.True(range.Fixed);
        Assert.Equal(new Flt64(5.0), range.FixedValue);
    }

    [Fact]
    public void Of_TwoValues_CreatesRange() {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> result = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0));
        Assert.True(result.IsOk);
        ValueRange<Flt64> range = result.Value;
        // Verify the range is well-formed
        Assert.True(range.Contains(new Flt64(5.0)));
        Assert.True(range.Contains(new Flt64(1.0)));
        Assert.True(range.Contains(new Flt64(10.0)));
        Assert.False(range.Contains(new Flt64(0.0)));
        Assert.False(range.Contains(new Flt64(11.0)));
    }

    [Fact]
    public void Full_RangeContainsAllValues() {
        var range = ValueRange<Flt64>.Full();
        Assert.True(range.Contains(new Flt64(0.0)));
        Assert.True(range.Contains(new Flt64(-1e100)));
        Assert.True(range.Contains(new Flt64(1e100)));
    }

    [Fact]
    public void Contains_ValueInRange_ReturnsTrue() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0)).Value;
        Assert.True(range.Contains(new Flt64(5.0)));
        Assert.True(range.Contains(new Flt64(1.0)));  // closed lower
        Assert.True(range.Contains(new Flt64(10.0))); // closed upper
    }

    [Fact]
    public void Contains_ValueOutOfRange_ReturnsFalse() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0)).Value;
        Assert.False(range.Contains(new Flt64(0.0)));
        Assert.False(range.Contains(new Flt64(11.0)));
    }

    [Fact]
    public void Contains_OpenInterval_ExcludesBoundary() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(
            new Flt64(1.0), new Flt64(10.0),
            new Interval.Open(), new Interval.Open()
        ).Value;
        Assert.True(range.Contains(new Flt64(5.0)));
        Assert.False(range.Contains(new Flt64(1.0)));
        Assert.False(range.Contains(new Flt64(10.0)));
    }

    [Fact]
    public void Geq_RangeContainsGreaterValues() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Geq(new Flt64(5.0)).Value;
        Assert.True(range.Contains(new Flt64(5.0)));
        Assert.True(range.Contains(new Flt64(100.0)));
        Assert.False(range.Contains(new Flt64(4.0)));
    }

    [Fact]
    public void Leq_RangeContainsLesserValues() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Leq(new Flt64(5.0)).Value;
        Assert.True(range.Contains(new Flt64(5.0)));
        Assert.True(range.Contains(new Flt64(-100.0)));
        Assert.False(range.Contains(new Flt64(6.0)));
    }

    [Fact]
    public void Gr_OpenLowerBound_ExcludesEqual() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Gr(new Flt64(5.0)).Value;
        Assert.True(range.Contains(new Flt64(6.0)));
        Assert.False(range.Contains(new Flt64(5.0)));
    }

    [Fact]
    public void Ls_OpenUpperBound_ExcludesEqual() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Ls(new Flt64(5.0)).Value;
        Assert.True(range.Contains(new Flt64(4.0)));
        Assert.False(range.Contains(new Flt64(5.0)));
    }

    [Fact]
    public void Plus_RangeAndScalar_TranslatesRange() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0)).Value;
        ValueRange<Flt64>? result = range + new Flt64(5.0);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(6.0)));
        Assert.True(result.Contains(new Flt64(15.0)));
        Assert.False(result.Contains(new Flt64(5.0)));
    }

    [Fact]
    public void Minus_RangeAndScalar_TranslatesRange() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(10.0), new Flt64(20.0)).Value;
        ValueRange<Flt64>? result = range - new Flt64(5.0);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(5.0)));
        Assert.True(result.Contains(new Flt64(15.0)));
    }

    [Fact]
    public void Plus_TwoRanges_MinkowskiSum() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(3.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(10.0), new Flt64(20.0)).Value;
        ValueRange<Flt64>? result = a + b;
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(11.0)));
        Assert.True(result.Contains(new Flt64(23.0)));
    }

    [Fact]
    public void Minus_TwoRanges_MinkowskiDifference() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(10.0), new Flt64(20.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(5.0)).Value;
        ValueRange<Flt64>? result = a - b;
        Assert.NotNull(result);
        // [10,20] - [1,5] = [10-5, 20-1] = [5, 19]
        Assert.True(result!.Contains(new Flt64(5.0)));
        Assert.True(result.Contains(new Flt64(19.0)));
    }

    [Fact]
    public void Times_ScalarPositive_PreservesDirection() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(6.0)).Value;
        ValueRange<Flt64>? result = range * new Flt64(3.0);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(6.0)));
        Assert.True(result.Contains(new Flt64(18.0)));
    }

    [Fact]
    public void Times_ScalarNegative_ReversesDirection() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(6.0)).Value;
        ValueRange<Flt64>? result = range * new Flt64(-1.0);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(-6.0)));
        Assert.True(result.Contains(new Flt64(-2.0)));
    }

    [Fact]
    public void Div_ByNonZero_ScalesRange() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(10.0), new Flt64(20.0)).Value;
        ValueRange<Flt64>? result = range.Div(new Flt64(2.0));
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(5.0)));
        Assert.True(result.Contains(new Flt64(10.0)));
    }

    [Fact]
    public void Div_ByZero_ReturnsNull() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(10.0), new Flt64(20.0)).Value;
        ValueRange<Flt64>? result = range.Div(new Flt64(0.0));
        Assert.Null(result);
    }

    [Fact]
    public void UnaryMinus_FlipsBounds() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(6.0)).Value;
        ValueRange<Flt64> result = -range;
        Assert.True(result.Contains(new Flt64(-6.0)));
        Assert.True(result.Contains(new Flt64(-2.0)));
        Assert.False(result.Contains(new Flt64(0.0)));
    }

    [Fact]
    public void Union_OverlappingRanges_ReturnsUnion() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(5.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(3.0), new Flt64(8.0)).Value;
        ValueRange<Flt64>? result = a.Union(b);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(1.0)));
        Assert.True(result.Contains(new Flt64(8.0)));
    }

    [Fact]
    public void Union_DisjointRanges_ReturnsNull() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(3.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(5.0), new Flt64(8.0)).Value;
        ValueRange<Flt64>? result = a.Union(b);
        Assert.Null(result);
    }

    [Fact]
    public void Intersect_OverlappingRanges_ReturnsIntersection() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(5.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(3.0), new Flt64(8.0)).Value;
        ValueRange<Flt64>? result = a.Intersect(b);
        Assert.NotNull(result);
        Assert.True(result!.Contains(new Flt64(3.0)));
        Assert.True(result.Contains(new Flt64(5.0)));
        Assert.False(result.Contains(new Flt64(1.0)));
        Assert.False(result.Contains(new Flt64(8.0)));
    }

    [Fact]
    public void ContainsRange_FullyContained_ReturnsTrue() {
        ValueRange<Flt64> outer = ValueRange<Flt64>.Of(new Flt64(0.0), new Flt64(10.0)).Value;
        ValueRange<Flt64> inner = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(8.0)).Value;
        Assert.True(outer.Contains(inner));
    }

    [Fact]
    public void ContainsRange_NotContained_ReturnsFalse() {
        ValueRange<Flt64> a = ValueRange<Flt64>.Of(new Flt64(0.0), new Flt64(5.0)).Value;
        ValueRange<Flt64> b = ValueRange<Flt64>.Of(new Flt64(3.0), new Flt64(8.0)).Value;
        Assert.False(a.Contains(b));
    }

    [Fact]
    public void Mean_RangeWithSymmetricBounds_ReturnsMidpoint() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(8.0)).Value;
        Assert.NotNull(range.MeanOrNull);
        Assert.Equal(new Flt64(5.0), range.MeanOrNull!.Unwrap());
    }

    [Fact]
    public void Diff_Range_ReturnsWidth() {
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(2.0), new Flt64(8.0)).Value;
        Assert.NotNull(range.DiffOrNull);
        Assert.Equal(new Flt64(6.0), range.DiffOrNull!.Unwrap());
    }

    [Fact]
    public void Copy_CreatesIndependentCopy() {
        ValueRange<Flt64> original = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0)).Value;
        ValueRange<Flt64> copy = original.Copy();
        Assert.Equal(original, copy);
    }

    [Fact]
    public void ToString_FormatsCorrectly() {
        ValueRange<Flt64> closedRange = ValueRange<Flt64>.Of(new Flt64(1.0), new Flt64(10.0)).Value;
        Assert.StartsWith("[", closedRange.ToString());
        Assert.EndsWith("]", closedRange.ToString());
    }

    [Fact]
    public void IsEmpty_LbGtUb_ReturnsTrue() {
        bool empty = ValueRange<Flt64>.IsEmpty(
            new ValueWrapper<Flt64>.Value(new Flt64(10.0)),
            new ValueWrapper<Flt64>.Value(new Flt64(1.0)),
            new Interval.Closed(), new Interval.Closed());
        Assert.True(empty);
    }

    [Fact]
    public void IsEmpty_ValidRange_ReturnsFalse() {
        bool empty = ValueRange<Flt64>.IsEmpty(
            new ValueWrapper<Flt64>.Value(new Flt64(1.0)),
            new ValueWrapper<Flt64>.Value(new Flt64(10.0)),
            new Interval.Closed(), new Interval.Closed());
        Assert.False(empty);
    }
}
