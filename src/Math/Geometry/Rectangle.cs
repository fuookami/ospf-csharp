#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 通用矩形（四个顶点，不要求轴对齐）/ General rectangle (four vertices, not necessarily axis-aligned).
/// </summary>
public sealed record Rectangle<D, V>
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V> {
    /// <summary>顶点 1 / Vertex 1.</summary>
    public Point<D, V> P1 { get; }
    /// <summary>顶点 2 / Vertex 2.</summary>
    public Point<D, V> P2 { get; }
    /// <summary>顶点 3 / Vertex 3.</summary>
    public Point<D, V> P3 { get; }
    /// <summary>顶点 4 / Vertex 4.</summary>
    public Point<D, V> P4 { get; }
    /// <summary>长边 / Longer side.</summary>
    public V Length { get; }
    /// <summary>短边 / Shorter side.</summary>
    public V Width { get; }

    public Rectangle(Point<D, V> p1, Point<D, V> p2, Point<D, V> p3, Point<D, V> p4) {
        P1 = p1;
        P2 = p2;
        P3 = p3;
        P4 = p4;
        var e1 = new Edge<D, V>(p1, p2);
        var e2 = new Edge<D, V>(p2, p3);
        V l1 = e1.Length;
        V l2 = e2.Length;
        (V mn, V mx) = GeometryOps.MinMax(l1, l2);
        Width = mn;
        Length = mx;
    }

    /// <summary>左上/右下构造二维轴对齐矩形 / 2D axis-aligned rectangle from corners.</summary>
    public static Rectangle<Dim2, Flt64> FromCorners(Point<Dim2, Flt64> leftUpper, Point<Dim2, Flt64> rightBottom) =>
        new(leftUpper,
            Point<Dim2, Flt64>.Point2(rightBottom.X(), leftUpper.Y()),
            rightBottom,
            Point<Dim2, Flt64>.Point2(leftUpper.X(), rightBottom.Y()));

    /// <summary>面积 / Area.</summary>
    public V Area => Length.Times(Width);

    private (V[] Mins, V[] Maxs) LeftUpperRightBottom {
        get {
            V[] mins = P1.Indices.Select(i => GeometryOps.MinMax(P1[i], P2[i], P3[i], P4[i]).Min).ToArray();
            V[] maxs = P1.Indices.Select(i => GeometryOps.MinMax(P1[i], P2[i], P3[i], P4[i]).Max).ToArray();
            return (mins, maxs);
        }
    }

    /// <summary>左上点 / Left-upper point.</summary>
    public Point<D, V> LeftUpperPoint => new(LeftUpperRightBottom.Mins, P1.Dim);

    /// <summary>右下点 / Right-bottom point.</summary>
    public Point<D, V> RightBottomPoint => new(LeftUpperRightBottom.Maxs, P1.Dim);
}

/// <summary>二维矩形扩展 / 2D rectangle extensions.</summary>
public static class Rectangle2DExtensions {
    /// <summary>点是否在矩形内 / Whether a point is inside the rectangle.</summary>
    public static bool Contains(
        this Rectangle<Dim2, Flt64> rect,
        Point<Dim2, Flt64> point,
        bool withLowerBound = true,
        bool withUpperBound = true,
        bool withBorder = true) {
        (Flt64 minX, Flt64 maxX) = GeometryOps.MinMax(rect.P1.X(), rect.P2.X(), rect.P3.X(), rect.P4.X());
        (Flt64 minY, Flt64 maxY) = GeometryOps.MinMax(rect.P1.Y(), rect.P2.Y(), rect.P3.Y(), rect.P4.Y());
        Interval lower = (withBorder && withLowerBound) ? (Interval)new Interval.Closed() : new Interval.Open();
        Interval upper = (withBorder && withUpperBound) ? (Interval)new Interval.Closed() : new Interval.Open();
        if (ValueRange<Flt64>.Of(minX, maxX, lower, upper) is not { } xRange) {
            return false;
        }

        if (ValueRange<Flt64>.Of(minY, maxY, lower, upper) is not { } yRange) {
            return false;
        }

        return xRange.Value.Contains(point.X()) && yRange.Value.Contains(point.Y());
    }
}
