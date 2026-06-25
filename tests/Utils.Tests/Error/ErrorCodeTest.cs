#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Error;
/// <summary>
/// ErrorCode 测试（移植自 ospf-kotlin ErrorCodeTest.kt）/ ErrorCode tests (ported from ospf-kotlin ErrorCodeTest.kt).
/// </summary>
public class ErrorCodeTest {
    [Fact]
    public void FromByte_ValidCode_ReturnsCorrectEnum() {
        ErrorCodeExtensions.FromByte(0x00).Should().Be(ErrorCode.None);
        ErrorCodeExtensions.FromByte(0x01).Should().Be(ErrorCode.AuthenticationError);
        ErrorCodeExtensions.FromByte(0x29).Should().Be(ErrorCode.ORModelInfeasible);
        ErrorCodeExtensions.FromByte(0xfe).Should().Be(ErrorCode.Other);
        ErrorCodeExtensions.FromByte(0xff).Should().Be(ErrorCode.Unknown);
    }

    [Fact]
    public void FromByte_InvalidCode_ReturnsUnknown() => ErrorCodeExtensions.FromByte(0x42).Should().Be(ErrorCode.Unknown);

    [Fact]
    public void ToByte_RoundTrip() {
        foreach (ErrorCode code in new[] { ErrorCode.None, ErrorCode.AuthenticationError, ErrorCode.ORModelInfeasible, ErrorCode.Other, ErrorCode.Unknown }) {
            ErrorCodeExtensions.FromByte(code.ToByte()).Should().Be(code);
        }
    }

    [Fact]
    public void ToUInt64_RoundTrip() => ErrorCodeExtensions.FromUInt64(ErrorCode.ORModelInfeasible.ToUInt64()).Should().Be(ErrorCode.ORModelInfeasible);

    [Fact]
    public void ToReadableString_ReturnsName() {
        ErrorCode.None.ToReadableString().Should().Be("None");
        ErrorCode.SolverNotFound.ToReadableString().Should().Be("SolverNotFound");
        ErrorCode.Unknown.ToReadableString().Should().Be("Unknown");
    }
}
