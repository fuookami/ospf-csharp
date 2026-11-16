#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 能量单位 / Energy units.
/// </summary>
public static class EnergyUnits {
    /// <summary>焦耳 / Joule (SI derived: N*m).</summary>
    public static readonly PhysicalUnit Joule = new DerivedUnit(
        ForceUnits.Newton.Multiply(SIBaseUnits.Meter),
        "joule", "J");
    /// <summary>千焦 / Kilojoule.</summary>
    public static readonly PhysicalUnit Kilojoule = new DerivedUnit(Joule.ScaleMultiply(Scale.Kilo), "kilojoule", "kJ");
    /// <summary>兆焦 / Megajoule.</summary>
    public static readonly PhysicalUnit Megajoule = new DerivedUnit(Joule.ScaleMultiply(Scale.Mega), "megajoule", "MJ");
    /// <summary>卡路里 / Calorie (1 cal = 4.184 J).</summary>
    public static readonly PhysicalUnit Calorie = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(4.184)), "calorie", "cal");
    /// <summary>千卡 / Kilocalorie.</summary>
    public static readonly PhysicalUnit Kilocalorie = new DerivedUnit(Calorie.ScaleMultiply(Scale.Kilo), "kilocalorie", "kcal");
    /// <summary>千瓦时 / Kilowatt-hour (1 kWh = 3.6e6 J).</summary>
    public static readonly PhysicalUnit KilowattHour = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(3600000)), "kilowatt-hour", "kWh");
    /// <summary>电子伏 / Electronvolt (1 eV = 1.602176634e-19 J).</summary>
    public static readonly PhysicalUnit Electronvolt = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(1.602176634e-19)), "electronvolt", "eV");

    // 以下 10 个按基线 `Energy.kt` 补齐。
    //
    // **这一组全是"焦耳的固定倍数"，所以风险不在公式，在三处语义：**
    // **① `WattHour` 是 `KilowattHour` 的千分之一**（`KilowattHour / Scale.kilo`），不是反过来；
    // **② `GigawattHour` 用的是 `mega` 而不是 `giga`**——
    // **因为它是 `KilowattHour × Scale.mega`，也就是 10³ Wh × 10⁶ = 10⁹ Wh，名字对但那个 Scale 看着像错的；**
    // **③ `Erg` 是 1e-7 J，比所有其他单位小得多，数量级最容易写错。**
    //
    // The ten below were added from the baseline Energy.kt.
    //
    // **All are fixed multiples of the joule, so the risk is not the formula but three semantic points:**
    // **① `WattHour` is a thousandth of `KilowattHour`**, not the other way round;
    // **② `GigawattHour` uses `mega` rather than `giga`** —
    // **because it is `KilowattHour × Scale.mega`, i.e. 10³ Wh × 10⁶ = 10⁹ Wh: the name is right while that Scale looks wrong; **
    // **③ `Erg` is 1e-7 J, far smaller than everything else, and the magnitude is easiest to mistype.**

    /// <summary>吉焦（10⁹）/ Gigajoule.</summary>
    public static readonly PhysicalUnit Gigajoule = new DerivedUnit(Joule.ScaleMultiply(Scale.Giga), "gigajoule", "GJ");
    /// <summary>**尔格（1e-7 J）** / Erg.</summary>
    public static readonly PhysicalUnit Erg = new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(1e-7)), "erg", "erg");
    /// <summary>**英热单位（1055.06 J）** / British thermal unit.</summary>
    /// <remarks>
    /// **1055.06 是 IT 卡路里制下的 BTU**；**热化学制是 1054.35，而两者都叫 BTU。**
    /// **差 0.07%——在能源账单里可以忽略，在热力学计算里不能。**
    ///
    /// **1055.06 is the BTU on the IT calorie definition**; **the thermochemical one is 1054.35, and both are called BTU.**
    /// **0.07% apart — ignorable in a utility bill, not in a thermodynamic calculation.**
    /// </remarks>
    public static readonly PhysicalUnit BritishThermalUnit =
        new DerivedUnit(Joule.ScaleMultiply(Scale.Invoke(1055.06)), "british thermal unit", "BTU");

    /// <summary>**瓦时（千分之一千瓦时）** / Watt-hour, a thousandth of a kilowatt-hour.</summary>
    /// <remarks>
    /// **方向是从 `KilowattHour` 除下来**，而不是从 `Joule` 乘上去——**两种写法数值相同，
    /// 但 `Joule × 3600` 少一个零就会变成"瓦秒"，而瓦秒在数值上恰好等于焦耳，于是判据会全绿。**
    ///
    /// **The direction is down from `KilowattHour`**, not up from `Joule` — **both give the same number,**
    /// **but dropping a zero from `Joule × 3600` yields a watt-second, which equals a joule numerically, so every
    /// criterion stays green.**
    /// </remarks>
    public static readonly PhysicalUnit WattHour =
        new DerivedUnit(KilowattHour.ScaleMultiply(Scale.Invoke(0.001)), "watt-hour", "Wh");
    /// <summary>兆瓦时（10⁶ Wh）/ Megawatt-hour.</summary>
    public static readonly PhysicalUnit MegawattHour =
        new DerivedUnit(KilowattHour.ScaleMultiply(Scale.Kilo), "megawatt-hour", "MWh");
    /// <summary>**吉瓦时（10⁹ Wh）** / Gigawatt-hour.</summary>
    /// <remarks>
    /// **它用的是 `Scale.mega`，而名字是 giga**——**因为基准是 `KilowattHour`（10³ Wh），10³ × 10⁶ = 10⁹。**
    /// **改成 `Scale.giga` 会得到 10¹² Wh，与名字差三个数量级，而单位本身仍然"看起来正常"。**
    ///
    /// **It uses `Scale.mega` while the name says giga** — **the base is `KilowattHour` (10³ Wh), and 10³ × 10⁶ = 10⁹.**
    /// **Switching to `Scale.giga` yields 10¹² Wh, three orders off the name, while the unit still "looks fine".**
    /// </remarks>
    public static readonly PhysicalUnit GigawattHour =
        new DerivedUnit(KilowattHour.ScaleMultiply(Scale.Mega), "gigawatt-hour", "GWh");

    /// <summary>千电子伏 / Kiloelectronvolt.</summary>
    public static readonly PhysicalUnit Kiloelectronvolt =
        new DerivedUnit(Electronvolt.ScaleMultiply(Scale.Kilo), "kiloelectronvolt", "keV");
    /// <summary>兆电子伏 / Megaelectronvolt.</summary>
    public static readonly PhysicalUnit Megaelectronvolt =
        new DerivedUnit(Electronvolt.ScaleMultiply(Scale.Mega), "megaelectronvolt", "MeV");
    /// <summary>吉电子伏 / Gigaelectronvolt.</summary>
    public static readonly PhysicalUnit Gigaelectronvolt =
        new DerivedUnit(Electronvolt.ScaleMultiply(Scale.Giga), "gigaelectronvolt", "GeV");

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
