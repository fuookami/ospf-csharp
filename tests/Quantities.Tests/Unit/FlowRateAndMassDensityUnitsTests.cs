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
/// **两个都叫"每升"，但一个按秒一个按分** / Two units both called "per litre", one by second and one by minute.
/// </summary>
/// <remarks>
/// 补了速度与加速度之后，继续补 `FlowRate` 与 `MassDensity` 两个量纲。
///
/// **这里的两个风险都不是"算错"，而是"拿错"**：
/// **`LitersPerMinute` 与 `LitersPerSecond` 只差一个词，混用差 60 倍**；
/// **`GramsPerCubicCentimeter` 与 `KilogramsPerCubicMeter` 描述同一杯水，数值差 1000 倍。**
///
/// Added
///
/// **Neither risk here is "computing wrong"; both are "picking the wrong unit"**:
/// **`LitersPerMinute` and `LitersPerSecond` differ by one word and by a factor of 60**;
/// **`GramsPerCubicCentimeter` and `KilogramsPerCubicMeter` describe the same water with values 1000 apart.**
/// </remarks>
public class FlowRateAndMassDensityUnitsTests
{
    /// <summary>**基线的三个流量单位都在** / All three baseline flow units are present.</summary>
    [Fact]
    public void TheFlowRateUnits_ShouldCoverTheBaseline()
    {
        string[] baseline = ["CubicMetersPerSecond", "LitersPerSecond", "LitersPerMinute"];

        string[] declared = typeof(FlowRateUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        foreach (string name in baseline)
        {
            declared.Should().Contain(name, $"the baseline declares the flow unit {name}");
        }

        declared.Should().HaveCount(baseline.Length);
    }

    /// <summary>**升每分钟正好是升每秒的六十分之一** / Litres per minute is exactly one sixtieth of litres per second.</summary>
    /// <remarks>
    /// **这是"别把分钟当秒"的判据**：**把 L/min 的数当成 L/s 用，结果差 60 倍**——
    /// **而两者都叫"升每…"，只差一个词。**
    ///
    /// **This is the "do not read minutes as seconds" criterion**: **treating an L/min figure as L/s is off by 60×** —
    /// **and both are called "litres per something", differing by one word.**
    /// </remarks>
    [Fact]
    public void ThePerMinuteFlow_ShouldBeOneSixtiethOfThePerSecond()
    {
        double perSecond = FactorOf(FlowRateUnits.LitersPerSecond);
        double perMinute = FactorOf(FlowRateUnits.LitersPerMinute);

        (perSecond / perMinute).Should().BeApproximately(60.0, 1e-12,
            "a litre per minute is one sixtieth of a litre per second");
    }

    /// <summary>**基线的四个质量密度单位都在** / All four baseline mass-density units are present.</summary>
    [Fact]
    public void TheMassDensityUnits_ShouldCoverTheBaseline()
    {
        string[] baseline = [
            "KilogramsPerCubicMeter", "KilogramsPerLiter",
            "KilogramsPerCubicCentimeter", "GramsPerCubicCentimeter",
        ];

        string[] declared = typeof(MassDensityUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        foreach (string name in baseline)
        {
            declared.Should().Contain(name, $"the baseline declares the mass-density unit {name}");
        }

        declared.Should().HaveCount(baseline.Length);
    }

    /// <summary>**克每立方厘米是千克每立方米的千分之一** / Grams per cubic centimetre is one thousandth of kilograms per cubic metre.</summary>
    /// <remarks>
    /// **这两个单位描述同一杯水，数值差 1000 倍**：
    /// **水的密度是 1000 kg/m³，也是 1 g/cm³**——**而 1 这个数看起来完全正常，只是单位错了。**
    ///
    /// **These two describe the same glass of water with values 1000 apart**:
    /// **water is 1000 kg/m³ and also 1 g/cm³** — **and 1 looks like an ordinary number, only the unit is wrong.**
    /// </remarks>
    [Fact]
    public void TheCgsDensity_ShouldBeOneThousandthOfTheSi()
    {
        double si = FactorOf(MassDensityUnits.KilogramsPerCubicMeter);
        double cgs = FactorOf(MassDensityUnits.GramsPerCubicCentimeter);

        // **方向要按"因子的含义"来定，不能凭直觉。**
        // **`Scale` 的语义是"一个本单位等于多少个基准"**，而 `Divide` 得到的是 `lhs.Scale / rhs.Scale`，
        // **所以 `KilogramsPerCubicMeter` 的因子是 1，`GramsPerCubicCentimeter` 是 1000。**
        // **第一版把方向写反，断言 1000 而实际 0.001。**
        //
        // **The direction follows what the factor means, not intuition.**
        // **`Scale` means "how many base units one of these is"**, and `Divide` yields `lhs.Scale / rhs.Scale`,
        // **so `KilogramsPerCubicMeter` is 1 and `GramsPerCubicCentimeter` is 1000.**
        // **The first version had the direction backwards, asserting 1000 where the value was 0.001.**
        (cgs / si).Should().BeApproximately(1000.0, 1e-9,
            "1000 kg/m³ and 1 g/cm³ are the same density, so one gram per cubic centimetre is a thousand times the SI unit");
    }

    /// <summary>**升与立方分米是同一个体积** / A litre and a cubic decimetre are the same volume.</summary>
    /// <remarks>
    /// **`KilogramsPerLiter` 与 `KilogramsPerCubicCentimeter` 相差 1000 倍，因为 1 L = 1000 cm³。**
    /// **这条判据把"升"与"立方厘米"的关系钉在密度上**——
    /// **如果哪天 `Liter` 被改错，这两条密度判据会一起红。**
    ///
    /// **`KilogramsPerLiter` and `KilogramsPerCubicCentimeter` differ by 1000 because 1 L = 1000 cm³.**
    /// **This pins the litre-to-cubic-centimetre relation through density** —
    /// **if `Liter` is ever changed wrongly, both density criteria go red together.**
    /// </remarks>
    [Fact]
    public void TheLitreAndCubicCentimetre_ShouldDifferByAThousand()
    {
        double perLiter = FactorOf(MassDensityUnits.KilogramsPerLiter);
        double perCubicCentimeter = FactorOf(MassDensityUnits.KilogramsPerCubicCentimeter);

        // **同一方向问题**：`KilogramsPerCubicCentimeter` 的因子是 1e6，`KilogramsPerLiter` 是 1e3，
        // **所以比值要反过来写。**
        //
        // **Same direction issue**: `KilogramsPerCubicCentimeter` is 1e6 and `KilogramsPerLiter` is 1e3,
        // **so the ratio is written the other way round.**
        (perCubicCentimeter / perLiter).Should().BeApproximately(1000.0, 1e-9,
            "one litre is a thousand cubic centimetres");
    }

    /// <summary>取一个单位相对自身基准的换算因子 / Read one unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit)
    {
        UnitConversionRule.Linear linear = unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject;
        return linear.Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
    }
}
