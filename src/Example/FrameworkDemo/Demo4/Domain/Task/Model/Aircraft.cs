#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 通过注册号标识的飞机（具有子机型和容量信息）。
/// An aircraft identified by register number, with minor type and capacity information.
/// </summary>
/// <param name="RegNo">注册号 / Register number.</param>
/// <param name="MinorType">子机型 / Minor type.</param>
/// <param name="Capacity">容量 / Capacity.</param>
public sealed class Aircraft(
    AircraftRegisterNumber RegNo,
    AircraftMinorType MinorType,
    AircraftCapacity Capacity
) : Executor(RegNo.No, RegNo.No) {
    private static readonly Dictionary<AircraftRegisterNumber, Aircraft> Pool = new();
    private AircraftUsability? _usability;

    /// <summary>注册号 / Register number.</summary>
    public AircraftRegisterNumber RegNo { get; } = RegNo;

    /// <summary>子机型 / Minor type.</summary>
    public AircraftMinorType MinorType { get; } = MinorType;

    /// <summary>容量 / Capacity.</summary>
    public AircraftCapacity Capacity { get; } = Capacity;

    /// <summary>所有已注册的飞机 / All registered aircraft.</summary>
    public static IReadOnlyCollection<Aircraft> Values => Pool.Values;

    /// <summary>飞机类型 / Aircraft type.</summary>
    public AircraftType Type => MinorType.Type;

    /// <summary>每小时成本 / Cost per hour.</summary>
    public Flt64 CostPerHour => MinorType.CostPerHour;

    /// <summary>航线飞行时间 / Route fly time map.</summary>
    public IReadOnlyDictionary<Route, TimeSpan> RouteFlyTime => MinorType.RouteFlyTime;

    /// <summary>最大飞行时间 / Maximum fly time.</summary>
    public TimeSpan? MaxFlyTime => MinorType.MaxFlyTime;

    /// <summary>最大航线飞行时间 / Maximum route fly time.</summary>
    public TimeSpan MaxRouteFlyTime => MinorType.MaxRouteFlyTime;

    /// <summary>连接时间 / Connection time map.</summary>
    public IReadOnlyDictionary<Airport, TimeSpan> ConnectionTime => MinorType.ConnectionTime;

    /// <summary>最大连接时间 / Maximum connection time.</summary>
    public TimeSpan MaxConnectionTime => MinorType.MaxConnectionTime;

    /// <summary>可用性 / Usability.</summary>
    public AircraftUsability Usability => _usability
        ?? throw new InvalidOperationException("Usability not initialized. Call SetUsability first.");

    /// <summary>
    /// 设置飞机可用性。
    /// Sets the aircraft usability.
    /// </summary>
    /// <param name="usability">可用性 / The usability.</param>
    public void SetUsability(AircraftUsability usability) => _usability = usability;

    /// <summary>
    /// 从池中按注册号检索飞机。
    /// Retrieves an Aircraft by register number from the pool.
    /// </summary>
    /// <param name="regNo">注册号 / Register number.</param>
    /// <returns>飞机，若不存在则为 null / The aircraft, or null if not found.</returns>
    public static Aircraft? Find(AircraftRegisterNumber regNo)
        => Pool.TryGetValue(regNo, out var aircraft) ? aircraft : null;

    /// <summary>
    /// 注册飞机到池中。
    /// Registers an aircraft into the pool.
    /// </summary>
    /// <param name="aircraft">飞机 / The aircraft.</param>
    public static void Register(Aircraft aircraft) => Pool[aircraft.RegNo] = aircraft;

    /// <summary>
    /// 返回给定舱位的乘客容量，如果不是客机则返回零。
    /// Returns the passenger capacity for the given class, or zero if not a passenger aircraft.
    /// </summary>
    /// <param name="cls">舱位 / The passenger class.</param>
    /// <returns>容量 / The capacity.</returns>
    public ulong GetCapacity(Infrastructure.PassengerClassId cls)
        => Capacity is AircraftCapacity.PassengerCapacity p ? p.GetCapacity(cls) : 0UL;

    /// <inheritdoc/>
    public override int GetHashCode() {
        ReadOnlySpan<char> no = RegNo.No;
        int ret = 0;
        foreach (char ch in no) {
            ret <<= 5;
            ret |= char.IsDigit(ch) ? ch - '0' : ch - 'A' + 10;
        }
        return ret;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is Aircraft other && RegNo == other.RegNo;

    /// <inheritdoc/>
    public override string ToString() => RegNo.ToString();
}

/// <summary>
/// 跟踪飞机可用性（包括位置、启用时间和飞行循环周期）。
/// Tracks aircraft usability including location, enabled time, and flight cycle periods.
/// </summary>
/// <param name="LastTask">上一个任务 / The last task.</param>
/// <param name="Location">当前位置 / Current location.</param>
/// <param name="EnabledTime">可用时间 / Enabled time.</param>
/// <param name="FlightCyclePeriods">飞行循环周期 / Flight cycle periods.</param>
public sealed class AircraftUsability {
    /// <summary>构造函数 / Constructor.</summary>
    public AircraftUsability(
        IAbstractTask<Aircraft, FlightTaskAssignment>? lastTask,
        Airport location,
        DateTimeOffset enabledTime,
        IReadOnlyList<FlightCyclePeriod>? flightCyclePeriods = null) {
        LastTask = lastTask;
        Location = location;
        EnabledTime = enabledTime;
        Periods = flightCyclePeriods ?? Array.Empty<FlightCyclePeriod>();
    }

    /// <summary>上一个任务 / Last task.</summary>
    public IAbstractTask<Aircraft, FlightTaskAssignment>? LastTask { get; }

    /// <summary>可用时间 / Enabled time.</summary>
    public DateTimeOffset EnabledTime { get; }
    /// <summary>当前位置 / Current location.</summary>
    public Airport Location { get; }

    /// <summary>飞行循环周期 / Flight cycle periods.</summary>
    public IReadOnlyList<FlightCyclePeriod> Periods { get; }

    /// <summary>
    /// 返回给定时间和飞行小时超出的飞行周期数。
    /// Returns the number of flight cycle periods exceeded for the given time and flight hour.
    /// </summary>
    /// <param name="time">时间 / The time.</param>
    /// <param name="flightHour">飞行小时 / The flight hour.</param>
    /// <returns>超出周期数 / Number of exceeded periods.</returns>
    public ulong OverFlightHourTimes(DateTimeOffset time, FlightHour flightHour) {
        ulong count = 0;
        foreach (var period in Periods) {
            if (time < period.ExpirationTime && !period.Enabled(flightHour)) count++;
        }
        return count;
    }

    /// <summary>
    /// 返回给定时间和飞行循环超出的飞行周期数。
    /// Returns the number of flight cycle periods exceeded for the given time and flight cycle.
    /// </summary>
    /// <param name="time">时间 / The time.</param>
    /// <param name="flightCycle">飞行循环 / The flight cycle.</param>
    /// <returns>超出周期数 / Number of exceeded periods.</returns>
    public ulong OverFlightCycleTimes(DateTimeOffset time, FlightCycle flightCycle) {
        ulong count = 0;
        foreach (var period in Periods) {
            if (time < period.ExpirationTime && !period.Enabled(flightCycle)) count++;
        }
        return count;
    }

    /// <summary>
    /// 返回所有周期的总超出飞行小时数。
    /// Returns the total excess flight hours across all periods.
    /// </summary>
    /// <param name="time">时间 / The time.</param>
    /// <param name="flightHour">飞行小时 / The flight hour.</param>
    /// <returns>总超出飞行小时 / Total excess flight hours.</returns>
    public FlightHour OverFlightHour(DateTimeOffset time, FlightHour flightHour) {
        var ret = FlightHour.Zero;
        foreach (var period in Periods) {
            if (time >= period.ExpirationTime) continue;
            ret += period.OverFlightHour(flightHour);
        }
        return ret;
    }

    /// <summary>
    /// 返回所有周期的总超出飞行循环数。
    /// Returns the total excess flight cycles across all periods.
    /// </summary>
    /// <param name="time">时间 / The time.</param>
    /// <param name="flightCycle">飞行循环 / The flight cycle.</param>
    /// <returns>总超出飞行循环 / Total excess flight cycles.</returns>
    public FlightCycle OverFlightCycle(DateTimeOffset time, FlightCycle flightCycle) {
        var ret = FlightCycle.Zero;
        foreach (var period in Periods) {
            if (time >= period.ExpirationTime) continue;
            ret += period.OverFlightCycle(flightCycle);
        }
        return ret;
    }
}
