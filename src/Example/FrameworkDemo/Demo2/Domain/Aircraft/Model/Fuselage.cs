#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 救生筏。Liferaft with weight and index.
/// </summary>
/// <param name="Weight">重量 / Weight (kg)</param>
/// <param name="Index">指数 / Index</param>
public sealed record Liferaft(double Weight, double Index);

/// <summary>
/// 机身。Fuselage with dry operating weight, DOI, and balanced arm.
/// </summary>
/// <param name="Liferaft">救生筏（可选） / Liferaft (optional)</param>
/// <param name="Dow">干操作重量 / Dry operating weight (kg)</param>
/// <param name="Doi">干操作指数 / Dry operating index</param>
/// <param name="BalancedArm">平衡力臂 / Balanced arm (inch)</param>
public sealed record Fuselage(
    Liferaft? Liferaft,
    double Dow,
    double Doi,
    double BalancedArm);
