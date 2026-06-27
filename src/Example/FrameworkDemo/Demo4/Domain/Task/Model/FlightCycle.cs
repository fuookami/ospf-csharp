#nullable enable

using System;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 包装时长的飞行小时值（支持算术和比较）。
/// A flight hour value wrapping a duration, supporting arithmetic and comparison.
/// </summary>
/// <param name="Hours">飞行小时 / The hours.</param>
public readonly record struct FlightHour(TimeSpan Hours) {
    /// <summary>零值 / Zero value.</summary>
    public static readonly FlightHour Zero = new(TimeSpan.Zero);

    /// <summary>加法 / Addition.</summary>
    public static FlightHour operator +(FlightHour a, FlightHour b) => new(a.Hours + b.Hours);

    /// <summary>减法 / Subtraction.</summary>
    public static FlightHour operator -(FlightHour a, FlightHour b) => new(a.Hours - b.Hours);

    /// <summary>小于比较 / Less-than comparison.</summary>
    public static bool operator <(FlightHour a, FlightHour b) => a.Hours < b.Hours;

    /// <summary>小于等于比较 / Less-than-or-equal comparison.</summary>
    public static bool operator <=(FlightHour a, FlightHour b) => a.Hours <= b.Hours;

    /// <summary>大于比较 / Greater-than comparison.</summary>
    public static bool operator >(FlightHour a, FlightHour b) => a.Hours > b.Hours;

    /// <summary>大于等于比较 / Greater-than-or-equal comparison.</summary>
    public static bool operator >=(FlightHour a, FlightHour b) => a.Hours >= b.Hours;
}

/// <summary>
/// 包装计数的飞行循环值（支持算术和比较）。
/// A flight cycle value wrapping a count, supporting arithmetic and comparison.
/// </summary>
/// <param name="Cycles">循环数 / The cycles.</param>
public readonly record struct FlightCycle(ulong Cycles) {
    /// <summary>零值 / Zero value.</summary>
    public static readonly FlightCycle Zero = new(0UL);

    /// <summary>加法 / Addition.</summary>
    public static FlightCycle operator +(FlightCycle a, FlightCycle b) => new(a.Cycles + b.Cycles);

    /// <summary>减法 / Subtraction.</summary>
    public static FlightCycle operator -(FlightCycle a, FlightCycle b) => new(a.Cycles - b.Cycles);

    /// <summary>小于比较 / Less-than comparison.</summary>
    public static bool operator <(FlightCycle a, FlightCycle b) => a.Cycles < b.Cycles;

    /// <summary>小于等于比较 / Less-than-or-equal comparison.</summary>
    public static bool operator <=(FlightCycle a, FlightCycle b) => a.Cycles <= b.Cycles;

    /// <summary>大于比较 / Greater-than comparison.</summary>
    public static bool operator >(FlightCycle a, FlightCycle b) => a.Cycles > b.Cycles;

    /// <summary>大于等于比较 / Greater-than-or-equal comparison.</summary>
    public static bool operator >=(FlightCycle a, FlightCycle b) => a.Cycles >= b.Cycles;
}

/// <summary>
/// 具有过期时间和剩余飞行小时/循环限制的飞行循环维护周期。
/// A flight cycle maintenance period with expiration time and remaining flight hour/cycle limits.
/// </summary>
/// <param name="ExpirationTime">过期时间 / Expiration time.</param>
/// <param name="RemainingFlightHour">剩余飞行小时 / Remaining flight hour limit.</param>
/// <param name="RemainingFlightCycle">剩余飞行循环 / Remaining flight cycle limit.</param>
public sealed record FlightCyclePeriod(
    DateTimeOffset ExpirationTime,
    FlightHour? RemainingFlightHour,
    FlightCycle? RemainingFlightCycle
) {
    /// <summary>
    /// 检查给定飞行小时是否在剩余限制内。
    /// Checks whether the given flight hour is within the remaining limit.
    /// </summary>
    /// <param name="flightHour">飞行小时 / The flight hour.</param>
    /// <returns>是否在限制内 / Whether within limit.</returns>
    public bool Enabled(FlightHour flightHour)
        => RemainingFlightHour is null || flightHour <= RemainingFlightHour.Value;

    /// <summary>
    /// 检查给定飞行循环是否在剩余限制内。
    /// Checks whether the given flight cycle is within the remaining limit.
    /// </summary>
    /// <param name="flightCycle">飞行循环 / The flight cycle.</param>
    /// <returns>是否在限制内 / Whether within limit.</returns>
    public bool Enabled(FlightCycle flightCycle)
        => RemainingFlightCycle is null || flightCycle <= RemainingFlightCycle.Value;

    /// <summary>
    /// 返回超出剩余限制的飞行小时数。
    /// Returns the excess flight hours beyond the remaining limit.
    /// </summary>
    /// <param name="flightHour">飞行小时 / The flight hour.</param>
    /// <returns>超出的飞行小时 / The excess flight hours.</returns>
    public FlightHour OverFlightHour(FlightHour flightHour)
        => RemainingFlightHour is not null && RemainingFlightHour.Value < flightHour
            ? flightHour - RemainingFlightHour.Value
            : FlightHour.Zero;

    /// <summary>
    /// 返回超出剩余限制的飞行循环数。
    /// Returns the excess flight cycles beyond the remaining limit.
    /// </summary>
    /// <param name="flightCycle">飞行循环 / The flight cycle.</param>
    /// <returns>超出的飞行循环 / The excess flight cycles.</returns>
    public FlightCycle OverFlightCycle(FlightCycle flightCycle)
        => RemainingFlightCycle is not null && RemainingFlightCycle.Value <= flightCycle
            ? flightCycle - RemainingFlightCycle.Value
            : FlightCycle.Zero;
}
