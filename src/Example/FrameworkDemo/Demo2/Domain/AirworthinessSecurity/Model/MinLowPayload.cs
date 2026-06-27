#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 最小低甲板载荷点。Minimum low-deck payload point.
/// </summary>
/// <param name="MinLowPayloadValue">最小低载荷值 / Minimum low payload value</param>
/// <param name="Zfw">零燃油重量 / Zero fuel weight</param>
public sealed record MinLowPayloadPoint(double MinLowPayloadValue, double Zfw);

/// <summary>
/// 从零燃油重量点插值的最小低甲板载荷约束。Minimum low-deck payload constraint interpolated from zero-fuel weight points.
/// </summary>
public sealed class MinLowPayload
{
    /// <summary>插值点 / Interpolation points</summary>
    public IReadOnlyList<MinLowPayloadPoint> Points { get; init; } = Array.Empty<MinLowPayloadPoint>();

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin MinLowPayload.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
