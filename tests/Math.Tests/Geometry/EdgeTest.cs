#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using System;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class EdgeTest {
    [Fact]
    public void Edge_Length() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(3.0), new Flt64(4.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        edge.Length.Value.Should().BeApproximately(5.0, 1e-10);
    }

    [Fact]
    public void Edge_LengthSquared() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(3.0), new Flt64(4.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        edge.LengthSquared.Value.Should().Be(25.0);
    }

    [Fact]
    public void Edge_Midpoint() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(4.0), new Flt64(6.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        Point<Dim2, Flt64> mid = edge.Midpoint();
        mid.X().Value.Should().Be(2.0);
        mid.Y().Value.Should().Be(3.0);
    }

    [Fact]
    public void Edge_Direction() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(4.0), new Flt64(6.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        edge.Direction.X().Value.Should().Be(3.0);
        edge.Direction.Y().Value.Should().Be(4.0);
    }

    [Fact]
    public void Edge_PointAt() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(10.0), new Flt64(10.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        Point<Dim2, Flt64> mid = edge.PointAt(new Flt64(0.5));
        mid.X().Value.Should().Be(5.0);
        mid.Y().Value.Should().Be(5.0);
    }

    [Fact]
    public void Edge_ContainsPoint() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(10.0), new Flt64(0.0));
        var edge = new Edge<Dim2, Flt64>(a, b);
        var mid = Point<Dim2, Flt64>.Point2(new Flt64(5.0), new Flt64(0.0));
        edge.ContainsPoint(mid).Should().BeTrue();
    }

    [Fact]
    public void Edge2D_Intersects() {
        var e1 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(2)));
        var e2 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(2)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(0)));
        e1.Intersects(e2).Should().BeTrue();
    }

    [Fact]
    public void Edge2D_IntersectionPoint() {
        var e1 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(2)));
        var e2 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(2)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(0)));
        Point<Dim2, Flt64>? pt = e1.IntersectionPoint(e2);
        pt.Should().NotBeNull();
        pt!.X().Value.Should().BeApproximately(1.0, 1e-10);
        pt!.Y().Value.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void Edge2D_Parallel_NoIntersection() {
        var e1 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(0)));
        var e2 = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(1)),
            Point<Dim2, Flt64>.Point2(new Flt64(2), new Flt64(1)));
        e1.Intersects(e2).Should().BeFalse();
    }

    [Fact]
    public void Edge2D_ClosestPoint() {
        var edge = new Edge<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(10), new Flt64(0)));
        var point = Point<Dim2, Flt64>.Point2(new Flt64(5), new Flt64(3));
        Point<Dim2, Flt64> closest = edge.ClosestPoint(point);
        closest.X().Value.Should().BeApproximately(5.0, 1e-10);
        closest.Y().Value.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void Edge_ApproxEq_Undirected() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(1), new Flt64(1));
        var e1 = new Edge<Dim2, Flt64>(a, b);
        var e2 = new Edge<Dim2, Flt64>(b, a);
        e1.ApproxEqUndirected(e2).Should().BeTrue();
        e1.ApproxEq(e2).Should().BeFalse();
    }
}
