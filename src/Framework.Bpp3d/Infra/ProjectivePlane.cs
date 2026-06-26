#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 投影平面 / Projective plane.
/// 定义三维物体投影到二维平面的方式。
/// Defines how 3D objects project onto 2D planes.
/// </summary>
public abstract record ProjectivePlane {
    /// <summary>底面投影（ZOX 平面）/ Bottom projection (ZOX plane).</summary>
    public sealed record Bottom : ProjectivePlane;

    /// <summary>侧面投影（XOY 平面）/ Side projection (XOY plane).</summary>
    public sealed record Side : ProjectivePlane;

    /// <summary>正面投影（ZOY 平面）/ Front projection (ZOY plane).</summary>
    public sealed record Front : ProjectivePlane;

    /// <summary>所有平面 / All planes.</summary>
    public static ProjectivePlane[] All { get; } = [new Bottom(), new Side(), new Front()];
}

/// <summary>
/// 投影形状 / Projection shape (FltX specialisation).
/// 二维投影的形状描述。
/// </summary>
public sealed record ProjectionShape(Quantity<FltX> Length, Quantity<FltX> Width) {
    /// <summary>面积 / Area.</summary>
    public Quantity<FltX> Area => Length.Multiply(Width);

    /// <summary>规范化（确保 Length >= Width）/ Normalize (ensure Length >= Width).</summary>
    public static ProjectionShape Normalize(Quantity<FltX> a, Quantity<FltX> b) {
        double av = a.Value.ToFlt64().ToDouble();
        double bv = b.Value.ToFlt64().ToDouble();
        return av >= bv ? new ProjectionShape(a, b) : new ProjectionShape(b, a);
    }
}

/// <summary>
/// 二维投射点 / 2D projective point (FltX specialisation).
/// </summary>
public sealed record QuantityPoint2(Quantity<FltX> X, Quantity<FltX> Y);

/// <summary>
/// 二维投射向量 / 2D projective vector (FltX specialisation).
/// </summary>
public sealed record QuantityVector2(Quantity<FltX> X, Quantity<FltX> Y);

/// <summary>
/// 轴对齐二维矩形 / Axis-aligned 2D rectangle (FltX specialisation).
/// </summary>
public sealed record Rectangle2(
    Quantity<FltX> MinX, Quantity<FltX> MinY,
    Quantity<FltX> MaxX, Quantity<FltX> MaxY) {
    /// <summary>宽度 / Width.</summary>
    public Quantity<FltX> Width => new(MaxX.Value.Minus(MinX.Value), MinX.Unit);

    /// <summary>高度 / Height.</summary>
    public Quantity<FltX> Height => new(MaxY.Value.Minus(MinY.Value), MinY.Unit);

    /// <summary>面积 / Area.</summary>
    public Quantity<FltX> Area => Width.Multiply(Height);

    /// <summary>
    /// 计算相交矩形 / Compute intersection rectangle.
    /// </summary>
    public Rectangle2? Intersect(Rectangle2 other) {
        double minX = global::System.Math.Max(MinX.Value.ToFlt64().ToDouble(), other.MinX.Value.ToFlt64().ToDouble());
        double minY = global::System.Math.Max(MinY.Value.ToFlt64().ToDouble(), other.MinY.Value.ToFlt64().ToDouble());
        double maxX = global::System.Math.Min(MaxX.Value.ToFlt64().ToDouble(), other.MaxX.Value.ToFlt64().ToDouble());
        double maxY = global::System.Math.Min(MaxY.Value.ToFlt64().ToDouble(), other.MaxY.Value.ToFlt64().ToDouble());
        if (minX >= maxX || minY >= maxY) return null;
        return new Rectangle2(
            new Quantity<FltX>(new FltX(minX), MinX.Unit),
            new Quantity<FltX>(new FltX(minY), MinY.Unit),
            new Quantity<FltX>(new FltX(maxX), MaxX.Unit),
            new Quantity<FltX>(new FltX(maxY), MaxY.Unit));
    }

    /// <summary>相交面积 / Intersection area.</summary>
    public Quantity<FltX> IntersectArea(Rectangle2 other) {
        Rectangle2? intersection = Intersect(other);
        if (intersection is null) {
            return new Quantity<FltX>(FltX.Zero, MinX.Unit);
        }
        return intersection.Area;
    }
}
