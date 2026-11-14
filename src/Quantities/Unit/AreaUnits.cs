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
    // **以下四个是 B1129 补的**：基线的 `Area.kt` 有 `SquareInch`/`SquareFoot`/`SquareYard`/`SquareMile`，
    // **而 `Stress` 量纲（psi/psf）正需要前两个**——**不补它们，英制压力单位就建不出来。**
    //
    // **The four below were added in B1129**: the baseline's `Area.kt` has `SquareInch`/`SquareFoot`/`SquareYard`/`SquareMile`,
    // **and the `Stress` dimension (psi/psf) needs the first two** — **without them the imperial pressure units cannot exist.**
    /// <summary>平方英寸 / Square inch.</summary>
    public static readonly PhysicalUnit SquareInch = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(0.00064516)), "square inch", "in2");
    /// <summary>平方英尺 / Square foot.</summary>
    public static readonly PhysicalUnit SquareFoot = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(0.09290304)), "square foot", "ft2");
    /// <summary>平方码 / Square yard.</summary>
    public static readonly PhysicalUnit SquareYard = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(0.83612736)), "square yard", "yd2");
    /// <summary>平方英里 / Square mile.</summary>
    public static readonly PhysicalUnit SquareMile = new DerivedUnit(SquareMeter.ScaleMultiply(Scale.Invoke(2589988.110336)), "square mile", "mi2");

    // 以下 4 个是 B1144 按基线 Area.kt 补的。
    //
    // **`SquareChain` 与 `SquareRod` 这两个名字里的 "square" 修饰的是长度单位，不是面积单位**：
    // **一个 `SquareRod` 是 1 rod × 1 rod = 16.5 ft 见方 ≈ 25.29 m²。**
    // **写成 "rod 的平方" 与写成 "边长为 1 rod 的正方形" 数值相同，但前者容易让人去找一个叫 rod² 的单位。**
    //
    // The four below were added in B1144 from Area.kt.
    //
    // **The "square" in `SquareChain` and `SquareRod` modifies a length unit, not an area unit**:
    // **a square rod is 1 rod by 1 rod, i.e. 16.5 ft on a side, about 25.29 m2.**
    // **"the square of a rod" and "a square one rod on a side" agree numerically, but the first phrasing sends
    // people looking for a unit called rod-squared.**

    /// <summary>**平方毫米（10⁻⁶ m²）** / Square millimetre.</summary>
    public static readonly PhysicalUnit SquareMillimeter = new DerivedUnit(
        SquareMeter.ScaleMultiply(Scale.Invoke(1e-6)), "square millimeter", "mm2");
    /// <summary>**平方分米（10⁻² m²）** / Square decimetre.</summary>
    public static readonly PhysicalUnit SquareDecimeter = new DerivedUnit(
        SquareMeter.ScaleMultiply(Scale.Invoke(0.01)), "square decimeter", "dm2");
    /// <summary>**平方杆（1 rod 见方 ≈ 25.2929 m²）** / Square rod.</summary>
    public static readonly PhysicalUnit SquareRod = new DerivedUnit(
        SquareMeter.ScaleMultiply(Scale.Invoke(25.29285264)), "square rod", "rd2");
    /// <summary>**平方链（1 chain 见方 = 404.6856 m²）** / Square chain.</summary>
    /// <remarks>
    /// **1 chain = 66 ft = 20.1168 m，所以平方链 = 404.68564 m²，恰好是十分之一英亩。**
    /// **这条关系是英国土地测量的基础（10 平方链 = 1 英亩）。**
    ///
    /// **A chain is 66 ft = 20.1168 m, so a square chain is 404.68564 m2, exactly a tenth of an acre.**
    /// **That relation underpins British land survey: ten square chains make an acre.**
    /// </remarks>
    public static readonly PhysicalUnit SquareChain = new DerivedUnit(
        SquareMeter.ScaleMultiply(Scale.Invoke(404.68564224)), "square chain", "ch2");

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
