#nullable enable
using System;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class CircleTest
{
    [Fact]
    public void Circle_Creation()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        c.Radius.Value.Should().Be(5.0);
    }

    [Fact]
    public void Circle_Area()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        c.Area().Value.Should().BeApproximately(global::System.Math.PI * 25, 1e-6);
    }

    [Fact]
    public void Circle_Circumference()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        c.Circumference().Value.Should().BeApproximately(2 * global::System.Math.PI * 5, 1e-6);
    }

    [Fact]
    public void Circle_Diameter()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        c.Diameter().Value.Should().Be(10.0);
    }

    [Fact]
    public void Circle_ContainsPoint()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        var inside = Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0));
        c.ContainsPoint(inside).Should().BeTrue();
        var outside = Point<Dim2, Flt64>.Point2(new Flt64(6), new Flt64(0));
        c.ContainsPoint(outside).Should().BeFalse();
    }

    [Fact]
    public void Circle_ContainsPointStrict()
    {
        var center = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var dir = Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0));
        var c = new Circle<Dim2, Flt64>(center, dir, new Flt64(5.0));
        var onBoundary = Point<Dim2, Flt64>.Point2(new Flt64(5), new Flt64(0));
        c.ContainsPointStrict(onBoundary).Should().BeFalse();
    }

    [Fact]
    public void Circle_Intersects()
    {
        var c1 = new Circle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0)),
            new Flt64(5.0));
        var c2 = new Circle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(8), new Flt64(0)),
            Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0)),
            new Flt64(5.0));
        c1.Intersects(c2).Should().BeTrue();
    }

    [Fact]
    public void Circle_NoIntersect()
    {
        var c1 = new Circle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0)),
            new Flt64(1.0));
        var c2 = new Circle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(10), new Flt64(0)),
            Vector<Dim2, Flt64>.Vector2(new Flt64(1), new Flt64(0)),
            new Flt64(1.0));
        c1.Intersects(c2).Should().BeFalse();
    }

    [Fact]
    public void Circle_CircumcircleOf()
    {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        var cc = Circle<Dim2, Flt64>.CircumcircleOf(t);
        cc.Center.X().Value.Should().BeApproximately(2.0, 1e-10);
        cc.Center.Y().Value.Should().BeApproximately(2.0, 1e-10);
    }

    [Fact]
    public void Sphere_Volume()
    {
        var center = Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0));
        var dir = Vector<Dim3, Flt64>.Vector3(new Flt64(1), new Flt64(0), new Flt64(0));
        var s = new Circle<Dim3, Flt64>(center, dir, new Flt64(3.0));
        s.Volume().Value.Should().BeApproximately(4.0 / 3.0 * global::System.Math.PI * 27, 1e-4);
    }

    [Fact]
    public void Sphere_SurfaceArea()
    {
        var center = Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0));
        var dir = Vector<Dim3, Flt64>.Vector3(new Flt64(1), new Flt64(0), new Flt64(0));
        var s = new Circle<Dim3, Flt64>(center, dir, new Flt64(3.0));
        s.SurfaceArea().Value.Should().BeApproximately(4 * global::System.Math.PI * 9, 1e-4);
    }

    [Fact]
    public void Sphere_ContainsPoint()
    {
        var center = Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0));
        var dir = Vector<Dim3, Flt64>.Vector3(new Flt64(1), new Flt64(0), new Flt64(0));
        var s = new Circle<Dim3, Flt64>(center, dir, new Flt64(3.0));
        var inside = Point<Dim3, Flt64>.Point3(new Flt64(1), new Flt64(1), new Flt64(1));
        s.ContainsPoint(inside).Should().BeTrue();
    }
}
