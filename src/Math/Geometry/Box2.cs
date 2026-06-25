#nullable enable
using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 二维包围盒 / 2D bounding box (position + shape).
/// </summary>
public sealed record Box2<V>(V X, V Y, Projection2<V> Shape) where V : struct, IFloatingNumber<V>
{
    /// <summary>在原点处创建 / Create at origin.</summary>
    public static Box2<V> AtOrigin(Projection2<V> shape) => shape switch
    {
        Rectangle2<V> r => new(GeometryOps.ZeroOf(r.Width), GeometryOps.ZeroOf(r.Height), shape),
        Circle2<V> c => new(GeometryOps.ZeroOf(c.Radius), GeometryOps.ZeroOf(c.Radius), shape),
        _ => throw new InvalidOperationException("unknown shape"),
    };

    /// <summary>宽度 / Width.</summary>
    public V Width => Shape switch { Rectangle2<V> r => r.Width, Circle2<V> c => c.Diameter, _ => default! };

    /// <summary>高度 / Height.</summary>
    public V Height => Shape switch { Rectangle2<V> r => r.Height, Circle2<V> c => c.Diameter, _ => default! };

    /// <summary>最大 X / Max X.</summary>
    public V MaxX => GeometryOps.Plus(X, Width);

    /// <summary>最大 Y / Max Y.</summary>
    public V MaxY => GeometryOps.Plus(Y, Height);

    /// <summary>点是否在包围盒内 / Whether point is inside bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(V x, V y,
        bool withLowerBound = true, bool withUpperBound = true, bool withBorder = true)
    {
        if (Shape is Circle2<V> c)
        {
            var dx = x.Minus(X.Plus(c.Radius));
            var dy = y.Minus(Y.Plus(c.Radius));
            var distSq = dx.Sqr().Plus(dy.Sqr());
            var rSq = c.Radius.Sqr();
            return Results.Ok(withBorder ? distSq.Leq(rSq) : distSq.Ls(rSq));
        }
        var xResult = GeometryOps.ContainsInRange(x, X, MaxX, withLowerBound, withUpperBound, "x");
        if (xResult is Failed<bool, ErrorCode, Error<ErrorCode>> fx) return Results.Failed<bool>(fx.Error);
        var yResult = GeometryOps.ContainsInRange(y, Y, MaxY, withLowerBound, withUpperBound, "y");
        if (yResult is Failed<bool, ErrorCode, Error<ErrorCode>> fy) return Results.Failed<bool>(fy.Error);
        return Results.Ok(xResult.Value && yResult.Value);
    }

    /// <summary>是否与另一包围盒重叠 / Whether overlaps another bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Box2<V> rhs)
    {
        var xOverlap = MaxX.PartialOrd(rhs.X) is Order.Greater or Order.Equal
                     && rhs.MaxX.PartialOrd(X) is Order.Greater or Order.Equal;
        if (!xOverlap) return Results.Ok(false);
        var yOverlap = MaxY.PartialOrd(rhs.Y) is Order.Greater or Order.Equal
                     && rhs.MaxY.PartialOrd(Y) is Order.Greater or Order.Equal;
        return Results.Ok(yOverlap);
    }

    /// <summary>与另一包围盒的交集 / Intersection with another bounding box.</summary>
    public Result<Box2<V>?, ErrorCode, Error<ErrorCode>> Intersect(Box2<V> rhs)
    {
        var newX = GeometryOps.Max(X, rhs.X, "x");
        var newY = GeometryOps.Max(Y, rhs.Y, "y");
        var newMaxX = GeometryOps.Min(MaxX, rhs.MaxX, "x");
        var newMaxY = GeometryOps.Min(MaxY, rhs.MaxY, "y");
        if (newX is Failed<V, ErrorCode, Error<ErrorCode>> || newY is Failed<V, ErrorCode, Error<ErrorCode>>
            || newMaxX is Failed<V, ErrorCode, Error<ErrorCode>> || newMaxY is Failed<V, ErrorCode, Error<ErrorCode>>)
            return Results.Ok<Box2<V>?>(null);
        if (newX.Value.Geq(newMaxX.Value) || newY.Value.Geq(newMaxY.Value))
            return Results.Ok<Box2<V>?>(null);
        var rectShape = new Rectangle2<V>(newMaxX.Value.Minus(newX.Value), newMaxY.Value.Minus(newY.Value));
        return Results.Ok<Box2<V>?>(new Box2<V>(newX.Value, newY.Value, rectShape));
    }
}
