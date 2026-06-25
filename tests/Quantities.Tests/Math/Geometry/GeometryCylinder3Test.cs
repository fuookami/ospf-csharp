#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class GeometryCylinder3Test
{
    private static Quantity<Flt64> Q(double v) => new(new Flt64(v), SIBaseUnits.Meter);

    [Fact]
    public void Cylinder3_Diameter()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        Assert.Equal(new Flt64(10.0), cyl.Diameter.Value);
    }

    [Fact]
    public void Cylinder3_BoundingCuboid_ZAxis()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        var bc = cyl.BoundingCuboid;
        Assert.Equal(new Flt64(10.0), bc.Width.Value);  // diameter
        Assert.Equal(new Flt64(10.0), bc.Height.Value); // diameter
        Assert.Equal(new Flt64(10.0), bc.Depth.Value);  // height
    }

    [Fact]
    public void Cylinder3_BoundingCuboid_XAxis()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.X);
        var bc = cyl.BoundingCuboid;
        Assert.Equal(new Flt64(10.0), bc.Width.Value);  // height
        Assert.Equal(new Flt64(10.0), bc.Height.Value); // diameter
        Assert.Equal(new Flt64(10.0), bc.Depth.Value);  // diameter
    }

    [Fact]
    public void Cylinder3_Along()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        Assert.Equal(new Flt64(10.0), cyl.Along(Axis3.Z).Value); // height
        Assert.Equal(new Flt64(10.0), cyl.Along(Axis3.X).Value); // diameter
    }

    [Fact]
    public void Cylinder3_ProjectionOn_PerpendicularPlane()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        var proj = cyl.ProjectionOn(AxisPlane3.XY);
        Assert.IsType<QuantityCircle2<Flt64>>(proj);
        var circle = (QuantityCircle2<Flt64>)proj;
        Assert.Equal(new Flt64(5.0), circle.Radius.Value);
    }

    [Fact]
    public void Cylinder3_ProjectionOn_ParallelPlane()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        var proj = cyl.ProjectionOn(AxisPlane3.XZ);
        Assert.IsType<QuantityRectangle2<Flt64>>(proj);
        var rect = (QuantityRectangle2<Flt64>)proj;
        Assert.Equal(new Flt64(10.0), rect.Width.Value);  // diameter
        Assert.Equal(new Flt64(10.0), rect.Height.Value); // height
    }

    [Fact]
    public void Cylinder3_BaseArea()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(3), Q(10), Axis3.Z);
        var area = cyl.BaseArea(new Flt64(3.14159));
        Assert.True(System.Math.Abs(area.Value.ToDouble() - 3.14159 * 9) < 0.01);
    }

    [Fact]
    public void Cylinder3_Volume()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(3), Q(10), Axis3.Z);
        var vol = cyl.Volume(new Flt64(3.14159));
        Assert.True(System.Math.Abs(vol.Value.ToDouble() - 3.14159 * 9 * 10) < 0.1);
    }

    [Fact]
    public void Cylinder3_Permute()
    {
        var cyl = new QuantityCylinder3<Flt64>(Q(5), Q(10), Axis3.Z);
        var result = cyl.Permute(AxisPermutation3.ZYX);
        Assert.True(result.IsOk);
        Assert.Equal(Axis3.X, result.Value.Axis); // Z -> X in ZYX
    }
}
