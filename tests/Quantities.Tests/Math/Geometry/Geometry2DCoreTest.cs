#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Math.Geometry;

public class Geometry2DCoreTest {
    private static Quantity<Flt64> Q(double v) => new(new Flt64(v), SIBaseUnits.Meter);

    [Fact]
    public void Rectangle2_Area() {
        var rect = new QuantityRectangle2<Flt64>(Q(3), Q(4));
        Quantity<Flt64> area = rect.Area;
        Assert.Equal(new Flt64(12.0), area.Value);
    }

    [Fact]
    public void Rectangle2_Along() {
        var rect = new QuantityRectangle2<Flt64>(Q(3), Q(4));
        Assert.Equal(Q(3), rect.Along(Axis2.X));
        Assert.Equal(Q(4), rect.Along(Axis2.Y));
    }

    [Fact]
    public void Circle2_Diameter() {
        var circle = new QuantityCircle2<Flt64>(Q(5));
        Assert.Equal(new Flt64(10.0), circle.Diameter.Value);
    }

    [Fact]
    public void Circle2_Area() {
        var circle = new QuantityCircle2<Flt64>(Q(3));
        Quantity<Flt64> area = circle.Area(new Flt64(3.14159));
        Assert.True(System.Math.Abs(area.Value.ToDouble() - 3.14159 * 9) < 0.01);
    }

    [Fact]
    public void Box2_RectangleContains() {
        var box = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Result<bool, ErrorCode, Error<ErrorCode>> result = box.Contains(Q(5), Q(5));
        Assert.True(result.IsOk);
        Assert.True(result.Value);
    }

    [Fact]
    public void Box2_RectangleDoesNotContain() {
        var box = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Result<bool, ErrorCode, Error<ErrorCode>> result = box.Contains(Q(15), Q(5));
        Assert.True(result.IsOk);
        Assert.False(result.Value);
    }

    [Fact]
    public void Box2_RectangleOverlap() {
        var box1 = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        var box2 = new QuantityBox2<Flt64>(Q(5), Q(5), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Result<bool, ErrorCode, Error<ErrorCode>> result = box1.Overlapped(box2);
        Assert.True(result.IsOk);
        Assert.True(result.Value);
    }

    [Fact]
    public void Box2_RectangleNoOverlap() {
        var box1 = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(5), Q(5)));
        var box2 = new QuantityBox2<Flt64>(Q(10), Q(10), new QuantityRectangle2<Flt64>(Q(5), Q(5)));
        Result<bool, ErrorCode, Error<ErrorCode>> result = box1.Overlapped(box2);
        Assert.True(result.IsOk);
        Assert.False(result.Value);
    }

    [Fact]
    public void Box2_RectangleIntersect() {
        var box1 = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        var box2 = new QuantityBox2<Flt64>(Q(5), Q(5), new QuantityRectangle2<Flt64>(Q(10), Q(10)));
        Result<QuantityBox2<Flt64>?, ErrorCode, Error<ErrorCode>> result = box1.Intersect(box2);
        Assert.True(result.IsOk);
        Assert.NotNull(result.Value);
        Assert.Equal(new Flt64(5.0), result.Value!.X.Value);
        Assert.Equal(new Flt64(5.0), result.Value!.Y.Value);
    }

    [Fact]
    public void Box2_RectangleDisjointIntersect() {
        var box1 = new QuantityBox2<Flt64>(Q(0), Q(0), new QuantityRectangle2<Flt64>(Q(5), Q(5)));
        var box2 = new QuantityBox2<Flt64>(Q(10), Q(10), new QuantityRectangle2<Flt64>(Q(5), Q(5)));
        Result<QuantityBox2<Flt64>?, ErrorCode, Error<ErrorCode>> result = box1.Intersect(box2);
        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public void AxisPermutation2_ApplyRectangle() {
        var rect = new QuantityRectangle2<Flt64>(Q(3), Q(4));
        QuantityRectangle2<Flt64> permuted = QuantityAxisPermutation2.YX.Apply(rect);
        Assert.Equal(Q(4), permuted.Width);
        Assert.Equal(Q(3), permuted.Height);
    }
}
