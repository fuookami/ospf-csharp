#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 二维放置信息 / 2D placement information.
/// 由位置坐标和投影形状定义的二维放置，支持包含测试、重叠检测和求交运算。
/// A 2D placement defined by position coordinates and a projection shape,
/// supporting containment tests, overlap detection, and intersection.
/// </summary>
public sealed record QuantityPlacement2<V>(
    Quantity<V> X,
    Quantity<V> Y,
    IQuantityProjection2<V> Shape
) where V : struct, IFloatingNumber<V> {
    private QuantityBox2<V> Box => new(X, Y, Shape);

    /// <summary>放置宽度 / Placement width.</summary>
    public Quantity<V> Width => Box.Width;

    /// <summary>放置高度 / Placement height.</summary>
    public Quantity<V> Height => Box.Height;

    /// <summary>x 方向最大值，失败时返回 null / Maximum x value, or null on failure.</summary>
    public Quantity<V>? MaxXOrNull => MaxX().Value;

    /// <summary>y 方向最大值，失败时返回 null / Maximum y value, or null on failure.</summary>
    public Quantity<V>? MaxYOrNull => MaxY().Value;

    /// <summary>x 方向最大值 / Maximum x value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() => Box.MaxX();

    /// <summary>y 方向最大值 / Maximum y value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() => Box.MaxY();

    /// <summary>判断点是否在放置区域内 / Check if a point is inside the placement area.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(
        Quantity<V> x,
        Quantity<V> y,
        bool withLowerBound = true,
        bool withUpperBound = true,
        bool withBorder = true) =>
        Box.Contains(x, y, withLowerBound, withUpperBound, withBorder);

    /// <summary>判断两个放置区域是否重叠 / Check if two placement areas overlap.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(QuantityPlacement2<V> rhs) =>
        Box.Overlapped(rhs.Box);

    /// <summary>计算两个放置区域的交集 / Compute the intersection of two placement areas.</summary>
    public Result<QuantityPlacement2<V>?, ErrorCode, Error<ErrorCode>> Intersect(QuantityPlacement2<V> rhs) {
        Result<QuantityBox2<V>?, ErrorCode, Error<ErrorCode>> result = Box.Intersect(rhs.Box);
        if (result is Failed<QuantityBox2<V>?, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<QuantityPlacement2<V>?>(f.Error);
        }

        QuantityBox2<V>? intersected = result.Value;
        if (intersected is null) {
            return Results.Ok<QuantityPlacement2<V>?>(null);
        }

        return Results.Ok<QuantityPlacement2<V>?>(
            new QuantityPlacement2<V>(intersected.X, intersected.Y, intersected.Shape));
    }
}
