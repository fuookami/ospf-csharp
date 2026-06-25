#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 温度单位 / Temperature units.
    /// </summary>
    public static class TemperatureUnits
    {
        private static readonly FltX FahrenheitLinearScaleValue = new FltX(5) / new FltX(9);
        private static readonly Scale FahrenheitLinearScale = Scale.Invoke(new RtnX(5, 9));
        private static readonly FltX FahrenheitAffineOffset = new FltX(273.15) - new FltX(32) * FahrenheitLinearScaleValue;

        /// <summary>开尔文 / Kelvin (SI base unit).</summary>
        public static readonly PhysicalUnit Kelvin = SIBaseUnits.Kelvin;

        /// <summary>摄氏度 / Celsius (affine: standard = value * 1 + 273.15).</summary>
        public static readonly PhysicalUnit Celsius = new CelsiusUnit();

        /// <summary>华氏度 / Fahrenheit (affine: standard = value * 5/9 + 255.3722...).</summary>
        public static readonly PhysicalUnit Fahrenheit = new FahrenheitUnit();

        /// <summary>兰氏度 / Rankine (linear: standard = value * 5/9).</summary>
        public static readonly PhysicalUnit Rankine = new RankineUnit();

        private sealed class CelsiusUnit : PhysicalUnit
        {
            public override string Name => "celsius";
            public override string Symbol => "C";
            public override DerivedQuantity Quantity => Dimensions.Temperature;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Affine(Scale.Invoke(1), new FltX(273.15));
        }

        private sealed class FahrenheitUnit : PhysicalUnit
        {
            public override string Name => "fahrenheit";
            public override string Symbol => "F";
            public override DerivedQuantity Quantity => Dimensions.Temperature;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Affine(FahrenheitLinearScale, FahrenheitAffineOffset);
        }

        private sealed class RankineUnit : PhysicalUnit
        {
            public override string Name => "rankine";
            public override string Symbol => "R";
            public override DerivedQuantity Quantity => Dimensions.Temperature;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(FahrenheitLinearScale);
        }
    }
}
