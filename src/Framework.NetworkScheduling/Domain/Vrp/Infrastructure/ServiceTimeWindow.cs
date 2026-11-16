// VRPTW 基础设施：行驶时间与服务时间窗 / VRPTW infrastructure: travel time and service time window.
// 与固定 Kotlin 、（41 行）对应。
// And.
//
// **时间类型的映射**：`kotlin.time.Duration` 与 `kotlin.time.Instant`，C# 侧用标准库 `TimeSpan` 与
// `DateTimeOffset`，不引入任何新依赖。`TimeSpan` 是有符号时长、`DateTimeOffset` 是带偏移的绝对时刻，语义一一对应。
// **Time type mapping**: the code uses `kotlin.time.Duration` and `kotlin.time.Instant`; the C# side uses the standard
// library's `TimeSpan` and `DateTimeOffset` with no new dependency. `TimeSpan` is a signed duration and `DateTimeOffset`
// an absolute instant with offset — a one-to-one correspondence.

using System;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;

/// <summary>
/// 非负行驶时间 / Non-negative travel time.
/// </summary>
/// <param name="Duration">行驶时长 / Travel duration.</param>
public sealed class TravelTime(TimeSpan Duration) {
    /// <summary>行驶时长 / Travel duration.</summary>
    public TimeSpan Duration { get; } = Duration;

    /// <summary>
    /// 创建行驶时间 / Create travel time.
    /// </summary>
    /// <remarks>
    /// **零时长合法**：表示两个节点重合（同一坐标的客户、仓库与首个客户同址），是常见输入而不是错误。
    /// **A zero duration is legal**: it means two nodes coincide (co-located customers, or a depot sharing its first
    /// customer's position), a common input rather than an error.
    /// </remarks>
    /// <param name="duration">行驶时长 / Travel duration.</param>
    /// <returns>行驶时间或校验失败 / The travel time, or a validation failure.</returns>
    public static Result<TravelTime, ErrorCode, Error<ErrorCode>> Invoke(TimeSpan duration) {
        if (duration < TimeSpan.Zero) {
            return NetworkSchedulingErrors.Failure<TravelTime>(
                "创建行驶时间失败：时间不能为负 / Failed to create travel time: time cannot be negative");
        }
        return Results.Ok(new TravelTime(duration));
    }
}

/// <summary>
/// 闭区间服务时间窗 / Closed service time window.
/// </summary>
/// <remarks>
/// **闭区间而非右开区间**：VRPTW 允许服务恰好在上限时刻开始，因此不能直接复用 Gantt 的右开时间区间。用错闭开性会让
/// "刚好赶上截止时刻"的客户被判为迟到，而这类边界解在实际排程里恰恰是最优解。
/// **Closed rather than half-open**: VRPTW allows service to start exactly at the upper bound, so Gantt's half-open range
/// cannot be reused. Getting the closedness wrong marks "just made the deadline" customers as late, and exactly those
/// boundary solutions are often optimal in practice.
/// </remarks>
/// <param name="ReadyTime">最早服务开始时刻 / Earliest service-start instant.</param>
/// <param name="DueTime">最晚服务开始时刻 / Latest service-start instant.</param>
public sealed class ServiceTimeWindow(DateTimeOffset ReadyTime, DateTimeOffset DueTime) {
    /// <summary>最早服务开始时刻 / Earliest service-start instant.</summary>
    public DateTimeOffset ReadyTime { get; } = ReadyTime;

    /// <summary>最晚服务开始时刻 / Latest service-start instant.</summary>
    public DateTimeOffset DueTime { get; } = DueTime;

    /// <summary>
    /// 判断服务开始时刻是否在闭区间内 / Check whether a service-start instant is inside the closed interval.
    /// </summary>
    /// <param name="serviceStart">服务开始时刻 / Service-start instant.</param>
    /// <returns>是否在窗内 / Whether it is inside the window.</returns>
    public bool Contains(DateTimeOffset serviceStart) =>
        ReadyTime <= serviceStart && serviceStart <= DueTime;

    /// <summary>
    /// 创建闭区间服务时间窗 / Create a closed service time window.
    /// </summary>
    /// <remarks>
    /// **上下界相等合法**：退化为"必须在某一时刻开始"，这在硬约束排程里是合法输入。
    /// **Equal bounds are legal**: the window degenerates to "must start at exactly this instant", a legitimate input under
    /// hard scheduling constraints.
    /// </remarks>
    /// <param name="readyTime">最早服务开始时刻 / Earliest service-start instant.</param>
    /// <param name="dueTime">最晚服务开始时刻 / Latest service-start instant.</param>
    /// <returns>服务时间窗或校验失败 / The window, or a validation failure.</returns>
    public static Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> Invoke(
        DateTimeOffset readyTime,
        DateTimeOffset dueTime) {
        if (dueTime < readyTime) {
            return NetworkSchedulingErrors.Failure<ServiceTimeWindow>(
                "创建服务时间窗失败：截止时刻早于起始时刻 / Failed to create service time window: due time is before "
                + "ready time");
        }
        return Results.Ok(new ServiceTimeWindow(readyTime, dueTime));
    }
}
