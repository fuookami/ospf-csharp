#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// SI 基本单位 / SI base units.
    /// </summary>
    public static class SIBaseUnits
    {
        /// <summary>米（基本单位）/ Meter (base unit).</summary>
        public static readonly PhysicalUnit Meter = new MeterUnit();
        /// <summary>千克（基本单位）/ Kilogram (base unit).</summary>
        public static readonly PhysicalUnit Kilogram = new KilogramUnit();
        /// <summary>秒（基本单位）/ Second (base unit).</summary>
        public static readonly PhysicalUnit Second = new SecondUnit();
        /// <summary>安培（基本单位）/ Ampere (base unit).</summary>
        public static readonly PhysicalUnit Ampere = new AmpereUnit();
        /// <summary>开尔文（基本单位）/ Kelvin (base unit).</summary>
        public static readonly PhysicalUnit Kelvin = new KelvinUnit();
        /// <summary>摩尔（基本单位）/ Mole (base unit).</summary>
        public static readonly PhysicalUnit Mole = new MoleUnit();
        /// <summary>坎德拉（基本单位）/ Candela (base unit).</summary>
        public static readonly PhysicalUnit Candela = new CandelaUnit();
        /// <summary>比特（基本单位）/ Bit (base unit).</summary>
        public static readonly PhysicalUnit Bit = new BitUnit();
        /// <summary>弧度（辅助单位）/ Radian (supplementary).</summary>
        public static readonly PhysicalUnit Radian = new RadianUnit();
        /// <summary>球面度（辅助单位）/ Steradian (supplementary).</summary>
        public static readonly PhysicalUnit Steradian = new SteradianUnit();

        private sealed class MeterUnit : PhysicalUnit
        {
            public override string Name => "meter";
            public override string Symbol => "m";
            public override DerivedQuantity Quantity => Dimensions.Length;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class KilogramUnit : PhysicalUnit
        {
            public override string Name => "kilogram";
            public override string Symbol => "kg";
            public override DerivedQuantity Quantity => Dimensions.Mass;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class SecondUnit : PhysicalUnit
        {
            public override string Name => "second";
            public override string Symbol => "s";
            public override DerivedQuantity Quantity => Dimensions.Time;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class AmpereUnit : PhysicalUnit
        {
            public override string Name => "ampere";
            public override string Symbol => "A";
            public override DerivedQuantity Quantity => Dimensions.Current;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class KelvinUnit : PhysicalUnit
        {
            public override string Name => "kelvin";
            public override string Symbol => "K";
            public override DerivedQuantity Quantity => Dimensions.Temperature;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class MoleUnit : PhysicalUnit
        {
            public override string Name => "mole";
            public override string Symbol => "mol";
            public override DerivedQuantity Quantity => Dimensions.AmountOfSubstance;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class CandelaUnit : PhysicalUnit
        {
            public override string Name => "candela";
            public override string Symbol => "cd";
            public override DerivedQuantity Quantity => Dimensions.LuminousIntensity;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class BitUnit : PhysicalUnit
        {
            public override string Name => "bit";
            public override string Symbol => "bit";
            public override DerivedQuantity Quantity => Dimensions.Information;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class RadianUnit : PhysicalUnit
        {
            public override string Name => "radian";
            public override string Symbol => "rad";
            public override DerivedQuantity Quantity => Dimensions.PlaneAngle;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }

        private sealed class SteradianUnit : PhysicalUnit
        {
            public override string Name => "steradian";
            public override string Symbol => "sr";
            public override DerivedQuantity Quantity => Dimensions.SolidAngle;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
        }
    }
}
