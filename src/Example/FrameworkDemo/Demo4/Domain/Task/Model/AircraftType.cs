#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 通过代码标识的飞机类型（具有池化实例管理）。
/// An aircraft type identified by code, with pooled instance management.
/// </summary>
/// <param name="Code">类型代码 / Type code.</param>
public sealed record AircraftType(AircraftTypeCode Code) {
    private static readonly Dictionary<AircraftTypeCode, AircraftType> Pool = new();

    /// <summary>所有已注册的飞机类型 / All registered aircraft types.</summary>
    public static IReadOnlyCollection<AircraftType> Values => Pool.Values;

    /// <summary>
    /// 按代码检索或创建 AircraftType。
    /// Retrieves or creates an AircraftType by code.
    /// </summary>
    /// <param name="code">类型代码 / Type code.</param>
    /// <returns>飞机类型 / The aircraft type.</returns>
    public static AircraftType GetOrAdd(AircraftTypeCode code) {
        if (!Pool.TryGetValue(code, out var type)) {
            type = new AircraftType(code);
            Pool[code] = type;
        }
        return type;
    }
}

/// <summary>
/// 具有成本、航线飞行时间和连接时间的飞机子机型。
/// An aircraft minor type with cost, route fly times, and connection times.
/// </summary>
/// <param name="Type">飞机类型 / Aircraft type.</param>
/// <param name="Code">子类型代码 / Minor type code.</param>
/// <param name="CostPerHour">每小时成本 / Cost per hour.</param>
/// <param name="RouteFlyTime">航线飞行时间 / Route fly time map.</param>
/// <param name="ConnectionTime">连接时间 / Connection time map.</param>
/// <param name="MaxFlyTime">最大飞行时间 / Maximum fly time.</param>
public sealed record AircraftMinorType(
    AircraftType Type,
    AircraftMinorTypeCode Code,
    Flt64 CostPerHour,
    IReadOnlyDictionary<Route, TimeSpan> RouteFlyTime,
    IReadOnlyDictionary<Airport, TimeSpan> ConnectionTime,
    TimeSpan? MaxFlyTime = null
) {
    private static readonly Dictionary<AircraftMinorTypeCode, AircraftMinorType> Pool = new();

    /// <summary>所有已注册的子类型 / All registered minor types.</summary>
    public static IReadOnlyCollection<AircraftMinorType> Values => Pool.Values;

    /// <summary>最大航线飞行时间 / Maximum route fly time.</summary>
    public TimeSpan MaxRouteFlyTime { get; } = ComputeMaxRouteFlyTime(RouteFlyTime);

    /// <summary>最大连接时间 / Maximum connection time.</summary>
    public TimeSpan MaxConnectionTime { get; } = ComputeMaxConnectionTime(ConnectionTime);

    /// <summary>
    /// 从池中按代码检索飞机子机型。
    /// Retrieves an AircraftMinorType by code from the pool.
    /// </summary>
    /// <param name="code">子类型代码 / Minor type code.</param>
    /// <returns>子类型，若不存在则为 null / The minor type, or null if not found.</returns>
    public static AircraftMinorType? Find(AircraftMinorTypeCode code)
        => Pool.TryGetValue(code, out var minorType) ? minorType : null;

    /// <summary>
    /// 注册子类型到池中。
    /// Registers a minor type into the pool.
    /// </summary>
    /// <param name="minorType">子类型 / The minor type.</param>
    public static void Register(AircraftMinorType minorType) => Pool[minorType.Code] = minorType;

    /// <inheritdoc/>
    public override string ToString() => Code.ToString();

    private static TimeSpan ComputeMaxRouteFlyTime(IReadOnlyDictionary<Route, TimeSpan> routeFlyTime) {
        TimeSpan max = TimeSpan.Zero;
        foreach (var kv in routeFlyTime) {
            if (kv.Value > max) max = kv.Value;
        }
        return max;
    }

    private static TimeSpan ComputeMaxConnectionTime(IReadOnlyDictionary<Airport, TimeSpan> connectionTime) {
        TimeSpan max = TimeSpan.Zero;
        foreach (var kv in connectionTime) {
            if (kv.Value > max) max = kv.Value;
        }
        return max;
    }
}

/// <summary>
/// 从给定映射查找航线时长的扩展方法。
/// Extension method for looking up route duration from a map.
/// </summary>
public static class RouteFlyTimeExtensions {
    /// <summary>
    /// 查找给定出发和到达机场的飞行时间。
    /// Looks up the fly time for a given departure and arrival airport.
    /// </summary>
    /// <param name="map">航线飞行时间映射 / The route fly time map.</param>
    /// <param name="dep">出发机场 / Departure airport.</param>
    /// <param name="arr">到达机场 / Arrival airport.</param>
    /// <returns>飞行时间，若不存在则为 null / The fly time, or null if not found.</returns>
    public static TimeSpan? GetFlyTime(this IReadOnlyDictionary<Route, TimeSpan> map, Airport dep, Airport arr)
        => map.TryGetValue(new Route(dep, arr), out var duration) ? duration : null;
}
