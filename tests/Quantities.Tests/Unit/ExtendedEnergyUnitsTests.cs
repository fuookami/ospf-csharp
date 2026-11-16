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
/// 吉瓦时用的是 mega 而不是 giga，因为基准是千瓦时 / GWh uses mega because its base is the kilowatt-hour.
/// </summary>
/// <remarks>
/// 补 10 个能量单位。全是焦耳的固定倍数，
/// **所以风险不在公式，在三处语义**：
/// **① `WattHour` 是 `KilowattHour` 的千分之一，方向容易反；**
/// **② `GigawattHour` 用 `Scale.mega`（基准是 10³ Wh），名字是 giga 而 Scale 看着像错的；**
/// **③ `Erg` 是 1e-7 J，数量级最容易写错。**
///
/// Adds **so the risk is semantic rather than formulaic**:
/// **① `WattHour` is a thousandth of `KilowattHour`, and the direction flips easily;**
/// **② `GigawattHour` uses `Scale.mega` (base 10³ Wh): the name says giga while that Scale looks wrong;**
/// **③ `Erg` is 1e-7 J, the magnitude easiest to mistype.**
/// </remarks>
public class ExtendedEnergyUnitsTests
{
    /// <summary>**瓦时是千瓦时的千分之一，方向不能反** / A watt-hour is a thousandth of a kilowatt-hour.</summary>
    /// <remarks>
    /// **反了会让"1 Wh = 3.6e10 J"——比正确值大 10⁶ 倍，而"千瓦时大于瓦时"这个定性判断仍然成立。**
    /// **所以判据必须钉比值，不能只钉大小关系。**
    ///
    /// **Reversing it makes "1 Wh = 3.6e10 J", a million times too large, while the qualitative "a kilowatt-hour
    /// exceeds a watt-hour" still holds.**
    /// **So the criterion has to pin the ratio, not the ordering.**
    /// </remarks>
    [Fact]
    public void TheWattHour_ShouldBeAThousandthOfTheKilowattHour()
    {
        (JoulesOf(EnergyUnits.WattHour) / JoulesOf(EnergyUnits.KilowattHour)).Should().Be(
            0.001,
            "the watt-hour is the smaller unit, and the ratio is exactly a thousandth");
    }

    /// <summary>**瓦时恰好是 3600 焦耳** / A watt-hour is exactly 3600 joules.</summary>
    [Fact]
    public void TheWattHour_ShouldBeThreeThousandSixHundredJoules()
    {
        JoulesOf(EnergyUnits.WattHour).Should().Be(
            3600.0,
            "a watt-hour is 3600 joules, and a dropped zero would make it a watt-second, which equals a joule");
    }

    /// <summary>**吉瓦时是 10⁹ Wh，不是 10¹² Wh** / GWh is 10⁹ Wh, not 10¹² Wh.</summary>
    /// <remarks>
    /// **名字是 giga 而它乘的是 `Scale.mega`**——**因为基准是 `KilowattHour`（10³ Wh）。**
    /// **改成 `Scale.giga` 会得到 10¹² Wh，与名字差三个数量级，而单位本身仍然"看起来正常"。**
    ///
    /// **The name says giga while it multiplies by `Scale.mega`** — **because the base is `KilowattHour` (10³ Wh).**
    /// **Changing it to `Scale.giga` yields 10¹² Wh, three orders off the name, while the unit still looks fine.**
    /// </remarks>
    [Fact]
    public void TheGigawattHour_ShouldBeOneBillionWattHours()
    {
        (JoulesOf(EnergyUnits.GigawattHour) / JoulesOf(EnergyUnits.WattHour)).Should().Be(
            1e9,
            "a gigawatt-hour is 10⁹ Wh: 10³ Wh in the kilowatt-hour times mega");
    }

    /// <summary>**整条瓦时阶梯每级 1000 倍** / The whole watt-hour ladder is a thousandfold at each step.</summary>
    [Fact]
    public void TheWattHourLadder_ShouldBeAThousandfoldAtEachStep()
    {
        PhysicalUnit[] ladder = {
            EnergyUnits.WattHour,
            EnergyUnits.KilowattHour,
            EnergyUnits.MegawattHour,
            EnergyUnits.GigawattHour,
        };

        for (int level = 1; level < ladder.Length; ++level) {
            (JoulesOf(ladder[level]) / JoulesOf(ladder[level - 1])).Should().Be(
                1000.0,
                $"watt-hour level {level} should be a thousand times level {level - 1}");
        }
    }

