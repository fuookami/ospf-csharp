#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 质量单位 / Mass units.
    /// </summary>
    public static class MassUnits
    {
        /// <summary>克 / Gram (1 g = 0.001 kg).</summary>
        public static readonly PhysicalUnit Gram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Milli, "gram", "g");
        /// <summary>毫克 / Milligram.</summary>
        public static readonly PhysicalUnit Milligram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Micro, "milligram", "mg");
        /// <summary>吨 / Ton (1 t = 1000 kg).</summary>
        public static readonly PhysicalUnit Ton = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Kilo, "ton", "t");
        /// <summary>磅 / Pound (1 lb = 0.45359237 kg).</summary>
        public static readonly PhysicalUnit Pound = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.45359237), "pound", "lb");
        /// <summary>盎司 / Ounce (1 oz = 0.028349523125 kg).</summary>
        public static readonly PhysicalUnit Ounce = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.028349523125), "ounce", "oz");

        private sealed class DerivedUnit : PhysicalUnit
        {
            private readonly PhysicalUnit _base;
            private readonly Scale _factor;
            private readonly string _name;
            private readonly string _symbol;

            public DerivedUnit(PhysicalUnit baseUnit, Scale factor, string name, string symbol)
            {
                _base = baseUnit;
                _factor = factor;
                _name = name;
                _symbol = symbol;
            }

            public override string Name => _name;
            public override string Symbol => _symbol;
            public override DerivedQuantity Quantity => _base.Quantity;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(_factor);
        }
    }
}
