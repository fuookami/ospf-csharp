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

    // 以下 2 个按补齐。
    //
    // The two below were added from Force.kt.

    /// <summary>**克力（千克力的千分之一 = 0.00980665 N）** / Gram-force.</summary>
    public static readonly PhysicalUnit GramForce = new DerivedUnit(
        Newton.ScaleMultiply(Scale.Invoke(0.00980665)), "gram-force", "gf");
    /// <summary>**千磅力（磅力的一千倍 = 4448.2216 N）** / Kilopound-force.</summary>
    /// <remarks>
    /// **名字里的 kilo 是"一千磅力"，不是"千磅"的质量**——**它在工程里常写作 `kip`。**
    ///
    /// **The kilo means "a thousand pound-forces", not a thousand pounds of mass** — **engineers write it as `kip`.**
    /// </remarks>
    public static readonly PhysicalUnit KilopoundForce = new DerivedUnit(
        Newton.ScaleMultiply(Scale.Invoke(4448.2216152605)), "kilopound-force", "kip");

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
