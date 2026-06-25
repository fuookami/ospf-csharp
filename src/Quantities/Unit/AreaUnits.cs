#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 面积单位 / Area units.
/// </summary>
public static class AreaUnits {
    /// <summary>平方米 / Square meter.</summary>
    public static readonly PhysicalUnit SquareMeter = new DerivedUnit(
        SIBaseUnits.Meter.Pow(2), "square meter", "m2");
    /// <summary>平方千米 / Square kilometer.</summary>
    public static readonly PhysicalUnit SquareKilometer = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(1e6)), "square kilometer", "km2");
    /// <summary>平方厘米 / Square centimeter.</summary>
    public static readonly PhysicalUnit SquareCentimeter = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(1e-4)), "square centimeter", "cm2");
    /// <summary>公顷 / Hectare (1 ha = 10000 m^2).</summary>
    public static readonly PhysicalUnit Hectare = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(10000)), "hectare", "ha");
    /// <summary>英亩 / Acre (1 ac = 4046.8564224 m^2).</summary>
    public static readonly PhysicalUnit Acre = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(4046.8564224)), "acre", "ac");

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
