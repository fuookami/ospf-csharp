#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 电阻单位 / Resistance units.
/// </summary>
public static class ResistanceUnits {
    /// <summary>欧姆 / Ohm (SI derived: V/A).</summary>
    public static readonly PhysicalUnit Ohm = new DerivedUnit(
        VoltageUnits.Volt.Divide(SIBaseUnits.Ampere), "ohm", "ohm");
    /// <summary>千欧 / Kilohm.</summary>
    public static readonly PhysicalUnit Kilohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Kilo), "kilohm", "kohm");
    /// <summary>兆欧 / Megohm.</summary>
    public static readonly PhysicalUnit Megohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Mega), "megohm", "Mohm");
    /// <summary>毫欧 / Milliohm.</summary>
    public static readonly PhysicalUnit Milliohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Milli), "milliohm", "mohm");

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
