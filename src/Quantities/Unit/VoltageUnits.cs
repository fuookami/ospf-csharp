#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 电压单位 / Voltage units.
    /// </summary>
    public static class VoltageUnits
    {
        /// <summary>伏特 / Volt (SI derived: W/A).</summary>
        public static readonly PhysicalUnit Volt = new DerivedUnit(
            PowerUnits.Watt.Divide(SIBaseUnits.Ampere),
            "volt", "V");
        /// <summary>千伏 / Kilovolt.</summary>
        public static readonly PhysicalUnit Kilovolt = new DerivedUnit(Volt.ScaleMultiply(Scale.Kilo), "kilovolt", "kV");
        /// <summary>毫伏 / Millivolt.</summary>
        public static readonly PhysicalUnit Millivolt = new DerivedUnit(Volt.ScaleMultiply(Scale.Milli), "millivolt", "mV");
        /// <summary>微伏 / Microvolt.</summary>
        public static readonly PhysicalUnit Microvolt = new DerivedUnit(Volt.ScaleMultiply(Scale.Micro), "microvolt", "uV");

        private sealed class DerivedUnit : PhysicalUnit
        {
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
}
