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
/// 0.76 米汞柱等于一个大气压，所以大气压要除下来 / 0.76 m of mercury is one atmosphere, so divide.
/// </summary>
/// <remarks>
/// B1140 补 5 个压力单位（基线 `Pressure.kt`）：`Hectopascal` / `StandardAtmosphericPressure` /
/// `MeterMercury` / `InchOfMercury` / `Millibar`。
///
/// **其中 `MeterMercury` 没有照抄基线，因为基线的公式与它自己的注释矛盾**：
/// 基线写 `标准大气压 × 0.76`，得到 77007 Pa，于是 mmHg = 77.007 Pa；
/// **而基线自己在上方注释里写着 "1 mmHg ≈ 133.322 Pa"。**
///
/// **0.76 米的汞柱恰好压出一个标准大气压，所以要从大气压"除"回 0.76 米（133322.37 Pa），不是乘。**
/// 本仓按注释与物理事实实现，并让 mmHg 由 mHg 派生，使"千分之一"成为恒等式。
///
/// B1140 adds five pressure units from the baseline.
///
/// **`MeterMercury` is not copied from the baseline, because the baseline formula contradicts its own comment**:
/// the baseline writes "standard atmosphere times 0.76", yielding 77007 Pa and so mmHg = 77.007 Pa,
/// **while its comment just above says "1 mmHg is about 133.322 Pa".**
///
/// **A column of mercury 0.76 m tall weighs out exactly one standard atmosphere, so the atmosphere is divided back
/// down to 0.76 m (133322.37 Pa), not multiplied.** This repo follows the comment and the physics, with mmHg derived
/// from mHg so that "a thousandth" is an identity.
/// </remarks>
public class ExtendedPressureUnitsTests
{
    /// <summary>**米汞柱比大气压大，不是小** / A metre of mercury exceeds an atmosphere.</summary>
    /// <remarks>
    /// **这条是"方向"判据，而它是这一批唯一能抓住照抄基线的地方。**
    /// **乘法会得到 77007，除法得 133322.37——两者都"是个压力值"，只有方向区分得开。**
    ///
    /// **This is the direction criterion, and it is the only thing here that catches copying the baseline.**
    /// **Multiplying yields 77007 and dividing yields 133322.37 — both "a pressure"; only the direction separates them.**
    /// </remarks>
    [Fact]
    public void TheMeterMercury_ShouldExceedTheStandardAtmosphere()
    {
        PascalsOf(PressureUnits.MeterMercury).Should().BeGreaterThan(
            PascalsOf(PressureUnits.StandardAtmosphericPressure),
            "0.76 m of mercury equals one atmosphere, so a full metre is 1/0.76 of one — larger, not smaller");
    }

    /// <summary>**米汞柱恰好是大气压的 1/0.76** / A metre of mercury is exactly 1/0.76 atmospheres.</summary>
    [Fact]
    public void TheMeterMercury_ShouldBeTheAtmosphereDividedByZeroPointSevenSix()
    {
        PascalsOf(PressureUnits.MeterMercury).Should().BeApproximately(
            101325.0 / 0.76, 1e-9,
            "the derivation is a division by 0.76");
    }

    /// <summary>**毫米汞柱约 133.322 Pa** / A millimetre of mercury is about 133.322 Pa.</summary>
    /// <remarks>
    /// **这条钉的是基线自己的注释**——**而照抄基线的公式会得到 77.007，与它自己的注释差 42%。**
    ///
    /// **This pins the baseline's own comment** — **and copying its formula yields 77.007, 42% off that comment.**
    /// </remarks>
    [Fact]
    public void TheMillimeterOfMercury_ShouldBeAboutOneThirtyThreePascals()
    {
        PascalsOf(PressureUnits.MillimeterOfMercury).Should().BeApproximately(
            133.322, 0.001,
            "the baseline comment states about 133.322 Pa, and that is the value clinical blood pressure uses");
    }

