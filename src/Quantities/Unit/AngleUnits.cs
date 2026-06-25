#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 角度单位 / Angle units.
    /// </summary>
    public static class AngleUnits
    {
        /// <summary>弧度 / Radian (SI).</summary>
        public static readonly PhysicalUnit Radian = SIBaseUnits.Radian;
        /// <summary>度 / Degree (1 deg = pi/180 rad).</summary>
        public static readonly PhysicalUnit Degree = new DerivedUnit(
            Radian.ScaleMultiply(Scale.Invoke(0.017453292519943295)), "degree", "deg");
        /// <summary>角分 / Arcminute.</summary>
        public static readonly PhysicalUnit Arcminute = new DerivedUnit(Degree.ScaleMultiply(Scale.Invoke(1.0 / 60.0)), "arcminute", "'");
        /// <summary>角秒 / Arcsecond.</summary>
        public static readonly PhysicalUnit Arcsecond = new DerivedUnit(Degree.ScaleMultiply(Scale.Invoke(1.0 / 3600.0)), "arcsecond", "\"");
        /// <summary>球面度 / Steradian.</summary>
        public static readonly PhysicalUnit Steradian = SIBaseUnits.Steradian;

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
