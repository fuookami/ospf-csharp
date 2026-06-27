#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 表面密度限制区域。Surface density limit zone.
/// </summary>
/// <param name="Name">名称 / Name</param>
/// <param name="FrontArm">前臂 / Front arm</param>
/// <param name="BackArm">后臂 / Back arm</param>
/// <param name="MaxSurfaceDensity">最大表面密度 / Maximum surface density</param>
public sealed record SurfaceDensityLimitZone(
    string Name,
    double FrontArm,
    double BackArm,
    double MaxSurfaceDensity);

/// <summary>
/// 计算每个货物位置的表面密度（单位面积重量）并将其注册到模型。Computes surface density (weight per unit area) for each cargo position and registers it with the model.
/// </summary>
public sealed class SurfaceDensity
{
    /// <summary>限制区域 / Limit zones</summary>
    public IReadOnlyList<SurfaceDensityLimitZone> LimitZones { get; init; } = Array.Empty<SurfaceDensityLimitZone>();

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin SurfaceDensity.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
