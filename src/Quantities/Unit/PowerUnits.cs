#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 功率单位 / Power units.
/// </summary>
public static class PowerUnits {
    /// <summary>瓦特 / Watt (SI derived: J/s).</summary>
    public static readonly PhysicalUnit Watt = new DerivedUnit(
        EnergyUnits.Joule.Divide(SIBaseUnits.Second),
        "watt", "W");
    /// <summary>千瓦 / Kilowatt.</summary>
    public static readonly PhysicalUnit Kilowatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Kilo), "kilowatt", "kW");
    /// <summary>兆瓦 / Megawatt.</summary>
    public static readonly PhysicalUnit Megawatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Mega), "megawatt", "MW");
    /// <summary>毫瓦 / Milliwatt.</summary>
    public static readonly PhysicalUnit Milliwatt = new DerivedUnit(Watt.ScaleMultiply(Scale.Milli), "milliwatt", "mW");
    /// <summary>马力 / Horsepower (1 hp = 745.69987158 W).</summary>
    public static readonly PhysicalUnit Horsepower = new DerivedUnit(Watt.ScaleMultiply(Scale.Invoke(745.69987158)), "horsepower", "hp");

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
