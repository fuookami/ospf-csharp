#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 压强单位 / Pressure units.
/// </summary>
public static class PressureUnits {
    /// <summary>帕斯卡 / Pascal (SI derived: N/m^2).</summary>
    public static readonly PhysicalUnit Pascal = new DerivedUnit(
        ForceUnits.Newton.Divide(SIBaseUnits.Meter.Pow(2)),
        "pascal", "Pa");
    /// <summary>千帕 / Kilopascal.</summary>
    public static readonly PhysicalUnit Kilopascal = new DerivedUnit(Pascal.ScaleMultiply(Scale.Kilo), "kilopascal", "kPa");
    /// <summary>兆帕 / Megapascal.</summary>
    public static readonly PhysicalUnit Megapascal = new DerivedUnit(Pascal.ScaleMultiply(Scale.Mega), "megapascal", "MPa");
    /// <summary>吉帕 / Gigapascal.</summary>
    public static readonly PhysicalUnit Gigapascal = new DerivedUnit(Pascal.ScaleMultiply(Scale.Giga), "gigapascal", "GPa");
    /// <summary>巴 / Bar (1 bar = 100000 Pa).</summary>
    public static readonly PhysicalUnit Bar = new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(100000)), "bar", "bar");
    /// <summary>标准大气压 / Atmosphere (1 atm = 101325 Pa).</summary>
    public static readonly PhysicalUnit Atmosphere = new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(101325)), "atmosphere", "atm");
    /// <summary>毫米汞柱 / Millimeter of mercury (1 mmHg = 133.322 Pa).</summary>
    public static readonly PhysicalUnit MillimeterOfMercury = new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(133.322)), "mmHg", "mmHg");

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
