#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Ordinary;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Math.Tests.Ordinary;

public class LcmTest {
    [Fact]
    public void LcmTwoUInt64() {
        var a = new UInt64(4UL);
        var b = new UInt64(6UL);
        Lcm.Of(a, b, UInt64Constants.Instance).Value.Should().Be(12UL);
    }

    [Fact]
    public void LcmSomeUInt64() {
        UInt64[] numbers = new[] { new UInt64(4UL), new UInt64(6UL), new UInt64(8UL) };
        Lcm.Of(numbers, UInt64Constants.Instance).Value.Should().Be(24UL);
    }

    [Fact]
    public void LcmByFactorizationUInt64() {
        UInt64[] numbers = new[] { new UInt64(4UL), new UInt64(6UL) };
        Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(12UL);
    }

    [Fact]
    public void LcmWithZero() {
        var a = new UInt64(5UL);
        UInt64 b = UInt64Constants.Instance.Zero;
        Lcm.Of(a, b, UInt64Constants.Instance).Value.Should().Be(0UL);
    }

    [Fact]
    public void LcmEmptyReturnsOne() {
        UInt64 result = Lcm.LcmByFactorization(System.Array.Empty<UInt64>(), UInt64Constants.Instance);
        result.Value.Should().Be(1UL);
    }

    [Fact]
    public void LcmSingleElement() {
        UInt64[] numbers = new[] { new UInt64(7UL) };
        Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(7UL);
    }

    [Fact]
    public void LcmManyWithZero() {
        UInt64[] numbers = new[] { new UInt64(4UL), new UInt64(0UL), new UInt64(6UL) };
        Lcm.LcmByFactorization(numbers, UInt64Constants.Instance).Value.Should().Be(0UL);
    }

    [Fact]
    public void LcmRegistryResolved() {
        UInt64[] numbers = new[] { new UInt64(4UL), new UInt64(6UL) };
        Result<UInt64, ErrorCode, Error<ErrorCode>> result = Lcm.Of(numbers);
        result.IsOk.Should().BeTrue();
        result.Value.Value.Should().Be(12UL);
    }

    [Fact]
    public void LcmFltX() {
        var a = new FltX(0.4m);
        var b = new FltX(0.6m);
        FltX result = Lcm.Of(a, b);
        result.Value.Should().Be(1.2m);
    }

    [Fact]
    public void LcmFltXSome() {
        FltX[] numbers = new[] { new FltX(0.4m), new FltX(0.6m) };
        FltX result = Lcm.Of(numbers);
        result.Value.Should().Be(1.2m);
    }
}
