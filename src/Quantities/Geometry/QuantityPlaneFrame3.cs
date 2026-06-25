#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>二维平面上的点 / 2D point on a plane.</summary>
public sealed record QuantityPlanePoint2<V>(Quantity<V> X, Quantity<V> Y)
    where V : struct, IFloatingNumber<V>;

/// <summary>三维空间中的点 / 3D point in space.</summary>
public sealed record QuantityPlanePoint3<V>(Quantity<V> X, Quantity<V> Y, Quantity<V> Z)
    where V : struct, IFloatingNumber<V> {
    /// <summary>获取沿指定轴的坐标 / Coordinate along axis.</summary>
    public Quantity<V> Along(Axis3 axis) => axis switch {
        Axis3.X => X,
        Axis3.Y => Y,
        Axis3.Z => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}

/// <summary>三维平面法向量 / 3D plane normal vector.</summary>
public sealed record QuantityPlaneVector3<V>(Quantity<V> X, Quantity<V> Y, Quantity<V> Z)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 三维平面框架 / 3D plane frame.
/// 定义三维空间中的平面坐标框架，支持点投影、法向量计算和长方体底面积投影。
/// Plane frame is pure geometry; BPP3D Bottom/Side/Front mapping stays in bridge layer.
/// </summary>
public sealed class QuantityPlaneFrame3 {
    private readonly Axis3 _normalAxis;

    private QuantityPlaneFrame3(Axis3 firstAxis, Axis3 secondAxis, Axis3 normalAxis) {
        FirstAxis = firstAxis;
        SecondAxis = secondAxis;
        _normalAxis = normalAxis;
    }

    /// <summary>第一轴 / First axis.</summary>
    public Axis3 FirstAxis { get; }

    /// <summary>第二轴 / Second axis.</summary>
    public Axis3 SecondAxis { get; }

    /// <summary>可空法向轴（垂直于平面的轴）/ Nullable normal axis (perpendicular to the plane).</summary>
    public Axis3? NormalAxisOrNull => _normalAxis;

    /// <summary>获取法向轴（垂直于平面的轴）/ Get normal axis (perpendicular to the plane).</summary>
    public Result<Axis3, ErrorCode, Error<ErrorCode>> NormalAxis() =>
        Results.Ok(_normalAxis);

    /// <summary>计算点到平面的距离 / Compute the distance from a point to the plane.</summary>
    public Quantity<V> Distance<V>(QuantityPlanePoint3<V> point)
        where V : struct, IFloatingNumber<V>
        => point.Along(_normalAxis);

    /// <summary>将三维点投影到二维平面坐标 / Project a 3D point to 2D plane coordinates.</summary>
    public QuantityPlanePoint2<V> Point2<V>(QuantityPlanePoint3<V> point)
        where V : struct, IFloatingNumber<V>
        => new(point.Along(FirstAxis), point.Along(SecondAxis));

    /// <summary>从二维平面坐标和距离恢复三维点 / Restore a 3D point from 2D plane coordinates and distance.</summary>
    public QuantityPlanePoint3<V> Point3<V>(QuantityPlanePoint2<V> point, Quantity<V> distance)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> x = FirstAxis == Axis3.X ? point.X : SecondAxis == Axis3.X ? point.Y : distance;
        Quantity<V> y = FirstAxis == Axis3.Y ? point.X : SecondAxis == Axis3.Y ? point.Y : distance;
        Quantity<V> z = FirstAxis == Axis3.Z ? point.X : SecondAxis == Axis3.Z ? point.Y : distance;
        return new QuantityPlanePoint3<V>(x, y, z);
    }

    /// <summary>根据距离生成法向量 / Generate a normal vector from a distance value.</summary>
    public QuantityPlaneVector3<V> Vector<V>(Quantity<V> distance)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> zero = QuantityOps.QuantityZeroOf(distance);
        return _normalAxis switch {
            Axis3.X => new QuantityPlaneVector3<V>(distance, zero, zero),
            Axis3.Y => new QuantityPlaneVector3<V>(zero, distance, zero),
            Axis3.Z => new QuantityPlaneVector3<V>(zero, zero, distance),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>计算长方体在平面上的投影（底面积）/ Compute the footprint of a cuboid on the plane.</summary>
    public QuantityRectangle2<V> Footprint<V>(QuantityCuboid3<V> cuboid)
        where V : struct, IFloatingNumber<V>
        => new(cuboid.Along(FirstAxis), cuboid.Along(SecondAxis));

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is QuantityPlaneFrame3 other
        && FirstAxis == other.FirstAxis
        && SecondAxis == other.SecondAxis;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(FirstAxis, SecondAxis);

    /// <inheritdoc/>
    public override string ToString() =>
        $"QuantityPlaneFrame3(firstAxis={FirstAxis}, secondAxis={SecondAxis})";

    /// <summary>创建平面框架，非法轴组合返回失败 / Create a plane frame, returning failure for invalid axis combinations.</summary>
    public static Result<QuantityPlaneFrame3, ErrorCode, Error<ErrorCode>> Of(Axis3 firstAxis, Axis3 secondAxis) =>
        OfOrNull(firstAxis, secondAxis) is { } frame
            ? Results.Ok(frame)
            : Results.Failed<QuantityPlaneFrame3>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"Invalid plane axes: {firstAxis}, {secondAxis}."));

    /// <summary>创建平面框架，非法轴组合返回 null / Create a plane frame, returning null for invalid axis combinations.</summary>
    public static QuantityPlaneFrame3? OfOrNull(Axis3 firstAxis, Axis3 secondAxis) =>
        NormalAxisOf(firstAxis, secondAxis) is { } normal
            ? new QuantityPlaneFrame3(firstAxis, secondAxis, normal)
            : null;

    private static Axis3? NormalAxisOf(Axis3 firstAxis, Axis3 secondAxis) =>
        (firstAxis, secondAxis) switch {
            (Axis3.X, Axis3.Y) or (Axis3.Y, Axis3.X) => Axis3.Z,
            (Axis3.X, Axis3.Z) or (Axis3.Z, Axis3.X) => Axis3.Y,
            (Axis3.Y, Axis3.Z) or (Axis3.Z, Axis3.Y) => Axis3.X,
            _ => null,
        };

    /// <summary>X-Y 平面框架 / X-Y plane frame.</summary>
    public static readonly QuantityPlaneFrame3 XY = new(Axis3.X, Axis3.Y, Axis3.Z);

    /// <summary>Y-X 平面框架 / Y-X plane frame.</summary>
    public static readonly QuantityPlaneFrame3 YX = new(Axis3.Y, Axis3.X, Axis3.Z);

    /// <summary>X-Z 平面框架 / X-Z plane frame.</summary>
    public static readonly QuantityPlaneFrame3 XZ = new(Axis3.X, Axis3.Z, Axis3.Y);

    /// <summary>Z-X 平面框架 / Z-X plane frame.</summary>
    public static readonly QuantityPlaneFrame3 ZX = new(Axis3.Z, Axis3.X, Axis3.Y);

    /// <summary>Y-Z 平面框架 / Y-Z plane frame.</summary>
    public static readonly QuantityPlaneFrame3 YZ = new(Axis3.Y, Axis3.Z, Axis3.X);

    /// <summary>Z-Y 平面框架 / Z-Y plane frame.</summary>
    public static readonly QuantityPlaneFrame3 ZY = new(Axis3.Z, Axis3.Y, Axis3.X);
}
