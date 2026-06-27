#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 公式。Aerodynamic formulas for balanced arm, index, and MAC calculations.
/// </summary>
public sealed class Formula
{
    /// <summary>飞机型号 / Aircraft model</summary>
    public AircraftModel AircraftModel { get; }
    /// <summary>LIP (Leading edge of MAC) / LIP</summary>
    public double Lip { get; }
    /// <summary>弦长 / Chord length</summary>
    public double Chord { get; }
    /// <summary>标准基准 / Standard datum</summary>
    public double StandardDatum { get; }
    /// <summary>力矩距离系数 / Force distance coefficient</summary>
    public double ForceDistanceCoefficient { get; }
    /// <summary>DOI 修正 / DOI correction</summary>
    public double DoiCorrection { get; }

    public Formula(
        AircraftModel aircraftModel,
        double lip,
        double chord,
        double standardDatum,
        double forceDistanceCoefficient,
        double doiCorrection)
    {
        AircraftModel = aircraftModel;
        Lip = lip;
        Chord = chord;
        StandardDatum = standardDatum;
        ForceDistanceCoefficient = forceDistanceCoefficient;
        DoiCorrection = doiCorrection;
    }

    /// <summary>计算平衡力臂（从 MAC） / Calculate balanced arm from MAC</summary>
    public double BalancedArm(double macPercent) => macPercent * Chord + Lip;

    /// <summary>计算平衡力臂（从指数和总重） / Calculate balanced arm from index and total weight</summary>
    public double BalancedArmFromIndex(double index, double totalWeight)
    {
        double correctedIndex = index - DoiCorrection;
        double correctedTorque = correctedIndex * ForceDistanceCoefficient;
        double armOffset = correctedTorque / totalWeight;
        return armOffset + StandardDatum;
    }

    /// <summary>计算指数（从 MAC 和总重） / Calculate index from MAC and total weight</summary>
    public double IndexFromMac(double macPercent, double totalWeight)
    {
        double armOffset = BalancedArm(macPercent) - StandardDatum;
        double torque = totalWeight * armOffset;
        return torque / ForceDistanceCoefficient + DoiCorrection;
    }

    /// <summary>计算指数（从重量和力臂） / Calculate index from weight and arm</summary>
    public double IndexFromWeight(double weight, double arm)
    {
        double armOffset = arm - StandardDatum;
        double torque = weight * armOffset;
        return torque / ForceDistanceCoefficient;
    }

    /// <summary>计算 MAC（从平衡力臂） / Calculate MAC from balanced arm</summary>
    public double MacFromBalancedArm(double balancedArm) => (balancedArm - Lip) / Chord;

    /// <summary>计算 MAC（从指数和总重） / Calculate MAC from index and total weight</summary>
    public double MacFromIndex(double index, double totalWeight) =>
        MacFromBalancedArm(BalancedArmFromIndex(index, totalWeight));
}
