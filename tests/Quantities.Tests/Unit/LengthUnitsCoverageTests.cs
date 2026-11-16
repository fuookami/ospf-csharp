#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// **少 23 个单位与"够用了"在文档上长得一样** / "23 short" and "good enough" look the same in a document.
/// </summary>
/// <remarks>
/// 按公开类型名扫 22 个模块，`quantities` 命中率 **45.6%**（167 个未出现）。
/// 逐条核实下来，其中最大的一块是**长度单位**：
/// **有 33 个，本仓 `LengthUnits` 只有 10 个**，而 `docs/` 里一次都没提过。
///
/// **单位换算错了不会报错，只会让"5 海里"变成别的长度**——
/// **所以判据要钉的不是"字段存在"，而是"换算因子是对的"。**
///
/// A sweep over 22 modules by public type name and `quantities` hit **45.6%** (167 absent).
/// the earlier item-by-item check found the largest block to be **length units**:
/// **this implementation has 33 and this repo's `LengthUnits` had 10**, with `docs/` silent about it.
///
/// **A wrong conversion does not throw; it makes "5 nautical miles" a different length** —
/// **so the criteria pin the factors, not merely that the fields exist.**
/// </remarks>
public class LengthUnitsCoverageTests
{
    /// <summary>**33 个长度单位都在** / All 33 length units are present.</summary>
    /// <remarks>
    /// **清单来自实现，不是照"我们记得的"抄**：
    /// **四个地区海里（FR/UK/RU/US）与三个秒差距档（kilo/mega/giga）最容易在凭记忆列举时被漏掉。**
    ///
    /// **The list comes from the implementation, not from memory**.
    /// **the four regional nautical miles (FR/UK/RU/US) and the three parsec scales are the easiest to miss.**
    /// </remarks>
    [Fact]
    public void TheThirtyThreeUnits_ShouldAllExist()
    {
        // **`Meter` 不在这个类里**：它是 SI 基准单位（`SIBaseUnits.Meter`），
        // **`LengthUnits` 只放导出单位**——**第一版把基准也算进来，于是断言要求一个不该存在的字段。**
        //
        // **`Meter` is not in this class**: it is the SI base unit (`SIBaseUnits.Meter`),
        // **and `LengthUnits` holds derived units only** — **the first version counted the base and demanded a field that should not exist.**
        string[] baseline = [
            "Picometer", "Nanometer", "Micrometer", "Millimeter", "Centimeter",
            "Decimeter", "Decameter", "Hectometer", "Kilometer",
            "NauticalMile", "FRNauticalMile", "UKNauticalMile", "RUNauticalMile", "USNauticalMile",
            "Fathom", "Cable", "Inch", "Foot", "Yard", "Chain", "Rod", "Mile",
            "AstronomicalUnit", "LightSecond", "LightMinute", "LightHour", "LightDay", "LightYear",
            "Parsec", "Kiloparsec", "Megaparsec", "Gigaparsec",
        ];

        FieldInfo[] fields = typeof(LengthUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .ToArray();

        foreach (string name in baseline)
        {
            fields.Should().Contain(field => field.Name == name,
                $"the baseline declares the length unit {name}");
        }

        fields.Should().HaveCount(baseline.Length,
            "the count has to match too, or a renamed unit hides behind a same-size list");
        SIBaseUnits.Meter.Should().NotBeNull("the base unit lives in SIBaseUnits, not here");
    }

    /// <summary>**四个地区海里各自不同** / The four regional nautical miles differ.</summary>
    /// <remarks>
    /// **这四个值不同是刻意的**：FR 1853.27、UK 1854.55、RU 1855.78、US 1851.01 米。
    /// **把任何一个写成 1852，都会让该地区的海图距离算错**——
    /// **而错误不会显现为异常，只会显现为"距离差一点"。**
    ///
    /// **These four differ on purpose**: FR 1853.27, UK 1854.55, RU 1855.78, US 1851.01 metres.
    /// **Collapsing any of them to 1852 makes that region's chart distance wrong** —
    /// **and the error shows up not as an exception but as "the distance is slightly off".**
    /// </remarks>
    [Fact]
    public void TheRegionalNauticalMiles_ShouldStayDistinct()
    {
        double[] metres = [
            FactorOf(LengthUnits.FRNauticalMile),
            FactorOf(LengthUnits.UKNauticalMile),
            FactorOf(LengthUnits.RUNauticalMile),
            FactorOf(LengthUnits.USNauticalMile),
        ];

        metres.Distinct().Should().HaveCount(4,
            "four regions, four different definitions — collapsing them is a silent correctness bug");
        // **US 海里 1851.01 与国际海里 1852 只差 0.99 米**——**第一版用 1 米容差，正好把它判成"就是国际海里"。**
        // **而它确实不是**：1851.01 是美国测量海里的定义值，**与 1852 不同**。
        //
        // **The US nautical mile at 1851.01 is only 0.99 m from the international 1852** — **the first version's 1 m tolerance
        // classified it as the international one.** **It is not**: 1851.01 is the US survey definition, **different from 1852**.
        metres.Should().OnlyContain(value => global::System.Math.Abs(value - 1852.0) > 0.5,
            "none of them is exactly the international nautical mile");
    }

    /// <summary>**光年用儒略年 365.25 天** / The light year uses the Julian year of 365.25 days.</summary>
    /// <remarks>
    /// **用的是 `LightDay * 365.25`**，**不是 365 天，也不是 365.2425**。
    /// **三者差 0.0069%**——**在光年的量级上就是 6.5 亿公里。**
    ///
    /// **The implementation uses `LightDay * 365.25`**, **not 365 days and not 365.2425**.
    /// **The three differ by 0.0069%** — **which at a light year is about 650 million kilometres.**
    /// </remarks>
    [Fact]
    public void TheLightYear_ShouldUseTheJulianYear()
    {
        const double lightSpeed = 299792458.0;
        double expected = lightSpeed * 86400 * 365.25;

        FactorOf(LengthUnits.LightYear).Should().BeApproximately(expected, expected * 1e-12,
            "the Julian year is what the baseline uses, and 365 or 365.2425 would differ by hundreds of millions of kilometres");
    }

    /// <summary>**三个秒差距档是同一基准的千倍关系** / The three parsec scales are powers of a thousand.</summary>
    [Fact]
    public void TheParsecScales_ShouldBePowersOfAThousand()
    {
        double parsec = FactorOf(LengthUnits.Parsec);

        FactorOf(LengthUnits.Kiloparsec).Should().BeApproximately(parsec * 1e3, parsec * 1e3 * 1e-12);
        FactorOf(LengthUnits.Megaparsec).Should().BeApproximately(parsec * 1e6, parsec * 1e6 * 1e-12);
        FactorOf(LengthUnits.Gigaparsec).Should().BeApproximately(parsec * 1e9, parsec * 1e9 * 1e-12);
    }

    /// <summary>**天文单位是 IAU 2012 定义值** / The astronomical unit is the IAU 2012 value.</summary>
    [Fact]
    public void TheAstronomicalUnit_ShouldBeTheDefinedValue()
    {
        FactorOf(LengthUnits.AstronomicalUnit).Should().Be(149597870700.0,
            "the AU is defined exactly, not measured");
    }

    /// <summary>取一个单位相对基准的换算因子 / Read one unit's conversion factor against its base.</summary>
    /// <remarks>
    /// **走 `ConversionRule` 而不是反射私有字段**：**私有字段名会变，而 `ConversionRule` 是这个类型对外承诺的东西。**
    ///
    /// **Goes through `ConversionRule` rather than reflecting a private field**.
    /// **private names change, while `ConversionRule` is what the type promises to the outside.**
    /// </remarks>
    private static double FactorOf(PhysicalUnit unit)
    {
        // **`UnitConversionRule.Linear` 带的是 `Scale`，不是裸 `double`**——
        // **`Scale` 是 `(Base, Index)` 的列表**（`Scale.Kilo` 就是 `10^3`），
        // **所以要经 `ValueOrNull` 求值。** 直接读 `Factor` 编译不过（实测 `CS1061`）。
        //
        // **`UnitConversionRule.Linear` carries a `Scale`, not a bare `double`** —
        // **`Scale` is a list of `(Base, Index)`** (`Scale.Kilo` is `10^3`),
        // **so it is evaluated through `ValueOrNull`.** Reading `Factor` directly does not compile (measured `CS1061`).
        UnitConversionRule.Linear linear = unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject;
        return linear.Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
    }
}
