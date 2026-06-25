#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 体积单位 / Volume units.
    /// </summary>
    public static class VolumeUnits
    {
        /// <summary>立方米 / Cubic meter.</summary>
        public static readonly PhysicalUnit CubicMeter = new DerivedUnit(
            SIBaseUnits.Meter.Pow(3), "cubic meter", "m3");
        /// <summary>升 / Liter (1 L = 0.001 m^3).</summary>
        public static readonly PhysicalUnit Liter = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Milli), "liter", "L");
        /// <summary>毫升 / Milliliter.</summary>
        public static readonly PhysicalUnit Milliliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Milli), "milliliter", "mL");
        /// <summary>立方厘米 / Cubic centimeter.</summary>
        public static readonly PhysicalUnit CubicCentimeter = new DerivedUnit(
            LengthUnits.Centimeter.Pow(3), "cubic centimeter", "cm3");
        /// <summary>加仑（美制）/ US gallon (1 gal = 3.785411784 L).</summary>
        public static readonly PhysicalUnit USGallon = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(3.785411784)), "US gallon", "gal");

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
