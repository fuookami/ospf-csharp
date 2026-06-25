#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Number;

public class Flt64Test {
    [Fact]
    public void Arithmetic_Addition() {
        var a = new Flt64(3.0);
        var b = new Flt64(4.0);
        (a + b).Value.Should().Be(7.0);
        a.Plus(b).Value.Should().Be(7.0);
    }

    [Fact]
    public void Arithmetic_Subtraction() {
        var a = new Flt64(10.0);
        var b = new Flt64(3.0);
        (a - b).Value.Should().Be(7.0);
    }

    [Fact]
    public void Arithmetic_Multiplication() {
        var a = new Flt64(3.0);
        var b = new Flt64(4.0);
        (a * b).Value.Should().Be(12.0);
    }

    [Fact]
    public void Arithmetic_Division() {
        var a = new Flt64(12.0);
        var b = new Flt64(4.0);
        (a / b).Value.Should().Be(3.0);
    }

    [Fact]
    public void Arithmetic_Negation() {
        var a = new Flt64(5.0);
        (-a).Value.Should().Be(-5.0);
    }

    [Fact]
    public void Arithmetic_Abs() {
        var a = new Flt64(-5.0);
        a.Abs().Value.Should().Be(5.0);
    }

    [Fact]
    public void Comparison_Equality() {
        var a = new Flt64(3.0);
        var b = new Flt64(3.0);
        (a == b).Should().BeTrue();
        a.Eq(b).Should().BeTrue();
    }

    [Fact]
    public void Comparison_LessThan() {
        var a = new Flt64(3.0);
        var b = new Flt64(5.0);
        (a < b).Should().BeTrue();
        a.Ls(b).Should().BeTrue();
    }

    [Fact]
    public void Comparison_GreaterThan() {
        var a = new Flt64(5.0);
        var b = new Flt64(3.0);
        (a > b).Should().BeTrue();
        a.Gr(b).Should().BeTrue();
    }

    [Fact]
    public void Constants_Zero() => Flt64.Zero.Value.Should().Be(0.0);

    [Fact]
    public void Constants_One() => Flt64.One.Value.Should().Be(1.0);

    [Fact]
    public void Constants_Pi() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.Pi!.Value.Value.Should().BeApproximately(System.Math.PI, 1e-10);
    }

    [Fact]
    public void Constants_E() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.E!.Value.Value.Should().BeApproximately(System.Math.E, 1e-10);
    }

    [Fact]
    public void Constants_Half() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.Half!.Value.Value.Should().Be(0.5);
    }

    [Fact]
    public void Constants_PositiveInfinity() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.PositiveInfinity!.Value.Value.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void Constants_NaN() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.NaN!.Value.Value.Should().Be(double.NaN);
        constants.IsNaN(constants.NaN!.Value).Should().BeTrue();
    }

    [Fact]
    public void Constants_Epsilon() {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        constants.Epsilon!.Value.Value.Should().Be(double.Epsilon);
    }

    [Fact]
    public void ToDouble_ReturnsValue() {
        var a = new Flt64(42.0);
        a.ToDouble().Should().Be(42.0);
    }

    [Fact]
    public void Copy_ReturnsEqualValue() {
        var a = new Flt64(42.0);
        Flt64 b = a.Copy();
        b.Value.Should().Be(a.Value);
    }
}
