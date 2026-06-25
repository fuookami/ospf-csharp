#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

public class SolverValueAdapterTests {
    [Fact]
    public void DefaultConstructorShouldUseScaleOne() {
        var adapter = new Bpp3dSolverValueAdapter();
        adapter.Scale.Should().Be(Flt64.One);
    }

    [Fact]
    public void ConstructorShouldAcceptCustomScale() {
        var adapter = new Bpp3dSolverValueAdapter(new Flt64(2.5));
        adapter.Scale.ToDouble().Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void AmountToSolverShouldConvertUInt64() {
        var adapter = new Bpp3dSolverValueAdapter();
        var val = new UInt64(42);
        adapter.AmountToSolver(val).ToDouble().Should().BeApproximately(42.0, 1e-9);
    }

    [Fact]
    public void LengthToSolverShouldReturnQuantityValue() {
        var adapter = new Bpp3dSolverValueAdapter();
        var q = new Quantity<Flt64>(new Flt64(3.5), SIBaseUnits.Meter);
        adapter.LengthToSolver(q).ToDouble().Should().BeApproximately(3.5, 1e-9);
    }

    [Fact]
    public void AreaToSolverShouldReturnQuantityValue() {
        var adapter = new Bpp3dSolverValueAdapter();
        var q = new Quantity<Flt64>(new Flt64(10.0), SIBaseUnits.Meter.Pow(2));
        adapter.AreaToSolver(q).ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void VolumeToSolverShouldReturnQuantityValue() {
        var adapter = new Bpp3dSolverValueAdapter();
        var q = new Quantity<Flt64>(new Flt64(27.0), SIBaseUnits.Meter.Pow(3));
        adapter.VolumeToSolver(q).ToDouble().Should().BeApproximately(27.0, 1e-9);
    }

    [Fact]
    public void DepthToSolverShouldDelegateToLengthToSolver() {
        var adapter = new Bpp3dSolverValueAdapter();
        var q = new Quantity<Flt64>(new Flt64(1.5), SIBaseUnits.Meter);
        adapter.DepthToSolver(q).ToDouble().Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void WeightToSolverShouldReturnQuantityValue() {
        var adapter = new Bpp3dSolverValueAdapter();
        var q = new Quantity<Flt64>(new Flt64(5.0), SIBaseUnits.Kilogram);
        adapter.WeightToSolver(q).ToDouble().Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void ScaledBpp3dSolverValueAdapterShouldInheritScale() {
        var adapter = new ScaledBpp3dSolverValueAdapter(new Flt64(3.0));
        adapter.Scale.ToDouble().Should().BeApproximately(3.0, 1e-9);
    }
}
