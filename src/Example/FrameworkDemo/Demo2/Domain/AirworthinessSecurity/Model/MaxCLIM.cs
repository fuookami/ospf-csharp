#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 最大中心线指数裕度点。Maximum CenterLine Index Margin point.
/// </summary>
/// <param name="Tow">起飞重量 / Takeoff weight</param>
/// <param name="MaxCLIMValue">最大 CLIM 值 / Maximum CLIM value</param>
public sealed record MaxCLIMPoint(double Tow, double MaxCLIMValue);

/// <summary>
/// 从起飞重量点插值的最大中心线指数裕度约束。Maximum CenterLine Index Margin constraint interpolated from takeoff weight points.
/// </summary>
public sealed class MaxCLIM
{
    /// <summary>插值点 / Interpolation points</summary>
    public IReadOnlyList<MaxCLIMPoint> Points { get; init; } = Array.Empty<MaxCLIMPoint>();

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin MaxCLIM.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
