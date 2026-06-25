#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 二维放置 / 2D placement (position + projection shape).
/// </summary>
public sealed record Placement2<V>(V X, V Y, Projection2<V> Shape) where V : struct, IFloatingNumber<V> {
    private Box2<V> Box => new(X, Y, Shape);

    /// <summary>宽度 / Width.</summary>
    public V Width => Box.Width;

    /// <summary>高度 / Height.</summary>
    public V Height => Box.Height;

    /// <summary>最大 X / Max X.</summary>
    public V MaxX => Box.MaxX;

    /// <summary>最大 Y / Max Y.</summary>
    public V MaxY => Box.MaxY;

    /// <summary>点是否在放置区域内 / Whether point is inside placement.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(V x, V y,
        bool withLowerBound = true, bool withUpperBound = true, bool withBorder = true)
        => Box.Contains(x, y, withLowerBound, withUpperBound, withBorder);

    /// <summary>是否与另一放置重叠 / Whether overlaps another placement.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(Placement2<V> rhs) => Box.Overlapped(rhs.Box);

    /// <summary>与另一放置的交集 / Intersection with another placement.</summary>
    public Result<Placement2<V>?, ErrorCode, Error<ErrorCode>> Intersect(Placement2<V> rhs) =>
        Box.Intersect(rhs.Box).Map(b => b is null ? null : new Placement2<V>(b.X, b.Y, b.Shape));
}
