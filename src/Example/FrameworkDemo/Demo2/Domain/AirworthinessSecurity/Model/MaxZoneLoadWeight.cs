#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 区域载荷限制部分。Zone load weight limit part.
/// </summary>
/// <param name="PositionIndex">位置索引 / Position index</param>
/// <param name="Weight">重量系数 / Weight coefficient</param>
public sealed record ZoneLoadLimitPart(int PositionIndex, double Weight);

/// <summary>
/// 区域载荷限制区域。Zone load weight limit zone.
/// </summary>
/// <param name="Name">名称 / Name</param>
/// <param name="MaxLoadWeight">最大载荷重量 / Maximum load weight</param>
/// <param name="Parts">限制部分 / Limit parts</param>
public sealed record ZoneLoadLimitZone(
    string Name,
    double MaxLoadWeight,
    IReadOnlyList<ZoneLoadLimitPart> Parts);

/// <summary>
/// 每个机身区域的最大允许载荷重量。Maximum allowable load weight per fuselage zone.
/// </summary>
public sealed class MaxZoneLoadWeight
{
    /// <summary>限制区域 / Limit zones</summary>
    public IReadOnlyList<ZoneLoadLimitZone> LimitZones { get; init; } = Array.Empty<ZoneLoadLimitZone>();
}
