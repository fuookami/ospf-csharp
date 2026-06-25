#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Concept;

public class AlgebraicStructureTest {
    [Fact]
    public void Flt64_ImplementsIFloatingNumber() =>
        // Flt64 should implement IFloatingNumber<Flt64>
        typeof(IFloatingNumber<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIRealNumber() => typeof(IRealNumber<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIScalar() => typeof(IScalar<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIField() => typeof(IField<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIIntegerNumber() => typeof(IIntegerNumber<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIInteger() => typeof(IInteger<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIRealNumber() => typeof(IRealNumber<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void UInt8_ImplementsIUIntegerNumber() => typeof(IUIntegerNumber<UInt8>).IsAssignableFrom(typeof(UInt8)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIBounded() => typeof(IBounded<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIInfinite() => typeof(IInfinite<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIEpsilon() => typeof(IEpsilon<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void RtnX_ImplementsIFloatingNumber() => typeof(IFloatingNumber<RtnX>).IsAssignableFrom(typeof(RtnX)).Should().BeTrue();
}
