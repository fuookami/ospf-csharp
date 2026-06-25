#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维放置信息 / 3D placement information.
/// 由位置坐标和三维形状定义的三维放置，支持包含测试、重叠检测和求交运算。
/// A 3D placement defined by position coordinates and a 3D shape,
/// supporting containment tests, overlap detection, and intersection.
/// </summary>
public sealed record QuantityPlacement3<V>(
    Quantity<V> X,
    Quantity<V> Y,
    Quantity<V> Z,
    IQuantityShape3<V> Shape
) where V : struct, IFloatingNumber<V>
{
    /// <summary>包围盒 / Bounding box.</summary>
    public QuantityBox3<V> Box => new(X, Y, Z, Shape.BoundingCuboid);

    /// <summary>放置宽度 / Placement width.</summary>
    public Quantity<V> Width => Box.Width;

    /// <summary>放置高度 / Placement height.</summary>
    public Quantity<V> Height => Box.Height;

    /// <summary>放置深度 / Placement depth.</summary>
    public Quantity<V> Depth => Box.Depth;

    /// <summary>x 方向最大值，失败时返回 null / Maximum x value, or null on failure.</summary>
    public Quantity<V>? MaxXOrNull => MaxX().Value;

    /// <summary>y 方向最大值，失败时返回 null / Maximum y value, or null on failure.</summary>
    public Quantity<V>? MaxYOrNull => MaxY().Value;

    /// <summary>z 方向最大值，失败时返回 null / Maximum z value, or null on failure.</summary>
    public Quantity<V>? MaxZOrNull => MaxZ().Value;

    /// <summary>x 方向最大值 / Maximum x value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() => Box.MaxX();

    /// <summary>y 方向最大值 / Maximum y value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() => Box.MaxY();

    /// <summary>z 方向最大值 / Maximum z value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxZ() => Box.MaxZ();

    /// <summary>判断点是否在放置区域内 / Check if a point is inside the placement area.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(
        Quantity<V> x,
        Quantity<V> y,
        Quantity<V> z,
        bool withLowerBound = true,
        bool withUpperBound = true,
        bool withBorder = true) =>
        Box.Contains(x, y, z, withLowerBound, withUpperBound, withBorder);

    /// <summary>判断两个放置区域是否重叠 / Check if two placement areas overlap.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(QuantityPlacement3<V> rhs) =>
        Box.Overlapped(rhs.Box);

    /// <summary>计算两个放置区域的交集 / Compute the intersection of two placement areas.</summary>
    public Result<QuantityPlacement3<V>?, ErrorCode, Error<ErrorCode>> Intersect(QuantityPlacement3<V> rhs)
    {
        var result = Box.Intersect(rhs.Box);
        if (result is Failed<QuantityBox3<V>?, ErrorCode, Error<ErrorCode>> f)
            return Results.Failed<QuantityPlacement3<V>?>(f.Error);
        var intersected = result.Value;
        if (intersected is null) return Results.Ok<QuantityPlacement3<V>?>(null);
        return Results.Ok<QuantityPlacement3<V>?>(
            new QuantityPlacement3<V>(intersected.X, intersected.Y, intersected.Z, intersected.Cuboid));
    }
}
