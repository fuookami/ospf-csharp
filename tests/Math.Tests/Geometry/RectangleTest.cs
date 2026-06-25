#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class RectangleTest
{
    [Fact]
    public void Rectangle_FromCorners()
    {
        var lu = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(10));
        var rb = Point<Dim2, Flt64>.Point2(new Flt64(5), new Flt64(0));
        var rect = Rectangle<Dim2, Flt64>.FromCorners(lu, rb);
        rect.Length.Value.Should().Be(10.0);
        rect.Width.Value.Should().Be(5.0);
        rect.Area.Value.Should().Be(50.0);
    }

    [Fact]
    public void Rectangle_Area()
    {
        var p1 = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(0));
        var p2 = Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(0));
        var p3 = Point<Dim2, Flt64>.Point2(new Flt64(3), new Flt64(4));
        var p4 = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(4));
        var rect = new Rectangle<Dim2, Flt64>(p1, p2, p3, p4);
        rect.Area.Value.Should().Be(12.0);
    }

    [Fact]
    public void Rectangle_Contains()
    {
        var lu = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(10));
        var rb = Point<Dim2, Flt64>.Point2(new Flt64(10), new Flt64(0));
        var rect = Rectangle<Dim2, Flt64>.FromCorners(lu, rb);
        var inside = Point<Dim2, Flt64>.Point2(new Flt64(5), new Flt64(5));
        rect.Contains(inside).Should().BeTrue();
    }

    [Fact]
    public void Rectangle_Contains_Outside()
    {
        var lu = Point<Dim2, Flt64>.Point2(new Flt64(0), new Flt64(10));
        var rb = Point<Dim2, Flt64>.Point2(new Flt64(10), new Flt64(0));
        var rect = Rectangle<Dim2, Flt64>.FromCorners(lu, rb);
        var outside = Point<Dim2, Flt64>.Point2(new Flt64(15), new Flt64(5));
        rect.Contains(outside).Should().BeFalse();
    }
}
