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
/// 三套吨、两套盎司、两套加仑：名字只差一点，量差很多 / Three tons, two ounces, two gallons: close names, far values.
/// </summary>
/// <remarks>
/// 补 Time(2) / Mass(12) / Volume(16) 共 30 个单位。
///
/// 这里的风险集中在"同名不同量"：
/// 长吨 2240 磅 / 短吨 2000 磅 / 公吨 1000 千克；
/// 常衡盎司 1/16 磅 / 金衡盎司 1/12 磅；
/// UK 加仑 4.54609 L / US 加仑 3.78541 L。
///
/// **每一对都只差一个词，而量差 10%—20%——写错了不会报错，只会让"3 吨"变成别的重量。**
///
/// The risk here is "same name, different size":
/// long ton 2240 lb / short ton 2000 lb / metric ton 1000 kg;
/// avoirdupois ounce 1/16 lb / troy ounce 1/12 lb;
/// UK gallon 4.54609 L / US gallon 3.78541 L.
///
/// **Each pair differs by one word and by 10—20 percent — get it wrong and nothing throws, only "three tons" becomes a
/// different weight.**
/// </remarks>
public class ExtendedTimeMassVolumeUnitsTests
{
    /// <summary>**三套吨必须互不相同，且长吨 &gt; 短吨 &gt; 公吨** / Three distinct tons, long &gt; short &gt; metric.</summary>
    /// <remarks>
    /// **顺序不是巧合**：长吨 2240 磅 ≈ 1016 千克，短吨 2000 磅 ≈ 907 千克，公吨恰好 1000 千克。
    /// **所以公吨落在短吨与长吨之间——如果按"英制更大"的直觉去排，会把公吨排在最后，那是错的。**
    ///
    /// **The order is not a coincidence**: long 2240 lb ≈ 1016 kg, short 2000 lb ≈ 907 kg, metric exactly 1000 kg.
    /// **So the metric ton sits between the short and the long — sorting by "imperial is bigger" puts it last, wrongly.**
    /// </remarks>
    [Fact]
    public void TheThreeTons_ShouldBeDistinctAndOrdered()
    {
        double metric = FactorOf(MassUnits.Ton);
        double shortTon = FactorOf(MassUnits.ShortTon);
        double longTon = FactorOf(MassUnits.LongTon);

        // 因子的语义是"一个本单位等于多少个基准单位"，所以公吨的因子是 1000（基准是 kg）。
        // **第一版把它当成 1，是又一次把"相对量"读成"绝对量"**——与密度判据同一个坑。
        //
        // The factor means "how many base units one of these is", so the metric ton's factor is 1000 (base is kg).
        // **The first version read it as 1, again confusing a relative quantity with an absolute one** —
        // the same trap as the earlier density criterion.
        metric.Should().Be(1000.0, "the metric ton is exactly one thousand kilograms");
        shortTon.Should().BeLessThan(metric, "2000 lb is about 907 kg");
        longTon.Should().BeGreaterThan(metric, "2240 lb is about 1016 kg");

        new[] { metric, shortTon, longTon }.Distinct().Should().HaveCount(3);
    }

    /// <summary>**金衡盎司比常衡盎司重** / The troy ounce is heavier than the avoirdupois ounce.</summary>
    /// <remarks>
    /// **常衡 1/16 磅，金衡 1/12 磅**——**分母更小，所以更重**。
    /// **而"金衡"听起来更"贵"，容易被想成更小，实际相反。**
    /// **金银报价用的是金衡盎司，用错了每盎司差 9.7%。**
    ///
    /// **Avoirdupois is 1/16 lb, troy is 1/12 lb** — **a smaller denominator, so heavier**.
    /// **"Troy" sounds more precious and is easily assumed smaller; it is the opposite.**
    /// **Precious metals quote in troy ounces, and the wrong one is off by 9.7% per ounce.**
    /// </remarks>
    [Fact]
    public void TheTroyOunce_ShouldBeHeavierThanTheAvoirdupoisOunce()
    {
        FactorOf(MassUnits.TroyOunce).Should().BeGreaterThan(
            FactorOf(MassUnits.Ounce),
            "one twelfth of a pound is heavier than one sixteenth");
    }

