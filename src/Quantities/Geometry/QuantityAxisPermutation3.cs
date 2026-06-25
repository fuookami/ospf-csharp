#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维轴置换 / 3D axis permutation.
/// 纯几何概念；Apply/mapAxis 可被桥接层复用。
/// Pure geometry; Apply/mapAxis are reusable by bridges.
/// </summary>
public sealed record QuantityAxisPermutation3(Axis3 WidthAxis, Axis3 HeightAxis, Axis3 DepthAxis)
{
    /// <summary>XYZ 置换 / XYZ permutation.</summary>
    public static readonly QuantityAxisPermutation3 XYZ = new(Axis3.X, Axis3.Y, Axis3.Z);

    /// <summary>ZYX 置换 / ZYX permutation.</summary>
    public static readonly QuantityAxisPermutation3 ZYX = new(Axis3.Z, Axis3.Y, Axis3.X);

    /// <summary>YXZ 置换 / YXZ permutation.</summary>
    public static readonly QuantityAxisPermutation3 YXZ = new(Axis3.Y, Axis3.X, Axis3.Z);

    /// <summary>ZXY 置换 / ZXY permutation.</summary>
    public static readonly QuantityAxisPermutation3 ZXY = new(Axis3.Z, Axis3.X, Axis3.Y);

    /// <summary>XZY 置换 / XZY permutation.</summary>
    public static readonly QuantityAxisPermutation3 XZY = new(Axis3.X, Axis3.Z, Axis3.Y);

    /// <summary>YZX 置换 / YZX permutation.</summary>
    public static readonly QuantityAxisPermutation3 YZX = new(Axis3.Y, Axis3.Z, Axis3.X);

    /// <summary>对长方体应用轴置换 / Apply axis permutation to a cuboid.</summary>
    public QuantityCuboid3<V> Apply<V>(QuantityCuboid3<V> cuboid)
        where V : struct, IFloatingNumber<V>
        => new(cuboid.Along(WidthAxis), cuboid.Along(HeightAxis), cuboid.Along(DepthAxis));

    /// <summary>对圆柱体应用轴置换 / Apply axis permutation to a cylinder.</summary>
    public Result<QuantityCylinder3<V>, ErrorCode, Error<ErrorCode>> Apply<V>(QuantityCylinder3<V> cylinder)
        where V : struct, IFloatingNumber<V>
        => MapAxis(cylinder.Axis).Map(axis => cylinder with { Axis = axis });

    /// <summary>将原始轴映射到置换后的轴 / Map an original axis to its permuted counterpart.</summary>
    public Result<Axis3, ErrorCode, Error<ErrorCode>> MapAxis(Axis3 axis) => axis switch
    {
        _ when axis == WidthAxis => Results.Ok(Axis3.X),
        _ when axis == HeightAxis => Results.Ok(Axis3.Y),
        _ when axis == DepthAxis => Results.Ok(Axis3.Z),
        _ => Results.Failed<Axis3>(new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Unsupported axis: {axis}")),
    };
}