    /// <summary>**尔格是 1e-7 焦耳** / An erg is 1e-7 joules.</summary>
    /// <remarks>
    /// **它比所有其他能量单位小得多，所以"几个数量级"是最容易写错的地方。**
    /// **判据同时钉住方向（小于焦耳）与幅度（恰好 1e-7）。**
    ///
    /// **It is far smaller than every other energy unit, so "how many orders" is where typos land.**
    /// **The criterion pins both the direction (below a joule) and the magnitude (exactly 1e-7).**
    /// </remarks>
    [Fact]
    public void TheErg_ShouldBeOneTenMillionthOfAJoule()
    {
        JoulesOf(EnergyUnits.Erg).Should().Be(1e-7);
        // 注意方向：**一个尔格远大于一个电子伏**（1e-7 对 1.6e-19，差 12 个数量级）。
        // **第一版把这句话写反了**——"erg 小于 eV"是个错误断言，而它恰好也会绿，
        // 因为这条 Should 被写在 Be(1e-7) 之后、看着像"补充说明"。
        //
        // Note the direction: **an erg is far larger than an electronvolt** (1e-7 against 1.6e-19, twelve orders apart).
        // **The first version wrote this backwards** — "erg below eV" is a wrong claim, and it happened to pass too,
        // because it sat after the Be(1e-7) check and read like an afterthought.
        JoulesOf(EnergyUnits.Erg).Should().BeGreaterThan(JoulesOf(EnergyUnits.Electronvolt),
            "1e-7 J exceeds an electronvolt's 1.6e-19 J by twelve orders");
    }

    /// <summary>**英热单位是 1055.06 焦耳（IT 制）** / BTU is 1055.06 joules on the IT definition.</summary>
    /// <remarks>
    /// **热化学制是 1054.35，两者都叫 BTU，差 0.07%。**
    /// **判据钉的是 IT 制——因为注释写的是它。**
    ///
    /// **The thermochemical definition is 1054.35, both are called BTU, 0.07% apart.**
    /// **The criterion pins the IT definition, which is what its comment states.**
    /// </remarks>
    [Fact]
    public void TheBritishThermalUnit_ShouldUseTheItDefinition()
    {
        JoulesOf(EnergyUnits.BritishThermalUnit).Should().BeApproximately(1055.06, 1e-9,
            "the baseline uses the IT calorie definition, not the thermochemical 1054.35");
    }

    /// <summary>**电子伏阶梯每级 1000 倍** / The electronvolt ladder is a thousandfold at each step.</summary>
    [Fact]
    public void TheElectronvoltLadder_ShouldBeAThousandfoldAtEachStep()
    {
        PhysicalUnit[] ladder = {
            EnergyUnits.Electronvolt,
            EnergyUnits.Kiloelectronvolt,
            EnergyUnits.Megaelectronvolt,
            EnergyUnits.Gigaelectronvolt,
        };

        for (int level = 1; level < ladder.Length; ++level) {
            (JoulesOf(ladder[level]) / JoulesOf(ladder[level - 1])).Should().Be(
                1000.0,
                $"electronvolt level {level} should be a thousand times level {level - 1}");
        }
    }

    /// <summary>**焦耳阶梯每级 10 的幂** / The joule ladder follows powers of ten.</summary>
    [Fact]
    public void TheJouleLadder_ShouldFollowPowersOfTen()
    {
        (JoulesOf(EnergyUnits.Kilojoule) / JoulesOf(EnergyUnits.Joule)).Should().Be(1e3);
        (JoulesOf(EnergyUnits.Megajoule) / JoulesOf(EnergyUnits.Joule)).Should().Be(1e6);
        (JoulesOf(EnergyUnits.Gigajoule) / JoulesOf(EnergyUnits.Joule)).Should().Be(1e9);
    }

    /// <summary>**单位清单** / The unit list matches the code.</summary>
    [Fact]
    public void TheUnitList_ShouldMatchTheBaseline()
    {
        string[] declared = typeof(EnergyUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(new[] {
            "Joule", "Kilojoule", "Megajoule", "Gigajoule",
            "Electronvolt", "Kiloelectronvolt", "Megaelectronvolt", "Gigaelectronvolt",
            "KilowattHour", "MegawattHour", "GigawattHour", "WattHour",
            "Calorie", "Kilocalorie",
            "BritishThermalUnit", "Erg",
        }, "the baseline's Energy.kt declares exactly these sixteen");
    }

    /// <summary>取一个单位相对焦耳的换算因子 / Read a unit's factor against the joule.</summary>
    private static double JoulesOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
