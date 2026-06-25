#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Number;

public class Int64Test {
    [Fact]
    public void Arithmetic_Addition() {
        var a = new Int64(3);
        var b = new Int64(4);
        (a + b).Value.Should().Be(7L);
    }

    [Fact]
    public void Arithmetic_Subtraction() {
        var a = new Int64(10);
        var b = new Int64(3);
        (a - b).Value.Should().Be(7L);
    }

    [Fact]
    public void Arithmetic_Multiplication() {
        var a = new Int64(3);
        var b = new Int64(4);
        (a * b).Value.Should().Be(12L);
    }

    [Fact]
    public void Arithmetic_Division() {
        var a = new Int64(12);
        var b = new Int64(4);
        (a / b).Value.Should().Be(3L);
    }

    [Fact]
    public void Arithmetic_Modulo() {
        var a = new Int64(10);
        var b = new Int64(3);
        (a % b).Value.Should().Be(1L);
    }

    [Fact]
    public void Arithmetic_Negation() {
        var a = new Int64(5);
        (-a).Value.Should().Be(-5L);
    }

    [Fact]
    public void Arithmetic_Abs() {
        var a = new Int64(-5);
        a.Abs().Value.Should().Be(5L);
    }

    [Fact]
    public void Comparison_Equality() {
        var a = new Int64(3);
        var b = new Int64(3);
        (a == b).Should().BeTrue();
        a.Eq(b).Should().BeTrue();
    }

    [Fact]
    public void Comparison_LessThan() {
        var a = new Int64(3);
        var b = new Int64(5);
        (a < b).Should().BeTrue();
    }

    [Fact]
    public void Constants_Zero() => Int64.Zero.Value.Should().Be(0L);

    [Fact]
    public void Constants_One() => Int64.One.Value.Should().Be(1L);

    [Fact]
    public void Registry_Constants() {
        INumericConstants<Int64> constants = NumericConstantsRegistry.For<Int64>();
        constants.Zero.Value.Should().Be(0L);
        constants.One.Value.Should().Be(1L);
        constants.Ten.Value.Should().Be(10L);
        constants.Minimum.Value.Should().Be(long.MinValue);
        constants.Maximum.Value.Should().Be(long.MaxValue);
    }

    [Fact]
    public void ToFlt64_Conversion() {
        var a = new Int64(42);
        a.ToFlt64().Value.Should().Be(42.0);
    }
}
