#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;

/// <summary>
/// 水平安定面。Horizontal stabilizer with trim and warn-slack intermediate symbols.
/// </summary>
public sealed class HorizontalStabilizer
{
    /// <summary>
    /// 水平安定面键。Horizontal stabilizer key (angle + thrust drate).
    /// </summary>
    public sealed record Key(
        Infrastructure.HorizontalStabilizerAngle Angle,
        Infrastructure.HorizontalStabilizerThrustDrate? ThrustDrate);

    /// <summary>
    /// 水平安定面数据点。Horizontal stabilizer data point.
    /// </summary>
    /// <param name="Tow">起飞重量 / Take-off weight (kg)</param>
    /// <param name="MacPercent">MAC 百分比 / MAC percentage</param>
    /// <param name="Trim">配平值 / Trim value</param>
    public sealed record Point(double Tow, double MacPercent, double Trim);

    /// <summary>
    /// 水平安定面限制。Horizontal stabilizer trim limits.
    /// </summary>
    /// <param name="MinTrim">最小配平 / Minimum trim</param>
    /// <param name="MaxTrim">最大配平 / Maximum trim</param>
    /// <param name="WarnMinTrim">警告最小配平 / Warning minimum trim</param>
    /// <param name="WarnMaxTrim">警告最大配平 / Warning maximum trim</param>
    public sealed record Limit(
        double? MinTrim = null,
        double? MaxTrim = null,
        double? WarnMinTrim = null,
        double? WarnMaxTrim = null);

    // TODO: Port from Kotlin HorizontalStabilizer class
    // Creates trim and warnSlack LinearIntermediateSymbol
}
