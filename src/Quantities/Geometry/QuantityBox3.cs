#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维包围盒 / 3D bounding box.
/// 由位置坐标和长方体形状定义的三维包围盒，支持包含测试、重叠检测和求交运算。
/// A 3D bounding box defined by position coordinates and a cuboid shape,
/// supporting containment tests, overlap detection, and intersection.
/// </summary>
public sealed record QuantityBox3<V>(
    Quantity<V> X,
    Quantity<V> Y,
    Quantity<V> Z,
    QuantityCuboid3<V> Cuboid
) where V : struct, IFloatingNumber<V>
{
    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public static QuantityBox3<V> AtOrigin(QuantityCuboid3<V> cuboid) => new(
        QuantityOps.QuantityZeroOf(cuboid.Width),
        QuantityOps.QuantityZeroOf(cuboid.Height),
        QuantityOps.QuantityZeroOf(cuboid.Depth),
        cuboid);

    /// <summary>包围盒宽度 / Bounding box width.</summary>
    public Quantity<V> Width => Cuboid.Width;

    /// <summary>包围盒高度 / Bounding box height.</summary>
    public Quantity<V> Height => Cuboid.Height;

    /// <summary>包围盒深度 / Bounding box depth.</summary>
    public Quantity<V> Depth => Cuboid.Depth;

    /// <summary>x 方向最大值，失败时返回 null / Maximum x value, or null on failure.</summary>
    public Quantity<V>? MaxXOrNull => MaxX().Value;

    /// <summary>y 方向最大值，失败时返回 null / Maximum y value, or null on failure.</summary>
    public Quantity<V>? MaxYOrNull => MaxY().Value;

    /// <summary>z 方向最大值，失败时返回 null / Maximum z value, or null on failure.</summary>
    public Quantity<V>? MaxZOrNull => MaxZ().Value;

    /// <summary>x 方向最大值 / Maximum x value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() => QuantityOps.PlusSafe(X, Width);

    /// <summary>y 方向最大值 / Maximum y value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() => QuantityOps.PlusSafe(Y, Height);

    /// <summary>z 方向最大值 / Maximum z value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxZ() => QuantityOps.PlusSafe(Z, Depth);

    /// <summary>判断点是否在包围盒内 / Check if a point is inside the bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(
        Quantity<V> x,
        Quantity<V> y,
        Quantity<V> z,
        bool withLowerBound = true,
        bool withUpperBound = true,
        bool withBorder = true)
    {
        var includeLower = withBorder && withLowerBound;
        var includeUpper = withBorder && withUpperBound;

        var maxXResult = MaxX();
        if (maxXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) return Results.Failed<bool>(f1.Error);
        var maxYResult = MaxY();
        if (maxYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) return Results.Failed<bool>(f2.Error);
        var maxZResult = MaxZ();
        if (maxZResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) return Results.Failed<bool>(f3.Error);

        var xIn = QuantityOps.ContainsInRangeSafe(x, X, maxXResult.Value, includeLower, includeUpper, "x");
        if (xIn is Failed<bool, ErrorCode, Error<ErrorCode>> f4) return Results.Failed<bool>(f4.Error);
        if (!xIn.Value) return Results.Ok(false);

        var yIn = QuantityOps.ContainsInRangeSafe(y, Y, maxYResult.Value, includeLower, includeUpper, "y");
        if (yIn is Failed<bool, ErrorCode, Error<ErrorCode>> f5) return Results.Failed<bool>(f5.Error);
        if (!yIn.Value) return Results.Ok(false);

        return QuantityOps.ContainsInRangeSafe(z, Z, maxZResult.Value, includeLower, includeUpper, "z");
    }

    /// <summary>判断两个包围盒是否重叠 / Check if two bounding boxes overlap.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(QuantityBox3<V> rhs)
    {
        var thisMaxX = MaxX();
        if (thisMaxX is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) return Results.Failed<bool>(f1.Error);
        var thisMaxY = MaxY();
        if (thisMaxY is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) return Results.Failed<bool>(f2.Error);
        var thisMaxZ = MaxZ();
        if (thisMaxZ is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) return Results.Failed<bool>(f3.Error);
        var rhsMaxX = rhs.MaxX();
        if (rhsMaxX is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f4) return Results.Failed<bool>(f4.Error);
        var rhsMaxY = rhs.MaxY();
        if (rhsMaxY is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f5) return Results.Failed<bool>(f5.Error);
        var rhsMaxZ = rhs.MaxZ();
        if (rhsMaxZ is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f6) return Results.Failed<bool>(f6.Error);

        var maxOrd = QuantityOps.OrdSafe(thisMaxX.Value, rhs.X, "x");
        if (maxOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f7) return Results.Failed<bool>(f7.Error);
        if (maxOrd.Value is not Order.Greater) return Results.Ok(false);

        var xOrd = QuantityOps.OrdSafe(X, rhsMaxX.Value, "x");
        if (xOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f8) return Results.Failed<bool>(f8.Error);
        if (xOrd.Value is not Order.Less) return Results.Ok(false);

        var maxYOrd = QuantityOps.OrdSafe(thisMaxY.Value, rhs.Y, "y");
        if (maxYOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f9) return Results.Failed<bool>(f9.Error);
        if (maxYOrd.Value is not Order.Greater) return Results.Ok(false);

        var yOrd = QuantityOps.OrdSafe(Y, rhsMaxY.Value, "y");
        if (yOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f10) return Results.Failed<bool>(f10.Error);
        if (yOrd.Value is not Order.Less) return Results.Ok(false);

        var maxZOrd = QuantityOps.OrdSafe(thisMaxZ.Value, rhs.Z, "z");
        if (maxZOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f11) return Results.Failed<bool>(f11.Error);
        if (maxZOrd.Value is not Order.Greater) return Results.Ok(false);

        var zOrd = QuantityOps.OrdSafe(Z, rhsMaxZ.Value, "z");
        if (zOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f12) return Results.Failed<bool>(f12.Error);
        return Results.Ok(zOrd.Value is Order.Less);
    }

    /// <summary>计算两个包围盒的交集 / Compute the intersection of two bounding boxes.</summary>
    public Result<QuantityBox3<V>?, ErrorCode, Error<ErrorCode>> Intersect(QuantityBox3<V> rhs)
    {
        var thisMaxX = MaxX(); if (thisMaxX.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var thisMaxY = MaxY(); if (thisMaxY.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var thisMaxZ = MaxZ(); if (thisMaxZ.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var rhsMaxX = rhs.MaxX(); if (rhsMaxX.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var rhsMaxY = rhs.MaxY(); if (rhsMaxY.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var rhsMaxZ = rhs.MaxZ(); if (rhsMaxZ.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);

        var minX = QuantityOps.MaxSafe(X, rhs.X, "x"); if (minX.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var maxX = QuantityOps.MinSafe(thisMaxX.Value, rhsMaxX.Value, "x"); if (maxX.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var minY = QuantityOps.MaxSafe(Y, rhs.Y, "y"); if (minY.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var maxY = QuantityOps.MinSafe(thisMaxY.Value, rhsMaxY.Value, "y"); if (maxY.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var minZ = QuantityOps.MaxSafe(Z, rhs.Z, "z"); if (minZ.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var maxZ = QuantityOps.MinSafe(thisMaxZ.Value, rhsMaxZ.Value, "z"); if (maxZ.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);

        var xOrd = QuantityOps.OrdSafe(minX.Value, maxX.Value, "x");
        if (xOrd.IsFailed || xOrd.Value is not Order.Less) return Results.Ok<QuantityBox3<V>?>(null);

        var yOrd = QuantityOps.OrdSafe(minY.Value, maxY.Value, "y");
        if (yOrd.IsFailed || yOrd.Value is not Order.Less) return Results.Ok<QuantityBox3<V>?>(null);

        var zOrd = QuantityOps.OrdSafe(minZ.Value, maxZ.Value, "z");
        if (zOrd.IsFailed || zOrd.Value is not Order.Less) return Results.Ok<QuantityBox3<V>?>(null);

        var w = QuantityOps.MinusSafe(maxX.Value, minX.Value); if (w.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var h = QuantityOps.MinusSafe(maxY.Value, minY.Value); if (h.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);
        var d = QuantityOps.MinusSafe(maxZ.Value, minZ.Value); if (d.IsFailed) return Results.Ok<QuantityBox3<V>?>(null);

        return Results.Ok<QuantityBox3<V>?>(new QuantityBox3<V>(
            minX.Value, minY.Value, minZ.Value,
            new QuantityCuboid3<V>(w.Value, h.Value, d.Value)));
    }
}
