#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 电阻单位 / Resistance units.
/// </summary>
public static class ResistanceUnits {
    /// <summary>欧姆 / Ohm (SI derived: V/A).</summary>
    public static readonly PhysicalUnit Ohm = new DerivedUnit(
        VoltageUnits.Volt.Divide(SIBaseUnits.Ampere), "ohm", "ohm");
    /// <summary>千欧 / Kilohm.</summary>
    public static readonly PhysicalUnit Kilohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Kilo), "kilohm", "kohm");
    /// <summary>兆欧 / Megohm.</summary>
    public static readonly PhysicalUnit Megohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Mega), "megohm", "Mohm");

    // 以下两个是命名别名。
    //
    // **本仓原来叫 `Kilohm`/`Megohm`，现在也提供 `Kiloohm`/`Megaohm`——两者都有人写。**
    // **同一单位两个名字不是冗余，因为按名字查找的调用方只认其中一个。**
    // **它们必须指向同一个值：`SameAs` 判据会钉住这一点。**
    //
    // The two below are naming aliases.
    //
    // **This repo used `Kilohm`/`Megohm` and now also provides `Kiloohm`/`Megaohm`; both spellings are current.**
    // **Two names for one unit is not redundancy, because a caller looking up by name knows only one of them.**
    // **They have to point at the same value, and a SameAs criterion pins that.**

    /// <summary>**千欧（另一种拼写）** / Kiloohm, the alternate spelling.</summary>
    public static readonly PhysicalUnit Kiloohm = Kilohm;
    /// <summary>**兆欧（另一种拼写）** / Megaohm, the alternate spelling.</summary>
    public static readonly PhysicalUnit Megaohm = Megohm;
    /// <summary>毫欧 / Milliohm.</summary>
    public static readonly PhysicalUnit Milliohm = new DerivedUnit(Ohm.ScaleMultiply(Scale.Milli), "milliohm", "mohm");

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
