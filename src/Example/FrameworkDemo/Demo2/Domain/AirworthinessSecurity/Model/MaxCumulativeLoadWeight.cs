#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 累积载荷方向。Direction for cumulative load weight.
/// </summary>
public enum CumulativeLoadDirection
{
    /// <summary>向前 / Forward</summary>
    FWD,
    /// <summary>向后 / Aft</summary>
    AFT
}

/// <summary>
/// 累积载荷重量检查点。Cumulative load weight check point.
/// </summary>
/// <param name="Zone">限制区域 / Limit zone</param>
/// <param name="ToArm">到力臂 / To arm</param>
/// <param name="MaxSum">最大总和 / Maximum sum</param>
public sealed record CumulativeCheckPoint(
    CumulativeLoadLimitZone Zone,
    double ToArm,
    double MaxSum);

/// <summary>
/// 累积载荷重量限制区域。Cumulative load weight limit zone.
/// </summary>
/// <param name="Direction">方向 / Direction</param>
/// <param name="Name">名称 / Name</param>
/// <param name="FromArm">起始力臂 / From arm</param>
/// <param name="CheckPoints">检查点 / Check points</param>
public sealed record CumulativeLoadLimitZone(
    CumulativeLoadDirection Direction,
    string Name,
    double FromArm,
    IReadOnlyList<CumulativeCheckPoint> CheckPoints);

/// <summary>
/// 沿机身向前或向后的累积载荷重量限制。Cumulative load weight limits along the fuselage in forward or aft direction.
/// </summary>
public sealed class MaxCumulativeLoadWeight
{
    /// <summary>限制区域 / Limit zones</summary>
    public IReadOnlyList<CumulativeLoadLimitZone> LimitZones { get; init; } = Array.Empty<CumulativeLoadLimitZone>();
}
