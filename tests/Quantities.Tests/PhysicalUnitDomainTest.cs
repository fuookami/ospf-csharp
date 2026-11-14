#nullable enable

using System;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests;

public class PhysicalUnitDomainTest {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void PositivePowersPreserveSourceDomain(int index) {
        Assert.Equal(QuantityDomain.Discrete, SIBaseUnits.Bit.Pow(index).Domain);
        Assert.Equal(QuantityDomain.Continuous, SIBaseUnits.Meter.Pow(index).Domain);
    }

    [Fact]
    public void MultiplicationRespectsUnitDomainOverrides() {
        var discreteMeter = new AnonymousPhysicalUnit(
            quantity: SIBaseUnits.Meter.Quantity,
            conversionRule: SIBaseUnits.Meter.ConversionRule,
            domain: QuantityDomain.Discrete);
        var continuousBit = new AnonymousPhysicalUnit(
            quantity: SIBaseUnits.Bit.Quantity,
            conversionRule: SIBaseUnits.Bit.ConversionRule,
            domain: QuantityDomain.Continuous);

        Assert.Equal(QuantityDomain.Discrete, discreteMeter.Multiply(discreteMeter).Domain);
        Assert.Equal(QuantityDomain.Discrete, discreteMeter.Multiply(SIBaseUnits.Bit).Domain);
        Assert.Equal(QuantityDomain.Discrete, SIBaseUnits.Bit.Multiply(discreteMeter).Domain);
        Assert.Equal(QuantityDomain.Continuous, continuousBit.Multiply(SIBaseUnits.Bit).Domain);
        Assert.Equal(QuantityDomain.Continuous, SIBaseUnits.Bit.Multiply(continuousBit).Domain);
        Assert.Equal(QuantityDomain.Continuous, continuousBit.Multiply(discreteMeter).Domain);
    }

    [Fact]
    public void DivisionAndNegativePowersRemainContinuous() {
        var discreteMeter = new AnonymousPhysicalUnit(
            quantity: SIBaseUnits.Meter.Quantity,
            conversionRule: SIBaseUnits.Meter.ConversionRule,
            domain: QuantityDomain.Discrete);

        Assert.Equal(QuantityDomain.Continuous, SIBaseUnits.Bit.Divide(SIBaseUnits.Bit).Domain);
        Assert.Equal(QuantityDomain.Continuous, discreteMeter.Divide(discreteMeter).Domain);
        Assert.Equal(QuantityDomain.Continuous, SIBaseUnits.Bit.Divide(discreteMeter).Domain);
        Assert.Equal(QuantityDomain.Continuous, discreteMeter.Reciprocal().Domain);
        Assert.Equal(QuantityDomain.Continuous, SIBaseUnits.Bit.Pow(-2).Domain);
        Assert.Equal(QuantityDomain.Continuous, discreteMeter.Pow(-2).Domain);
    }

    [Fact]
    public void PositivePowersPreserveDimensionAndScale() {
        var discreteMeter = new AnonymousPhysicalUnit(
            quantity: SIBaseUnits.Meter.Quantity,
            conversionRule: new UnitConversionRule.Linear(Scale.Invoke(2) * Scale.Invoke(3)),
            domain: QuantityDomain.Discrete);

        foreach (int index in new[] { -3, -2, -1, 1, 2, 3 }) {
            PhysicalUnit powered = discreteMeter.Pow(index);
            Assert.Equal(discreteMeter.Quantity.Pow(index), powered.Quantity);
            Assert.Equal(index > 0 ? QuantityDomain.Discrete : QuantityDomain.Continuous, powered.Domain);

            var expectedScale = Scale.Invoke(1);
            for (int i = 0; i < System.Math.Abs(index); i++) {
                expectedScale = index > 0
                    ? expectedScale * discreteMeter.Scale
                    : expectedScale / discreteMeter.Scale;
            }
            Assert.Equal(expectedScale.Value, powered.Scale.Value);
        }
    }

    [Fact]
    public void AffineUnitsRejectCompoundOperations() {
        PhysicalUnit celsius = TemperatureUnits.Celsius;

        Assert.Throws<InvalidOperationException>(() => celsius.Multiply(SIBaseUnits.Bit));
        Assert.Throws<InvalidOperationException>(() => SIBaseUnits.Bit.Multiply(celsius));
        Assert.Throws<InvalidOperationException>(() => celsius.Divide(SIBaseUnits.Bit));
        Assert.Throws<InvalidOperationException>(() => SIBaseUnits.Bit.Divide(celsius));
        Assert.Throws<InvalidOperationException>(() => celsius.Pow(2));
        Assert.Throws<InvalidOperationException>(celsius.Reciprocal);
    }
}
