#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 能量单位 / Energy units.
/// </summary>
public static class EnergyUnits {
    /// <summary>焦耳 / Joule (SI derived: N*m).</summary>
    public static readonly PhysicalUnit Joule = new DerivedUnit(
        ForceUnits.Newton.Multiply(SIBaseUnits.Meter),
        "joule", "J");
    /// <summary>千焦 / Kilojoule.</summary>
    public static readonly PhysicalUnit Kilojoule = new DerivedUnit(Joule.ScaleMultiply(Scale.Kilo), "kilojoule", "kJ");
    /// <summary>兆焦 / Megajoule.</summary>
    public static readonly PhysicalUnit Megajoule = new DerivedUnit(Joule.ScaleMultiply(Scale.Mega), "megajoule", "MJ");
    /// <summary>卡路里 / Calorie (1 cal = 4.184 J).</summary>
    public static readonly PhysicalUnit Calorie = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(4.184)), "calorie", "cal");
    /// <summary>千卡 / Kilocalorie.</summary>
    public static readonly PhysicalUnit Kilocalorie = new DerivedUnit(Calorie.ScaleMultiply(Scale.Kilo), "kilocalorie", "kcal");
    /// <summary>千瓦时 / Kilowatt-hour (1 kWh = 3.6e6 J).</summary>
    public static readonly PhysicalUnit KilowattHour = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(3600000)), "kilowatt-hour", "kWh");
    /// <summary>电子伏 / Electronvolt (1 eV = 1.602176634e-19 J).</summary>
    public static readonly PhysicalUnit Electronvolt = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(1.602176634e-19)), "electronvolt", "eV");

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
