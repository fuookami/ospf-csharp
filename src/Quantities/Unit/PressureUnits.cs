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
    // 以下 5 个按补齐：
    // Hectopascal / StandardAtmosphericPressure / MeterMercury / InchOfMercury / Millibar。
    //
    // **其中 MeterMercury 不能照做，因为公式与它自己的注释矛盾。**
    // 基线写的是 `StandardAtmosphericPressure * 0.76`，
    // **但 0.76 米汞柱恰好等于一个标准大气压——所以要从大气压"除"回 0.76 米，不是乘。**
    // 沿用会得到 77007 Pa，于是 mmHg = 77.007 Pa，**而基线自己在上方注释里写着 1 mmHg ≈ 133.322 Pa。**
    //
    // **这里按注释与物理事实实现（除法），并让 mmHg 由 mHg 派生**，
    // **使 "mmHg 是 mHg 的千分之一" 成为恒等式而不是巧合。**
    //
    // The five below were added from the code Pressure.kt:
    // Hectopascal / StandardAtmosphericPressure / MeterMercury / InchOfMercury / Millibar.
    //
    // **MeterMercury cannot be copied from the code, because its formula contradicts its own comment.**
    // The code says `StandardAtmosphericPressure * 0.76`,
    // **but 0.76 m of mercury is exactly one standard atmosphere — so the atmosphere has to be divided back down
    // to 0.76 m, not multiplied.**
    // Copied as written it yields 77007 Pa, so mmHg = 77.007 Pa, **while the comment just above says
    // 1 mmHg is about 133.322 Pa.**
    //
    // **Implemented per the comment and the physics (division), with mmHg derived from mHg**,
    // **so that mmHg being a thousandth of mHg is an identity rather than a coincidence.**

    /// <summary>百帕（10² Pa）/ Hectopascal.</summary>
    public static readonly PhysicalUnit Hectopascal =
        new DerivedUnit(Pascal.ScaleMultiply(Scale.Hecto), "hectopascal", "hPa");

    /// <summary>**标准大气压（101325 Pa）** / Standard atmospheric pressure.</summary>
    /// <remarks>
    /// **与 `Atmosphere` 数值相同而名字不同**：这个名字，本仓原来用 `Atmosphere`。
    /// **两个都保留，因为汞柱单位的推导要读起来像公式。**
    ///
    /// **Same value as `Atmosphere`, different name**: the code uses this one, this repo used `Atmosphere`.
    /// **Both stay, so the mercury derivations read like the code formula.**
    /// </remarks>
    public static readonly PhysicalUnit StandardAtmosphericPressure =
        new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(101325)), "standard atmosphere", "atm");

    /// <summary>**米汞柱（0.76 m Hg = 1 atm，故为 101325 / 0.76 ≈ 133322.37 Pa）** / Meter of mercury.</summary>
    /// <remarks>
    /// **这里是除法，不是乘法。** **0.76 米高的汞柱恰好压出一个标准大气压**，
    /// **所以一个"米汞柱"比一个大气压大 1/0.76 = 1.3158 倍，而不是小 0.76 倍。**
    ///
    /// **Division here, not multiplication.** **A column of mercury 0.76 m tall weighs out exactly one standard
    /// atmosphere**, **so a metre of mercury is 1/0.76 = 1.3158 atmospheres, not 0.76 of one.**
    /// </remarks>
    public static readonly PhysicalUnit MeterMercury =
        new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(101325.0 / 0.76)), "meter mercury", "mHg");

    /// <summary>**英寸汞柱（mHg / 39.37）** / Inch of mercury.</summary>
    /// <remarks>
    /// **除以"每米有多少英寸"（39.3700787），所以一英寸比一米小**——**与 mHg 的除法方向一致。**
    /// 气象报文里的 `inHg` 就是这个值，约 3386.39 Pa。
    ///
    /// **Divided by how many inches fit in a metre (39.3700787), so an inch is smaller than a metre** —
    /// **the same direction as mHg. Weather reports quote this as `inHg`, about 3386.39 Pa.**
    /// </remarks>
    public static readonly PhysicalUnit InchOfMercury =
        new DerivedUnit(
            Pascal.ScaleMultiply(Scale.Invoke(101325.0 / 0.76 / (1.0 / 0.0254))),
            "inch mercury",
            "inHg");

    /// <summary>**毫巴（Bar 的千分之一 = 100 Pa）** / Millibar.</summary>
    public static readonly PhysicalUnit Millibar =
        new DerivedUnit(Pascal.ScaleMultiply(Scale.Invoke(100)), "millibar", "mbar");

    /// <summary>**毫米汞柱（mHg 的千分之一）** / Millimeter of mercury.</summary>
    /// <remarks>
    /// **由 `MeterMercury` 派生而不是写死 133.322**：**写死的 133.322 是它的四舍五入，
    /// 于是"千分之一"成了巧合；派生之后这条关系是恒等式。**
    /// **此处改动，原值是字面量 133.322。**
    ///
    /// **Derived from `MeterMercury` rather than hard-coding 133.322**: **the literal was its rounding,
    /// which made a thousandth a coincidence; derived, the relation is an identity.**
    /// **Changed; the literal used to be 133.322.**
    /// </remarks>
    public static readonly PhysicalUnit MillimeterOfMercury =
        new DerivedUnit(MeterMercury.ScaleMultiply(Scale.Invoke(0.001)), "millimeter mercury", "mmHg");

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
