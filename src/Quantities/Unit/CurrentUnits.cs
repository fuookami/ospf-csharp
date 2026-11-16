#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 电流单位 / Electric current units.
/// </summary>
/// <remarks>
/// **补齐电流单位。** 四个单位（`Ampere`/`Milliampere`/`Microampere`/`Kiloampere`）
/// 本仓此前一个都没有独立文件**——`Ampere` 只作为 SI 基准存在，派生单位缺失。
///
/// **这里同时把电学三件套（电流、电压、电阻）补齐**，因为它们是互相定义的：
/// `Volt = Watt / Ampere`、`Ohm = Volt / Ampere`，
/// **所以缺了电流，电压与电阻的推导就无从谈起。**
///
/// Created. **None of the four current units had a file of its own here** —
/// `Ampere` existed only as an SI base, with no derived units.
///
/// **This also completes the electrical trio (current, voltage, resistance), because they define each other:**
/// `Volt = Watt / Ampere` and `Ohm = Volt / Ampere`,
/// **so without current, the voltage and resistance derivations have nothing to stand on.**
/// </remarks>
public static class CurrentUnits {
    /// <summary>安培（SI 基准）/ Ampere, the SI base.</summary>
    public static readonly PhysicalUnit Ampere = SIBaseUnits.Ampere;

    /// <summary>毫安（10⁻³ A）/ Milliampere.</summary>
    public static readonly PhysicalUnit Milliampere =
        new DerivedUnit(SIBaseUnits.Ampere.ScaleMultiply(Scale.Milli), "milliampere", "mA");
    /// <summary>微安（10⁻⁶ A）/ Microampere.</summary>
    public static readonly PhysicalUnit Microampere =
        new DerivedUnit(SIBaseUnits.Ampere.ScaleMultiply(Scale.Micro), "microampere", "uA");
    /// <summary>千安（10³ A）/ Kiloampere.</summary>
    public static readonly PhysicalUnit Kiloampere =
        new DerivedUnit(SIBaseUnits.Ampere.ScaleMultiply(Scale.Kilo), "kiloampere", "kA");

    private sealed class DerivedUnit : PhysicalUnit {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;
        public DerivedUnit(PhysicalUnit inner, string name, string symbol) { _inner = inner; _name = name; _symbol = symbol; }
        public override string Name => _name;
        public override string Symbol => _symbol;
        public override DerivedQuantity Quantity => _inner.Quantity;
        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
