#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 应力单位 / Stress units.
/// </summary>
/// <remarks>
/// **按基线 `Stress.kt` 新建，7 个单位。**
///
/// **应力与压强在 SI 里同量纲**（`N/m²`），**而本仓 `PressureUnits` 已有 `Pascal` 等**——
/// **基线的 `Stress` 与 `Pressure` 是两套独立单位文件，本仓照做，不合并**：
/// **合并会让"管道的许用应力"与"管道内压"共用同一个类型，而它们在工程上是两件事。**
///
/// **Created from the baseline `Stress.kt`, seven units.**
///
/// **Stress and pressure share a dimension in SI** (`N/m²`), **and this repo's `PressureUnits` already has `Pascal`** —
/// **the baseline keeps `Stress` and `Pressure` as separate files and this repo follows suit rather than merging them**:
/// **merging would make "a pipe's allowable stress" and "a pipe's internal pressure" the same type, and in engineering
/// they are two different things.**
/// </remarks>
public static class StressUnits
{
    /// <summary>帕斯卡（应力）/ Pascal, as a stress.</summary>
    public static readonly PhysicalUnit PascalStress = new DerivedUnit(
        ForceUnits.Newton.Divide(AreaUnits.SquareMeter), "pascal", "Pa");

    /// <summary>千帕（应力）/ Kilopascal, as a stress.</summary>
    public static readonly PhysicalUnit KilopascalStress = new DerivedUnit(
        ForceUnits.Newton.Divide(AreaUnits.SquareMeter).ScaleMultiply(Scale.Kilo), "kilopascal", "kPa");

    /// <summary>兆帕（应力）/ Megapascal, as a stress.</summary>
    /// <remarks>
    /// **MPa 是钢结构与压力容器最常用的应力单位**——**而它与"兆帕的压强"数值相同、含义不同。**
    ///
    /// **MPa is the usual stress unit in steelwork and pressure vessels** — **same number as megapascal pressure,
    /// different meaning.**
    /// </remarks>
    public static readonly PhysicalUnit MegapascalStress = new DerivedUnit(
        ForceUnits.Newton.Divide(AreaUnits.SquareMeter).ScaleMultiply(Scale.Mega), "megapascal", "MPa");

    /// <summary>**磅力每平方英寸** / Pounds force per square inch.</summary>
    /// <remarks>
    /// **psi 是英制压力与应力的通用单位**，**而 `PoundForce` 是 4.4482216152605 N，不是 1 N。**
    ///
    /// **psi is the customary imperial unit for both pressure and stress**, **and `PoundForce` is 4.4482216152605 N, not 1 N.**
    /// </remarks>
    public static readonly PhysicalUnit PoundForcePerSquareInch = new DerivedUnit(
        ForceUnits.PoundForce.Divide(AreaUnits.SquareInch), "pounds per square inch", "psi");

    /// <summary>磅力每平方英尺 / Pounds force per square foot.</summary>
    public static readonly PhysicalUnit PoundForcePerSquareFoot = new DerivedUnit(
        ForceUnits.PoundForce.Divide(AreaUnits.SquareFoot), "pounds per square foot", "psf");

    /// <summary>千克力每平方厘米 / Kilogram force per square centimetre.</summary>
    /// <remarks>
    /// **kgf/cm² 在旧式工程图纸上极常见**，**1 kgf/cm² ≈ 98.0665 kPa**——
    /// **它常被口头简称为"一个压"，而它与 1 bar（100 kPa）差 2%。**
    ///
    /// **kgf/cm² is common on older engineering drawings**, **1 kgf/cm² ≈ 98.0665 kPa** —
    /// **it is often called "one atmosphere of gauge" colloquially, and it differs from 1 bar (100 kPa) by 2%.**
    /// </remarks>
    public static readonly PhysicalUnit KilogramForcePerSquareCentimeter = new DerivedUnit(
        ForceUnits.KilogramForce.Divide(AreaUnits.SquareCentimeter), "kilogram force per square centimeter", "kgf/cm2");

    /// <summary>千克力每平方米 / Kilogram force per square metre.</summary>
    public static readonly PhysicalUnit KilogramForcePerSquareMeter = new DerivedUnit(
        ForceUnits.KilogramForce.Divide(AreaUnits.SquareMeter), "kilogram force per square meter", "kgf/m2");

    private sealed class DerivedUnit : PhysicalUnit
    {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;

        public DerivedUnit(PhysicalUnit inner, string name, string symbol)
        {
            _inner = inner;
            _name = name;
            _symbol = symbol;
        }

        public override string Name => _name;

        public override string Symbol => _symbol;

        public override DerivedQuantity Quantity => _inner.Quantity;

        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
