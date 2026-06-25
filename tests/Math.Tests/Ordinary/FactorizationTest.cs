#nullable enable

using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Ordinary
{
    public class FactorizationTest
    {
        [Fact]
        public void FactorizeUInt64()
        {
            var f2 = Factorization.Of(new UInt64(2UL), UInt64Constants.Instance);
            f2.Should().HaveCount(1);
            f2[0].Prime.Value.Should().Be(2UL);
            f2[0].Exponent.Should().Be(1);

            var f4 = Factorization.Of(new UInt64(4UL), UInt64Constants.Instance);
            f4.Should().HaveCount(1);
            f4[0].Prime.Value.Should().Be(2UL);
            f4[0].Exponent.Should().Be(2);

            var f12 = Factorization.Of(new UInt64(12UL), UInt64Constants.Instance);
            f12.Should().HaveCount(2);
            f12[0].Prime.Value.Should().Be(2UL);
            f12[0].Exponent.Should().Be(2);
            f12[1].Prime.Value.Should().Be(3UL);
            f12[1].Exponent.Should().Be(1);
        }

        [Fact]
        public void FactorizeBoundary()
        {
            Factorization.Of(UInt64Constants.Instance.Zero, UInt64Constants.Instance).Should().BeEmpty();
            Factorization.Of(UInt64Constants.Instance.One, UInt64Constants.Instance).Should().BeEmpty();
        }

        [Fact]
        public void DefactorizeUInt64()
        {
            var factors = new (UInt64, int)[]
            {
                (new UInt64(2UL), 2),
                (new UInt64(3UL), 1)
            };
            var result = Factorization.Defactorize(factors, UInt64Constants.Instance);
            result.IsOk.Should().BeTrue();
            result.Value.Value.Should().Be(12UL);
        }

        [Fact]
        public void DivisorsUInt64()
        {
            var divisors = Factorization.Divisors(new UInt64(12UL), UInt64Constants.Instance);
            divisors.Select(d => d.Value).Should().BeEquivalentTo(new ulong[] { 1, 2, 3, 4, 6, 12 });
        }

        [Fact]
        public void DivisorsBoundary()
        {
            var d0 = Factorization.Divisors(UInt64Constants.Instance.Zero, UInt64Constants.Instance);
            d0.Should().HaveCount(1);
            d0[0].Value.Should().Be(1UL);

            var d1 = Factorization.Divisors(UInt64Constants.Instance.One, UInt64Constants.Instance);
            d1.Should().HaveCount(1);
            d1[0].Value.Should().Be(1UL);
        }

        [Fact]
        public void DivisorCountUInt64()
        {
            Factorization.DivisorCount(new UInt64(12UL), UInt64Constants.Instance).Should().Be(6);
        }

        [Fact]
        public void DivisorCountBoundary()
        {
            Factorization.DivisorCount(UInt64Constants.Instance.Zero, UInt64Constants.Instance).Should().Be(1);
            Factorization.DivisorCount(UInt64Constants.Instance.One, UInt64Constants.Instance).Should().Be(1);
        }

        [Fact]
        public void EulerTotientUInt64()
        {
            Factorization.EulerTotient(UInt64Constants.Instance.One, UInt64Constants.Instance).Value.Should().Be(1UL);
            Factorization.EulerTotient(new UInt64(10UL), UInt64Constants.Instance).Value.Should().Be(4UL);
            Factorization.EulerTotient(new UInt64(7UL), UInt64Constants.Instance).Value.Should().Be(6UL);
        }

        [Fact]
        public void EulerTotientBoundary()
        {
            Factorization.EulerTotient(UInt64Constants.Instance.Zero, UInt64Constants.Instance).Value.Should().Be(0UL);
        }

        [Fact]
        public void FactorizeRegistryResolved()
        {
            var result = Factorization.Of(new UInt64(12UL));
            result.IsOk.Should().BeTrue();
            result.Value.Should().HaveCount(2);
        }
    }
}
