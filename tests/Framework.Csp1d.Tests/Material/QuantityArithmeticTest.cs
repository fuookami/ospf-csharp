#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Material
{
    /// <summary>
    /// 物理量算术测试 / Quantity arithmetic tests.
    /// </summary>
    public class QuantityArithmeticTest
    {
        [Fact]
        public void Addition_SameDimension_ShouldSucceed()
        {
            var a = new Quantity<Flt64>(new Flt64(3.0), SIBaseUnits.Meter);
            var b = new Quantity<Flt64>(new Flt64(4.0), SIBaseUnits.Meter);
            var result = a.Add(b);
            result.IsOk.Should().BeTrue();
        }

        [Fact]
        public void Subtraction_SameDimension_ShouldSucceed()
        {
            var a = new Quantity<Flt64>(new Flt64(10.0), SIBaseUnits.Meter);
            var b = new Quantity<Flt64>(new Flt64(3.0), SIBaseUnits.Meter);
            var result = a.Subtract(b);
            result.IsOk.Should().BeTrue();
        }

        [Fact]
        public void Addition_DifferentDimension_ShouldFail()
        {
            var a = new Quantity<Flt64>(new Flt64(3.0), SIBaseUnits.Meter);
            var b = new Quantity<Flt64>(new Flt64(4.0), SIBaseUnits.Second);
            var result = a.Add(b);
            result.IsFailed.Should().BeTrue();
        }
    }
}
