#nullable enable
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 圆形/球体 / Circle/Sphere: 圆心、方向、半径。
/// A circle/sphere defined by center, direction, and radius.
/// </summary>
public sealed record Circle<D, V>(Point<D, V> Center, Vector<D, V> Direction, V Radius)
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V>
{
    /// <summary>由圆心和半径向量构造 / Construct from center and radius vector.</summary>
    public Circle(Point<D, V> center, Vector<D, V> radiusVec) : this(center, radiusVec.Unit, radiusVec.Norm) { }

    /// <summary>三角形外接圆 / Circumcircle of a 2D triangle.</summary>
    public static Circle<Dim2, Flt64> CircumcircleOf(Triangle<Dim2, Flt64> t)
    {
        var ax = t.P1.X(); var ay = t.P1.Y();
        var bx = t.P2.X(); var by = t.P2.Y();
        var cx = t.P3.X(); var cy = t.P3.Y();

        var two = new Flt64(2.0);
        var d = two.Times(ax.Times(by.Minus(cy)).Plus(bx.Times(cy.Minus(ay))).Plus(cx.Times(ay.Minus(by))));
        if (d.Eq(default)) throw new ArgumentException("Degenerate triangle: collinear points.");

        var ax2ay2 = ax.Sqr().Plus(ay.Sqr());
        var bx2by2 = bx.Sqr().Plus(by.Sqr());
        var cx2cy2 = cx.Sqr().Plus(cy.Sqr());

        var ux = ax2ay2.Times(by.Minus(cy)).Plus(bx2by2.Times(cy.Minus(ay))).Plus(cx2cy2.Times(ay.Minus(by))).Div(d);
        var uy = ax2ay2.Times(cx.Minus(bx)).Plus(bx2by2.Times(ax.Minus(cx))).Plus(cx2cy2.Times(bx.Minus(ax))).Div(d);

        var center = Point<Dim2, Flt64>.Point2(ux, uy);
        var radius = center.DistanceTo(t.P1);
        var dir = Vector<Dim2, Flt64>.Vector2(Flt64.Zero, Flt64.Zero);
        return new Circle<Dim2, Flt64>(center, dir, radius);
    }
}

/// <summary>二维圆形扩展 / 2D circle extensions.</summary>
public static class Circle2DExtensions
{
    /// <summary>圆心 X / Center X.</summary>
    public static Flt64 X(this Circle<Dim2, Flt64> c) => c.Center.X();

    /// <summary>圆心 Y / Center Y.</summary>
    public static Flt64 Y(this Circle<Dim2, Flt64> c) => c.Center.Y();

    /// <summary>面积 / Area.</summary>
    public static Flt64 Area(this Circle<Dim2, Flt64> c)
    {
        var pi = ((IFloatingNumber<Flt64>)c.Radius).Constants.Pi;
        return pi.Times(c.Radius).Times(c.Radius);
    }

    /// <summary>周长 / Circumference.</summary>
    public static Flt64 Circumference(this Circle<Dim2, Flt64> c)
    {
        var pi = ((IFloatingNumber<Flt64>)c.Radius).Constants.Pi;
        return new Flt64(2.0).Times(pi).Times(c.Radius);
    }

    /// <summary>直径 / Diameter.</summary>
    public static Flt64 Diameter(this Circle<Dim2, Flt64> c) => new Flt64(2.0).Times(c.Radius);

    /// <summary>点是否在圆内（含边界）/ Whether point is inside circle (inclusive).</summary>
    public static bool ContainsPoint(this Circle<Dim2, Flt64> c, Point<Dim2, Flt64> p) =>
        p.X().Minus(c.Center.X()).Sqr().Plus(p.Y().Minus(c.Center.Y()).Sqr()).Leq(c.Radius.Times(c.Radius));

    /// <summary>点是否在圆内（严格）/ Whether point is strictly inside circle.</summary>
    public static bool ContainsPointStrict(this Circle<Dim2, Flt64> c, Point<Dim2, Flt64> p) =>
        p.X().Minus(c.Center.X()).Sqr().Plus(p.Y().Minus(c.Center.Y()).Sqr()).Ls(c.Radius.Times(c.Radius));

    /// <summary>两圆是否相交 / Whether two circles intersect.</summary>
    public static bool Intersects(this Circle<Dim2, Flt64> c, Circle<Dim2, Flt64> other)
    {
        var dist = c.Center.DistanceTo(other.Center);
        return dist.Leq(c.Radius.Plus(other.Radius));
    }