    /// <summary>**毫米汞柱是米汞柱的千分之一（恒等式）** / mmHg is a thousandth of mHg, identically.</summary>
    [Fact]
    public void TheMillimeterOfMercury_ShouldBeAThousandthOfTheMeter()
    {
        (PascalsOf(PressureUnits.MillimeterOfMercury) / PascalsOf(PressureUnits.MeterMercury)).Should().Be(
            0.001,
            "mmHg is derived from mHg, so the relation holds exactly rather than by rounding");
    }

    /// <summary>**英寸汞柱约 3386.39 Pa** / An inch of mercury is about 3386.39 Pa.</summary>
    /// <remarks>
    /// **气象报文里的 `inHg` 就是这个值。** **而它必须小于米汞柱**——
    /// **除以"每米多少英寸"（39.37）得到，方向与 mHg 的除法一致。**
    ///
    /// **This is the `inHg` of weather reports.** **And it must be below the metre of mercury** —
    /// **obtained by dividing by the inches per metre (39.37), the same direction as mHg.**
    /// </remarks>
    [Fact]
    public void TheInchOfMercury_ShouldBeAboutThirtyThreeEightySixPascals()
    {
        PascalsOf(PressureUnits.InchOfMercury).Should().BeApproximately(3386.389, 0.01,
            "the conventional value weather reports quote");
        PascalsOf(PressureUnits.InchOfMercury).Should().BeLessThan(
            PascalsOf(PressureUnits.MeterMercury),
            "an inch is smaller than a metre, so its mercury column is lighter");
    }

    /// <summary>**毫巴是 100 Pa，即巴的千分之一** / A millibar is 100 Pa, a thousandth of a bar.</summary>
    [Fact]
    public void TheMillibar_ShouldBeAHundredPascals()
    {
        PascalsOf(PressureUnits.Millibar).Should().Be(100.0);
        (PascalsOf(PressureUnits.Bar) / PascalsOf(PressureUnits.Millibar)).Should().Be(1000.0);
    }

    /// <summary>**百帕是 100 Pa** / A hectopascal is 100 Pa.</summary>
    /// <remarks>
    /// **毫巴与百帕数值相同**——**两个名字同一个大小，这正是气象领域两套说法并存的原因，
    /// 而"它们相等"是一条恒等式，写错任一个都会破。**
    ///
    /// **The millibar and the hectopascal are numerically identical** — **which is why meteorology carries both names,
    /// and "they are equal" is an identity that breaks if either is wrong.**
    /// </remarks>
    [Fact]
    public void TheHectopascal_ShouldEqualTheMillibar()
    {
        PascalsOf(PressureUnits.Hectopascal).Should().Be(100.0);
        PascalsOf(PressureUnits.Hectopascal).Should().Be(PascalsOf(PressureUnits.Millibar));
    }

    /// <summary>**标准大气压与 Atmosphere 是同一个值** / Standard atmospheric pressure equals Atmosphere.</summary>
    [Fact]
    public void TheStandardAtmosphere_ShouldMatchTheExistingAtmosphere()
    {
        PascalsOf(PressureUnits.StandardAtmosphericPressure).Should().Be(101325.0);
        PascalsOf(PressureUnits.StandardAtmosphericPressure).Should().Be(
            PascalsOf(PressureUnits.Atmosphere),
            "the two names denote the same pressure, and they must not drift apart");
    }

    /// <summary>**单位清单与基线一致（加上本仓原有的两个）** / The unit list covers the baseline plus two.</summary>
    [Fact]
    public void TheUnitList_ShouldCoverTheBaseline()
    {
        string[] declared = typeof(PressureUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().Contain(new[] {
            "Pascal", "Hectopascal", "Kilopascal", "Megapascal",
            "StandardAtmosphericPressure", "MeterMercury", "MillimeterOfMercury", "InchOfMercury",
            "Bar", "Millibar",
        }, "every name in the baseline has to exist");

        declared.Should().Contain(new[] { "Gigapascal", "Atmosphere" },
            "the two this repo already had are kept, not dropped in favour of the baseline names");
    }

    /// <summary>取一个单位相对帕斯卡的换算因子 / Read a unit's factor against the pascal.</summary>
    private static double PascalsOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
