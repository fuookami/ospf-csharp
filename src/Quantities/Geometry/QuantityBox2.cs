#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 二维包围盒 / 2D bounding box.
/// 由位置坐标和投影形状定义的二维包围盒，支持包含测试、重叠检测和求交运算。
/// A 2D bounding box defined by position coordinates and a projection shape,
/// supporting containment tests, overlap detection, and intersection.
/// </summary>
public sealed record QuantityBox2<V>(
    Quantity<V> X,
    Quantity<V> Y,
    IQuantityProjection2<V> Shape
) where V : struct, IFloatingNumber<V> {
    private static Result<bool, ErrorCode, Error<ErrorCode>> PropagateError<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        if (result is Failed<T, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<bool>(f.Error);
        }

        return Results.Ok(false); // unreachable
    }

    /// <summary>在原点创建包围盒 / Create a bounding box at the origin.</summary>
    public static QuantityBox2<V> AtOrigin(IQuantityProjection2<V> shape) => shape switch {
        QuantityRectangle2<V> r => new(QuantityOps.QuantityZeroOf(r.Width), QuantityOps.QuantityZeroOf(r.Height), shape),
        QuantityCircle2<V> c => new(QuantityOps.QuantityZeroOf(c.Radius), QuantityOps.QuantityZeroOf(c.Radius), shape),
        _ => throw new System.InvalidOperationException("Unknown projection shape"),
    };

    /// <summary>包围盒宽度 / Bounding box width.</summary>
    public Quantity<V> Width => Shape switch {
        QuantityRectangle2<V> r => r.Width,
        QuantityCircle2<V> c => c.Diameter,
        _ => default!,
    };

    /// <summary>包围盒高度 / Bounding box height.</summary>
    public Quantity<V> Height => Shape switch {
        QuantityRectangle2<V> r => r.Height,
        QuantityCircle2<V> c => c.Diameter,
        _ => default!,
    };

    /// <summary>x 方向最大值，失败时返回 null / Maximum x value, or null on failure.</summary>
    public Quantity<V>? MaxXOrNull => MaxX().Value;

    /// <summary>y 方向最大值，失败时返回 null / Maximum y value, or null on failure.</summary>
    public Quantity<V>? MaxYOrNull => MaxY().Value;

    /// <summary>x 方向最大值 / Maximum x value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() => QuantityOps.PlusSafe(X, Width);

    /// <summary>y 方向最大值 / Maximum y value.</summary>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() => QuantityOps.PlusSafe(Y, Height);

    private Quantity<V>? CenterXOrNull => CenterX().Value;
    private Quantity<V>? CenterYOrNull => CenterY().Value;

    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> CenterX() => Shape switch {
        QuantityRectangle2<V> => Results.Ok(X),
        QuantityCircle2<V> c => QuantityOps.PlusSafe(X, c.Radius),
        _ => Results.Ok(X),
    };

    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> CenterY() => Shape switch {
        QuantityRectangle2<V> => Results.Ok(Y),
        QuantityCircle2<V> c => QuantityOps.PlusSafe(Y, c.Radius),
        _ => Results.Ok(Y),
    };

    /// <summary>判断点是否在包围盒内 / Check if a point is inside the bounding box.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(
        Quantity<V> x,
        Quantity<V> y,
        bool withLowerBound = true,
        bool withUpperBound = true,
        bool withBorder = true) {
        bool includeLower = withBorder && withLowerBound;
        bool includeUpper = withBorder && withUpperBound;
        return Shape switch {
            QuantityRectangle2<V> => ContainsRect(x, y, includeLower, includeUpper),
            QuantityCircle2<V> c => ContainsCircle(x, y, c, withBorder),
            _ => Results.Ok(false),
        };
    }

    private Result<bool, ErrorCode, Error<ErrorCode>> ContainsRect(
        Quantity<V> x, Quantity<V> y, bool includeLower, bool includeUpper) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxXResult = MaxX();
        if (maxXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) {
            return Results.Failed<bool>(f1.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxYResult = MaxY();
        if (maxYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) {
            return Results.Failed<bool>(f2.Error);
        }

        Result<bool, ErrorCode, Error<ErrorCode>> xIn = QuantityOps.ContainsInRangeSafe(x, X, maxXResult.Value, includeLower, includeUpper, "x");
        if (xIn is Failed<bool, ErrorCode, Error<ErrorCode>> f3) {
            return Results.Failed<bool>(f3.Error);
        }

        if (!xIn.Value) {
            return Results.Ok(false);
        }

        return QuantityOps.ContainsInRangeSafe(y, Y, maxYResult.Value, includeLower, includeUpper, "y");
    }

    private Result<bool, ErrorCode, Error<ErrorCode>> ContainsCircle(
        Quantity<V> x, Quantity<V> y, QuantityCircle2<V> c, bool withBorder) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cxResult = CenterX();
        if (cxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) {
            return Results.Failed<bool>(f1.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cyResult = CenterY();
        if (cyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) {
            return Results.Failed<bool>(f2.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dxResult = QuantityOps.MinusSafe(x, cxResult.Value);
        if (dxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) {
            return Results.Failed<bool>(f3.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dyResult = QuantityOps.MinusSafe(y, cyResult.Value);
        if (dyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f4) {
            return Results.Failed<bool>(f4.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dist2 = QuantityOps.PlusSafe(
            QuantityOps.QuantityProduct(dxResult.Value, dxResult.Value),
            QuantityOps.QuantityProduct(dyResult.Value, dyResult.Value));
        if (dist2 is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f5) {
            return Results.Failed<bool>(f5.Error);
        }

        Quantity<V> r2 = QuantityOps.QuantityProduct(c.Radius, c.Radius);
        Result<Order, ErrorCode, Error<ErrorCode>> ord = QuantityOps.OrdSafe(dist2.Value, r2, "circle-contains");
        if (ord is Failed<Order, ErrorCode, Error<ErrorCode>> f6) {
            return Results.Failed<bool>(f6.Error);
        }

        return Results.Ok(withBorder
            ? ord.Value is Order.Less or Order.Equal
            : ord.Value is Order.Less);
    }

    /// <summary>判断两个包围盒是否重叠 / Check if two bounding boxes overlap.</summary>
    public Result<bool, ErrorCode, Error<ErrorCode>> Overlapped(QuantityBox2<V> rhs) => (Shape, rhs.Shape) switch {
        (QuantityRectangle2<V>, QuantityRectangle2<V>) => RectangleOverlapped(rhs),
        (QuantityRectangle2<V>, QuantityCircle2<V> cr) => RectCircleOverlapped(rhs, cr),
        (QuantityCircle2<V> cl, QuantityRectangle2<V>) => rhs.RectCircleOverlapped(this, cl),
        (QuantityCircle2<V> cl, QuantityCircle2<V> cr) => CircleOverlapped(rhs, cl, cr),
        _ => Results.Ok(false),
    };

    private Result<bool, ErrorCode, Error<ErrorCode>> RectangleOverlapped(QuantityBox2<V> rhs) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxXResult = MaxX();
        if (maxXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) {
            return Results.Failed<bool>(f1.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxYResult = MaxY();
        if (maxYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) {
            return Results.Failed<bool>(f2.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rhsMaxXResult = rhs.MaxX();
        if (rhsMaxXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) {
            return Results.Failed<bool>(f3.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rhsMaxYResult = rhs.MaxY();
        if (rhsMaxYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f4) {
            return Results.Failed<bool>(f4.Error);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> maxOrd = QuantityOps.OrdSafe(maxXResult.Value, rhs.X, "x");
        if (maxOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f5) {
            return Results.Failed<bool>(f5.Error);
        }

        if (maxOrd.Value is not Order.Greater) {
            return Results.Ok(false);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> xOrd = QuantityOps.OrdSafe(X, rhsMaxXResult.Value, "x");
        if (xOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f6) {
            return Results.Failed<bool>(f6.Error);
        }

        if (xOrd.Value is not Order.Less) {
            return Results.Ok(false);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> maxYOrd = QuantityOps.OrdSafe(maxYResult.Value, rhs.Y, "y");
        if (maxYOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f7) {
            return Results.Failed<bool>(f7.Error);
        }

        if (maxYOrd.Value is not Order.Greater) {
            return Results.Ok(false);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> yOrd = QuantityOps.OrdSafe(Y, rhsMaxYResult.Value, "y");
        if (yOrd is Failed<Order, ErrorCode, Error<ErrorCode>> f8) {
            return Results.Failed<bool>(f8.Error);
        }

        return Results.Ok(yOrd.Value is Order.Less);
    }

    private Result<bool, ErrorCode, Error<ErrorCode>> RectCircleOverlapped(
        QuantityBox2<V> circleBox, QuantityCircle2<V> circle) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxXResult = MaxX();
        if (maxXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) {
            return Results.Failed<bool>(f1.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxYResult = MaxY();
        if (maxYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) {
            return Results.Failed<bool>(f2.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ccxResult = QuantityOps.PlusSafe(circleBox.X, circle.Radius);
        if (ccxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) {
            return Results.Failed<bool>(f3.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ccyResult = QuantityOps.PlusSafe(circleBox.Y, circle.Radius);
        if (ccyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f4) {
            return Results.Failed<bool>(f4.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> closestXResult = QuantityOps.ClampSafe(ccxResult.Value, X, maxXResult.Value, "x");
        if (closestXResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f5) {
            return Results.Failed<bool>(f5.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> closestYResult = QuantityOps.ClampSafe(ccyResult.Value, Y, maxYResult.Value, "y");
        if (closestYResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f6) {
            return Results.Failed<bool>(f6.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dxResult = QuantityOps.MinusSafe(ccxResult.Value, closestXResult.Value);
        if (dxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f7) {
            return Results.Failed<bool>(f7.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dyResult = QuantityOps.MinusSafe(ccyResult.Value, closestYResult.Value);
        if (dyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f8) {
            return Results.Failed<bool>(f8.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dist2Result = QuantityOps.PlusSafe(
            QuantityOps.QuantityProduct(dxResult.Value, dxResult.Value),
            QuantityOps.QuantityProduct(dyResult.Value, dyResult.Value));
        if (dist2Result is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f9) {
            return Results.Failed<bool>(f9.Error);
        }

        Quantity<V> r2 = QuantityOps.QuantityProduct(circle.Radius, circle.Radius);
        Result<Order, ErrorCode, Error<ErrorCode>> ord = QuantityOps.OrdSafe(dist2Result.Value, r2, "rect-circle-overlap");
        if (ord is Failed<Order, ErrorCode, Error<ErrorCode>> f10) {
            return Results.Failed<bool>(f10.Error);
        }

        return Results.Ok(ord.Value is Order.Less or Order.Equal);
    }

    private Result<bool, ErrorCode, Error<ErrorCode>> CircleOverlapped(
        QuantityBox2<V> rhs, QuantityCircle2<V> lhs, QuantityCircle2<V> rhsCircle) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cxResult = CenterX();
        if (cxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f1) {
            return Results.Failed<bool>(f1.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cyResult = CenterY();
        if (cyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f2) {
            return Results.Failed<bool>(f2.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rcxResult = rhs.CenterX();
        if (rcxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f3) {
            return Results.Failed<bool>(f3.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rcyResult = rhs.CenterY();
        if (rcyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f4) {
            return Results.Failed<bool>(f4.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dxResult = QuantityOps.MinusSafe(cxResult.Value, rcxResult.Value);
        if (dxResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f5) {
            return Results.Failed<bool>(f5.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dyResult = QuantityOps.MinusSafe(cyResult.Value, rcyResult.Value);
        if (dyResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f6) {
            return Results.Failed<bool>(f6.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> dist2Result = QuantityOps.PlusSafe(
            QuantityOps.QuantityProduct(dxResult.Value, dxResult.Value),
            QuantityOps.QuantityProduct(dyResult.Value, dyResult.Value));
        if (dist2Result is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f7) {
            return Results.Failed<bool>(f7.Error);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> reachResult = QuantityOps.PlusSafe(lhs.Radius, rhsCircle.Radius);
        if (reachResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f8) {
            return Results.Failed<bool>(f8.Error);
        }

        Quantity<V> reach2 = QuantityOps.QuantityProduct(reachResult.Value, reachResult.Value);
        Result<Order, ErrorCode, Error<ErrorCode>> ord = QuantityOps.OrdSafe(dist2Result.Value, reach2, "circle-circle-overlap");
        if (ord is Failed<Order, ErrorCode, Error<ErrorCode>> f9) {
            return Results.Failed<bool>(f9.Error);
        }

        return Results.Ok(ord.Value is Order.Less or Order.Equal);
    }

    /// <summary>计算两个包围盒的交集 / Compute the intersection of two bounding boxes.</summary>
    public Result<QuantityBox2<V>?, ErrorCode, Error<ErrorCode>> Intersect(QuantityBox2<V> rhs) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> thisMaxX = MaxX(); if (thisMaxX.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> thisMaxY = MaxY(); if (thisMaxY.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rhsMaxX = rhs.MaxX(); if (rhsMaxX.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rhsMaxY = rhs.MaxY(); if (rhsMaxY.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> minX = QuantityOps.MaxSafe(X, rhs.X, "x"); if (minX.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxX = QuantityOps.MinSafe(thisMaxX.Value, rhsMaxX.Value, "x"); if (maxX.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> minY = QuantityOps.MaxSafe(Y, rhs.Y, "y"); if (minY.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxY = QuantityOps.MinSafe(thisMaxY.Value, rhsMaxY.Value, "y"); if (maxY.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> xOrd = QuantityOps.OrdSafe(minX.Value, maxX.Value, "x");
        if (xOrd.IsFailed || xOrd.Value is not Order.Less) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Order, ErrorCode, Error<ErrorCode>> yOrd = QuantityOps.OrdSafe(minY.Value, maxY.Value, "y");
        if (yOrd.IsFailed || yOrd.Value is not Order.Less) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> w = QuantityOps.MinusSafe(maxX.Value, minX.Value);
        if (w.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> h = QuantityOps.MinusSafe(maxY.Value, minY.Value);
        if (h.IsFailed) {
            return Results.Ok<QuantityBox2<V>?>(null);
        }

        return Results.Ok<QuantityBox2<V>?>(new QuantityBox2<V>(
            minX.Value, minY.Value,
            new QuantityRectangle2<V>(w.Value, h.Value)));
    }
}
