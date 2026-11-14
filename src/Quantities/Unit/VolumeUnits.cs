#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 体积单位 / Volume units.
/// </summary>
public static class VolumeUnits {
    /// <summary>立方米 / Cubic meter.</summary>
    public static readonly PhysicalUnit CubicMeter = new DerivedUnit(
        SIBaseUnits.Meter.Pow(3), "cubic meter", "m3");
    /// <summary>升 / Liter (1 L = 0.001 m^3).</summary>
    public static readonly PhysicalUnit Liter = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Milli), "liter", "L");
    /// <summary>毫升 / Milliliter.</summary>
    public static readonly PhysicalUnit Milliliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Milli), "milliliter", "mL");
    /// <summary>立方厘米 / Cubic centimeter.</summary>
    public static readonly PhysicalUnit CubicCentimeter = new DerivedUnit(
        LengthUnits.Centimeter.Pow(3), "cubic centimeter", "cm3");
    /// <summary>加仑（美制）/ US gallon (1 gal = 3.785411784 L).</summary>
    public static readonly PhysicalUnit USGallon = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(3.785411784)), "US gallon", "gal");

    // 以下 16 个是 B1137 按基线 Volume.kt 补的。
    //
    // **英制与美制的液量单位同名而不同量**：
    // **UK 液量盎司 28.4130625 mL，US 液量盎司 29.5735295625 mL**；
    // **UK 加仑 4.54609 L，US 加仑 3.78541178 L**——**两者差 20%，而名字只差两个字母。**
    //
    // The sixteen below were added in B1137 from the baseline Volume.kt.
    //
    // **Imperial and US liquid measures share names and differ in size:**
    // **UK fluid ounce 28.4130625 mL, US fluid ounce 29.5735295625 mL**;
    // **UK gallon 4.54609 L, US gallon 3.78541178 L** — **twenty percent apart, with two letters of difference in the name.**

    /// <summary>立方毫米 / Cubic millimetre.</summary>
    public static readonly PhysicalUnit CubicMillimeter = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1e-9)), "cubic millimeter", "mm3");
    /// <summary>立方分米 / Cubic decimetre.</summary>
    public static readonly PhysicalUnit CubicDecimeter = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1e-3)), "cubic decimeter", "dm3");
    /// <summary>立方十米 / Cubic decametre.</summary>
    public static readonly PhysicalUnit CubicDecameter = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1e3)), "cubic decameter", "dam3");
    /// <summary>立方百米 / Cubic hectometre.</summary>
    public static readonly PhysicalUnit CubicHectometer = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1e6)), "cubic hectometer", "hm3");
    /// <summary>立方千米 / Cubic kilometre.</summary>
    public static readonly PhysicalUnit CubicKilometer = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1e9)), "cubic kilometer", "km3");

    /// <summary>微升 / Microlitre.</summary>
    public static readonly PhysicalUnit Microliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(1e-6)), "microliter", "uL");
    /// <summary>厘升 / Centilitre.</summary>
    public static readonly PhysicalUnit Centiliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(0.01)), "centiliter", "cL");
    /// <summary>分升 / Decilitre.</summary>
    public static readonly PhysicalUnit Deciliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(0.1)), "deciliter", "dL");
    /// <summary>百升 / Hectolitre.</summary>
    public static readonly PhysicalUnit Hectoliter = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(100)), "hectoliter", "hL");

    /// <summary>立方英寸 / Cubic inch.</summary>
    public static readonly PhysicalUnit CubicInch = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(1.6387064e-5)), "cubic inch", "in3");
    /// <summary>立方英尺 / Cubic foot.</summary>
    public static readonly PhysicalUnit CubicFoot = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(0.028316846592)), "cubic foot", "ft3");
    /// <summary>立方码 / Cubic yard.</summary>
    public static readonly PhysicalUnit CubicYard = new DerivedUnit(CubicMeter.ScaleMultiply(Scale.Invoke(0.764554857984)), "cubic yard", "yd3");

    /// <summary>**英制液量盎司 28.4130625 mL** / UK fluid ounce.</summary>
    public static readonly PhysicalUnit UKFluidOunce = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(0.0284130625)), "UK fluid ounce", "fl_oz_UK");
    /// <summary>**美制液量盎司 29.5735295625 mL** / US fluid ounce.</summary>
    public static readonly PhysicalUnit USFluidOunce = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(0.0295735295625)), "US fluid ounce", "fl_oz_US");
    /// <summary>**英制加仑 4.54609 L** / UK gallon.</summary>
    public static readonly PhysicalUnit UKGallon = new DerivedUnit(Liter.ScaleMultiply(Scale.Invoke(4.54609)), "UK gallon", "gal_UK");

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
