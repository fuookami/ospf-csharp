#nullable enable
using System;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class VectorTest
{
    [Fact]
    public void Vector2_Creation()
    {
        var v = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        v.X().Value.Should().Be(3.0);
        v.Y().Value.Should().Be(4.0);
    }

    [Fact]
    public void Vector2_Norm()
    {
        var v = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        v.Norm.Value.Should().BeApproximately(5.0, 1e-10);
    }

    [Fact]
    public void Vector2_Unit()
    {
        var v = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        var u = v.Unit;
        u.Norm.Value.Should().BeApproximately(1.0, 1e-10);
        u.X().Value.Should().BeApproximately(0.6, 1e-10);
        u.Y().Value.Should().BeApproximately(0.8, 1e-10);
    }

    [Fact]
    public void Vector2_Dot()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(2.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        a.Dot(b).Value.Should().Be(11.0);
    }

    [Fact]
    public void Vector2_Cross()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(0.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(0.0), new Flt64(1.0));
        a.Cross(b).Value.Should().Be(1.0);
    }

    [Fact]
    public void Vector3_Cross()
    {
        var a = Vector<Dim3, Flt64>.Vector3(new Flt64(1.0), new Flt64(0.0), new Flt64(0.0));
        var b = Vector<Dim3, Flt64>.Vector3(new Flt64(0.0), new Flt64(1.0), new Flt64(0.0));
        var c = a.Cross(b);
        c.X().Value.Should().BeApproximately(0.0, 1e-10);
        c.Y().Value.Should().BeApproximately(0.0, 1e-10);
        c.Z().Value.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void Vector2_Angle()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(0.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(0.0), new Flt64(1.0));
        var angle = a.Angle(b);
        angle.Should().NotBeNull();
        angle!.Value.Value.Should().BeApproximately(global::System.Math.PI / 2, 1e-10);
    }

    [Fact]
    public void Vector2_Scale()
    {
        var v = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        var scaled = v.Scale(new Flt64(2.0));
        scaled.X().Value.Should().Be(6.0);
        scaled.Y().Value.Should().Be(8.0);
    }

    [Fact]
    public void Vector2_Addition()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(2.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        var c = a + b;
        c.X().Value.Should().Be(4.0);
        c.Y().Value.Should().Be(6.0);
    }

    [Fact]
    public void Vector2_Subtraction()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(5.0), new Flt64(7.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        var c = a - b;
        c.X().Value.Should().Be(2.0);
        c.Y().Value.Should().Be(3.0);
    }

    [Fact]
    public void Vector2_IsOrthogonal()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(0.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(0.0), new Flt64(1.0));
        a.IsOrthogonal(b, new Flt64(1e-10)).Should().BeTrue();
    }

    [Fact]
    public void Vector2_Project()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(0.0));
        var proj = a.Project(b);
        proj.Should().NotBeNull();
        proj!.X().Value.Should().BeApproximately(3.0, 1e-10);
        proj!.Y().Value.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void Vector2_NormSquared()
    {
        var v = Vector<Dim2, Flt64>.Vector2(new Flt64(3.0), new Flt64(4.0));
        v.NormSquared().Value.Should().Be(25.0);
    }

    [Fact]
    public void ZeroVector_Angle_ReturnsNull()
    {
        var a = Vector<Dim2, Flt64>.Vector2(new Flt64(0.0), new Flt64(0.0));
        var b = Vector<Dim2, Flt64>.Vector2(new Flt64(1.0), new Flt64(0.0));
        a.Angle(b).Should().BeNull();
    }
}
