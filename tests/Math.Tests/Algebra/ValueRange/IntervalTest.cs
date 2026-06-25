#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Tests.Algebra.ValueRange
{
    public class IntervalTest
    {
        [Fact]
        public void Open_LowerSign_IsLeftParen()
        {
            var open = new Interval.Open();
            Assert.Equal("(", open.LowerSign);
        }

        [Fact]
        public void Open_UpperSign_IsRightParen()
        {
            var open = new Interval.Open();
            Assert.Equal(")", open.UpperSign);
        }

        [Fact]
        public void Closed_LowerSign_IsLeftBracket()
        {
            var closed = new Interval.Closed();
            Assert.Equal("[", closed.LowerSign);
        }

        [Fact]
        public void Closed_UpperSign_IsRightBracket()
        {
            var closed = new Interval.Closed();
            Assert.Equal("]", closed.UpperSign);
        }

        [Fact]
        public void Union_OpenWithOpen_ReturnsOpen()
        {
            var result = new Interval.Open().Union(new Interval.Open());
            Assert.IsType<Interval.Open>(result);
        }

        [Fact]
        public void Union_OpenWithClosed_ReturnsClosed()
        {
            var result = new Interval.Open().Union(new Interval.Closed());
            Assert.IsType<Interval.Closed>(result);
        }

        [Fact]
        public void Union_ClosedWithOpen_ReturnsClosed()
        {
            var result = new Interval.Closed().Union(new Interval.Open());
            Assert.IsType<Interval.Closed>(result);
        }

        [Fact]
        public void Union_ClosedWithClosed_ReturnsClosed()
        {
            var result = new Interval.Closed().Union(new Interval.Closed());
            Assert.IsType<Interval.Closed>(result);
        }

        [Fact]
        public void Intersect_OpenWithOpen_ReturnsOpen()
        {
            var result = new Interval.Open().Intersect(new Interval.Open());
            Assert.IsType<Interval.Open>(result);
        }

        [Fact]
        public void Intersect_OpenWithClosed_ReturnsOpen()
        {
            var result = new Interval.Open().Intersect(new Interval.Closed());
            Assert.IsType<Interval.Open>(result);
        }

        [Fact]
        public void Intersect_ClosedWithOpen_ReturnsOpen()
        {
            var result = new Interval.Closed().Intersect(new Interval.Open());
            Assert.IsType<Interval.Open>(result);
        }

        [Fact]
        public void Intersect_ClosedWithClosed_ReturnsClosed()
        {
            var result = new Interval.Closed().Intersect(new Interval.Closed());
            Assert.IsType<Interval.Closed>(result);
        }

        [Fact]
        public void Outer_OpenIsNeverOuter()
        {
            var open = new Interval.Open();
            Assert.False(open.Outer(new Interval.Open()));
            Assert.False(open.Outer(new Interval.Closed()));
        }

        [Fact]
        public void Outer_ClosedIsOuterOfOpen()
        {
            var closed = new Interval.Closed();
            Assert.True(closed.Outer(new Interval.Open()));
            Assert.False(closed.Outer(new Interval.Closed()));
        }

        [Fact]
        public void LowerBoundOperator_Open_UsesStrictLessThan()
        {
            var op = new Interval.Open().LowerBoundOperator<Flt64>();
            Assert.True(op(new Flt64(1.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(2.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(3.0), new Flt64(2.0)));
        }

        [Fact]
        public void LowerBoundOperator_Closed_UsesLessThanOrEqual()
        {
            var op = new Interval.Closed().LowerBoundOperator<Flt64>();
            Assert.True(op(new Flt64(1.0), new Flt64(2.0)));
            Assert.True(op(new Flt64(2.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(3.0), new Flt64(2.0)));
        }

        [Fact]
        public void UpperBoundOperator_Open_UsesStrictGreaterThan()
        {
            var op = new Interval.Open().UpperBoundOperator<Flt64>();
            Assert.True(op(new Flt64(3.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(2.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(1.0), new Flt64(2.0)));
        }

        [Fact]
        public void UpperBoundOperator_Closed_UsesGreaterThanOrEqual()
        {
            var op = new Interval.Closed().UpperBoundOperator<Flt64>();
            Assert.True(op(new Flt64(3.0), new Flt64(2.0)));
            Assert.True(op(new Flt64(2.0), new Flt64(2.0)));
            Assert.False(op(new Flt64(1.0), new Flt64(2.0)));
        }
    }
}
