#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests
{
    public class UnitConversionTest
    {
        [Fact]
        public void TestKilometerToMeter()
        {
            var q = new Quantity<Flt64>(new Flt64(1.0), LengthUnits.Kilometer);
            var result = q.To(SIBaseUnits.Meter);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(1000.0), result.Value.Value);
        }

        [Fact]
        public void TestMeterToKilometer()
        {
            var q = new Quantity<Flt64>(new Flt64(1000.0), SIBaseUnits.Meter);
            var result = q.To(LengthUnits.Kilometer);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(1.0), result.Value.Value);
        }

        [Fact]
        public void TestMinuteToSecond()
        {
            var q = new Quantity<Flt64>(new Flt64(1.0), TimeUnits.Minute);
            var result = q.To(SIBaseUnits.Second);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(60.0), result.Value.Value);
        }

        [Fact]
        public void TestHourToSecond()
        {
            var q = new Quantity<Flt64>(new Flt64(1.0), TimeUnits.Hour);
            var result = q.To(SIBaseUnits.Second);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(3600.0), result.Value.Value);
        }

        [Fact]
        public void TestDimensionMismatchConversion()
        {
            var q = new Quantity<Flt64>(new Flt64(100.0), SIBaseUnits.Meter);
            var result = q.To(SIBaseUnits.Second);
            Assert.True(result.IsFailed);
        }

        [Fact]
        public void TestSameUnitConversion()
        {
            var q = new Quantity<Flt64>(new Flt64(42.0), SIBaseUnits.Meter);
            var result = q.To(SIBaseUnits.Meter);
            Assert.True(result.IsOk);
            Assert.Equal(new Flt64(42.0), result.Value.Value);
        }
    }
}
