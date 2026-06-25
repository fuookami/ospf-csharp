#nullable enable
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三角形 / Triangle: 三个顶点。
/// A triangle defined by three vertices.
/// </summary>
public sealed record Triangle<D, V>(Point<D, V> P1, Point<D, V> P2, Point<D, V> P3)
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V>
{
    /// <summary>边 1 / Edge 1.</summary>
    public Edge<D, V> E1 => new(P1, P2);
    /// <summary>边 2 / Edge 2.</summary>
    public Edge<D, V> E2 => new(P2, P3);
    /// <summary>边 3 / Edge 3.</summary>
    public Edge<D, V> E3 => new(P3, P1);

    /// <summary>边列表 / Edges.</summary>
    public IReadOnlyList<Edge<D, V>> Edges => new[] { E1, E2, E3, };

    /// <summary>顶点列表 / Vertices.</summary>
    public IReadOnlyList<Point<D, V>> Vertices => new[] { P1, P2, P3, };

    /// <summary>周长 / Perimeter.</summary>
    public V Perimeter => E1.Length.Plus(E2.Length).Plus(E3.Length);

    /// <summary>重心 / Centroid.</summary>
    public Point<D, V> Centroid
    {
        get
        {
            var three = ((IRealNumberConstants<V>)P1[0].Constants).Three;
            return new(P1.Indices.Select(i => P1[i].Plus(P2[i]).Plus(P3[i]).Div(three)).ToArray(), P1.Dim);
        }
    }

    /// <summary>退化（存在重合顶点）/ Degenerate (has coincident vertices).</summary>
    public bool IsDegenerate => P1.ApproxEq(P2) || P2.ApproxEq(P3) || P3.ApproxEq(P1);

    /// <summary>非法（三点共线）/ Illegal (three collinear points).</summary>
    public bool Illegal =>
        P1.Indices.Any(i => P1[i].Eq(P2[i]) && P2[i].Eq(P3[i]));

    /// <summary>面积（海伦公式）/ Area (Heron's formula).</summary>
    public V Area
    {
        get
        {
            var (a, b, c) = (E1.Length, E2.Length, E3.Length);
            var two = ((IRealNumberConstants<V>)P1[0].Constants).Two;
            var p = a.Plus(b).Plus(c).Div(two);
            var s = p.Times(p.Minus(a)).Times(p.Minus(b)).Times(p.Minus(c));
            return s.Sqrt();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"Triangle({P1}, {P2}, {P3})";
}

/// <summary>二维三角形扩展 / 2D triangle extensions.</summary>
public static class Triangle2DExtensions
{
    /// <summary>二维面积（叉积法）/ 2D area (cross product).</summary>
    public static Flt64 Area2D(this Triangle<Dim2, Flt64> t)
    {
        var v1x = t.P2.X().Minus(t.P1.X());
        var v1y = t.P2.Y().Minus(t.P1.Y());
        var v2x = t.P3.X().Minus(t.P1.X());
        var v2y = t.P3.Y().Minus(t.P1.Y());
        return v1x.Times(v2y).Minus(v1y.Times(v2x)).Abs().Div(new Flt64(2.0));
    }

    /// <summary>点是否在三角形内（重心法）/ Whether point is inside triangle (barycentric).</summary>
    public static bool ContainsPoint(this Triangle<Dim2, Flt64> t, Point<Dim2, Flt64> point)
    {
        var d1 = Sign(point, t.P1, t.P2);
        var d2 = Sign(point, t.P2, t.P3);
        var d3 = Sign(point, t.P3, t.P1);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);

        static double Sign(Point<Dim2, Flt64> p1, Point<Dim2, Flt64> p2, Point<Dim2, Flt64> p3) =>
            (p1.X().Minus(p2.X())).Times(p3.Y().Minus(p2.Y())).Minus(
                (p3.X().Minus(p2.X())).Times(p1.Y().Minus(p2.Y()))).Value;
    }

    /// <summary>外接圆 / Circumcircle.</summary>
    public static Circle<Dim2, Flt64> Circumcircle(this Triangle<Dim2, Flt64> t) =>
        Circle<Dim2, Flt64>.CircumcircleOf(t);

    /// <summary>外心 / Circumcenter.</summary>
    public static Point<Dim2, Flt64> Circumcenter(this Triangle<Dim2, Flt64> t) =>
        t.Circumcircle().Center;

    /// <summary>内心 / Incenter.</summary>
    public static Point<Dim2, Flt64> Incenter(this Triangle<Dim2, Flt64> t)
    {
        var a = t.E2.Length;
        var b = t.E3.Length;
        var c = t.E1.Length;
        var p = a.Plus(b).Plus(c);
        var x = a.Times(t.P1.X()).Plus(b.Times(t.P2.X())).Plus(c.Times(t.P3.X())).Div(p);
        var y = a.Times(t.P1.Y()).Plus(b.Times(t.P2.Y())).Plus(c.Times(t.P3.Y())).Div(p);
        return Point<Dim2, Flt64>.Point2(x, y);
    }
}

/// <summary>三维三角形扩展 / 3D triangle extensions.</summary>
public static class Triangle3DExtensions
{
    /// <summary>三维面积（叉积法）/ 3D area (cross product).</summary>
    public static Flt64 Area3D(this Triangle<Dim3, Flt64> t)
    {
        var v1 = Vector<Dim3, Flt64>.Vector3(
            t.P2.X().Minus(t.P1.X()), t.P2.Y().Minus(t.P1.Y()), t.P2.Z().Minus(t.P1.Z()));
        var v2 = Vector<Dim3, Flt64>.Vector3(
            t.P3.X().Minus(t.P1.X()), t.P3.Y().Minus(t.P1.Y()), t.P3.Z().Minus(t.P1.Z()));
        return v1.Cross(v2).Norm.Div(new Flt64(2.0));
    }

    /// <summary>法向量 / Normal vector.</summary>
    public static Vector<Dim3, Flt64>? Normal(this Triangle<Dim3, Flt64> t)
    {
        var v1 = Vector<Dim3, Flt64>.Vector3(
            t.P2.X().Minus(t.P1.X()), t.P2.Y().Minus(t.P1.Y()), t.P2.Z().Minus(t.P1.Z()));
        var v2 = Vector<Dim3, Flt64>.Vector3(
            t.P3.X().Minus(t.P1.X()), t.P3.Y().Minus(t.P1.Y()), t.P3.Z().Minus(t.P1.Z()));
        var cross = v1.Cross(v2);
        return cross.Norm.Eq(default) ? null : cross.Unit;
    }
}
