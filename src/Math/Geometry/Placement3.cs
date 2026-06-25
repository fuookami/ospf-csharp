#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维放置 / 3D placement (position + 3D shape).
/// </summary>
public sealed record Placement3<V>(V X, V Y, V Z, IShape3<V> Shape) where V : struct, IFloatingNumber<V> {
    /// <summary>包围盒 / Bounding box.</summary>
    public Box3<V> Box => new(X, Y, Z, Shape.BoundingCuboid);

    /// <summary>宽度 / Width.</summary>
    public V Width => Box.Width;

    /// <summary>高度 / Height.</summary>
    public V Height => Box.Height;

    /// <summary>深度 / Depth.</summary>
    public V Depth => Box.Depth;

    /// <summary>最大 X / Max X.</summary>
    public V MaxX => Box.MaxX;

    /// <summary>最大 Y / Max Y.</summary>
    public V MaxY => Box.MaxY;

    /// <summary>最大 Z / Max Z.</summary>
    public V MaxZ => Box.MaxZ;

    /// <summary>点是否在放置区域内 / Whether point is inside placement.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(V x, V y, V z,
        bool withLowerBound = true, bool withUpperBound = true, bool withBorder = true)
        => Box.Contains(x, y, z, withLowerBound, withUpperBound, withBorder);

    /// <summary>是否与另一放置重叠 / Whether overlaps another placement.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Placement3<V> rhs) => Box.Overlapped(rhs.Box);

    /// <summary>与另一放置的交集 / Intersection with another placement.</summary>
    public Result<Placement3<V>?, ErrorCode, Error<ErrorCode>> Intersect(Placement3<V> rhs) =>
        Box.Intersect(rhs.Box).Map(b => b is null ? null : new Placement3<V>(b.X, b.Y, b.Z, b.Cuboid));
}
