#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Ordinary
{
    public class OrdinaryExplicitConstantsPathTest
    {
        [Fact]
        public void ExplicitConstantsPathsShouldWorkWhenFallbackDisabled()
        {
            var factorizeResult = Factorization.Of(new UInt64(12UL), UInt64Constants.Instance);
            factorizeResult.Should().HaveCount(2);

            var defactorizeResult = Factorization.Defactorize(factorizeResult, UInt64Constants.Instance);
            defactorizeResult.Value.Value.Should().Be(12UL);

            var divisors = Factorization.Divisors(new UInt64(12UL), UInt64Constants.Instance);
            divisors.Should().NotBeEmpty();

            var divisorCount = Factorization.DivisorCount(new UInt64(12UL), UInt64Constants.Instance);
            divisorCount.Should().Be(6);

            var eulerResult = Factorization.EulerTotient(new UInt64(10UL), UInt64Constants.Instance);
            eulerResult.Value.Should().Be(4UL);

            var gcdResult = Gcd.Of(new UInt64(12UL), new UInt64(8UL));
            gcdResult.Value.Should().Be(4UL);

            var gcdModResult = Gcd.GcdMod(new UInt64(48UL), new UInt64(18UL));
            gcdModResult.Value.Should().Be(6UL);

            var lcmResult = Lcm.Of(new UInt64(4UL), new UInt64(6UL), UInt64Constants.Instance);
            lcmResult.Value.Should().Be(12UL);

            var lcmByFactResult = Lcm.LcmByFactorization(
                new[] { new UInt64(4UL), new UInt64(6UL) }, UInt64Constants.Instance);
            lcmByFactResult.Value.Should().Be(12UL);

            var primesResult = Prime.GetPrimes(new UInt64(10UL), UInt64Constants.Instance);
            primesResult.Should().HaveCount(4);

            var lnResult = Log.Ln(new Flt64(2.0), Flt64Constants.Instance, 10);
            lnResult.Should().NotBeNull();

            var logResult = Log.Logarithm(new Flt64(8.0), new Flt64(2.0), Flt64Constants.Instance, 10);
            logResult.Should().NotBeNull();
            System.Math.Abs(logResult!.Value.Value - 3.0).Should().BeLessThan(0.001);

            var powSafeResult = Pow.Safe(new Flt64(2.0), 10, Flt64Constants.Instance, 10);
            powSafeResult.IsOk.Should().BeTrue();
            System.Math.Abs(powSafeResult.Value.Value - 1024.0).Should().BeLessThan(0.01);

            var expResult = Pow.Exp(new Flt64(1.0), Flt64Constants.Instance, 10);
            System.Math.Abs(expResult.Value - System.Math.E).Should().BeLessThan(0.001);
        }

        [Fact]
        public void RegistryOverloadsShouldSucceedForRegisteredTypes()
        {
            var gcdResult = Gcd.Of(new[] { new UInt64(12UL), new UInt64(8UL) });
            gcdResult.IsOk.Should().BeTrue();
            gcdResult.Value.Value.Should().Be(4UL);

            var factorizeResult = Factorization.Of(new UInt64(12UL));
            factorizeResult.IsOk.Should().BeTrue();

            var lcmResult = Lcm.Of(new[] { new UInt64(4UL), new UInt64(6UL) });
            lcmResult.IsOk.Should().BeTrue();
            lcmResult.Value.Value.Should().Be(12UL);

            var primesResult = Prime.GetPrimes(new UInt64(10UL));
            primesResult.IsOk.Should().BeTrue();
        }

        [Fact]
        public void ForOrNullReturnsNullForUnregisteredType()
        {
            NumericConstantsRegistry.ForOrNull<UInt64>().Should().NotBeNull();
            NumericConstantsRegistry.ForOrNull<Flt64>().Should().NotBeNull();
        }
    }
}
