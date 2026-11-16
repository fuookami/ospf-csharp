// 求解器心跳信号 DTO / Solver heartbeat signal DTOs.

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 运行中求解器的心跳信号 / A running solver's heartbeat signal.
/// </summary>
/// <remarks>
/// 逐条对齐：`id` / `runTime` / `estimatedTime` / `optimizedRate`，外加一个构造时的时间戳。
///
/// **`Duration` 用 `TimeSpan`**：`Duration` 是"时长"，`TimeSpan` 是 .NET 里同一个概念的现成类型，自己再造一个只会多一层换算。
/// **时间戳用 `DateTimeOffset`** 而不是 `LocalDateTime`：`LocalDateTime` 丢了时区，跨机器对比日志时那个字段是没法用的。
/// 这一处是有意的改进，不是偏差——**它不影响任何排序或判断，只影响人读日志**。
///
/// `id`, `runTime`, `estimatedTime`, `optimizedRate`, plus a timestamp captured at construction.
///
/// **`Duration` maps to `TimeSpan`**: a duration is an elapsed length, and `TimeSpan` is the ready-made .NET type for exactly that concept; inventing
/// another only adds a conversion layer. **The timestamp is a `DateTimeOffset` rather than a `LocalDateTime`**: a `LocalDateTime` drops the zone, which makes that
/// field useless when comparing logs across machines. That is a deliberate improvement, not a deviation — **it affects no ordering or decision, only how a human
/// reads the log**.
/// </remarks>
/// <param name="Id">请求标识 / The request identifier</param>
/// <param name="RunTime">启动以来的已用时长 / Elapsed time since the solver started</param>
/// <param name="EstimatedTime">预估剩余时长 / Estimated remaining time</param>
/// <param name="OptimizedRate">当前优化进度比率 / Current optimisation progress rate</param>
public sealed record RunningHeartBeatDTO(
    string Id,
    TimeSpan RunTime,
    TimeSpan EstimatedTime,
    Flt64 OptimizedRate)
{
    /// <summary>心跳创建时间 / When this heartbeat was created</summary>
    public DateTimeOffset Time { get; } = DateTimeOffset.Now;
}

/// <summary>
/// 已完成求解器的心跳信号 / A finished solver's heartbeat signal.
/// </summary>
/// <remarks>
/// 字段：`id` / `runTime` / `code` / `message`，外加时间戳。
///
/// **`code` 是 `ErrorCode` 的序数**（`UInt64(error.code.ordinal)`），**零表示成功**。本仓的 `ErrorCode` 是 `byte` 枚举，
/// 直接转 `ulong` 即可——**不要改成"code 就是 ErrorCode 本身"**：那样"成功"就没有零值可用了。
///
/// **三个工厂对应三个 `invoke` 重载**：`Finished`（无错误）/ `Failed`（带时长）/ `Failed`（不带时长）。
/// 三个重载里有两个同名，本仓按语义给了两个名字——**同名的重载在 C# 里靠参数个数区分，读代码时不如名字清楚**。
///
/// `id`, `runTime`, `code`, `message`, plus a timestamp. **`code` is the ordinal of `ErrorCode`** (written as
/// `UInt64(error.code.ordinal)`), **zero meaning success**. This implementation's `ErrorCode` is a `byte` enum, so a plain cast to `ulong` does it — **do not "simplify" it to
/// "code is the ErrorCode itself"**: then there is no zero value left for success. **The three factories mirror the three `invoke` overloads**:
/// `Finished`, `Failed` with a duration, and `Failed` without one. overloads are two same-named pairs; this implementation names them by intent — **same-named
/// overloads distinguished only by arity read worse than names**.
/// </remarks>
/// <param name="Id">请求标识 / The request identifier</param>
/// <param name="RunTime">求解器运行总时长 / Total time the solver ran</param>
/// <param name="Code">完成码（零表示成功）/ Completion code (zero means success)</param>
/// <param name="Message">完成消息或错误描述 / Completion message or error description</param>
public sealed record FinnishHeartBeatDTO(
    string Id,
    TimeSpan RunTime,
    Fuookami.Ospf.Math.Algebra.Number.UInt64 Code,
    string Message)
{
    /// <summary>心跳创建时间 / When this heartbeat was created</summary>
    public DateTimeOffset Time { get; } = DateTimeOffset.Now;

    /// <summary>
    /// 成功完成 / Completed successfully.
    /// </summary>
    /// <param name="id">请求标识 / The request identifier</param>
    /// <param name="runTime">总时长 / Total duration</param>
    /// <returns>心跳 / The heartbeat</returns>
    public static FinnishHeartBeatDTO Finished(string id, TimeSpan runTime) =>
        new(id, runTime, new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), string.Empty);

    /// <summary>
    /// 带时长地失败 / Failed, with the elapsed duration.
    /// </summary>
    /// <param name="id">请求标识 / The request identifier</param>
    /// <param name="runTime">总时长 / Total duration</param>
    /// <param name="error">错误 / The error</param>
    /// <returns>心跳 / The heartbeat</returns>
    public static FinnishHeartBeatDTO Failed(string id, TimeSpan runTime, Error<ErrorCode> error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(id, runTime, new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)error.Code), error.Message);
    }

    /// <summary>
    /// 不带时长地失败（还不知道跑了多久）/ Failed without a duration (the elapsed time is not known yet).
    /// </summary>
    /// <param name="id">请求标识 / The request identifier</param>
    /// <param name="error">错误 / The error</param>
    /// <returns>心跳 / The heartbeat</returns>
    public static FinnishHeartBeatDTO Failed(string id, Error<ErrorCode> error) =>
        Failed(id, TimeSpan.Zero, error);
}
