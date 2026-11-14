#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Unit;
using System;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// 90 度必须是一个直角；而 735 与 745.7 都叫 "horsepower" / Ninety degrees must be one right angle.
/// </summary>
/// <remarks>
/// B1143 补角度(4+2 别名) / 力(2) / 功率(4)。
///
/// **两处基线问题**：
/// ① 基线写 `RightAngle = Radian / 4.0`，**而直角是 π/2 弧度**——正确的式子是 `RoundAngle / 4`；
/// ② 基线写 `UKHorsepower = Watt * 550`，**而 550 是 ft·lbf/s 的数值，漏了换算**
///    （550 × 0.3048 × 4.4482216152605 = 745.69987 W）。
///
/// **一处同名不同值**：基线的 `Horsepower` 是 735 W（公制），本仓的是 745.69987 W（机械），
/// **同一个 `hp` 符号，差 1.4%。**
///
/// B1143 adds plane angle (4 plus 2 aliases), force (2) and power (4).
///
/// **Two baseline problems**:
/// ① the baseline writes `RightAngle = Radian / 4.0`, **while a right angle is pi/2** — correct is `RoundAngle / 4`;
/// ② the baseline writes `UKHorsepower = Watt * 550`, **and 550 is the ft-lbf/s figure with the conversion dropped**
///    (550 x 0.3048 x 4.4482216152605 = 745.69987 W).
///
/// **One same-name-different-value case**: baseline `Horsepower` is 735 W (metric), this repo's is 745.69987 W
/// (mechanical), **same `hp` symbol, 1.4% apart.**
/// </remarks>
public class AngleForcePowerUnitsTests
{
    /// <summary>**90 度恰好是一个直角** / Ninety degrees is exactly one right angle.</summary>
    /// <remarks>
    /// **这是本批最强的一条判据**：**它把 `Degree`（来自周角）与 `RightAngle`（来自周角四等分）钉在一起。**
    /// **基线写 `Radian / 4` 时，这条会红成 14.3 度对 90 度。**
    /// **而"`Gradian` 是直角的百分之一"那条仍会绿**——**因为两者一起错，比值不动。**
    ///
    /// **The strongest criterion in this batch**: **it pins `Degree` (from the full turn) against `RightAngle`
    /// (from quartering the turn).** **With the baseline's `Radian / 4` it goes red at 14.3 degrees against 90.**
    /// **And "'Gradian' is a hundredth of a right angle" would still pass** — **both go wrong together and the ratio
    /// does not move.**
    /// </remarks>
    [Fact]
    public void TheRightAngle_ShouldBeNinetyDegrees()
    {
        (FactorOf(AngleUnits.RightAngle) / FactorOf(AngleUnits.Degree)).Should().BeApproximately(
            90.0, 1e-9,
            "a right angle is ninety degrees, and the baseline's Radian / 4 gives 14.3 instead");
    }

    /// <summary>**四个直角是一个周角** / Four right angles make a full turn.</summary>
    [Fact]
    public void TheFourRightAngles_ShouldMakeAFullTurn()
    {
        (FactorOf(AngleUnits.RoundAngle) / FactorOf(AngleUnits.RightAngle)).Should().BeApproximately(
            4.0, 1e-12,
            "quartering the turn is what a right angle is");
    }

    /// <summary>**周角是 2π 弧度** / A full turn is 2π radians.</summary>
    [Fact]
    public void TheRoundAngle_ShouldBeTwoPiRadians()
    {
        (FactorOf(AngleUnits.RoundAngle) / FactorOf(AngleUnits.Radian)).Should().BeApproximately(
            2.0 * global::System.Math.PI, 1e-12);
    }

    /// <summary>**百分度是直角的百分之一（π/200）** / A gradian is a hundredth of a right angle.</summary>
    /// <remarks>
    /// **这条单独不能发现基线那个错误**——**所以它必须与"90 度 = 直角"一起存在。**
    /// **这也是本批的一个方法论要点：由错误派生出的单位，比值判据永远是绿的。**
    ///
    /// **This alone cannot catch the baseline error** — **so it has to sit beside "90 degrees = one right angle".**
    /// **That is the methodological point here: for a unit derived from a wrong one, ratio criteria stay green.**
    /// </remarks>
    [Fact]
    public void TheGradian_ShouldBeAHundredthOfARightAngle()
    {
        (FactorOf(AngleUnits.RightAngle) / FactorOf(AngleUnits.Gradian)).Should().BeApproximately(100.0, 1e-9);
        (FactorOf(AngleUnits.Gradian) / FactorOf(AngleUnits.Radian)).Should().BeApproximately(
            global::System.Math.PI / 200.0, 1e-12);
    }

