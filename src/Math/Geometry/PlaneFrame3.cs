#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>二维平面点 / 2D plane point.</summary>
public sealed record PlanePoint2<V>(V X, V Y) where V : struct, IFloatingNumber<V>;

/// <summary>三维空间点 / 3D space point.</summary>
public sealed record PlanePoint3<V>(V X, V Y, V Z) where V : struct, IFloatingNumber<V> {
    /// <summary>沿指定轴的坐标 / Coordinate along axis.</summary>
    public V Along(Axis3 axis) => axis switch {
        Axis3.X => X,
        Axis3.Y => Y,
        Axis3.Z => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}

/// <summary>三维平面法向量 / 3D plane normal vector.</summary>
public sealed record PlaneVector3<V>(V X, V Y, V Z) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 平面坐标框架 / Plane coordinate frame (two distinct axes).
/// </summary>
public sealed record PlaneFrame3 {
    /// <summary>第一轴 / First axis.</summary>
    public Axis3 FirstAxis { get; }
    /// <summary>第二轴 / Second axis.</summary>
    public Axis3 SecondAxis { get; }

    public PlaneFrame3(Axis3 firstAxis, Axis3 secondAxis) {
        if (firstAxis == secondAxis) {
            throw new ArgumentException("firstAxis and secondAxis must be different.");
        }

        FirstAxis = firstAxis;
        SecondAxis = secondAxis;
    }

    /// <summary>法向轴，非法框架返回 null / Normal axis, null for an invalid frame.</summary>
    public Axis3? NormalAxisOrNull => (FirstAxis, SecondAxis) switch {
        (Axis3.X, Axis3.Y) or (Axis3.Y, Axis3.X) => Axis3.Z,
        (Axis3.X, Axis3.Z) or (Axis3.Z, Axis3.X) => Axis3.Y,
        (Axis3.Y, Axis3.Z) or (Axis3.Z, Axis3.Y) => Axis3.X,
        _ => null,
    };

    /// <summary>法向轴或失败原因 / Normal axis or failure.</summary>
    public Result<Axis3, ErrorCode, Error<ErrorCode>> NormalAxis() =>
        NormalAxisOrNull is { } a
            ? Results.Ok(a)
            : Results.Failed<Axis3>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Unsupported axis frame: {FirstAxis}, {SecondAxis}"));

    /// <summary>点到平面的距离 / Distance from point to plane.</summary>
    public Result<V, ErrorCode, Error<ErrorCode>> Distance<V>(PlanePoint3<V> point) where V : struct, IFloatingNumber<V>
        => NormalAxis().Map(point.Along);

    /// <summary>三维点投影到二维 / Project 3D point to 2D.</summary>
    public PlanePoint2<V> Point2<V>(PlanePoint3<V> point) where V : struct, IFloatingNumber<V>
        => new(point.Along(FirstAxis), point.Along(SecondAxis));

    /// <summary>二维点提升到三维 / Lift 2D point to 3D.</summary>
    public PlanePoint3<V> Point3<V>(PlanePoint2<V> point, V distance) where V : struct, IFloatingNumber<V> {
        V x = FirstAxis == Axis3.X ? point.X : SecondAxis == Axis3.X ? point.Y : distance;
        V y = FirstAxis == Axis3.Y ? point.X : SecondAxis == Axis3.Y ? point.Y : distance;
        V z = FirstAxis == Axis3.Z ? point.X : SecondAxis == Axis3.Z ? point.Y : distance;
        return new(x, y, z);
    }

    /// <summary>创建法向量 / Create normal vector.</summary>
    public Result<PlaneVector3<V>, ErrorCode, Error<ErrorCode>> Vector<V>(V distance) where V : struct, IFloatingNumber<V> {
        V zero = GeometryOps.ZeroOf(distance);
        return NormalAxis().Map(a => a switch {
            Axis3.X => new PlaneVector3<V>(distance, zero, zero),
            Axis3.Y => new PlaneVector3<V>(zero, distance, zero),
            Axis3.Z => new PlaneVector3<V>(zero, zero, distance),
            _ => throw new InvalidOperationException(),
        });
    }

    /// <summary>长方体在该平面上的投影 / Footprint of cuboid on this plane.</summary>
    public Rectangle2<V> Footprint<V>(Cuboid3<V> cuboid) where V : struct, IFloatingNumber<V>
        => new(cuboid.Along(FirstAxis), cuboid.Along(SecondAxis));

    /// <summary>XY 框架 / XY frame.</summary>
    public static readonly PlaneFrame3 XY = new(Axis3.X, Axis3.Y);
    /// <summary>YX 框架 / YX frame.</summary>
    public static readonly PlaneFrame3 YX = new(Axis3.Y, Axis3.X);
    /// <summary>XZ 框架 / XZ frame.</summary>
    public static readonly PlaneFrame3 XZ = new(Axis3.X, Axis3.Z);
    /// <summary>ZX 框架 / ZX frame.</summary>
    public static readonly PlaneFrame3 ZX = new(Axis3.Z, Axis3.X);
    /// <summary>YZ 框架 / YZ frame.</summary>
    public static readonly PlaneFrame3 YZ = new(Axis3.Y, Axis3.Z);
    /// <summary>ZY 框架 / ZY frame.</summary>
    public static readonly PlaneFrame3 ZY = new(Axis3.Z, Axis3.Y);
}
