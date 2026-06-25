#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryGenericFltXPathTest {
    private static Quantity<FltX> Q(decimal v) => new(new FltX(v), SIBaseUnits.Meter);

    [Fact]
    public void FltX_Cuboid3_Volume() {
        var cuboid = new QuantityCuboid3<FltX>(Q(2), Q(3), Q(4));
        Assert.Equal(new FltX(24m), cuboid.Volume.Value);
    }

    [Fact]
    public void FltX_Box3_Contains() {
        var box = new QuantityBox3<FltX>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<FltX>(Q(10), Q(10), Q(10)));
        Assert.True(box.Contains(Q(5), Q(5), Q(5)).Value);
        Assert.False(box.Contains(Q(15), Q(5), Q(5)).Value);
    }

    [Fact]
    public void FltX_Cylinder3_BoundingCuboid() {
        var cyl = new QuantityCylinder3<FltX>(Q(5), Q(10), Axis3.Z);
        QuantityCuboid3<FltX> bc = cyl.BoundingCuboid;
        Assert.Equal(new FltX(10m), bc.Width.Value);
        Assert.Equal(new FltX(10m), bc.Height.Value);
        Assert.Equal(new FltX(10m), bc.Depth.Value);
    }

    [Fact]
    public void FltX_Rectangle2_Area() {
        var rect = new QuantityRectangle2<FltX>(Q(3), Q(4));
        Assert.Equal(new FltX(12m), rect.Area.Value);
    }
}
