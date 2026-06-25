#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests
{
    public class QuantityTest
    {
        [Fact]
        public void TestCreateQuantity()
        {
            var q = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            Assert.Equal(new Flt64(100.0), q.Value);
            Assert.Equal(SIBaseUnits.Meter, q.Unit);
        }

        [Fact]
        public void TestQuantityAdd()
        {
            var q1 = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            var q2 = new Quantity<Flt64>(new Flt64(200.0), SIBaseUnits.Meter);
            var result = q1.Add(q2);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(300.0), result.Value.Value);
        }

        [Fact]
        public void TestQuantityAddDimensionMismatch()
        {
            var q1 = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            var q2 = new Quantity<Flt64>(new Flt64(10.0), SIBaseUnits.Second);
            var result = q1.Add(q2);
            Assert.True(result.IsFailed);
        }

        [Fact]
        public void TestQuantitySubtract()
        {
            var q1 = new Quantity<Flt64>(new Flt64(300.0), SIBaseUnits.Meter);
            var q2 = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            var result = q1.Subtract(q2);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(200.0), result.Value.Value);
        }

        [Fact]
        public void TestQuantityMultiply()
        {
            var q1 = new Quantity<Flt64>(new Flt64(10.0), SIBaseUnits.Meter);
            var q2 = new Quantity<Flt64>(new Flt64(5.0), SIBaseUnits.Second);
            var result = q1.Multiply(q2);
            Assert.Equal(new Flt64(50.0), result.Value);
        }

        [Fact]
        public void TestQuantityDivide()
        {
            var q1 = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            var q2 = new Quantity<Flt64>(new Flt64(10.0), SIBaseUnits.Second);
            var result = q1.Divide(q2);
            Assert.Equal(new Flt64(10.0), result.Value);
        }
    }
}
