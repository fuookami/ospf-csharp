#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Variable;

public class VariableCombinationItemTests {
    [Fact]
    public void BinVariable1_CanBeCreated() {
        var var = new BinVariable1("x", 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("x");
    }

    [Fact]
    public void BinVariable2_CanBeCreated() {
        var var = new BinVariable2("y", 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("y");
    }

    [Fact]
    public void BinVariable3_CanBeCreated() {
        var var = new BinVariable3("z", 2, 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("z");
    }

    [Fact]
    public void BinVariable4_CanBeCreated() {
        var var = new BinVariable4("w", 2, 3, 4, 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("w");
    }

    [Fact]
    public void IntVariable1_CanBeCreated() {
        var var = new IntVariable1("i", 10);

        var.Should().NotBeNull();
        var.Name.Should().Be("i");
    }

    [Fact]
    public void IntVariable2_CanBeCreated() {
        var var = new IntVariable2("j", 5, 6);

        var.Should().NotBeNull();
        var.Name.Should().Be("j");
    }

    [Fact]
    public void RealVariable1_CanBeCreated() {
        var var = new RealVariable1("r", 8);

        var.Should().NotBeNull();
        var.Name.Should().Be("r");
    }

    [Fact]
    public void RealVariable2_CanBeCreated() {
        var var = new RealVariable2("s", 3, 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("s");
    }

    [Fact]
    public void TerVariable1_CanBeCreated() {
        var var = new TerVariable1("t", 6);

        var.Should().NotBeNull();
        var.Name.Should().Be("t");
    }

    [Fact]
    public void UIntVar1_CanBeCreated() {
        var var = new UIntVar1("u", 7);

        var.Should().NotBeNull();
        var.Name.Should().Be("u");
    }

    [Fact]
    public void PctVariable1_CanBeCreated() {
        var var = new PctVariable1("p", 4);

        var.Should().NotBeNull();
        var.Name.Should().Be("p");
    }

    [Fact]
    public void URealVariable1_CanBeCreated() {
        var var = new URealVariable1("ur", 5);

        var.Should().NotBeNull();
        var.Name.Should().Be("ur");
    }

    [Fact]
    public void BTerVariable1_CanBeCreated() {
        var var = new BTerVariable1("bt", 3);

        var.Should().NotBeNull();
        var.Name.Should().Be("bt");
    }

    [Fact]
    public void Variable1_HasCorrectType() {
        var var = new BinVariable1("bin", 5);

        var.Should().BeAssignableTo<Variable1<UInt8, Binary>>();
    }

    [Fact]
    public void Variable1_IntType_HasCorrectType() {
        var var = new IntVariable1("intVar", 5);

        var.Should().BeAssignableTo<Variable1<Int64, Integer>>();
    }

    [Fact]
    public void Variable1_RealType_HasCorrectType() {
        var var = new RealVariable1("realVar", 5);

        var.Should().BeAssignableTo<Variable1<Flt64, Continuous>>();
    }
}
