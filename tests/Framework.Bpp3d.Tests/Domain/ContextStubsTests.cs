#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Bla;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Bla.Service;
using Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading;
using Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

public class ContextStubsTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void BLAContextShouldBeInstantiable() {
        var ctx = new BLAContext();
        ctx.Should().NotBeNull();
    }

    [Fact]
    public void BottomUpLeftJustifiedAlgorithmShouldHaveName() {
        var algo = new BottomUpLeftJustifiedAlgorithm();
        algo.Name.Should().Be("BottomUpLeftJustifiedAlgorithm");
    }

    [Fact]
    public void BottomUpLeftJustifiedAlgorithm3DShouldHaveName() {
        var algo = new BottomUpLeftJustifiedAlgorithm3D();
        algo.Name.Should().Be("BottomUpLeftJustifiedAlgorithm3D");
    }

    [Fact]
    public void BlockLoadingContextShouldBeInstantiable() {
        var ctx = new BlockLoadingContext();
        ctx.Should().NotBeNull();
    }

    [Fact]
    public void SimpleBlockGeneratorShouldHaveName() {
        var gen = new SimpleBlockGenerator();
        gen.Name.Should().Be("SimpleBlockGenerator");
    }

    [Fact]
    public void ComplexBlockGeneratorShouldHaveName() {
        var gen = new ComplexBlockGenerator();
        gen.Name.Should().Be("ComplexBlockGenerator");
    }

    [Fact]
    public void DepthFirstSearchAlgorithmShouldHaveName() {
        var algo = new DepthFirstSearchAlgorithm();
        algo.Name.Should().Be("DepthFirstSearchAlgorithm");
    }

    [Fact]
    public void MultiLayerHeuristicSearchAlgorithmShouldHaveName() {
        var algo = new MultiLayerHeuristicSearchAlgorithm();
        algo.Name.Should().Be("MultiLayerHeuristicSearchAlgorithm");
    }

    [Fact]
    public void SpaceShouldStoreDimensions() {
        var space = new Space<FltX>(M(2), M(3), M(4));
        D(space.Width).Should().BeApproximately(2.0, 1e-9);
        D(space.Height).Should().BeApproximately(3.0, 1e-9);
        D(space.Depth).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void SpaceVolumeShouldBeProduct() {
        var space = new Space<FltX>(M(2), M(3), M(4));
        D(space.Volume).Should().BeApproximately(24.0, 1e-9);
    }

    [Fact]
    public void ItemContextShouldBeInstantiable() {
        var ctx = new ItemContext();
        ctx.Should().NotBeNull();
    }
}
