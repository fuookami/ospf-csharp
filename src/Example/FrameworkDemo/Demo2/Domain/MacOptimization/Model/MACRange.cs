#nullable enable

using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

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

    /// <summary>
    /// 按机型、公式与总重建出 MAC 范围 / Build the MAC range from the model, formula and total weight.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="formula">公式 / The formula.</param>
    /// <param name="totalWeight">总重 / The total weight.</param>
    /// <returns>MAC 范围 / The MAC range.</returns>
    /// <remarks>
    /// 与基线 `MACRange.invoke` 对齐。**四个固定 MAC 值**：`A = -1`、`OPT = 0`、`B = 1`、`C = 2`（不是对称的 `-1/0/1`——`C` 在 `2`）。
    ///
    /// **参考重量的三级回退**照做：`ComputedTotalWeight[TakeOff]` → `MaxTotalWeight[TakeOff]` → `1`（机型重量单位）。
    /// 第三级不能省：前两级都缺时若取 0，扭矩恒为 0，整个平衡优化退化成恒等式而**不报任何错**。
    ///
    /// **Four fixed MAC values**: `A = -1`, `OPT = 0`, `B = 1`, `C = 2` — not the symmetric `-1/0/1`, since `C` sits at `2`. The **three-level fallback for
    /// the reference weight** is copied: `ComputedTotalWeight[TakeOff]` then `MaxTotalWeight[TakeOff]` then `1`. The third level is not optional: with both absent and a zero taken
    /// instead, every torque is zero and the whole balance optimisation degenerates into an identity **without reporting anything**.
    /// </remarks>
    public static MACRange From(AircraftModel aircraftModel, Formula formula, TotalWeight totalWeight)
    {
        System.ArgumentNullException.ThrowIfNull(aircraftModel);
        System.ArgumentNullException.ThrowIfNull(formula);
        System.ArgumentNullException.ThrowIfNull(totalWeight);

        double referenceWeight = totalWeight.ComputedTotalWeight.TryGetValue(FlightPhase.TakeOff, out double computed)
            ? computed
            : totalWeight.MaxTotalWeight.TryGetValue(FlightPhase.TakeOff, out double maximum)
                ? maximum
                : 1.0;

        var points = new List<MACRangePoint>(4);
        foreach ((MACRangeType type, double mac) in new[] {
            (MACRangeType.A, -1.0),
            (MACRangeType.OPT, 0.0),
            (MACRangeType.B, 1.0),
            (MACRangeType.C, 2.0),
        }) {
            double balancedArm = formula.BalancedArm(mac);
            // **重力 × 臂长**。基线写 `aircraftModel.gravity(referenceWeight) * balancedArm` 再 `.to(torqueUnit)`，那是"力 × 长度"。
            // 本仓 `Gravity` 的量纲是 N（kg·m/s²），而 `TorqueUnit` 是 kgf·in，两者**不能直接换算**（`To` 返回 null，实测）。
            // 这里按 kgf 数值语义计算：`kg × 9.80665 / 9.80665 = kg`，即千克力数值等于千克数，再乘英寸臂长得到 kgf·in。
            // 这是数值等价，不是量纲等价——记录在此，避免后来者以为是笔误。
            //
            // **Gravity times arm.** The code writes `aircraftModel.gravity(referenceWeight) * balancedArm` then `.to(torqueUnit)`, i.e. force times length. This implementation's `Gravity`
            // has dimension N (kg·m/s²) while `TorqueUnit` is kgf·in, and the two **do not convert** (`To` returns null, measured). The kgf value is computed numerically instead:
            // `kg × 9.80665 / 9.80665 = kg`, so the kilogram-force value equals the kilogram count, times the inch arm gives kgf·in. This is numerical equivalence, not dimensional —
            // recorded here so it is not mistaken for a typo.
            double torque = referenceWeight * balancedArm;
            double index = torque / formula.ForceDistanceCoefficient + formula.DoiCorrection;
            points.Add(new MACRangePoint(type, mac, balancedArm, torque, index));
        }
        return new MACRange { Points = points };
    }
}
