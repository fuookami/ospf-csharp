#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Number;

public class Rtn64Test {
    [Fact]
    public void Arithmetic_Addition() {
        var a = new RtnX(1, 2); // 1/2
        var b = new RtnX(1, 3); // 1/3
        RtnX result = a + b;
        // 1/2 + 1/3 = 5/6
        result.Numerator.Should().Be(5);
        result.Denominator.Should().Be(6);
    }

    [Fact]
    public void Arithmetic_Subtraction() {
        var a = new RtnX(3, 4); // 3/4
        var b = new RtnX(1, 4); // 1/4
        RtnX result = a - b;
        // 3/4 - 1/4 = (3*4 - 1*4)/(4*4) = 8/16 (unsimplified)
        result.Numerator.Should().Be(8);
        result.Denominator.Should().Be(16);
    }

    [Fact]
    public void Arithmetic_Multiplication() {
        var a = new RtnX(2, 3);
        var b = new RtnX(3, 4);
        RtnX result = a * b;
        // 2/3 * 3/4 = 6/12
        result.Numerator.Should().Be(6);
        result.Denominator.Should().Be(12);
    }

    [Fact]
    public void Arithmetic_Division() {
        var a = new RtnX(1, 2);
        var b = new RtnX(1, 3);
        RtnX result = a / b;
        // 1/2 / 1/3 = 3/2
        result.Numerator.Should().Be(3);
        result.Denominator.Should().Be(2);
    }

    [Fact]
    public void ZeroDenominator_ReturnsRet() {
        // Of() should return Failed when denominator is zero
        Result<RtnX, ErrorCode, Error<ErrorCode>> result = RtnX.Of(1, 0);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ZeroDenominator_OfOrNull_ReturnsNull() {
        RtnX? result = RtnX.OfOrNull(1, 0);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidDenominator_OfOrNull_ReturnsValue() {
        RtnX? result = RtnX.OfOrNull(1, 2);
        result.Should().NotBeNull();
        result!.Value.Numerator.Should().Be(1);
        result.Value.Denominator.Should().Be(2);
    }

    [Fact]
    public void Comparison_Equality() {
        var a = new RtnX(1, 2);
        var b = new RtnX(1, 2);
        (a == b).Should().BeTrue();
        a.Eq(b).Should().BeTrue();
    }

    [Fact]
    public void Comparison_LessThan() {
        var a = new RtnX(1, 3);
        var b = new RtnX(1, 2);
        (a < b).Should().BeTrue();
    }

    [Fact]
    public void Constants() {
        INumericConstants<RtnX> constants = NumericConstantsRegistry.For<RtnX>();
        constants.Zero.Numerator.Should().Be(0);
        constants.Zero.Denominator.Should().Be(1);
        constants.One.Numerator.Should().Be(1);
        constants.One.Denominator.Should().Be(1);
    }
}
