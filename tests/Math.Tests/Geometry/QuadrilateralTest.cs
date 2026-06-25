#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class QuadrilateralTest
{
    [Fact]
    public void Quadrilateral_Perimeter()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(3)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(3)));
        q.Perimeter.Value.Should().Be(14.0);
    }

    [Fact]
    public void Quadrilateral_Area()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(3)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(3)));
        q.Area().Value.Should().Be(12.0);
    }

    [Fact]
    public void Quadrilateral_IsConvex()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(3)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(3)));
        q.IsConvex().Should().BeTrue();
    }

    [Fact]
    public void Quadrilateral_Edges()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(3)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(3)));
        q.Edges.Should().HaveCount(4);
        q.Diagonals.Should().HaveCount(2);
    }

    [Fact]
    public void Quadrilateral_Centroid()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(4), new Flt64(4)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4)));
        var c = q.Centroid;
        c.X().Value.Should().Be(2.0);
        c.Y().Value.Should().Be(2.0);
    }

    [Fact]
    public void Quadrilateral_Illegal()
    {
        var q = Quadrilateral2DExtensions.Quadrilateral2(
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)),
            Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0)));
        q.Illegal().Should().BeTrue();
    }
}
