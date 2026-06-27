#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model;

/// <summary>
/// 相邻位置对。Adjacent position pair.
/// </summary>
/// <param name="First">第一个位置索引 / First position index</param>
/// <param name="Second">第二个位置索引 / Second position index</param>
public sealed record PositionPair(int First, int Second);

/// <summary>
/// 分隔空载装载模型。Divide empty loading model for soft security constraints.
/// </summary>
public sealed class DivideEmptyLoading
{
    /// <summary>相邻位置对 / Adjacent position pairs</summary>
    public IReadOnlyList<PositionPair> AdjacentPositions { get; init; } = Array.Empty<PositionPair>();

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin DivideEmptyLoading.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
