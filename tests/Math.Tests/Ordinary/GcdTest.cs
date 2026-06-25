#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Ordinary
{
    public class GcdTest
    {
        [Fact]
        public void GcdTwoUInt64()
        {
            var a = new UInt64(12UL);
            var b = new UInt64(8UL);
            Gcd.Of(a, b).Value.Should().Be(4UL);
        }

        [Fact]
        public void GcdSomeUInt64()
        {
            var numbers = new[] { new UInt64(12UL), new UInt64(8UL), new UInt64(6UL) };
            Gcd.Of(numbers, UInt64Constants.Instance).Value.Should().Be(2UL);
        }

        [Fact]
        public void GcdModUInt64()
        {
            var a = new UInt64(48UL);
            var b = new UInt64(18UL);
            Gcd.GcdMod(a, b).Value.Should().Be(6UL);
        }

        [Fact]
        public void ExtendedGcdInt64()
        {
            var a = new Int64(35L);
            var b = new Int64(15L);
            var result = Gcd.ExtendedGcd(a, b);
            result.Gcd.Value.Should().Be(5L);
            var check = a.Times(result.X).Plus(b.Times(result.Y));
            check.Value.Should().Be(5L);
        }

        [Fact]
        public void GcdWithZeroUInt64()
        {
            var zero = UInt64Constants.Instance.Zero;
            var five = new UInt64(5UL);
            Gcd.Of(five, zero).Value.Should().Be(5UL);
            Gcd.Of(zero, five).Value.Should().Be(5UL);
            Gcd.Of(zero, zero).Value.Should().Be(0UL);
        }

        [Fact]
        public void GcdEmptyReturnsOne()
        {
            var result = Gcd.Of(System.Array.Empty<UInt64>(), UInt64Constants.Instance);
            result.Value.Should().Be(1UL);
        }

        [Fact]
        public void GcdSingleElement()
        {
            var numbers = new[] { new UInt64(7UL) };
            Gcd.Of(numbers, UInt64Constants.Instance).Value.Should().Be(7UL);
        }

        [Fact]
        public void GcdManyWithZero()
        {
            var numbers = new[] { new UInt64(12UL), new UInt64(0UL), new UInt64(8UL) };
            Gcd.Of(numbers, UInt64Constants.Instance).Value.Should().Be(4UL);
        }

        [Fact]
        public void GcdRegistryResolved()
        {
            var numbers = new[] { new UInt64(12UL), new UInt64(8UL) };
            var result = Gcd.Of(numbers);
            result.IsOk.Should().BeTrue();
            result.Value.Value.Should().Be(4UL);
        }

        [Fact]
        public void GcdFltX()
        {
            var a = new FltX(0.4m);
            var b = new FltX(0.6m);
            var result = Gcd.Of(a, b);
            result.Value.Should().Be(0.2m);
        }

        [Fact]
        public void GcdModRegistryResolved()
        {
            var numbers = new[] { new UInt64(48UL), new UInt64(18UL) };
            var result = Gcd.GcdMod(numbers);
            result.IsOk.Should().BeTrue();
            result.Value.Value.Should().Be(6UL);
        }
    }
}
