#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 速度单位 / Speed units.
/// </summary>
public static class SpeedUnits {
    /// <summary>米每秒 / Meters per second.</summary>
    public static readonly PhysicalUnit MetersPerSecond = new DerivedUnit(
        SIBaseUnits.Meter.Divide(SIBaseUnits.Second), "meters per second", "m/s");
    /// <summary>千米每小时 / Kilometers per hour.</summary>
    public static readonly PhysicalUnit KilometersPerHour = new DerivedUnit(
        LengthUnits.Kilometer.Divide(TimeUnits.Hour), "kilometers per hour", "km/h");
    /// <summary>英里每小时 / Miles per hour.</summary>
    public static readonly PhysicalUnit MilesPerHour = new DerivedUnit(
        LengthUnits.Mile.Divide(TimeUnits.Hour), "miles per hour", "mph");
    /// <summary>节 / Knot (1 kn = 1 nmi/h).</summary>
    public static readonly PhysicalUnit Knot = new DerivedUnit(
        LengthUnits.NauticalMile.Divide(TimeUnits.Hour), "knot", "kn");

    // **以下 10 个速度单位按基线 `Velocity.kt` 补齐。**
    //
    // **基线有 14 个速度单位，本仓原来只有 4 个**，而 `docs/` 里一次都没提过。
    // **这四个地区节与四个地区海里是配套的**：海里不同，节也不同——
    // **只补海里不补节，等于让"用 FR 海里算出的距离"无法换成"FR 节表示的速度"。**
    //
    // **The 10 speed units below were added from the baseline `Velocity.kt`.**
    //
    // **The baseline has 14 speed units and this repo had 4**, with `docs/` silent about it.
    // **The four regional knots pair with the four regional nautical miles**: different miles mean different knots —
    // **adding the miles without the knots leaves a distance measured in FR miles unable to become a speed in FR knots.**

    /// <summary>厘米每秒 / Centimeters per second.</summary>
    public static readonly PhysicalUnit CentimetersPerSecond = new DerivedUnit(
        LengthUnits.Centimeter.Divide(SIBaseUnits.Second), "centimeters per second", "cm/s");
    /// <summary>千米每秒 / Kilometers per second.</summary>
    public static readonly PhysicalUnit KilometersPerSecond = new DerivedUnit(
        LengthUnits.Kilometer.Divide(SIBaseUnits.Second), "kilometers per second", "km/s");
    /// <summary>英寸每秒 / Inches per second.</summary>
    public static readonly PhysicalUnit InchesPerSecond = new DerivedUnit(
        LengthUnits.Inch.Divide(SIBaseUnits.Second), "inches per second", "in/s");
    /// <summary>英尺每秒 / Feet per second.</summary>
    public static readonly PhysicalUnit FeetPerSecond = new DerivedUnit(
        LengthUnits.Foot.Divide(SIBaseUnits.Second), "feet per second", "ft/s");

    /// <summary>**马赫，按 340.3 m/s 折算** / Mach, at 340.3 m/s.</summary>
    /// <remarks>
    /// **基线用的是 340.3，不是 343**（343 是 20 °C 干空气的声速）。
    /// **两者差 0.8%**——**在 0.8 马赫上就是 2 米每秒。**
    ///
    /// **The baseline uses 340.3, not 343** (343 is the speed of sound in dry air at 20 °C).
    /// **They differ by 0.8%** — **which at Mach 0.8 is 2 metres per second.**
    /// </remarks>
    public static readonly PhysicalUnit Mach = new DerivedUnit(
        SIBaseUnits.Meter.Divide(SIBaseUnits.Second).ScaleMultiply(Scale.Invoke(340.3)), "mach", "Ma");

    /// <summary>**四个地区节，与四个地区海里配套** / Four regional knots, paired with the four regional miles.</summary>
    /// <remarks>
    /// **节 = 海里每小时**，所以海里不同节就不同。**把它们都写成国际节（1852 m/h），
    /// 会让"用 UK 海里算的航程"与"用 UK 节算的速度"对不上**——
    /// **而两者都是这台机器算出来的，所以没人会怀疑。**
    ///
    /// **A knot is one nautical mile per hour**, so different miles mean different knots. **Collapsing them all to the
    /// international knot (1852 m/h) makes a voyage measured in UK miles disagree with a speed measured in UK knots** —
    /// **and both come out of this same machine, so nobody suspects either.**
    /// </remarks>
    public static readonly PhysicalUnit FRKnot = new DerivedUnit(
        LengthUnits.FRNauticalMile.Divide(TimeUnits.Hour), "French knot", "kn_FR");
    /// <inheritdoc cref="FRKnot" />
    public static readonly PhysicalUnit UKKnot = new DerivedUnit(
        LengthUnits.UKNauticalMile.Divide(TimeUnits.Hour), "UK knot", "kn_UK");
    /// <inheritdoc cref="FRKnot" />
    public static readonly PhysicalUnit RUKnot = new DerivedUnit(
        LengthUnits.RUNauticalMile.Divide(TimeUnits.Hour), "Russian knot", "kn_RU");
    /// <inheritdoc cref="FRKnot" />
    public static readonly PhysicalUnit USKnot = new DerivedUnit(
        LengthUnits.USNauticalMile.Divide(TimeUnits.Hour), "US knot", "kn_US");

    /// <summary>**光速，用光秒每秒表示** / Light speed, expressed as light seconds per second.</summary>
    /// <remarks>
    /// **写成 `LightSecond / Second` 而不是 `299792458 m/s`**：**两者在数值上相同，
    /// 但前者让"光速"与"光秒"共享同一个定义来源**——**后者会在两处各写一个数，改一处忘另一处。**
    ///
    /// **Written as `LightSecond / Second` rather than `299792458 m/s`**: **the numbers agree,
    /// but the former makes light speed and the light second share one definition** —
    /// **the latter writes the constant twice and forgets one of them.**
    /// </remarks>
    public static readonly PhysicalUnit LightSpeed = new DerivedUnit(
        LengthUnits.LightSecond.Divide(SIBaseUnits.Second), "speed of light", "c");


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
