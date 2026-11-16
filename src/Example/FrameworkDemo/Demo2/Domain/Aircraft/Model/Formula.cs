#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

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

    /// <summary>计算力臂（从指数和重量） / Calculate the arm from an index and a weight</summary>
    /// <param name="index">指数 / The index.</param>
    /// <param name="weight">重量 / The weight.</param>
    /// <returns>力臂 / The arm.</returns>
    /// <remarks>
    /// <see cref="IndexFromWeight"/> 的逆：`arm = index × k / weight + standardDatum`。
    ///
    /// **重量为零时无定义**。`arm` 用浮点除直接算，重量为 0 会得到 `Infinity` 并悄悄污染力矩约束；这里返回 `NaN` 交给调用方判断，
    /// 因为在力矩语境下"无重量的物体的力臂"本来就没有意义。
    ///
    /// The inverse of <see cref="IndexFromWeight"/>: `arm = index × k / weight + standardDatum`. **Undefined for zero weight** — the code divides in floating point and
    /// lets `Infinity` quietly poison the torque constraints; this returns `NaN` for the caller to notice, because "the arm of a weightless object" has no meaning in a torque
    /// context.
    /// </remarks>
    public double Arm(double index, double weight) =>
        weight == 0.0 ? double.NaN : index * ForceDistanceCoefficient / weight + StandardDatum;

    /// <summary>计算 MAC（从平衡力臂） / Calculate MAC from balanced arm</summary>
    public double MacFromBalancedArm(double balancedArm) => (balancedArm - Lip) / Chord;

    /// <summary>计算 MAC（从指数和总重） / Calculate MAC from index and total weight</summary>
    public double MacFromIndex(double index, double totalWeight) =>
        MacFromBalancedArm(BalancedArmFromIndex(index, totalWeight));

    /// <summary>计算平衡力臂的**线性多项式** / Calculate the balanced arm as a **linear polynomial**.</summary>
    /// <param name="index">指数多项式 / The index polynomial.</param>
    /// <param name="totalWeight">总重（必须为正）/ The total weight, which must be positive.</param>
    /// <returns>力臂多项式 / The arm polynomial.</returns>
    /// <remarks>
    /// 与标量版 <see cref="BalancedArmFromIndex"/> 逐步对应：`(index − DoiCorrection) × k / weight + StandardDatum`，
    /// 只是每个系数与常数项各算一遍。**除数是已知常数**（总重），所以多项式形状不变、不会引入二次项。
    ///
    /// Mirrors the scalar <see cref="BalancedArmFromIndex"/> step for step — `(index − DoiCorrection) × k / weight + StandardDatum` — applied to every coefficient and to the
    /// constant. The divisor is a **known constant** (the total weight), so the polynomial keeps its shape and no quadratic term appears.
    /// </remarks>
    public LinearPolynomial<Flt64> BalancedArmPolynomial(LinearPolynomial<Flt64> index, double totalWeight) {
        var monomials = new List<LinearMonomial<Flt64>>(index.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in index.Monomials) {
            double coefficient = monomial.Coefficient.ToDouble() * ForceDistanceCoefficient / totalWeight;
            monomials.Add(new LinearMonomial<Flt64>(new Flt64(coefficient), monomial.Symbol));
        }
        double constant = ((index.Constant.ToDouble() - DoiCorrection) * ForceDistanceCoefficient / totalWeight) + StandardDatum;
        return new LinearPolynomial<Flt64>(monomials, new Flt64(constant));
    }

    /// <summary>计算 MAC 的**线性多项式** / Calculate MAC as a **linear polynomial**.</summary>
    /// <param name="index">指数多项式 / The index polynomial.</param>
    /// <param name="totalWeight">总重 / The total weight.</param>
    /// <returns>MAC 多项式 / The MAC polynomial.</returns>
    /// <remarks>
    /// 与标量版 <see cref="MacFromIndex"/> 同构：先取平衡力臂，再 `(balanced − Lip) / Chord`。
    ///
    /// Isomorphic to the scalar <see cref="MacFromIndex"/>: take the balanced arm, then `(balanced − Lip) / Chord`.
    /// </remarks>
    public LinearPolynomial<Flt64> MacPolynomial(LinearPolynomial<Flt64> index, double totalWeight) {
        LinearPolynomial<Flt64> balanced = BalancedArmPolynomial(index, totalWeight);
        var monomials = new List<LinearMonomial<Flt64>>(balanced.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in balanced.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(new Flt64(monomial.Coefficient.ToDouble() / Chord), monomial.Symbol));
        }
        double constant = (balanced.Constant.ToDouble() - Lip) / Chord;
        return new LinearPolynomial<Flt64>(monomials, new Flt64(constant));
    }
}
