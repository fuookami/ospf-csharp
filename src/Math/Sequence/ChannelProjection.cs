#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Sequence;

/// <summary>
/// 通道投影策略 / Channel projection strategy.
/// 将标量、分量序列或几何点投影为单个标量通道 / Projects scalars, component sequences, or geometric points into one scalar channel.
/// </summary>
public abstract record ChannelProjection {
    /// <summary>标量通道 / Scalar channel.</summary>
    public sealed record Scalar : ChannelProjection;

    /// <summary>指定分量通道 / Component channel.</summary>
    public sealed record Component(int Index) : ChannelProjection;

    /// <summary>L2 模长通道 / L2 magnitude channel.</summary>
    public sealed record Magnitude : ChannelProjection;

    /// <summary>复平面相位通道 / Complex-plane phase channel.</summary>
    public sealed record Phase : ChannelProjection;
}

/// <summary>通道投影扩展 / Channel projection extensions.</summary>
public static class ChannelProjectionExtensions {
    /// <summary>投影标量 / Project a scalar.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Project(
        this ChannelProjection projection,
        Flt64 value) {
        return projection switch {
            ChannelProjection.Scalar => Results.Ok(value),
            ChannelProjection.Magnitude => Results.Ok(value.Abs()),
            _ => new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "该投影需要多分量输入 / projection requires a multi-component value"),
        };
    }

    /// <summary>投影分量序列 / Project a component sequence.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Project(
        this ChannelProjection projection,
        IReadOnlyList<Flt64> components) {
        if (components.Count == 0) {
            return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "分量列表不得为空 / components must be non-empty");
        }

        return projection switch {
            ChannelProjection.Scalar when components.Count == 1 => Results.Ok(components[0]),
            ChannelProjection.Scalar => new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "标量投影要求分量个数恰为 1 / scalar projection requires exactly one component"),
            ChannelProjection.Component component when component.Index >= 0 && component.Index < components.Count
                => Results.Ok(components[component.Index]),
            ChannelProjection.Component component => new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"分量下标 {component.Index} 越界 / component index {component.Index} is out of range"),
            ChannelProjection.Magnitude => Results.Ok(Magnitude(components)),
            ChannelProjection.Phase when components.Count >= 2 => Results.Ok(new Flt64(
                System.Math.Atan2(components[1].ToDouble(), components[0].ToDouble()))),
            ChannelProjection.Phase => new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "相位投影至少需要两个分量 / phase projection requires at least two components"),
            _ => new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "不支持的投影策略 / unsupported projection strategy"),
        };
    }

    /// <summary>投影几何点 / Project a geometric point.</summary>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Project<D>(
        this ChannelProjection projection,
        Point<D, Flt64> point)
        where D : struct, IDimension {
        var components = new Flt64[point.Count];
        for (int index = 0; index < components.Length; index++) {
            components[index] = point[index];
        }
        return projection.Project(components);
    }

    private static Flt64 Magnitude(IReadOnlyList<Flt64> components) {
        double squared = 0.0;
        for (int index = 0; index < components.Count; index++) {
            double value = components[index].ToDouble();
            squared += value * value;
        }
        return new Flt64(System.Math.Sqrt(squared));
    }
}
