#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using System;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class GeometryPrimitiveTest {
    [Fact]
    public void Dim2_Size_Is2() {
        var dim = default(Dim2);
        dim.Size.Should().Be(2);
    }

    [Fact]
    public void Dim3_Size_Is3() {
        var dim = default(Dim3);
        dim.Size.Should().Be(3);
    }

    [Fact]
    public void Dim4_Size_Is4() {
        var dim = default(Dim4);
        dim.Size.Should().Be(4);
    }

    [Fact]
    public void Dim2_Instance_IsSingleton() {
        Dim2 a = Dim2.Instance;
        Dim2 b = Dim2.Instance;
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Point2_Creation() {
        var p = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        p.X().Value.Should().Be(1.0);
        p.Y().Value.Should().Be(2.0);
        p.Size.Should().Be(2);
    }

    [Fact]
    public void Point3_Creation() {
        var p = Point<Dim3, Flt64>.Point3(new Flt64(1.0), new Flt64(2.0), new Flt64(3.0));
        p.X().Value.Should().Be(1.0);
        p.Y().Value.Should().Be(2.0);
        p.Z().Value.Should().Be(3.0);
        p.Size.Should().Be(3);
    }

    [Fact]
    public void Point2_Origin() {
        Point<Dim2, Flt64> origin = Point<Dim2, Flt64>.Origin2;
        origin.X().Value.Should().Be(0.0);
        origin.Y().Value.Should().Be(0.0);
    }

    [Fact]
    public void Point3_Origin() {
        Point<Dim3, Flt64> origin = Point<Dim3, Flt64>.Origin3;
        origin.X().Value.Should().Be(0.0);
        origin.Y().Value.Should().Be(0.0);
        origin.Z().Value.Should().Be(0.0);
    }

    [Fact]
    public void Point2_Addition() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(3.0), new Flt64(4.0));
        Point<Dim2, Flt64> c = a + b;
        c.X().Value.Should().Be(4.0);
        c.Y().Value.Should().Be(6.0);
    }

    [Fact]
    public void Point2_Subtraction() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(5.0), new Flt64(7.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(3.0), new Flt64(4.0));
        Point<Dim2, Flt64> c = a - b;
        c.X().Value.Should().Be(2.0);
        c.Y().Value.Should().Be(3.0);
    }

    [Fact]
    public void Point2_Midpoint() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(0.0), new Flt64(0.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(4.0), new Flt64(6.0));
        Point<Dim2, Flt64> mid = a.Midpoint(b);
        mid.X().Value.Should().Be(2.0);
        mid.Y().Value.Should().Be(3.0);
    }

    [Fact]
    public void Point2_Equality() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Point2_ApproxEq() {
        var a = Point<Dim2, Flt64>.Point2(new Flt64(1.0), new Flt64(2.0));
        var b = Point<Dim2, Flt64>.Point2(new Flt64(1.0 + 1e-16), new Flt64(2.0 - 1e-16));
        a.ApproxEq(b).Should().BeTrue();
    }

    [Fact]
    public void Point2_Create_HasCorrectDimension() {
        var p = Point<Dim2, Flt64>.Create(new Flt64(1), new Flt64(2));
        p.Count.Should().Be(2);
        p.X().Value.Should().Be(1.0);
        p.Y().Value.Should().Be(2.0);
    }

    [Fact]
    public void Axis2_Values() {
        Axis2.X.Should().Be(Axis2.X);
        Axis2.Y.Should().Be(Axis2.Y);
    }

    [Fact]
    public void Axis3_Values() {
        Axis3.X.Should().Be(Axis3.X);
        Axis3.Y.Should().Be(Axis3.Y);
        Axis3.Z.Should().Be(Axis3.Z);
    }

    [Fact]
    public void AxisPlane3_XY() {
        AxisPlane3.XY.FirstAxis.Should().Be(Axis3.X);
        AxisPlane3.XY.SecondAxis.Should().Be(Axis3.Y);
        AxisPlane3.XY.NormalAxis.Should().Be(Axis3.Z);
        AxisPlane3.XY.Contains(Axis3.X).Should().BeTrue();
        AxisPlane3.XY.Contains(Axis3.Z).Should().BeFalse();
    }

    [Fact]
    public void AxisPermutation2_Apply() {
        var rect = new Rectangle2<Flt64>(new Flt64(3.0), new Flt64(5.0));
        Rectangle2<Flt64> permuted = AxisPermutation2.YX.Apply(rect);
        permuted.Width.Value.Should().Be(5.0);
        permuted.Height.Value.Should().Be(3.0);
    }
}
