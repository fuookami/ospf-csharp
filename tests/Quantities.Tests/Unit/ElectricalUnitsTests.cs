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
/// 电流、电压、电阻互相定义，缺一个另外两个就站不住 / The electrical trio defines each other.
/// </summary>
/// <remarks>
/// **新建 `CurrentUnits`**（的四个单位本仓一个都没有独立文件），
/// 并补 `Voltage.Megavolt` 与 `Resistance` 的基线命名别名 `Kiloohm`/`Megaohm`。
///
/// **为什么三个一起补**：`Volt = Watt / Ampere`、`Ohm = Volt / Ampere`——
/// **缺了电流，电压与电阻的推导无从谈起。**
///
/// Creates `CurrentUnits` (none of its four current units had a file here)
/// and adds `Voltage.Megavolt` plus the code spellings `Kiloohm`/`Megaohm`.
///
/// **Why all three at once**: the code defines `Volt = Watt / Ampere` and `Ohm = Volt / Ampere` —
/// **without current, the voltage and resistance derivations have nothing to stand on.**
/// </remarks>
public class ElectricalUnitsTests
{
    /// <summary>**电流阶梯每级 1000 倍** / The current ladder is a thousandfold at each step.</summary>
    [Fact]
    public void TheCurrentLadder_ShouldBeAThousandfoldAroundTheAmpere()
    {
        (AmperesOf(CurrentUnits.Milliampere) * 1000).Should().Be(AmperesOf(CurrentUnits.Ampere));
        (AmperesOf(CurrentUnits.Microampere) * 1000000).Should().Be(AmperesOf(CurrentUnits.Ampere));
        (AmperesOf(CurrentUnits.Kiloampere) / 1000).Should().Be(AmperesOf(CurrentUnits.Ampere));
    }

    /// <summary>**安培就是 SI 基准本身** / The ampere is the SI base itself.</summary>
    /// <remarks>
    /// **用 `BeSameAs` 而不是比值**：**比值只能证明"数值对"，证明不了"它真的是基准"**——
    /// **而如果这里另造一个 `DerivedUnit`，单位体系里就会出现两个不同的安培对象，
    /// 换算在它们之间走一圈会引入不必要的刻度运算。**
    ///
    /// **`BeSameAs` rather than a ratio**: **a ratio only shows the number matches, not that this really is the base** —
    /// **a separate `DerivedUnit` here would put two distinct ampere objects in the system, and converting between
    /// them would run an unnecessary scale operation.**
    /// </remarks>
    [Fact]
    public void TheAmpere_ShouldBeTheSiBaseItself()
    {
        CurrentUnits.Ampere.Should().BeSameAs(SIBaseUnits.Ampere,
            "an alias must point at the base, not at a copy of it");
    }

    /// <summary>**基线命名的电阻别名指向同一个值** / The code resistance spellings agree.</summary>
    /// <remarks>
    /// **`Kiloohm` 与 `Kilohm`、`Megaohm` 与 `Megohm` 必须完全相等。**
    /// **写成两个独立的 `DerivedUnit` 会"数值相等而对象不同"——**、
    /// **这在地图/字典按对象键查找时会变成两个条目。**
    ///
    /// **`Kiloohm` and `Kilohm`, `Megaohm` and `Megohm` must be exactly equal.**
    /// **Two separate `DerivedUnit` instances would be "equal in value, distinct in identity" —**
    /// **which becomes two entries in any dictionary keyed by object.**
    /// </remarks>
    [Fact]
    public void TheBaselineResistanceSpellings_ShouldPointAtTheSameUnit()
    {
        ResistanceUnits.Kiloohm.Should().BeSameAs(ResistanceUnits.Kilohm,
            "the baseline spelling is an alias, not a second unit");
        ResistanceUnits.Megaohm.Should().BeSameAs(ResistanceUnits.Megohm);
    }

    /// <summary>**电阻阶梯与电压阶梯都每级 1000 倍** / Both ladders step by a thousand.</summary>
    [Fact]
    public void TheResistanceAndVoltageLadders_ShouldStepByAThousand()
    {
        (OhmsOf(ResistanceUnits.Kilohm) / OhmsOf(ResistanceUnits.Ohm)).Should().Be(1000.0);
        (OhmsOf(ResistanceUnits.Megohm) / OhmsOf(ResistanceUnits.Kilohm)).Should().Be(1000.0);

        (VoltsOf(VoltageUnits.Kilovolt) / VoltsOf(VoltageUnits.Volt)).Should().Be(1000.0);
        (VoltsOf(VoltageUnits.Megavolt) / VoltsOf(VoltageUnits.Kilovolt)).Should().Be(1000.0);
    }

    /// <summary>**微伏与毫伏都是伏的十进制分数** / Microvolt and millivolt are decimal fractions of the volt.</summary>
    [Fact]
    public void TheSmallVoltages_ShouldBeDecimalFractions()
    {
        (VoltsOf(VoltageUnits.Millivolt) * 1000).Should().Be(VoltsOf(VoltageUnits.Volt));
        (VoltsOf(VoltageUnits.Microvolt) * 1000000).Should().Be(VoltsOf(VoltageUnits.Volt));
    }

    /// <summary>**兆伏比千伏大，千伏比伏大** / Megavolt exceeds kilovolt, which exceeds volt.</summary>
    /// <remarks>
    /// **顺序判据看似平凡，但它是唯一能发现"某一级用了 micro 而不是 mega"的东西**——
    /// **那种错误下数值仍然"是个电压"，只是方向错了。**
    ///
    /// **The ordering looks trivial, but it is the only thing that catches "a level used micro instead of mega"** —
    /// **where the number is still "a voltage", only in the wrong direction.**
    /// </remarks>
    [Fact]
    public void TheVoltageOrdering_ShouldBeMonotonic()
    {
        double[] ladder = {
            VoltsOf(VoltageUnits.Microvolt),
            VoltsOf(VoltageUnits.Millivolt),
            VoltsOf(VoltageUnits.Volt),
            VoltsOf(VoltageUnits.Kilovolt),
            VoltsOf(VoltageUnits.Megavolt),
        };

        ladder.Should().BeInAscendingOrder("micro < milli < 1 < kilo < mega");
    }

    /// <summary>**单位清单** / The unit lists match the code.</summary>
    [Theory]
    [InlineData(typeof(CurrentUnits), "Ampere,Milliampere,Microampere,Kiloampere")]
    [InlineData(typeof(VoltageUnits), "Volt,Microvolt,Millivolt,Kilovolt,Megavolt")]
    public void TheUnitList_ShouldMatchTheBaseline(Type unitClass, string expected)
    {
        string[] declared = unitClass
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(expected.Split(','),
            $"{unitClass.Name} has to cover the baseline's unit list");
    }

    /// <summary>**电阻清单同时含本仓旧名与基线名** / Resistance carries both spellings.</summary>
    [Fact]
    public void TheResistanceList_ShouldCarryBothSpellings()
    {
        string[] declared = typeof(ResistanceUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().Contain(new[] { "Ohm", "Kiloohm", "Megaohm" },
            "the baseline's three names must all exist");
        declared.Should().Contain(new[] { "Kilohm", "Megohm" },
            "the spellings this repo already had are kept, not renamed away");
    }

    private static double AmperesOf(PhysicalUnit unit) => FactorOf(unit);

    private static double OhmsOf(PhysicalUnit unit) => FactorOf(unit);

    private static double VoltsOf(PhysicalUnit unit) => FactorOf(unit);

    /// <summary>取一个单位相对其基准的换算因子 / Read a unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
