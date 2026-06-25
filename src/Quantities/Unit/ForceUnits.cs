#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 力单位 / Force units.
/// </summary>
public static class ForceUnits {
    /// <summary>牛顿 / Newton (SI derived: kg*m/s^2).</summary>
    public static readonly PhysicalUnit Newton = new DerivedUnit(
        SIBaseUnits.Kilogram.Multiply(SIBaseUnits.Meter).Divide(SIBaseUnits.Second.Pow(2)),
        "newton", "N");
    /// <summary>千牛 / Kilonewton.</summary>
    public static readonly PhysicalUnit Kilonewton = new DerivedUnit(Newton.ScaleMultiply(Scale.Kilo), "kilonewton", "kN");
    /// <summary>兆牛 / Meganewton.</summary>
    public static readonly PhysicalUnit Meganewton = new DerivedUnit(Newton.ScaleMultiply(Scale.Mega), "meganewton", "MN");
    /// <summary>达因 / Dyne (1 dyn = 10^-5 N).</summary>
    public static readonly PhysicalUnit Dyne = new DerivedUnit(Newton.ScaleMultiply(Scale.Invoke(10, -5)), "dyne", "dyn");
    /// <summary>千克力 / Kilogram-force (1 kgf = 9.80665 N).</summary>
    public static readonly PhysicalUnit KilogramForce = new DerivedUnit(Newton.ScaleMultiply(Scale.Invoke(9.80665)), "kilogram-force", "kgf");
    /// <summary>磅力 / Pound-force (1 lbf = 4.4482216152605 N).</summary>
    public static readonly PhysicalUnit PoundForce = new DerivedUnit(Newton.ScaleMultiply(Scale.Invoke(4.4482216152605)), "pound-force", "lbf");

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
