#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using System;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class TriangleTest {
    [Fact]
    public void Triangle_Perimeter() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        t.Perimeter.Value.Should().BeApproximately(12.0, 1e-10);
    }

    [Fact]
    public void Triangle_Area2D() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        t.Area2D().Value.Should().Be(6.0);
    }

    [Fact]
    public void Triangle_HeronArea() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        t.Area.Value.Should().BeApproximately(6.0, 1e-10);
    }

    [Fact]
    public void Triangle_Centroid() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(6), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(6)));
        Point<Dim2, Flt64> c = t.Centroid;
        c.X().Value.Should().Be(2.0);
        c.Y().Value.Should().Be(2.0);
    }

    [Fact]
    public void Triangle_IsDegenerate() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(1), new Flt64(1)));
        t.IsDegenerate.Should().BeTrue();
    }

    [Fact]
    public void Triangle_Edges() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        t.Edges.Should().HaveCount(3);
    }

    [Fact]
    public void Triangle3D_Area3D() {
        var t = new Triangle<Dim3, Flt64>(
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(1), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(1), new Flt64(0)));
        t.Area3D().Value.Should().BeApproximately(0.5, 1e-10);
    }

    [Fact]
    public void Triangle3D_Normal() {
        var t = new Triangle<Dim3, Flt64>(
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(1), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(1), new Flt64(0)));
        Vector<Dim3, Flt64>? normal = t.Normal();
        normal.Should().NotBeNull();
        normal!.Z().Value.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void Triangle_ContainsPoint() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(10), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(10)));
        var inside = Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(2));
        t.ContainsPoint(inside).Should().BeTrue();
        var outside = Point<Dim2, Flt64>.Point2(new Flt64(8), new Flt64(8));
        t.ContainsPoint(outside).Should().BeFalse();
    }

    [Fact]
    public void Triangle_Circumcircle() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        Circle<Dim2, Flt64> cc = t.Circumcircle();
        cc.Center.X().Value.Should().BeApproximately(2.0, 1e-10);
        cc.Center.Y().Value.Should().BeApproximately(2.0, 1e-10);
        cc.Radius.Value.Should().BeApproximately(global::System.Math.Sqrt(8.0), 1e-10);
    }

    [Fact]
    public void Triangle_Incenter() {
        var t = new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(3)));
        Point<Dim2, Flt64> ic = t.Incenter();
        // For 3-4-5 triangle, incenter is at (1, 1)
        ic.X().Value.Should().BeApproximately(1.0, 1e-10);
        ic.Y().Value.Should().BeApproximately(1.0, 1e-10);
    }
}
