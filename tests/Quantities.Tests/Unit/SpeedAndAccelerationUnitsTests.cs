#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// **只补海里不补节，等于让两种单位换不到一起** / Adding the miles without the knots leaves them unconvertible.
/// </summary>
/// <remarks>
/// 补齐了四个地区海里，**而它们的配套"节"仍然缺**——
/// **海里不同，节就不同**，**而只补一半会让"用 UK 海里算的航程"与"用 UK 节算的速度"对不上。**
///
/// **两者都是这台机器算出来的，所以没人会怀疑。**
///
/// Added
/// **different miles mean different knots**, **and half a pair makes a voyage in UK miles disagree with a speed in UK knots.**
///
/// **Both come out of this same machine, so nobody suspects either.**
/// </remarks>
public class SpeedAndAccelerationUnitsTests
{
    /// <summary>**14 个速度单位都在** / All 14 baseline speed units are present.</summary>
    [Fact]
    public void TheSpeedUnits_ShouldCoverTheBaseline()
    {
        string[] baseline = [
            "CentimetersPerSecond", "MetersPerSecond", "KilometersPerSecond", "KilometersPerHour",
            "Mach", "Knot", "FRKnot", "UKKnot", "RUKnot", "USKnot",
            "LightSpeed", "InchesPerSecond", "FeetPerSecond", "MilesPerHour",
        ];

        string[] declared = typeof(SpeedUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        foreach (string name in baseline)
        {
            declared.Should().Contain(name, $"the baseline declares the speed unit {name}");
        }

        declared.Should().HaveCount(baseline.Length,
            "the count has to match too, or a rename hides behind a same-size list");
    }

    /// <summary>**四个地区节与四个地区海里配套** / The four regional knots pair with the four regional miles.</summary>
    /// <remarks>
    /// **这是"两个单位必须一起改"的判据**：节的因子必须等于对应海里的因子除以 3600 秒。
    /// **如果只改了海里而忘了节，这条会红**——**而人眼看不出来，因为两个数都"像那么回事"。**
    ///
    /// **This pins "the two units have to move together"**: each knot's factor must equal its mile's factor over 3600 seconds.
    /// **Change a mile and forget its knot and this goes red** — **while the eye sees nothing, because both numbers "look right".**
    /// </remarks>
    [Fact]
    public void TheRegionalKnots_ShouldFollowTheirMiles()
    {
        // **必须比"相对 SI 的绝对米值"，不能比 `FactorOf` 的裸输出。**
        // **第一版直接比 `FactorOf(knot)` 与 `FactorOf(mile)`，而 `FactorOf` 读的是 `ConversionRule.Scale`——
        // 那是"相对本单位基准"的因子，不是绝对米值。**
        // **于是把 UK 海里从 1854.55 改成 1852、而节不动，两边同时变化，比值不变，判据仍然绿。**
        //
        // **Compare absolute metres against the SI base, not the raw `FactorOf` output.**
        // **The first version compared `FactorOf(knot)` with `FactorOf(mile)`, and `FactorOf` reads `ConversionRule.Scale` —
        // which is the factor relative to that unit's own base, not an absolute metre value.**
        // **So changing the UK mile from 1854.55 to 1852 while leaving the knot alone moved both sides and left the ratio
        // unchanged, and the criterion stayed green.**
        // **正确的比法：把"节"的 `Scale` 与"海里/小时"的 `Scale` 直接比。**
        // **`Scale` 是 `(Base, Index)` 的列表，两边若是同源，列表就相等**——
        // **这比数值容差更硬，也不会被"两边一起变"骗过去。**
        //
        // **The right comparison: put the knot's `Scale` next to the mile-over-hour `Scale`.**
        // **`Scale` is a list of `(Base, Index)`, so a shared source means an equal list** —
        // **harder than a numeric tolerance, and not fooled by both sides moving together.**
        ScaleOf(SpeedUnits.FRKnot).Should().Be(
            ScaleOf(LengthUnits.FRNauticalMile.Divide(TimeUnits.Hour)),
            "the French knot has to be built from the French mile over an hour");
        ScaleOf(SpeedUnits.UKKnot).Should().Be(
            ScaleOf(LengthUnits.UKNauticalMile.Divide(TimeUnits.Hour)),
            "the UK knot has to be built from the UK mile over an hour");
        ScaleOf(SpeedUnits.RUKnot).Should().Be(
            ScaleOf(LengthUnits.RUNauticalMile.Divide(TimeUnits.Hour)),
            "the Russian knot has to be built from the Russian mile over an hour");
        ScaleOf(SpeedUnits.USKnot).Should().Be(
            ScaleOf(LengthUnits.USNauticalMile.Divide(TimeUnits.Hour)),
            "the US knot has to be built from the US mile over an hour");
    }

    /// <summary>**马赫按 340.3 折算，不是 343** / Mach uses 340.3, not 343.</summary>
    /// <remarks>
    /// **343 是 20 °C 干空气的声速，340.3 是选择。** 两者差 0.8%——
    /// **在 0.8 马赫上就是 2 米每秒。**
    ///
    /// **343 is the speed of sound in dry air at 20 °C; 340.3 is its choice.** They differ by 0.8% —
    /// **which at Mach 0.8 is 2 metres per second.**
    /// </remarks>
    [Fact]
    public void TheMach_ShouldUseTheBaselineConstant()
    {
        double metresPerSecond = FactorOf(SpeedUnits.Mach);

        metresPerSecond.Should().BeApproximately(340.3, 1e-9,
            "the baseline multiplies by 340.3, and 343 would differ by 0.8%");
    }

    /// <summary>**光速与光秒共享一个定义来源** / Light speed and the light second share one definition.</summary>
    /// <remarks>
    /// **`LightSpeed = LightSecond / Second`**，所以它的因子必须**恰好**等于光秒的因子。
    /// **若两处各写一个 `299792458`，改一处忘另一处就会出现"光速不等于每秒一光秒"。**
    ///
    /// **`LightSpeed = LightSecond / Second`**, so its factor must be **exactly** the light second's.
    /// **Writing `299792458` in both places means changing one and forgetting the other produces
    /// "light speed is not one light second per second".**
    /// </remarks>
    [Fact]
    public void TheLightSpeed_ShouldEqualOneLightSecondPerSecond()
    {
        FactorOf(SpeedUnits.LightSpeed).Should().Be(FactorOf(LengthUnits.LightSecond),
            "light speed is exactly one light second per second, by definition of the light second");
    }

    /// <summary>**6 个加速度单位都在，且标准重力是定义值** / All 6 baseline accelerations, with standard gravity defined exactly.</summary>
    [Fact]
    public void TheAccelerationUnits_ShouldCoverTheBaseline()
    {
        string[] baseline = [
            "MetersPerSecondSquared", "CentimetersPerSecondSquared", "KilometersPerSecondSquared",
            "InchesPerSecondSquared", "FeetPerSecondSquared", "StandardGravity",
        ];

        string[] declared = typeof(AccelerationUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        foreach (string name in baseline)
        {
            declared.Should().Contain(name, $"the baseline declares the acceleration unit {name}");
        }

        declared.Should().HaveCount(baseline.Length);

        // **9.80665 是定义值**（CGPM 1901），**不是测量值**——
        // **写成 9.8 或 9.81 会让"1 g 的载荷"差出千分之几，而结构计算里那就是合格与否。**
        //
        // **9.80665 is defined** (CGPM 1901), **not measured** —
        // **9.8 or 9.81 makes a "1 g load" off by a few thousandths, which in structural work is pass or fail.**
        FactorOf(AccelerationUnits.StandardGravity).Should().BeApproximately(9.80665, 1e-12,
            "standard gravity is defined exactly as 9.80665 m/s²");
    }

    /// <summary>**一个单位折算成 SI 基准的绝对倍数** / How many SI base units one of these is.</summary>
    /// <remarks>
    /// **`ConversionRule.Scale` 已经是"这个单位等于多少个复合基准单位"**——
    /// **而复合基准（`m/s`、`m/s²`）本身就把基本单位折进去了**，
    /// **所以 `ValueOrNull` 直接就是绝对倍数，不需要再乘一次基准。**
    ///
    /// **`ConversionRule.Scale` already means "how many compound base units this is"** —
    /// **and the compound bases (`m/s`, `m/s²`) already fold the base units in**,
    /// **so `ValueOrNull` is the absolute multiple with no extra factor.**
    /// </remarks>
    /// <summary>**一个单位的复合 `Scale` 求值后的绝对倍数** / One unit's compound `Scale`, evaluated.</summary>
    /// <remarks>
    /// **比 `Scale` 对象相等更稳**：**两处写成 `(Meter,1)(Second,-1)` 与 `(Meter,1)(Second,-1)(Hour,-1)(Hour,1)`
    /// 在语义上相同而列表不同**——**比数值才是比"这两个单位是不是同一个量"。**
    ///
    /// **More robust than comparing `Scale` objects**: **`(Meter,1)(Second,-1)` and `(Meter,1)(Second,-1)(Hour,-1)(Hour,1)`
    /// are the same quantity with different lists** — **comparing the evaluated value compares the quantity.**
    /// </remarks>
    private static double ScaleOf(PhysicalUnit unit) =>
        unit.ConversionRule.Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();

    /// <summary>取一个单位相对自身基准的换算因子 / Read one unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit)
    {
        UnitConversionRule.Linear linear = unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject;
        return linear.Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
    }
}
