#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 线性密度限制区域。Linear density limit zone.
/// </summary>
/// <param name="Name">名称 / Name</param>
/// <param name="FrontArm">前臂 / Front arm</param>
/// <param name="BackArm">后臂 / Back arm</param>
/// <param name="MaxLinearDensity">最大线性密度 / Maximum linear density</param>
public sealed record LinearDensityLimitZone(
    string Name,
    double FrontArm,
    double BackArm,
    double MaxLinearDensity);

/// <summary>
/// 线性密度限制线。Linear density limit line.
/// </summary>
/// <param name="Zone">限制区域 / Limit zone</param>
/// <param name="Arm">力臂 / Arm</param>
/// <param name="PositionIndices">位置索引 / Position indices</param>
public sealed record LinearDensityLimitLine(
    LinearDensityLimitZone Zone,
    double Arm,
    IReadOnlyList<int> PositionIndices);

/// <summary>
/// 计算每个货物位置的线性密度（单位长度重量）并将其注册到模型。Computes linear density (weight per unit length) for each cargo position and registers it with the model.
/// </summary>
public sealed class LinearDensity
{
    /// <summary>限制区域 / Limit zones</summary>
    public IReadOnlyList<LinearDensityLimitZone> LimitZones { get; init; } = Array.Empty<LinearDensityLimitZone>();
    /// <summary>限制线 / Limit lines</summary>
    public IReadOnlyList<LinearDensityLimitLine> LimitLines { get; init; } = Array.Empty<LinearDensityLimitLine>();

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin LinearDensity.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