    /// <summary>是否包含另一圆 / Whether contains another circle.</summary>
    public static bool ContainsCircle(this Circle<Dim2, Flt64> c, Circle<Dim2, Flt64> other)
    {
        var dist = c.Center.DistanceTo(other.Center);
        return dist.Plus(other.Radius).Leq(c.Radius);
    }

    /// <summary>点是否在边界上 / Whether point is on boundary.</summary>
    public static bool PointOnBoundary(this Circle<Dim2, Flt64> c, Point<Dim2, Flt64> p, Flt64? epsilon = null)
    {
        var eps = epsilon ?? new Flt64(1e-10);
        var dist = p.DistanceTo(c.Center);
        return (dist.Minus(c.Radius)).Abs().Leq(eps);
    }

    /// <summary>两圆是否相切 / Whether two circles are tangent.</summary>
    public static bool IsTangent(this Circle<Dim2, Flt64> c, Circle<Dim2, Flt64> other, Flt64? epsilon = null)
    {
        var eps = epsilon ?? new Flt64(1e-10);
        var dist = c.Center.DistanceTo(other.Center);
        var sumR = c.Radius.Plus(other.Radius);
        return (dist.Minus(sumR)).Abs().Leq(eps);
    }

    /// <summary>两圆交点 / Intersection points of two circles.</summary>
    public static IReadOnlyList<Point<Dim2, Flt64>> IntersectionPoints(
        this Circle<Dim2, Flt64> c, Circle<Dim2, Flt64> other)
    {
        var dist = c.Center.DistanceTo(other.Center);
        var r1 = c.Radius;
        var r2 = other.Radius;
        if (dist.Gr(r1.Plus(r2)) || dist.Ls(r1.Minus(r2).Abs()) || (dist.Eq(default) && r1.Eq(r2)))
            return Array.Empty<Point<Dim2, Flt64>>();

        var two = new Flt64(2.0);
        var a = r1.Sqr().Minus(r2.Sqr()).Plus(dist.Sqr()).Div(two.Times(dist));
        var h = r1.Sqr().Minus(a.Sqr()).Sqrt();

        var cx = c.Center.X();
        var cy = c.Center.Y();
        var ox = other.Center.X();
        var oy = other.Center.Y();

        var px = cx.Plus(a.Times(ox.Minus(cx)).Div(dist));
        var py = cy.Plus(a.Times(oy.Minus(cy)).Div(dist));

        var p1 = Point<Dim2, Flt64>.Point2(
            px.Plus(h.Times(oy.Minus(cy)).Div(dist)),
            py.Minus(h.Times(ox.Minus(cx)).Div(dist)));
        var p2 = Point<Dim2, Flt64>.Point2(
            px.Minus(h.Times(oy.Minus(cy)).Div(dist)),
            py.Plus(h.Times(ox.Minus(cx)).Div(dist)));

        if (h.Eq(default)) return new[] { p1 };
        return new[] { p1, p2 };
    }
}

/// <summary>三维球体扩展 / 3D sphere extensions.</summary>
public static class Circle3DExtensions
{
    /// <summary>体积 / Volume.</summary>
    public static Flt64 Volume(this Circle<Dim3, Flt64> s)
    {
        var pi = ((IFloatingNumber<Flt64>)s.Radius).Constants.Pi;
        return new Flt64(4.0).Div(new Flt64(3.0)).Times(pi).Times(s.Radius).Times(s.Radius).Times(s.Radius);
    }

    /// <summary>表面积 / Surface area.</summary>
    public static Flt64 SurfaceArea(this Circle<Dim3, Flt64> s)
    {
        var pi = ((IFloatingNumber<Flt64>)s.Radius).Constants.Pi;
        return new Flt64(4.0).Times(pi).Times(s.Radius).Times(s.Radius);
    }

    /// <summary>点是否在球内 / Whether point is inside sphere.</summary>
    public static bool ContainsPoint(this Circle<Dim3, Flt64> s, Point<Dim3, Flt64> p) =>
        p.X().Minus(s.Center.X()).Sqr().Plus(p.Y().Minus(s.Center.Y()).Sqr()).Plus(p.Z().Minus(s.Center.Z()).Sqr())
            .Leq(s.Radius.Times(s.Radius));
}
