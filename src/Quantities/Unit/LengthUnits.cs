#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 长度单位 / Length units.
/// </summary>
public static class LengthUnits {
    /// <summary>千米 / Kilometer.</summary>
    public static readonly PhysicalUnit Kilometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Kilo, "kilometer", "km");
    /// <summary>厘米 / Centimeter.</summary>
    public static readonly PhysicalUnit Centimeter = new DerivedUnit(SIBaseUnits.Meter, Scale.Centi, "centimeter", "cm");
    /// <summary>毫米 / Millimeter.</summary>
    public static readonly PhysicalUnit Millimeter = new DerivedUnit(SIBaseUnits.Meter, Scale.Milli, "millimeter", "mm");
    /// <summary>微米 / Micrometer.</summary>
    public static readonly PhysicalUnit Micrometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Micro, "micrometer", "um");
    /// <summary>纳米 / Nanometer.</summary>
    public static readonly PhysicalUnit Nanometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Nano, "nanometer", "nm");
    /// <summary>英寸 / Inch (1 in = 2.54 cm).</summary>
    public static readonly PhysicalUnit Inch = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.0254), "inch", "in");
    /// <summary>英尺 / Foot (1 ft = 0.3048 m).</summary>
    public static readonly PhysicalUnit Foot = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.3048), "foot", "ft");
    /// <summary>码 / Yard (1 yd = 0.9144 m).</summary>
    public static readonly PhysicalUnit Yard = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.9144), "yard", "yd");
    /// <summary>英里 / Mile (1 mi = 1609.344 m).</summary>
    public static readonly PhysicalUnit Mile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1609.344), "mile", "mi");
    /// <summary>海里 / Nautical mile (1 nmi = 1852 m).</summary>
    public static readonly PhysicalUnit NauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1852), "nautical mile", "nmi");

    private sealed class DerivedUnit : PhysicalUnit {
        private readonly PhysicalUnit _base;
        private readonly Scale _factor;
        private readonly string _name;
        private readonly string _symbol;

        public DerivedUnit(PhysicalUnit baseUnit, Scale factor, string name, string symbol) {
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
