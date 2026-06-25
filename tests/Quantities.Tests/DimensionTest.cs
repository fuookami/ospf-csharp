#nullable enable

using Fuookami.Ospf.Quantities.Dimension;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class DimensionTest {
    [Fact]
    public void TestFundamentalDimensions() {
        Assert.Equal("L", Dims.L.Symbol);
        Assert.Equal("M", Dims.M.Symbol);
        Assert.Equal("T", Dims.T.Symbol);
        Assert.Equal("I", Dims.I.Symbol);
        Assert.Equal("Θ", Dims.Theta.Symbol);
        Assert.Equal("N", Dims.N.Symbol);
        Assert.Equal("J", Dims.J.Symbol);
        Assert.Equal("rad", Dims.rad.Symbol);
        Assert.Equal("sr", Dims.sr.Symbol);
        Assert.Equal("B", Dims.B.Symbol);
    }

    [Fact]
    public void TestDerivedQuantityEquality() {
        var length1 = new DerivedQuantity(Dims.L, "length", "L");
        var length2 = new DerivedQuantity(Dims.L, "length", "L");
        Assert.Equal(length1, length2);
    }

    [Fact]
    public void TestDerivedQuantityMultiply() {
        var length = new DerivedQuantity(Dims.L, "length", "L");
        DerivedQuantity area = length * length;
        Assert.Equal(2, area.GetPower(Dims.L));
    }

    [Fact]
    public void TestDerivedQuantityDivide() {
        var length = new DerivedQuantity(Dims.L, "length", "L");
        var time = new DerivedQuantity(Dims.T, "time", "t");
        DerivedQuantity velocity = length / time;
        Assert.Equal(1, velocity.GetPower(Dims.L));
        Assert.Equal(-1, velocity.GetPower(Dims.T));
    }

    [Fact]
    public void TestDerivedQuantityPow() {
        var length = new DerivedQuantity(Dims.L, "length", "L");
        DerivedQuantity volume = length.Pow(3);
        Assert.Equal(3, volume.GetPower(Dims.L));
    }

    [Fact]
    public void TestDimensionless() {
        Assert.True(DerivedQuantity.Dimensionless.IsNone());
        Assert.Equal("1", DerivedQuantity.Dimensionless.DimensionSymbol());
    }

    [Fact]
    public void TestDimensionsPredefined() {
        Assert.Equal("L", Dimensions.Length.DimensionSymbol());
        Assert.Equal(1, Dimensions.Force.GetPower(Dims.M));
        Assert.Equal(1, Dimensions.Force.GetPower(Dims.L));
        Assert.Equal(-2, Dimensions.Force.GetPower(Dims.T));
    }
}
