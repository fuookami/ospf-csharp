#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 频率单位 / Frequency units.
/// </summary>
public static class FrequencyUnits {
    /// <summary>赫兹 / Hertz (1 Hz = 1/s).</summary>
    public static readonly PhysicalUnit Hertz = new DerivedUnit(SIBaseUnits.Second.Reciprocal(), "hertz", "Hz");
    /// <summary>千赫 / Kilohertz.</summary>
    public static readonly PhysicalUnit Kilohertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Kilo), "kilohertz", "kHz");
    /// <summary>兆赫 / Megahertz.</summary>
    public static readonly PhysicalUnit Megahertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Mega), "megahertz", "MHz");
    /// <summary>吉赫 / Gigahertz.</summary>
    public static readonly PhysicalUnit Gigahertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Giga), "gigahertz", "GHz");

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
