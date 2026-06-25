#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维包围盒 / 3D bounding box (position + cuboid).
/// </summary>
public sealed record Box3<V>(V X, V Y, V Z, Cuboid3<V> Cuboid) where V : struct, IFloatingNumber<V>
{
    /// <summary>在原点处创建 / Create at origin.</summary>
    public static Box3<V> AtOrigin(Cuboid3<V> cuboid) =>
        new(GeometryOps.ZeroOf(cuboid.Width), GeometryOps.ZeroOf(cuboid.Height), GeometryOps.ZeroOf(cuboid.Depth), cuboid);

    /// <summary>宽度 / Width.</summary>
    public V Width => Cuboid.Width;

    /// <summary>高度 / Height.</summary>
    public V Height => Cuboid.Height;

    /// <summary>深度 / Depth.</summary>
    public V Depth => Cuboid.Depth;

    /// <summary>最大 X / Max X.</summary>
    public V MaxX => GeometryOps.Plus(X, Width);

    /// <summary>最大 Y / Max Y.</summary>
    public V MaxY => GeometryOps.Plus(Y, Height);

    /// <summary>最大 Z / Max Z.</summary>
    public V MaxZ => GeometryOps.Plus(Z, Depth);

    /// <summary>点是否在包围盒内 / Whether point is inside bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(V x, V y, V z,
        bool withLowerBound = true, bool withUpperBound = true, bool withBorder = true)
    {
        var xResult = GeometryOps.ContainsInRange(x, X, MaxX, withLowerBound, withUpperBound, "x");
        if (xResult is Failed<bool, ErrorCode, Error<ErrorCode>> fx) return Results.Failed<bool>(fx.Error);
        var yResult = GeometryOps.ContainsInRange(y, Y, MaxY, withLowerBound, withUpperBound, "y");
        if (yResult is Failed<bool, ErrorCode, Error<ErrorCode>> fy) return Results.Failed<bool>(fy.Error);
        var zResult = GeometryOps.ContainsInRange(z, Z, MaxZ, withLowerBound, withUpperBound, "z");
        if (zResult is Failed<bool, ErrorCode, Error<ErrorCode>> fz) return Results.Failed<bool>(fz.Error);
        return Results.Ok(xResult.Value && yResult.Value && zResult.Value);
    }

    /// <summary>是否与另一包围盒重叠 / Whether overlaps another bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Box3<V> rhs)
    {
        var xOverlap = MaxX.PartialOrd(rhs.X) is Order.Greater or Order.Equal
                     && rhs.MaxX.PartialOrd(X) is Order.Greater or Order.Equal;
        if (!xOverlap) return Results.Ok(false);
        var yOverlap = MaxY.PartialOrd(rhs.Y) is Order.Greater or Order.Equal
                     && rhs.MaxY.PartialOrd(Y) is Order.Greater or Order.Equal;
        if (!yOverlap) return Results.Ok(false);
        var zOverlap = MaxZ.PartialOrd(rhs.Z) is Order.Greater or Order.Equal
                     && rhs.MaxZ.PartialOrd(Z) is Order.Greater or Order.Equal;
        return Results.Ok(zOverlap);
    }

    /// <summary>与另一包围盒的交集 / Intersection with another bounding box.</summary>
    public Result<Box3<V>?, ErrorCode, Error<ErrorCode>> Intersect(Box3<V> rhs)
    {
        var newX = GeometryOps.Max(X, rhs.X, "x");
        var newY = GeometryOps.Max(Y, rhs.Y, "y");
        var newZ = GeometryOps.Max(Z, rhs.Z, "z");
        var newMaxX = GeometryOps.Min(MaxX, rhs.MaxX, "x");
        var newMaxY = GeometryOps.Min(MaxY, rhs.MaxY, "y");
        var newMaxZ = GeometryOps.Min(MaxZ, rhs.MaxZ, "z");
        if (newX is Failed<V, ErrorCode, Error<ErrorCode>> || newY is Failed<V, ErrorCode, Error<ErrorCode>>
            || newZ is Failed<V, ErrorCode, Error<ErrorCode>>
            || newMaxX is Failed<V, ErrorCode, Error<ErrorCode>> || newMaxY is Failed<V, ErrorCode, Error<ErrorCode>>
            || newMaxZ is Failed<V, ErrorCode, Error<ErrorCode>>)
            return Results.Ok<Box3<V>?>(null);
        if (newX.Value.Geq(newMaxX.Value) || newY.Value.Geq(newMaxY.Value) || newZ.Value.Geq(newMaxZ.Value))
            return Results.Ok<Box3<V>?>(null);
        var cuboid = new Cuboid3<V>(newMaxX.Value.Minus(newX.Value), newMaxY.Value.Minus(newY.Value), newMaxZ.Value.Minus(newZ.Value));
        return Results.Ok<Box3<V>?>(new Box3<V>(newX.Value, newY.Value, newZ.Value, cuboid));
    }
}
