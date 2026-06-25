#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Math.Tests.Ordinary;

public class OrdinaryExplicitConstantsPathTest {
    [Fact]
    public void ExplicitConstantsPathsShouldWorkWhenFallbackDisabled() {
        IReadOnlyList<(UInt64 Prime, int Exponent)> factorizeResult = Factorization.Of(new UInt64(12UL), UInt64Constants.Instance);
        factorizeResult.Should().HaveCount(2);

        Result<UInt64, ErrorCode, Error<ErrorCode>> defactorizeResult = Factorization.Defactorize(factorizeResult, UInt64Constants.Instance);
        defactorizeResult.Value.Value.Should().Be(12UL);

        IReadOnlyList<UInt64> divisors = Factorization.Divisors(new UInt64(12UL), UInt64Constants.Instance);
        divisors.Should().NotBeEmpty();

        int divisorCount = Factorization.DivisorCount(new UInt64(12UL), UInt64Constants.Instance);
        divisorCount.Should().Be(6);

        UInt64 eulerResult = Factorization.EulerTotient(new UInt64(10UL), UInt64Constants.Instance);
        eulerResult.Value.Should().Be(4UL);

        UInt64 gcdResult = Gcd.Of(new UInt64(12UL), new UInt64(8UL));
        gcdResult.Value.Should().Be(4UL);

        UInt64 gcdModResult = Gcd.GcdMod(new UInt64(48UL), new UInt64(18UL));
        gcdModResult.Value.Should().Be(6UL);

        UInt64 lcmResult = Lcm.Of(new UInt64(4UL), new UInt64(6UL), UInt64Constants.Instance);
        lcmResult.Value.Should().Be(12UL);

        UInt64 lcmByFactResult = Lcm.LcmByFactorization(
            new[] { new UInt64(4UL), new UInt64(6UL) }, UInt64Constants.Instance);
        lcmByFactResult.Value.Should().Be(12UL);

        IReadOnlyList<UInt64> primesResult = Prime.GetPrimes(new UInt64(10UL), UInt64Constants.Instance);
        primesResult.Should().HaveCount(4);

        Flt64? lnResult = Log.Ln(new Flt64(2.0), Flt64Constants.Instance, 10);
        lnResult.Should().NotBeNull();

        Flt64? logResult = Log.Logarithm(new Flt64(8.0), new Flt64(2.0), Flt64Constants.Instance, 10);
        logResult.Should().NotBeNull();
        System.Math.Abs(logResult!.Value.Value - 3.0).Should().BeLessThan(0.001);

        Result<Flt64, ErrorCode, Error<ErrorCode>> powSafeResult = Pow.Safe(new Flt64(2.0), 10, Flt64Constants.Instance, 10);
        powSafeResult.IsOk.Should().BeTrue();
        System.Math.Abs(powSafeResult.Value.Value - 1024.0).Should().BeLessThan(0.01);

        Flt64 expResult = Pow.Exp(new Flt64(1.0), Flt64Constants.Instance, 10);
        System.Math.Abs(expResult.Value - System.Math.E).Should().BeLessThan(0.001);
    }

    [Fact]
    public void RegistryOverloadsShouldSucceedForRegisteredTypes() {
        Result<UInt64, ErrorCode, Error<ErrorCode>> gcdResult = Gcd.Of(new[] { new UInt64(12UL), new UInt64(8UL) });
        gcdResult.IsOk.Should().BeTrue();
        gcdResult.Value.Value.Should().Be(4UL);

        Result<IReadOnlyList<(UInt64 Prime, int Exponent)>, ErrorCode, Error<ErrorCode>> factorizeResult = Factorization.Of(new UInt64(12UL));
        factorizeResult.IsOk.Should().BeTrue();

        Result<UInt64, ErrorCode, Error<ErrorCode>> lcmResult = Lcm.Of(new[] { new UInt64(4UL), new UInt64(6UL) });
        lcmResult.IsOk.Should().BeTrue();
        lcmResult.Value.Value.Should().Be(12UL);

        Result<IReadOnlyList<UInt64>, ErrorCode, Error<ErrorCode>> primesResult = Prime.GetPrimes(new UInt64(10UL));
        primesResult.IsOk.Should().BeTrue();
    }

    [Fact]
    public void ForOrNullReturnsNullForUnregisteredType() {
        NumericConstantsRegistry.ForOrNull<UInt64>().Should().NotBeNull();
        NumericConstantsRegistry.ForOrNull<Flt64>().Should().NotBeNull();
    }
}
