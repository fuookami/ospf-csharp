#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Ordinary
{
    public class FltXPowerStrategyTest
    {
        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        public void DefaultPrecisionShouldRespectDigitsBounds(int digits)
        {
            var precision = FltXPowerStrategy.DefaultPrecision(digits);
            precision.Value.Should().BeGreaterThan(0m);
        }

        [Fact]
        public void LnAndExpShouldRoundTrip()
        {
            var x = new FltX(2.0m);
            var digits = 10;
            var lnResult = FltXPowerStrategy.Ln(x, digits);
            lnResult.Should().NotBeNull();
            var expResult = FltXPowerStrategy.Exp(lnResult!.Value, digits);
            System.Math.Abs((double)(expResult.Value - x.Value)).Should().BeLessThan(0.001);
        }

        [Fact]
        public void PowShouldHandleIntegerIndex()
        {
            var result = FltXPowerStrategy.PowOrNull(
                new FltX(2.0m), new FltX(10.0m), 10);
            result.Should().NotBeNull();
            System.Math.Abs((double)(result!.Value.Value - new FltX(1024m).Value)).Should().BeLessThan(0.01);
        }

        [Fact]
        public void PowShouldHandleFractionalIndex()
        {
            var result = FltXPowerStrategy.PowOrNull(
                new FltX(9.0m), new FltX(0.5m), 10);
            result.Should().NotBeNull();
            System.Math.Abs((double)(result!.Value.Value - new FltX(3m).Value)).Should().BeLessThan(0.01);
        }

        [Fact]
        public void LnShouldReturnNullForNonPositiveValues()
        {
            FltXPowerStrategy.Ln(FltXConstants.Instance.Zero, 10).Should().BeNull();
            FltXPowerStrategy.Ln(new FltX(-1m), 10).Should().BeNull();
        }

        [Fact]
        public void ExpWithStatsShouldRespectMaxIterations()
        {
            var result = FltXPowerStrategy.ExpWithStats(
                new FltX(1.0m), 10, maxIterations: 5);
            result.Iterations.Should().BeLessThanOrEqualTo(5);
            result.Value.Value.Should().BeGreaterThan(0m);
        }

        [Fact]
        public void PowShouldFailForFractionalExponentOnNonPositiveBase()
        {
            var result = FltXPowerStrategy.PowSafe(
                new FltX(-2.0m), new FltX(0.5m), 10);
            result.IsFailed.Should().BeTrue();
        }
    }
}
