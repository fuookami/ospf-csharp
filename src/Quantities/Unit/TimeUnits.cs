#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 时间单位 / Time units.
/// </summary>
public static class TimeUnits {
    /// <summary>分钟 / Minute (60 s).</summary>
    public static readonly PhysicalUnit Minute = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(60), "minute", "min");
    /// <summary>小时 / Hour (3600 s).</summary>
    public static readonly PhysicalUnit Hour = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(3600), "hour", "h");
    /// <summary>天 / Day (86400 s).</summary>
    public static readonly PhysicalUnit Day = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(86400), "day", "d");

    // 以下两个按补齐。
    //
    // Week 与 Year 都不是"多少个基本单位"那么简单：
    // **Year 用的是儒略年 365.25 天**（与 LightYear 同源），不是 365，也不是 365.2425。
    //
    // The two below were added from the code Time.kt.
    //
    // Neither Week nor Year is a plain multiple of a base unit:
    // **Year uses the Julian year of 365.25 days** (the same source as LightYear), not 365 and not 365.2425.

    /// <summary>周 / Week.</summary>
    public static readonly PhysicalUnit Week = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(604800), "week", "wk");

    /// <summary>**儒略年 365.25 天** / Julian year of 365.25 days.</summary>
    /// <remarks>
    /// **365.25 是儒略年的定义**，天文与轨道计算用它；**民用历法的一年是 365 或 366**，
    /// **而 365.2425 是格里历的平均年长——三者都叫"年"。**
    /// **用错一个，一光年就会差 6.5 亿公里（见 LightYear 的说明）。**
    ///
    /// **365.25 is the Julian year**, used in astronomy and orbital work; **a civil year is 365 or 366**,
    /// **and 365.2425 is the Gregorian mean year — all three are called "a year".**
    /// **Pick the wrong one and a light year is off by 650 million kilometres (see the LightYear remarks).**
    /// </remarks>
    public static readonly PhysicalUnit Year = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(31557600), "year", "a");
    /// <summary>毫秒 / Millisecond.</summary>
    public static readonly PhysicalUnit Millisecond = new DerivedUnit(SIBaseUnits.Second, Scale.Milli, "millisecond", "ms");
    /// <summary>微秒 / Microsecond.</summary>
    public static readonly PhysicalUnit Microsecond = new DerivedUnit(SIBaseUnits.Second, Scale.Micro, "microsecond", "us");
    /// <summary>纳秒 / Nanosecond.</summary>
    public static readonly PhysicalUnit Nanosecond = new DerivedUnit(SIBaseUnits.Second, Scale.Nano, "nanosecond", "ns");

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
