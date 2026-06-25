#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Linq;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 边 / Edge: 连接两点的线段。
/// A line segment connecting two points.
/// </summary>
public sealed record Edge<D, V>(Point<D, V> From, Point<D, V> To)
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V> {
    /// <summary>长度（欧几里得）/ Length (Euclidean).</summary>
    public V Length => From.DistanceTo(To);

    /// <summary>按指定度量计算长度 / Length under a given metric.</summary>
    public V LengthOf(Distance? distance = null) => (distance ?? Distance.Euclidean).Compute(From, To);

    /// <summary>长度平方 / Squared length.</summary>
    public V LengthSquared {
        get {
            IFloatingNumberConstants<V> c = From[0].Constants;
            return From.Indices.Aggregate(c.Zero, (acc, i) => acc.Plus(To[i].Minus(From[i]).Sqr()));
        }
    }

    /// <summary>方向向量 / Direction vector.</summary>
    public Vector<D, V> Vector => new(From.Indices.Select(i => To[i].Minus(From[i])).ToArray(), From.Dim);

    /// <summary>方向向量（别名）/ Direction vector (alias).</summary>
    public Vector<D, V> Direction => Vector;

    /// <summary>单位方向向量，零长度返回 null / Unit direction, null if zero length.</summary>
    public Vector<D, V>? UnitDirection =>
        LengthSquared.Eq(From[0].Constants.Zero) ? null : Vector.Unit;

    /// <summary>中点 / Midpoint.</summary>
    public Point<D, V> Midpoint() {
        V two = ((IRealNumberConstants<V>)From[0].Constants).Two;
        return new(From.Indices.Select(i => From[i].Plus(To[i]).Div(two)).ToArray(), From.Dim);
    }

    /// <summary>参数 t 处的点（0=From，1=To）/ Point at parameter t.</summary>
    public Point<D, V> PointAt(V t) =>
        new(From.Indices.Select(i => From[i].Plus(t.Times(To[i].Minus(From[i])))).ToArray(), From.Dim);

    /// <summary>点是否在边上（含容差）/ Whether a point lies on the edge (with tolerance).</summary>
    public bool ContainsPoint(Point<D, V> point, V? epsilon = null) {
        V eps = epsilon ?? ((IRealNumberConstants<V>)From[0].Constants).DecimalPrecision;
        V distToFrom = point.DistanceTo(From);
        V distToTo = point.DistanceTo(To);
        return (distToFrom.Plus(distToTo).Minus(Length)).Abs().Leq(eps);
    }

    /// <summary>近似相等（有向）/ Approximate equality (directed).</summary>
    public bool ApproxEq(Edge<D, V> other) => From.ApproxEq(other.From) && To.ApproxEq(other.To);

    /// <summary>指定精度近似相等（有向）/ Approximate equality with epsilon (directed).</summary>
    public bool ApproxEq(Edge<D, V> other, V epsilon) =>
        From.ApproxEq(other.From, epsilon) && To.ApproxEq(other.To, epsilon);

    /// <summary>近似相等（无向）/ Approximate equality (undirected).</summary>
    public bool ApproxEqUndirected(Edge<D, V> other) =>
        (From.ApproxEq(other.From) && To.ApproxEq(other.To)) ||
        (From.ApproxEq(other.To) && To.ApproxEq(other.From));

    /// <summary>指定精度近似相等（无向）/ Approximate equality with epsilon (undirected).</summary>
    public bool ApproxEqUndirected(Edge<D, V> other, V epsilon) =>
        (From.ApproxEq(other.From, epsilon) && To.ApproxEq(other.To, epsilon)) ||
        (From.ApproxEq(other.To, epsilon) && To.ApproxEq(other.From, epsilon));

    /// <inheritdoc/>
    public override string ToString() => $"{From} -> {To}";
}

/// <summary>二维边扩展 / 2D edge extensions.</summary>
public static class Edge2DExtensions {
    /// <summary>两线段是否相交 / Whether two edges intersect.</summary>
    public static bool Intersects(this Edge<Dim2, Flt64> self, Edge<Dim2, Flt64> other) =>
        self.IntersectionPoint(other) is not null;

    /// <summary>两线段交点 / Intersection point of two edges.</summary>
    public static Point<Dim2, Flt64>? IntersectionPoint(this Edge<Dim2, Flt64> self, Edge<Dim2, Flt64> other) {
        (Point<Dim2, Flt64>? p1, Point<Dim2, Flt64>? p2, Point<Dim2, Flt64>? p3, Point<Dim2, Flt64>? p4) = (self.From, self.To, other.From, other.To);
        Flt64 d1x = p2.X().Minus(p1.X());
        Flt64 d1y = p2.Y().Minus(p1.Y());
        Flt64 d2x = p4.X().Minus(p3.X());
        Flt64 d2y = p4.Y().Minus(p3.Y());
        Flt64 denom = d1x.Times(d2y).Minus(d1y.Times(d2x));
        if (denom.Eq(default)) {
            return null;
        }

        Flt64 dx = p3.X().Minus(p1.X());
        Flt64 dy = p3.Y().Minus(p1.Y());
        Flt64 t = dx.Times(d2y).Minus(dy.Times(d2x)).Div(denom);
        Flt64 s = dx.Times(d1y).Minus(dy.Times(d1x)).Div(denom);
        return (t.Geq(default) && t.Leq(Flt64.One) && s.Geq(default) && s.Leq(Flt64.One))
            ? self.PointAt(t) : null;
    }

    /// <summary>线段上距给定点最近的点 / Closest point on edge to given point.</summary>
    public static Point<Dim2, Flt64> ClosestPoint(this Edge<Dim2, Flt64> self, Point<Dim2, Flt64> point) {
        Vector<Dim2, Flt64> dir = self.Direction;
        Flt64 dx = point.X().Minus(self.From.X());
        Flt64 dy = point.Y().Minus(self.From.Y());
        Flt64 lengthSq = self.LengthSquared;
        if (lengthSq.Eq(default)) {
            return self.From;
        }

        Flt64 t = dx.Times(dir.X()).Plus(dy.Times(dir.Y())).Div(lengthSq);
        Flt64 tc = t.Ls(default) ? default : t.Gr(Flt64.One) ? Flt64.One : t;
        return self.PointAt(tc);
    }

    /// <summary>点到线段的距离 / Distance from point to edge.</summary>
    public static Flt64 DistanceToPoint(this Edge<Dim2, Flt64> self, Point<Dim2, Flt64> point) =>
        point.DistanceTo(self.ClosestPoint(point));
}
