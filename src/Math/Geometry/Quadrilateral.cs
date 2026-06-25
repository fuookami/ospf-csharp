#nullable enable
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 四边形 / Quadrilateral: 四个顶点。
/// A quadrilateral defined by four vertices.
/// </summary>
public sealed record Quadrilateral<D, V>(Point<D, V> P1, Point<D, V> P2, Point<D, V> P3, Point<D, V> P4)
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V>
{
    /// <summary>边 1 / Edge 1.</summary>
    public Edge<D, V> E1 => new(P1, P2);
    /// <summary>边 2 / Edge 2.</summary>
    public Edge<D, V> E2 => new(P2, P3);
    /// <summary>边 3 / Edge 3.</summary>
    public Edge<D, V> E3 => new(P3, P4);
    /// <summary>边 4 / Edge 4.</summary>
    public Edge<D, V> E4 => new(P4, P1);

    /// <summary>边列表 / Edges.</summary>
    public IReadOnlyList<Edge<D, V>> Edges => new[] { E1, E2, E3, E4, };

    /// <summary>对角线 / Diagonals.</summary>
    public IReadOnlyList<Edge<D, V>> Diagonals => new[] { new Edge<D, V>(P1, P3), new Edge<D, V>(P2, P4), };

    /// <summary>周长 / Perimeter.</summary>
    public V Perimeter => E1.Length.Plus(E2.Length).Plus(E3.Length).Plus(E4.Length);

    /// <summary>重心 / Centroid.</summary>
    public Point<D, V> Centroid
    {
        get
        {
            var two = ((IRealNumberConstants<V>)P1[0].Constants).Two;
            var four = two.Plus(two);
            return new(P1.Indices.Select(i => P1[i].Plus(P2[i]).Plus(P3[i]).Plus(P4[i]).Div(four)).ToArray(), P1.Dim);
        }
    }

    /// <summary>面积（三角形分解）/ Area (triangle decomposition).</summary>
    public V AreaByTriangles =>
        new Triangle<D, V>(P1, P2, P3).Area.Plus(new Triangle<D, V>(P1, P3, P4).Area);
}

/// <summary>二维四边形扩展 / 2D quadrilateral extensions.</summary>
public static class Quadrilateral2DExtensions
{
    /// <summary>二维四边形面积（鞋带公式）/ 2D area (Shoelace).</summary>
    public static Flt64 Area(this Quadrilateral<Dim2, Flt64> q)
    {
        var sum1 = q.P1.X().Times(q.P2.Y()).Plus(q.P2.X().Times(q.P3.Y()))
                       .Plus(q.P3.X().Times(q.P4.Y())).Plus(q.P4.X().Times(q.P1.Y()));
        var sum2 = q.P1.Y().Times(q.P2.X()).Plus(q.P2.Y().Times(q.P3.X()))
                       .Plus(q.P3.Y().Times(q.P4.X())).Plus(q.P4.Y().Times(q.P1.X()));
        var d = sum1.Minus(sum2);
        var abs = d.Ls(default) ? d.Negate() : d;
        return abs.Div(new Flt64(2.0));
    }

    /// <summary>是否为凸四边形 / Whether convex.</summary>
    public static bool IsConvex(this Quadrilateral<Dim2, Flt64> q)
    {
        var cross1 = Cross2D(q.P1, q.P2, q.P3);
        var cross2 = Cross2D(q.P2, q.P3, q.P4);
        var cross3 = Cross2D(q.P3, q.P4, q.P1);
        var cross4 = Cross2D(q.P4, q.P1, q.P2);
        var pos = cross1 > 0 && cross2 > 0 && cross3 > 0 && cross4 > 0;
        var neg = cross1 < 0 && cross2 < 0 && cross3 < 0 && cross4 < 0;
        return pos || neg;
    }

    /// <summary>非法（面积为零）/ Illegal (zero area).</summary>
    public static bool Illegal(this Quadrilateral<Dim2, Flt64> q) => q.Area().Eq(default);

    /// <summary>创建二维四边形 / Create 2D quadrilateral.</summary>
    public static Quadrilateral<Dim2, Flt64> Quadrilateral2(
        Point<Dim2, Flt64> p1, Point<Dim2, Flt64> p2, Point<Dim2, Flt64> p3, Point<Dim2, Flt64> p4) => new(p1, p2, p3, p4);

    private static double Cross2D(Point<Dim2, Flt64> a, Point<Dim2, Flt64> b, Point<Dim2, Flt64> c) =>
        (b.X().Minus(a.X())).Times(c.Y().Minus(a.Y())).Minus(
            (b.Y().Minus(a.Y())).Times(c.X().Minus(a.X()))).Value;
}