    /// <summary>**毫弧度是弧度的千分之一，基线拼写指向同一个对象** / Milliradian, and the spellings agree.</summary>
    [Fact]
    public void TheMilliradianAndSpellings_ShouldBehave()
    {
        (FactorOf(AngleUnits.Milliradian) * 1000).Should().BeApproximately(FactorOf(AngleUnits.Radian), 1e-15);
        AngleUnits.ArcMinute.Should().BeSameAs(AngleUnits.Arcminute,
            "the baseline spelling is an alias of the existing unit");
        AngleUnits.ArcSecond.Should().BeSameAs(AngleUnits.Arcsecond);
    }

    /// <summary>**克力是千克力的千分之一** / Gram-force is a thousandth of kilogram-force.</summary>
    [Fact]
    public void TheGramForce_ShouldBeAThousandthOfTheKilogramForce()
    {
        (FactorOf(ForceUnits.KilogramForce) / FactorOf(ForceUnits.GramForce)).Should().BeApproximately(
            1000.0, 1e-12,
            "one gram-force is a thousandth of one kilogram-force");
    }

    /// <summary>**千磅力是磅力的一千倍** / Kilopound-force is a thousand pound-forces.</summary>
    [Fact]
    public void TheKilopoundForce_ShouldBeAThousandPoundForces()
    {
        (FactorOf(ForceUnits.KilopoundForce) / FactorOf(ForceUnits.PoundForce)).Should().BeApproximately(
            1000.0, 1e-12);
    }

    /// <summary>**UK 马力等于机械马力，而不是 550 W** / UK horsepower is mechanical, not 550 W.</summary>
    /// <remarks>
    /// **基线写 550，那是 ft·lbf/s 的数值。** **550 × 0.3048 × 4.4482216152605 = 745.69987 W。**
    /// **这条判据同时钉住"值正确"与"是别名而非新对象"。**
    ///
    /// **The baseline writes 550, which is the ft-lbf/s figure.** **550 x 0.3048 x 4.4482216152605 = 745.69987 W.**
    /// **This pins both "the value is right" and "it is an alias, not a new object".**
    /// </remarks>
    [Fact]
    public void TheUkHorsepower_ShouldEqualMechanicalHorsepower()
    {
        FactorOf(PowerUnits.UKHorsepower).Should().BeApproximately(
            550.0 * 0.3048 * 4.4482216152605, 1e-6,
            "550 ft-lbf/s is 745.69987 W once the conversion is applied");
        PowerUnits.UKHorsepower.Should().BeSameAs(PowerUnits.Horsepower);
    }

    /// <summary>**机械马力比公制马力大 1.4%** / Mechanical horsepower exceeds metric by 1.4%.</summary>
    /// <remarks>
    /// **两个都叫 horsepower，而 `hp` 在两种语境下都在用**——**差 1.4% 足以让选型算错，而名字看不出来。**
    ///
    /// **Both are called horsepower, and `hp` is used in both senses** — **1.4% is enough to break sizing, and the
    /// name gives no hint.**
    /// </remarks>
    [Fact]
    public void TheMechanicalAndMetricHorsepower_ShouldDifferByName()
    {
        double ratio = FactorOf(PowerUnits.Horsepower) / FactorOf(PowerUnits.MetricHorsepower);

        ratio.Should().BeApproximately(1.0139, 0.0001,
            "745.69987 / 735.49875 is about 1.0139");
        FactorOf(PowerUnits.MetricHorsepower).Should().BeApproximately(735.49875, 1e-9);
    }

    /// <summary>**焦耳每秒与牛顿米每秒同值但各自独立** / J/s and N*m/s equal the watt under their own names.</summary>
    [Fact]
    public void TheWattSynonyms_ShouldBeEqualButSeparatelyNamed()
    {
        (FactorOf(PowerUnits.JoulePerSecond) / FactorOf(PowerUnits.Watt)).Should().Be(1.0);
        (FactorOf(PowerUnits.NewtonMeterPerSecond) / FactorOf(PowerUnits.Watt)).Should().Be(1.0);

        PowerUnits.JoulePerSecond.Name.Should().NotBe(PowerUnits.Watt.Name,
            "a caller looking up J/s must not be handed the watt");
        PowerUnits.NewtonMeterPerSecond.Name.Should().NotBe(PowerUnits.Watt.Name);
    }

    /// <summary>取一个单位相对其基准的换算因子 / Read a unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
