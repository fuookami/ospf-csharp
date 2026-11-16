#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 质量单位 / Mass units.
/// </summary>
public static class MassUnits {
    /// <summary>克 / Gram (1 g = 0.001 kg).</summary>
    public static readonly PhysicalUnit Gram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Milli, "gram", "g");
    /// <summary>毫克 / Milligram.</summary>
    public static readonly PhysicalUnit Milligram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Micro, "milligram", "mg");
    /// <summary>吨 / Ton (1 t = 1000 kg).</summary>
    public static readonly PhysicalUnit Ton = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Kilo, "ton", "t");
    /// <summary>磅 / Pound (1 lb = 0.45359237 kg).</summary>
    public static readonly PhysicalUnit Pound = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.45359237), "pound", "lb");
    /// <summary>盎司 / Ounce (1 oz = 0.028349523125 kg).</summary>
    public static readonly PhysicalUnit Ounce = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.028349523125), "ounce", "oz");

    // 以下 12 个按补齐。
    //
    // **这一组里有三套"吨"与两套"盎司"，它们各自不同：**
    // **长吨 2240 磅、短吨 2000 磅、公吨 1000 千克**；
    // **常衡盎司 1/16 磅、金衡盎司 1/12 磅**——**金衡盎司更重，而名字只差一个词。**
    //
 // The twelve below were added Mass.kt.
    //
    // **This group carries three tons and two ounces, all different:**
    // **long ton 2240 lb, short ton 2000 lb, metric ton 1000 kg**;
    // **avoirdupois ounce 1/16 lb, troy ounce 1/12 lb** — **the troy ounce is heavier, and the names differ by one word.**

    /// <summary>微克 / Microgram.</summary>
    public static readonly PhysicalUnit Microgram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(1e-9), "microgram", "ug");
    /// <summary>百克 / Hectogram.</summary>
    public static readonly PhysicalUnit Hectogram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.1), "hectogram", "hg");
    /// <summary>公担（100 千克）/ Kintal.</summary>
    public static readonly PhysicalUnit Kintal = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(100), "kintal", "q");

    /// <summary>**磅点（珠宝计量，1/100 克拉）** / Point, one hundredth of a carat.</summary>
    public static readonly PhysicalUnit Point = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(2e-6), "point", "pt");
    /// <summary>**克拉（0.2 克）** / Carat.</summary>
    public static readonly PhysicalUnit Carat = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(2e-4), "carat", "ct");
    /// <summary>**格令（1/7000 磅）** / Grain.</summary>
    public static readonly PhysicalUnit Gran = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(6.479891e-5), "grain", "gr");

    /// <summary>**长吨（2240 磅）** / Long ton.</summary>
    public static readonly PhysicalUnit LongTon = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(1016.0469088), "long ton", "ton_UK");
    /// <summary>**短吨（2000 磅）** / Short ton.</summary>
    public static readonly PhysicalUnit ShortTon = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(907.18474), "short ton", "ton_US");
    /// <summary>**英石（14 磅）** / Stone.</summary>
    public static readonly PhysicalUnit Stone = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(6.35029318), "stone", "st");
    /// <summary>**金衡盎司（1/12 磅）** / Troy ounce.</summary>
    public static readonly PhysicalUnit TroyOunce = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.0311034768), "troy ounce", "oz_t");
    /// <summary>**打兰（1/16 盎司）** / Dram.</summary>
    public static readonly PhysicalUnit Dram = new DerivedUnit(SIBaseUnits.Kilogram, Scale.Invoke(0.0017718451953125), "dram", "dr");

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
