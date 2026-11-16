#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 加速度单位 / Acceleration units.
/// </summary>
/// <remarks>
/// **按新建。** 六个加速度单位全部是
/// `XPerSecond / Second` 的形状——**"某个速度单位每秒"**，
/// **所以它们不是各写一个数，而是复用速度单位的定义。**
///
/// **Created from the code.** All six baseline acceleration units take the shape
/// `XPerSecond / Second` — **"a speed unit per second"** —
/// **so they reuse the speed definitions rather than each carrying a number.**
/// </remarks>
public static class AccelerationUnits
{
    /// <summary>米每二次方秒 / Metres per second squared.</summary>
    public static readonly PhysicalUnit MetersPerSecondSquared = new DerivedUnit(
        SpeedUnits.MetersPerSecond.Divide(SIBaseUnits.Second), "meters per second squared", "m/s2");

    /// <summary>厘米每二次方秒 / Centimetres per second squared.</summary>
    public static readonly PhysicalUnit CentimetersPerSecondSquared = new DerivedUnit(
        SpeedUnits.CentimetersPerSecond.Divide(SIBaseUnits.Second), "centimeters per second squared", "cm/s2");

    /// <summary>千米每二次方秒 / Kilometres per second squared.</summary>
    public static readonly PhysicalUnit KilometersPerSecondSquared = new DerivedUnit(
        SpeedUnits.KilometersPerSecond.Divide(SIBaseUnits.Second), "kilometers per second squared", "km/s2");

    /// <summary>英寸每二次方秒 / Inches per second squared.</summary>
    public static readonly PhysicalUnit InchesPerSecondSquared = new DerivedUnit(
        SpeedUnits.InchesPerSecond.Divide(SIBaseUnits.Second), "inches per second squared", "in/s2");

    /// <summary>英尺每二次方秒 / Feet per second squared.</summary>
    public static readonly PhysicalUnit FeetPerSecondSquared = new DerivedUnit(
        SpeedUnits.FeetPerSecond.Divide(SIBaseUnits.Second), "feet per second squared", "ft/s2");

    /// <summary>**标准重力加速度 9.80665 m/s²** / Standard gravity, 9.80665 m/s².</summary>
    /// <remarks>
    /// **9.80665 是定义值**（CGPM 1901），**不是测量值**——**所以它不该有不确定度，也不该随地点变化。**
    /// **写成 9.8 或 9.81 都会让"1 g 的载荷"差出千分之几**，
    /// **而航空与结构计算里"千分之几"就是合格与不合格的差别。**
    ///
    /// **9.80665 is a defined value** (CGPM 1901), **not a measurement** — **so it carries no uncertainty and does not vary by place.**
    /// **Writing 9.8 or 9.81 makes a "1 g load" off by a few thousandths**,
    /// **and in aviation and structural work a few thousandths is the difference between passing and failing.**
    /// </remarks>
    public static readonly PhysicalUnit StandardGravity = new DerivedUnit(
        SpeedUnits.MetersPerSecond.Divide(SIBaseUnits.Second).ScaleMultiply(Scale.Invoke(9.80665)),
        "standard gravity", "g0");

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
