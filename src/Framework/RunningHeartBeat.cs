#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework;

/// <summary>
/// 子进度心跳 / Sub-progress heartbeat.
/// </summary>
/// <param name="EstimatedTime">预估剩余时间 / Estimated remaining time.</param>
/// <param name="Progress">子进度百分比 / Sub-progress percentage.</param>
/// <param name="Message">附加消息 / Optional message.</param>
public sealed record SubProgressHeartBeat(
    TimeSpan EstimatedTime,
    Flt64 Progress,
    string? Message = null);

/// <summary>
/// 运行中心跳 / Running heartbeat.
/// </summary>
/// <param name="Id">任务标识 / Task identifier.</param>
/// <param name="RunTime">已运行时间 / Elapsed run time.</param>
/// <param name="EstimatedTime">预估剩余时间 / Estimated remaining time.</param>
/// <param name="OptimizedRate">优化率 / Optimization rate.</param>
/// <param name="Progress">进度描述 / Progress description.</param>
/// <param name="Message">附加消息 / Optional message.</param>
public sealed record RunningHeartBeat(
    string Id,
    TimeSpan RunTime,
    TimeSpan EstimatedTime,
    Flt64 OptimizedRate,
    string? Progress = null,
    string? Message = null) {
    /// <summary>心跳时间戳 / Heartbeat timestamp.</summary>
    public DateTime Time { get; } = DateTime.Now;
}

/// <summary>
/// 完成心跳 / Finish heartbeat.
/// </summary>
/// <param name="Id">任务标识 / Task identifier.</param>
/// <param name="RunTime">总运行时间 / Total run time.</param>
/// <param name="Code">状态码 / Status code.</param>
/// <param name="Message">完成消息 / Finish message.</param>
public sealed record FinnishHeartBeat(
    string Id,
    TimeSpan RunTime,
    ulong Code,
    string Message) {
    /// <summary>心跳时间戳 / Heartbeat timestamp.</summary>
    public DateTime Time { get; } = DateTime.Now;

    /// <summary>
    /// 构造成功完成心跳 / Construct a successful finish heartbeat.
    /// </summary>
    /// <param name="id">任务标识 / Task identifier.</param>
    /// <param name="runTime">总运行时间 / Total run time.</param>
    /// <returns>完成心跳 / Finish heartbeat.</returns>
    public static FinnishHeartBeat Success(string id, TimeSpan runTime) =>
        new(id, runTime, 0, "");

    /// <summary>
    /// 构造带错误的完成心跳 / Construct a finish heartbeat with an error.
    /// </summary>
    /// <param name="id">任务标识 / Task identifier.</param>
    /// <param name="runTime">总运行时间 / Total run time.</param>
    /// <param name="error">错误信息 / Error information.</param>
    /// <returns>完成心跳 / Finish heartbeat.</returns>
    public static FinnishHeartBeat FromError(
        string id,
        TimeSpan runTime,
        Error<ErrorCode> error) =>
        new(id, runTime, (ulong)error.Code, error.Message);

    /// <summary>
    /// 构造零运行时间的错误完成心跳 / Construct an error heartbeat with zero run time.
    /// </summary>
    /// <param name="id">任务标识 / Task identifier.</param>
    /// <param name="error">错误信息 / Error information.</param>
    /// <returns>完成心跳 / Finish heartbeat.</returns>
    public static FinnishHeartBeat FromError(string id, Error<ErrorCode> error) =>
        FromError(id, TimeSpan.Zero, error);
}
