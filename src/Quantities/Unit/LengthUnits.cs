#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 长度单位 / Length units.
/// </summary>
public static class LengthUnits {
    /// <summary>千米 / Kilometer.</summary>
    public static readonly PhysicalUnit Kilometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Kilo, "kilometer", "km");
    /// <summary>厘米 / Centimeter.</summary>
    public static readonly PhysicalUnit Centimeter = new DerivedUnit(SIBaseUnits.Meter, Scale.Centi, "centimeter", "cm");
    /// <summary>毫米 / Millimeter.</summary>
    public static readonly PhysicalUnit Millimeter = new DerivedUnit(SIBaseUnits.Meter, Scale.Milli, "millimeter", "mm");
    /// <summary>微米 / Micrometer.</summary>
    public static readonly PhysicalUnit Micrometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Micro, "micrometer", "um");
    /// <summary>纳米 / Nanometer.</summary>
    public static readonly PhysicalUnit Nanometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Nano, "nanometer", "nm");
    /// <summary>英寸 / Inch (1 in = 2.54 cm).</summary>
    public static readonly PhysicalUnit Inch = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.0254), "inch", "in");
    /// <summary>英尺 / Foot (1 ft = 0.3048 m).</summary>
    public static readonly PhysicalUnit Foot = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.3048), "foot", "ft");
    /// <summary>码 / Yard (1 yd = 0.9144 m).</summary>
    public static readonly PhysicalUnit Yard = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(0.9144), "yard", "yd");
    /// <summary>英里 / Mile (1 mi = 1609.344 m).</summary>
    public static readonly PhysicalUnit Mile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1609.344), "mile", "mi");
    /// <summary>海里 / Nautical mile (1 nmi = 1852 m).</summary>
    public static readonly PhysicalUnit NauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1852), "nautical mile", "nmi");

    // **以下 22 个单位按补齐。**
    //
    // **33 个长度单位，本仓原来只有 10 个**，而 `docs/` 里一次都没提过这件事——
    // **"少 23 个"与"够用了"在文档上长得一样。**
    //
    // **换算因子逐条抄自的 `DerivedPhysicalUnit(... * Scale...)` 表达式**，
    // **一个数都没有自己算**：单位换算错了不会报错，只会让"5 海里"变成别的长度。
    //
 // **The 22 units below were added.**
    //
 // **This implementation has 33 length units and this repo had 10**, and `docs/` never mentioned it —
    // **"23 short" and "good enough" look the same in a document.**
    //
    // **Every factor is copied from its `DerivedPhysicalUnit(... * Scale...)` expressions**,
    // **with no number recomputed here**: a wrong conversion does not throw, it just makes "5 nautical miles" a different length.

    /// <summary>皮米 / Picometer.</summary>
    public static readonly PhysicalUnit Picometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Pico, "picometer", "pm");
    /// <summary>分米 / Decimeter.</summary>
    public static readonly PhysicalUnit Decimeter = new DerivedUnit(SIBaseUnits.Meter, Scale.Deci, "decimeter", "dm");
    /// <summary>十米 / Decameter.</summary>
    public static readonly PhysicalUnit Decameter = new DerivedUnit(SIBaseUnits.Meter, Scale.Deca, "decameter", "dam");
    /// <summary>百米 / Hectometer.</summary>
    public static readonly PhysicalUnit Hectometer = new DerivedUnit(SIBaseUnits.Meter, Scale.Hecto, "hectometer", "hm");

    /// <summary>**四个地区海里，各自不同** / Four regional nautical miles, each different.</summary>
    /// <remarks>
    /// **这四个值不同是刻意的**：FR 1853.27、UK 1854.55、RU 1855.78、US 1851.01 米。
    /// **把任何一个写成 1852，都会让该地区的海图距离算错**——
    /// **而错误不会显现为异常，只会显现为"距离差一点"。**
    ///
    /// **These four differ on purpose**: FR 1853.27, UK 1854.55, RU 1855.78, US 1851.01 metres.
    /// **Collapsing any of them to 1852 makes that region's chart distance wrong** —
    /// **and the error shows up not as an exception but as "the distance is slightly off".**
    /// </remarks>
    public static readonly PhysicalUnit FRNauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1853.27), "French nautical mile", "nmi_FR");
    /// <inheritdoc cref="FRNauticalMile" />
    public static readonly PhysicalUnit UKNauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1854.55), "UK nautical mile", "nmi_UK");
    /// <inheritdoc cref="FRNauticalMile" />
    public static readonly PhysicalUnit RUNauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1855.78), "Russian nautical mile", "nmi_RU");
    /// <inheritdoc cref="FRNauticalMile" />
    public static readonly PhysicalUnit USNauticalMile = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1851.01), "US nautical mile", "nmi_US");

    /// <summary>英寻 / Fathom.</summary>
    public static readonly PhysicalUnit Fathom = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(1.8288), "fathom", "ftm");
    /// <summary>链（十分之一海里）/ Cable.</summary>
    public static readonly PhysicalUnit Cable = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(185.2), "cable", "cb");
    /// <summary>测链 / Chain.</summary>
    public static readonly PhysicalUnit Chain = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(20.1168), "chain", "ch");
    /// <summary>杆 / Rod.</summary>
    public static readonly PhysicalUnit Rod = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(5.0292), "rod", "rd");

    /// <summary>天文单位 / Astronomical unit.</summary>
    public static readonly PhysicalUnit AstronomicalUnit = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(149597870700.0), "astronomical unit", "au");
    /// <summary>光秒 / Light second.</summary>
    public static readonly PhysicalUnit LightSecond = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(299792458.0), "light second", "ls");
    /// <summary>光分 / Light minute.</summary>
    public static readonly PhysicalUnit LightMinute = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(299792458.0 * 60), "light minute", "lm");
    /// <summary>光时 / Light hour.</summary>
    public static readonly PhysicalUnit LightHour = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(299792458.0 * 3600), "light hour", "lh");
    /// <summary>光日 / Light day.</summary>
    public static readonly PhysicalUnit LightDay = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(299792458.0 * 86400), "light day", "ld");
    /// <summary>光年 / Light year.</summary>
    /// <remarks>
    /// **的是儒略年 365.25 天**（`LightDay * 365.25`），**不是 365 天也不是 365.2425**。
    /// **三者差 0.0069%**——**在光年的量级上就是 6.5 亿公里。**
    ///
 /// **This implementation uses the Julian year of 365.25 days** (`LightDay * 365.25`), **not 365 and not 365.2425**.
    /// **The three differ by 0.0069%** — **which at a light year is about 650 million kilometres.**
    /// </remarks>
    public static readonly PhysicalUnit LightYear = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(299792458.0 * 86400 * 365.25), "light year", "ly");
    /// <summary>秒差距 / Parsec.</summary>
    public static readonly PhysicalUnit Parsec = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(30856775814913673.0), "parsec", "pc");
    /// <summary>千秒差距 / Kiloparsec.</summary>
    public static readonly PhysicalUnit Kiloparsec = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(30856775814913673.0 * 1000), "kiloparsec", "kpc");
    /// <summary>兆秒差距 / Megaparsec.</summary>
    public static readonly PhysicalUnit Megaparsec = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(30856775814913673.0 * 1e6), "megaparsec", "Mpc");
    /// <summary>吉秒差距 / Gigaparsec.</summary>
    public static readonly PhysicalUnit Gigaparsec = new DerivedUnit(SIBaseUnits.Meter, Scale.Invoke(30856775814913673.0 * 1e9), "gigaparsec", "Gpc");


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
