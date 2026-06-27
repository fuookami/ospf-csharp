#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 按国内/地区/国际分类枚举机场类型。
/// Enumerates the airport types by domestic/regional/international classification.
/// </summary>
public enum AirportType {
    /// <summary>国内 / Domestic.</summary>
    Domestic,
    /// <summary>地区 / Regional.</summary>
    Regional,
    /// <summary>国际 / International.</summary>
    International
}

/// <summary>
/// 机场类型扩展方法。
/// Extension methods for airport type.
/// </summary>
public static class AirportTypeExtensions {
    /// <summary>
    /// 是否为国内类型。
    /// Whether this is a domestic type.
    /// </summary>
    /// <param name="type">机场类型 / The airport type.</param>
    /// <returns>是否为国内类型 / Whether domestic.</returns>
    public static bool IsDomesticType(this AirportType type) => type == AirportType.Domestic;
}

/// <summary>
/// 通过 ICAO 代码标识的机场（具有类型、中转时间和基地标志）。
/// An airport identified by ICAO code, with type, transfer times, and base flag.
/// </summary>
/// <param name="Icao">ICAO 代码 / ICAO code.</param>
/// <param name="Type">机场类型 / Airport type.</param>
/// <param name="PassengerTransferTime">乘客中转时间 / Passenger transfer time.</param>
/// <param name="CargoTransferTime">货物中转时间 / Cargo transfer time.</param>
/// <param name="Base">是否为基地 / Whether this is a base.</param>
public sealed record Airport(
    ICAO Icao,
    AirportType Type,
    TimeSpan PassengerTransferTime = default,
    TimeSpan CargoTransferTime = default,
    bool Base = false
) {
    private static readonly Dictionary<ICAO, Airport> Pool = new();

    /// <summary>所有已注册的机场 / All registered airports.</summary>
    public static IReadOnlyCollection<Airport> Values => Pool.Values;

    /// <summary>
    /// 通过 ICAO 代码从池中获取机场。
    /// Retrieves an Airport by ICAO code from the pool.
    /// </summary>
    /// <param name="icao">ICAO 代码 / ICAO code.</param>
    /// <returns>机场，若不存在则为 null / The airport, or null if not found.</returns>
    public static Airport? Find(ICAO icao)
        => Pool.TryGetValue(icao, out var airport) ? airport : null;

    /// <summary>
    /// 注册机场到池中。
    /// Registers an airport into the pool.
    /// </summary>
    /// <param name="airport">机场 / The airport.</param>
    public static void Register(Airport airport) => Pool[airport.Icao] = airport;

    /// <inheritdoc/>
    public override int GetHashCode() {
        ReadOnlySpan<char> code = Icao.Code;
        int ret = 0;
        foreach (char ch in code) {
            ret <<= 4;
            ret |= ch - 'A';
        }
        return ret;
    }

    /// <inheritdoc/>
    public override string ToString() => Icao.ToString();
}

/// <summary>
/// 由出发和到达机场定义的航线。
/// A route defined by departure and arrival airports.
/// </summary>
/// <param name="Dep">出发机场 / Departure airport.</param>
/// <param name="Arr">到达机场 / Arrival airport.</param>
public sealed record Route(Airport Dep, Airport Arr);
