#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 速度单位 / Speed units.
/// </summary>
public static class SpeedUnits {
    /// <summary>米每秒 / Meters per second.</summary>
    public static readonly PhysicalUnit MetersPerSecond = new DerivedUnit(
        SIBaseUnits.Meter.Divide(SIBaseUnits.Second), "meters per second", "m/s");
    /// <summary>千米每小时 / Kilometers per hour.</summary>
    public static readonly PhysicalUnit KilometersPerHour = new DerivedUnit(
        LengthUnits.Kilometer.Divide(TimeUnits.Hour), "kilometers per hour", "km/h");
    /// <summary>英里每小时 / Miles per hour.</summary>
    public static readonly PhysicalUnit MilesPerHour = new DerivedUnit(
        LengthUnits.Mile.Divide(TimeUnits.Hour), "miles per hour", "mph");
    /// <summary>节 / Knot (1 kn = 1 nmi/h).</summary>
    public static readonly PhysicalUnit Knot = new DerivedUnit(
        LengthUnits.NauticalMile.Divide(TimeUnits.Hour), "knot", "kn");

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
