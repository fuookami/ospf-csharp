#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 不对称线性密度限制点。Unsymmetrical linear density limit point.
/// </summary>
/// <param name="Lhs">左侧值 / Left-hand side value</param>
/// <param name="Rhs">右侧值 / Right-hand side value</param>
public sealed record UnsymmetricalLinearDensityLimitPoint(double Lhs, double Rhs);

/// <summary>
/// 不对称线性密度限制。Unsymmetrical linear density limit.
/// </summary>
/// <param name="LeftCoefficient">左系数 / Left coefficient</param>
/// <param name="RightCoefficient">右系数 / Right coefficient</param>
/// <param name="MaxSum">最大总和 / Maximum sum</param>
public sealed record UnsymmetricalLinearDensityLimit(
    double? LeftCoefficient,
    double? RightCoefficient,
    double MaxSum);

/// <summary>
/// 不对称线性密度限制区域。Unsymmetrical linear density limit zone.
/// </summary>
/// <param name="Name">名称 / Name</param>
/// <param name="FrontArm">前臂 / Front arm</param>
/// <param name="BackArm">后臂 / Back arm</param>
/// <param name="Limits">限制列表 / Limits list</param>
public sealed record UnsymmetricalLinearDensityLimitZone(
    string Name,
    double FrontArm,
    double BackArm,
    IReadOnlyList<UnsymmetricalLinearDensityLimit> Limits);

/// <summary>
/// 飞机左右两侧不对称线性密度的限制。Limits on unsymmetrical linear density between left and right sides of the aircraft.
/// </summary>
public sealed class MaxUnsymmetricalLinearDensity
{
    /// <summary>限制区域 / Limit zones</summary>
    public IReadOnlyList<UnsymmetricalLinearDensityLimitZone> LimitZones { get; init; } =
        Array.Empty<UnsymmetricalLinearDensityLimitZone>();
}
