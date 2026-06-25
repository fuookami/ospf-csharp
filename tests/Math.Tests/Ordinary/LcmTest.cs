#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Ordinary
{
    public class LcmTest
    {
        [Fact]
        public void LcmTwoUInt64()
        {
            var a = new UInt64(4UL);
            var b = new UInt64(6UL);
            Lcm.Of(a, b, UInt64Constants.Instance).Value.Should().Be(12UL);
        }

        [Fact]
        public void LcmSomeUInt64()
        {
            var numbers = new[] { new UInt64(4UL), new UInt64(6UL), new UInt64(8UL) };
            Lcm.Of(numbers, UInt64Constants.Instance).Value.Should().Be(24UL);
        }

        [Fact]
        public void LcmByFactorizationUInt64()
        {
            var numbers = new[] { new UInt64(4UL), new UInt64(6UL) };
            Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(12UL);
        }

        [Fact]
        public void LcmWithZero()
        {
            var a = new UInt64(5UL);
            var b = UInt64Constants.Instance.Zero;
            Lcm.Of(a, b, UInt64Constants.Instance).Value.Should().Be(0UL);
        }

        [Fact]
        public void LcmEmptyReturnsOne()
        {
            var result = Lcm.LcmByFactorization(System.Array.Empty<UInt64>(), UInt64Constants.Instance);
            result.Value.Should().Be(1UL);
        }

        [Fact]
        public void LcmSingleElement()
        {
            var numbers = new[] { new UInt64(7UL) };
            Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(7UL);
        }

        [Fact]
        public void LcmManyWithZero()
        {
            var numbers = new[] { new UInt64(4UL), new UInt64(0UL), new UInt64(6UL) };
            Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(0UL);
        }

        [Fact]
        public void LcmRegistryResolved()
        {
            var numbers = new[] { new UInt64(4UL), new UInt64(6UL) };
            var result = Lcm.Of(numbers);
            result.IsOk.Should().BeTrue();
            result.Value.Value.Should().Be(12UL);
        }

        [Fact]
        public void LcmFltX()
        {
            var a = new FltX(0.4m);
            var b = new FltX(0.6m);
            var result = Lcm.Of(a, b);
            result.Value.Should().Be(1.2m);
        }

        [Fact]
        public void LcmFltXSome()
        {
            var numbers = new[] { new FltX(0.4m), new FltX(0.6m) };
            var result = Lcm.Of(numbers);
            result.Value.Should().Be(1.2m);
        }
    }
}
