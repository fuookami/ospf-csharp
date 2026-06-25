#nullable enable

using Fuookami.Ospf.Quantities.Dimension;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class QuantityDomainTest {
    [Fact]
    public void TestMultiplyDiscreteDiscrete() {
        Assert.Equal(QuantityDomain.Discrete,
            QuantityDomainOps.Multiply(QuantityDomain.Discrete, QuantityDomain.Discrete));
    }

    [Fact]
    public void TestMultiplyContinuousDiscrete() {
        Assert.Equal(QuantityDomain.Continuous,
            QuantityDomainOps.Multiply(QuantityDomain.Continuous, QuantityDomain.Discrete));
    }

    [Fact]
    public void TestDivideAlwaysContinuous() {
        Assert.Equal(QuantityDomain.Continuous,
            QuantityDomainOps.Divide(QuantityDomain.Discrete, QuantityDomain.Discrete));
    }

    [Fact]
    public void TestPowPositive() {
        Assert.Equal(QuantityDomain.Discrete,
            QuantityDomainOps.Pow(QuantityDomain.Discrete, 3));
    }

    [Fact]
    public void TestPowZero() {
        Assert.Equal(QuantityDomain.Continuous,
            QuantityDomainOps.Pow(QuantityDomain.Discrete, 0));
    }

    [Fact]
    public void TestPowNegative() {
        Assert.Equal(QuantityDomain.Continuous,
            QuantityDomainOps.Pow(QuantityDomain.Discrete, -1));
    }
}
