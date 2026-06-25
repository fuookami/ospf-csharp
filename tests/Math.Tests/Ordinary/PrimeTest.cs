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

public class PrimeTest {
    [Theory]
    [InlineData(2UL, true)]
    [InlineData(3UL, true)]
    [InlineData(4UL, false)]
    [InlineData(5UL, true)]
    [InlineData(6UL, false)]
    [InlineData(7UL, true)]
    [InlineData(8UL, false)]
    [InlineData(9UL, false)]
    public void IsPrimeUInt64(ulong value, bool expected) {
        var num = new UInt64(value);
        var cache = new PrimeCache();
        cache.IsPrime(num).Should().Be(expected);
    }

    [Fact]
    public void GetPrimesUpTo() {
        var cache = new PrimeCache();
        IReadOnlyList<UInt64> primes = cache.GetPrimes(new UInt64(20UL));
        primes.Should().HaveCount(8);
        primes[0].Value.Should().Be(2UL);
        primes[7].Value.Should().Be(19UL);
    }

    [Fact]
    public void GetPrimesGeneric() {
        IReadOnlyList<UInt64> primes = Prime.GetPrimes(new UInt64(10UL), UInt64Constants.Instance);
        primes.Should().HaveCount(4);
    }

    [Fact]
    public void GetPrimesRegistryResolved() {
        Result<IReadOnlyList<UInt64>, ErrorCode, Error<ErrorCode>> result = Prime.GetPrimes(new UInt64(10UL));
        result.IsOk.Should().BeTrue();
        result.Value.Should().HaveCount(4);
    }
}
