#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryPlacementTest
{
    private static Quantity<Flt64> Q(double v) => new(new Flt64(v), SIBaseUnits.Meter);

    [Fact]
    public void Placement2_Box()
    {
        var placement = new QuantityPlacement2<Flt64>(Q(1), Q(2),
            new QuantityRectangle2<Flt64>(Q(3), Q(4)));
        Assert.Equal(new Flt64(3), placement.Width.Value);
        Assert.Equal(new Flt64(4), placement.Height.Value);
    }

    [Fact]
    public void Placement2_Contains()
    {
        var placement = new QuantityPlacement2<Flt64>(Q(0), Q(0),
            new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Assert.True(placement.Contains(Q(5), Q(5)).Value);
        Assert.False(placement.Contains(Q(15), Q(5)).Value);
    }

    [Fact]
    public void Placement2_Overlapped()
    {
        var p1 = new QuantityPlacement2<Flt64>(Q(0), Q(0),
            new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        var p2 = new QuantityPlacement2<Flt64>(Q(5), Q(5),
            new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Assert.True(p1.Overlapped(p2).Value);
    }

    [Fact]
    public void Placement2_Intersect()
    {
        var p1 = new QuantityPlacement2<Flt64>(Q(0), Q(0),
            new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        var p2 = new QuantityPlacement2<Flt64>(Q(5), Q(5),
            new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        var result = p1.Intersect(p2);
        Assert.True(result.IsOk);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void Placement3_Box()
    {
        var placement = new QuantityPlacement3<Flt64>(Q(1), Q(2), Q(3),
            new QuantityCuboid3<Flt64>(Q(4), Q(5), Q(6)));
        Assert.Equal(new Flt64(4), placement.Width.Value);
        Assert.Equal(new Flt64(5), placement.Height.Value);
        Assert.Equal(new Flt64(6), placement.Depth.Value);
    }

    [Fact]
    public void Placement3_Contains()
    {
        var placement = new QuantityPlacement3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        Assert.True(placement.Contains(Q(5), Q(5), Q(5)).Value);
        Assert.False(placement.Contains(Q(15), Q(5), Q(5)).Value);
    }

    [Fact]
    public void Placement3_Overlapped()
    {
        var p1 = new QuantityPlacement3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var p2 = new QuantityPlacement3<Flt64>(Q(5), Q(5), Q(5),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        Assert.True(p1.Overlapped(p2).Value);
    }

    [Fact]
    public void Placement3_Intersect()
    {
        var p1 = new QuantityPlacement3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var p2 = new QuantityPlacement3<Flt64>(Q(5), Q(5), Q(5),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var result = p1.Intersect(p2);
        Assert.True(result.IsOk);
        Assert.NotNull(result.Value);
        Assert.Equal(new Flt64(5), result.Value!.X.Value);
    }
}
