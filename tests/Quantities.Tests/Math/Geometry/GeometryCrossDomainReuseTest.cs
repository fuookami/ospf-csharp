#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryCrossDomainReuseTest
{
    private static Quantity<Flt64> Q(double v) => new(new Flt64(v), SIBaseUnits.Meter);

    [Fact]
    public void Cuboid3_AtOrigin_CreatesBox3()
    {
        var cuboid = new QuantityCuboid3<Flt64>(Q(2), Q(3), Q(4));
        var box = cuboid.AtOrigin();
        Assert.Equal(new Flt64(0), box.X.Value);
        Assert.Equal(new Flt64(0), box.Y.Value);
        Assert.Equal(new Flt64(0), box.Z.Value);
        Assert.Equal(cuboid, box.Cuboid);
    }

    [Fact]
    public void Cuboid3_At_CreatesBox3AtPosition()
    {
        var cuboid = new QuantityCuboid3<Flt64>(Q(2), Q(3), Q(4));
        var box = cuboid.At(Q(1), Q(2), Q(3));
        Assert.Equal(new Flt64(1), box.X.Value);
        Assert.Equal(new Flt64(2), box.Y.Value);
        Assert.Equal(new Flt64(3), box.Z.Value);
    }

    [Fact]
    public void Cuboid3_Volume()
    {
        var cuboid = new QuantityCuboid3<Flt64>(Q(2), Q(3), Q(4));
        Assert.Equal(new Flt64(24.0), cuboid.Volume.Value);
    }

    [Fact]
    public void Cuboid3_Along()
    {
        var cuboid = new QuantityCuboid3<Flt64>(Q(2), Q(3), Q(4));
        Assert.Equal(Q(2), cuboid.Along(Axis3.X));
        Assert.Equal(Q(3), cuboid.Along(Axis3.Y));
        Assert.Equal(Q(4), cuboid.Along(Axis3.Z));
    }

    [Fact]
    public void Cuboid3_Permute()
    {
        var cuboid = new QuantityCuboid3<Flt64>(Q(2), Q(3), Q(4));
        var permuted = cuboid.Permute(AxisPermutation3.ZYX);
        Assert.Equal(Q(4), permuted.Width);
        Assert.Equal(Q(3), permuted.Height);
        Assert.Equal(Q(2), permuted.Depth);
    }

    [Fact]
    public void Box3_Contains()
    {
        var box = new QuantityBox3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        Assert.True(box.Contains(Q(5), Q(5), Q(5)).Value);
        Assert.False(box.Contains(Q(15), Q(5), Q(5)).Value);
    }

    [Fact]
    public void Box3_Overlapped()
    {
        var box1 = new QuantityBox3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var box2 = new QuantityBox3<Flt64>(Q(5), Q(5), Q(5),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        Assert.True(box1.Overlapped(box2).Value);
    }

    [Fact]
    public void Box3_Intersect()
    {
        var box1 = new QuantityBox3<Flt64>(Q(0), Q(0), Q(0),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var box2 = new QuantityBox3<Flt64>(Q(5), Q(5), Q(5),
            new QuantityCuboid3<Flt64>(Q(10), Q(10), Q(10)));
        var result = box1.Intersect(box2);
        Assert.True(result.IsOk);
        Assert.NotNull(result.Value);
        Assert.Equal(new Flt64(5), result.Value!.Cuboid.Width.Value);
        Assert.Equal(new Flt64(5), result.Value!.Cuboid.Height.Value);
        Assert.Equal(new Flt64(5), result.Value!.Cuboid.Depth.Value);
    }
}
