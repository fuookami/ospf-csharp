#nullable enable
using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维轴置换 / 3D axis permutation.
/// </summary>
public sealed record AxisPermutation3
{
    /// <summary>宽度轴 / Width axis.</summary>
    public Axis3 WidthAxis { get; }
    /// <summary>高度轴 / Height axis.</summary>
    public Axis3 HeightAxis { get; }
    /// <summary>深度轴 / Depth axis.</summary>
    public Axis3 DepthAxis { get; }

    public AxisPermutation3(Axis3 widthAxis, Axis3 heightAxis, Axis3 depthAxis)
    {
        if (widthAxis == heightAxis || heightAxis == depthAxis || widthAxis == depthAxis)
            throw new ArgumentException("AxisPermutation3 requires three distinct axes.");
        WidthAxis = widthAxis;
        HeightAxis = heightAxis;
        DepthAxis = depthAxis;
    }

    /// <summary>XYZ 置换 / XYZ permutation.</summary>
    public static readonly AxisPermutation3 XYZ = new(Axis3.X, Axis3.Y, Axis3.Z);
    /// <summary>ZYX 置换 / ZYX permutation.</summary>
    public static readonly AxisPermutation3 ZYX = new(Axis3.Z, Axis3.Y, Axis3.X);
    /// <summary>YXZ 置换 / YXZ permutation.</summary>
    public static readonly AxisPermutation3 YXZ = new(Axis3.Y, Axis3.X, Axis3.Z);
    /// <summary>ZXY 置换 / ZXY permutation.</summary>
    public static readonly AxisPermutation3 ZXY = new(Axis3.Z, Axis3.X, Axis3.Y);
    /// <summary>XZY 置换 / XZY permutation.</summary>
    public static readonly AxisPermutation3 XZY = new(Axis3.X, Axis3.Z, Axis3.Y);
    /// <summary>YZX 置换 / YZX permutation.</summary>
    public static readonly AxisPermutation3 YZX = new(Axis3.Y, Axis3.Z, Axis3.X);

    /// <summary>按轴置置换长方体 / Permute a cuboid by axes.</summary>
    public Cuboid3<V> Apply<V>(Cuboid3<V> cuboid) where V : struct, IFloatingNumber<V>
        => new(cuboid.Along(WidthAxis), cuboid.Along(HeightAxis), cuboid.Along(DepthAxis));

    /// <summary>按轴置置换圆柱体 / Permute a cylinder by axes.</summary>
    public Result<Cylinder3<V>, ErrorCode, Error<ErrorCode>> Apply<V>(Cylinder3<V> cylinder)
        where V : struct, IFloatingNumber<V>
        => MapAxis(cylinder.Axis).Map(axis => cylinder with { Axis = axis, });

    /// <summary>原始轴 → 置换后标准轴 / Original axis → permuted standard axis.</summary>
    public Result<Axis3, ErrorCode, Error<ErrorCode>> MapAxis(Axis3 axis) => axis switch
    {
        _ when axis == WidthAxis => Results.Ok(Axis3.X),
        _ when axis == HeightAxis => Results.Ok(Axis3.Y),
        _ when axis == DepthAxis => Results.Ok(Axis3.Z),
        _ => Results.Failed<Axis3>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Unsupported axis: {axis}")),
    };
}