    /// <summary>**英制加仑比美制大约 20%** / The UK gallon is about twenty percent larger.</summary>
    /// <remarks>
    /// **4.54609 / 3.78541 ≈ 1.2009**——**所以"20% 左右"是精确的，不是估计。**
    /// **这条判据钉的是"两个都写对了"：只写一个数、另一个复制过去，比值会变成 1。**
    ///
    /// **4.54609 / 3.78541 ≈ 1.2009** — **so "about twenty percent" is exact, not an estimate.**
    /// **This pins that both are right**: copying one value into the other makes the ratio 1.
    /// </remarks>
    [Fact]
    public void TheUkGallon_ShouldBeAboutTwentyPercentLarger()
    {
        double ratio = FactorOf(VolumeUnits.UKGallon) / FactorOf(VolumeUnits.USGallon);

        ratio.Should().BeApproximately(1.2009, 0.001,
            "4.54609 / 3.78541 is about 1.2009");
    }

    /// <summary>**英制液量盎司比美制小** / The UK fluid ounce is the smaller one.</summary>
    /// <remarks>
    /// **注意方向与加仑相反**：**加仑是英制更大，而液量盎司是英制更小**——
    /// **因为一英制加仑是 160 液量盎司，而一美制加仑是 128。**
    /// **"英制总是更大"这个直觉在这里恰好反了。**
    ///
    /// **Note the direction flips against the gallon**: **the UK gallon is larger while the UK fluid ounce is smaller** —
    /// **because an imperial gallon holds 160 fluid ounces and a US gallon holds 128.**
    /// **"Imperial is always bigger" is exactly wrong here.**
    /// </remarks>
    [Fact]
    public void TheUkFluidOunce_ShouldBeSmallerThanTheUsOne()
    {
        FactorOf(VolumeUnits.UKFluidOunce).Should().BeLessThan(
            FactorOf(VolumeUnits.USFluidOunce),
            "an imperial gallon holds 160 fluid ounces against the US gallon's 128, so each imperial ounce is smaller");
    }

    /// <summary>**年用儒略年 365.25 天** / The year uses the Julian year of 365.25 days.</summary>
    [Fact]
    public void TheYear_ShouldUseTheJulianYear()
    {
        FactorOf(TimeUnits.Year).Should().BeApproximately(365.25 * 86400, 1e-6,
            "the Julian year is 365.25 days, and 365 or 365.2425 would differ");
    }

    /// <summary>**周恰好是七天** / A week is exactly seven days.</summary>
    [Fact]
    public void TheWeek_ShouldBeSevenDays() =>
        (FactorOf(TimeUnits.Week) / FactorOf(TimeUnits.Day)).Should().BeApproximately(7.0, 1e-12);

    /// <summary>**升与立方分米是同一个体积** / A litre and a cubic decimetre are the same volume.</summary>
    /// <remarks>
    /// **这条把"体积"的两条构造路径钉在一起**：**升走的是"Liter 的十进制倍数"，
    /// 立方分米走的是"立方米的千分之一"——两条路必须落在同一个数上。**
    ///
    /// **This ties the two construction paths for volume together**: **the litre path is "decimal multiples of Liter",
    /// the cubic-decimetre path is "a thousandth of a cubic metre" — both must land on the same number.**
    /// </remarks>
    [Fact]
    public void TheLitre_ShouldEqualACubicDecimetre()
    {
        FactorOf(VolumeUnits.Liter).Should().BeApproximately(
            FactorOf(VolumeUnits.CubicDecimeter), 1e-12,
            "a litre is defined as one cubic decimetre");
    }

 /// <summary>**三个类别的单位清单** / All three categories match the implementation list.</summary>
    [Theory]
    [InlineData(typeof(TimeUnits),
        "Nanosecond,Microsecond,Millisecond,Minute,Hour,Day,Week,Year")]
    [InlineData(typeof(MassUnits),
        "Microgram,Milligram,Point,Carat,Gram,Hectogram,Kintal,Ton,Pound,Gran,"
        + "LongTon,ShortTon,Stone,Ounce,TroyOunce,Dram")]
    [InlineData(typeof(VolumeUnits),
        "CubicMillimeter,CubicCentimeter,CubicDecimeter,CubicMeter,CubicDecameter,CubicHectometer,"
        + "CubicKilometer,Liter,Microliter,Milliliter,Centiliter,Deciliter,Hectoliter,"
        + "CubicInch,CubicFoot,CubicYard,UKFluidOunce,USFluidOunce,UKGallon,USGallon")]
    public void TheUnitList_ShouldMatchTheBaseline(Type unitClass, string expected)
    {
        string[] declared = unitClass
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(expected.Split(','),
            $"{unitClass.Name} has to match the baseline's unit list exactly");
    }

    /// <summary>取一个单位相对自身基准的换算因子 / Read one unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
