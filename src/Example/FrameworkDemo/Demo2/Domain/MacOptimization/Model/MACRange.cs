#nullable enable

using System.Linq;

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;

/// <summary>
/// MAC 范围点类型。MAC range point type.
/// </summary>
public enum MACRangeType
{
    /// <summary>最优点 / Optimal point</summary>
    OPT,
    /// <summary>A 点 / Point A</summary>
    A,
    /// <summary>B 点 / Point B</summary>
    B,
    /// <summary>C 点 / Point C</summary>
    C
}

/// <summary>
/// MAC 范围点。MAC range point with optimal and boundary data.
/// </summary>
/// <param name="Type">点类型 / Point type</param>
/// <param name="MacValue">MAC 百分比值 / MAC percentage value</param>
/// <param name="BalancedArm">平衡力臂 / Balanced arm</param>
/// <param name="TorqueValue">扭矩值 / Torque value</param>
/// <param name="IndexValue">指数值 / Index value</param>
public sealed record MACRangePoint(
    MACRangeType Type,
    double MacValue,
    double BalancedArm,
    double TorqueValue,
    double IndexValue);

/// <summary>
/// 定义具有最优点和边界点的 MAC 范围用于平衡优化。Defines the MAC range with optimal and boundary points for balance optimization.
/// </summary>
public sealed class MACRange
{
    /// <summary>范围点 / Range points</summary>
    public IReadOnlyList<MACRangePoint> Points { get; init; } = Array.Empty<MACRangePoint>();

    /// <summary>最优点 / Optimal point</summary>
    public MACRangePoint? OptPoint => Points.FirstOrDefault(p => p.Type == MACRangeType.OPT);

    /// <summary>左侧点 / Left-hand side points</summary>
    public IReadOnlyList<MACRangePoint> LhsPoints =>
        OptPoint != null ? Points.Where(p => p.IndexValue < OptPoint.IndexValue).ToList() : Array.Empty<MACRangePoint>();

    /// <summary>右侧点 / Right-hand side points</summary>
    public IReadOnlyList<MACRangePoint> RhsPoints =>
        OptPoint != null ? Points.Where(p => p.IndexValue > OptPoint.IndexValue).ToList() : Array.Empty<MACRangePoint>();
}
