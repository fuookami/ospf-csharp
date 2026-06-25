#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 电荷单位 / Electric charge units.
    /// </summary>
    public static class ElectricChargeUnits
    {
        /// <summary>库仑 / Coulomb (SI derived: A*s).</summary>
        public static readonly PhysicalUnit Coulomb = new DerivedUnit(
            SIBaseUnits.Ampere.Multiply(SIBaseUnits.Second), "coulomb", "C");
        /// <summary>毫库 / Millicoulomb.</summary>
        public static readonly PhysicalUnit Millicoulomb = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Milli), "millicoulomb", "mC");
        /// <summary>微库 / Microcoulomb.</summary>
        public static readonly PhysicalUnit Microcoulomb = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Micro), "microcoulomb", "uC");
        /// <summary>安时 / Ampere-hour (1 Ah = 3600 C).</summary>
        public static readonly PhysicalUnit AmpereHour = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(3600)), "ampere-hour", "Ah");

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
