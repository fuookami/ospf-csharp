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
/// **剩下七个量纲一次补齐** / The last seven dimensions, in one batch.
/// </summary>
/// <remarks>
/// 补齐 `quantities` 最后七个量纲，共 **19 个单位**：
/// `SolidAngle`(2) / `Stress`(7) / `Wavenumber`(1) / `CatalyticActivity`(4) /
/// `LuminousIntensity`(1) / `AngularVelocity`(2) / `AngularAcceleration`(2)，
/// 并补上 `Stress` 所依赖的四个面积单位（`SquareInch`/`SquareFoot`/`SquareYard`/`SquareMile`）。
///
/// **这里最值得钉的是三个"不是十进倍数"的换算**：
/// **`SquareDegree` 是 `(π/180)²`**、**`EnzymeUnit` 是 `1/60000000`**、**`DegreePerSecond` 依赖 `π/180`**——
/// **它们都不是"换个前缀"就能写对的，而写错了不会报错。**
///
/// Completes the last seven `quantities` dimensions with **19 units**:
/// `SolidAngle`(2) / `Stress`(7) / `Wavenumber`(1) / `CatalyticActivity`(4) /
/// `LuminousIntensity`(1) / `AngularVelocity`(2) / `AngularAcceleration`(2),
/// plus the four area units `Stress` depends on (`SquareInch`/`SquareFoot`/`SquareYard`/`SquareMile`).
///
/// **The three conversions that are not decimal multiples are the ones worth pinning here**:
/// **`SquareDegree` is `(π/180)²`**, **`EnzymeUnit` is `1/60000000`**, **`DegreePerSecond` rides on `π/180`** —
/// **none of them is right by swapping a prefix, and none of them fails loudly when wrong.**
/// </remarks>
public class RemainingDimensionUnitsTests
{
    /// <summary>**七个量纲的单位都齐** / All seven dimensions have their baseline units.</summary>
    [Theory]
    [InlineData(typeof(SolidAngleUnits), "Steradian,SquareDegree")]
    [InlineData(typeof(StressUnits),
        "PascalStress,KilopascalStress,MegapascalStress,PoundForcePerSquareInch,PoundForcePerSquareFoot,"
        + "KilogramForcePerSquareCentimeter,KilogramForcePerSquareMeter")]
    [InlineData(typeof(WavenumberUnits), "ReciprocalMeter")]
    [InlineData(typeof(CatalyticActivityUnits), "Katal,Millikatal,Microkatal,EnzymeUnit")]
    [InlineData(typeof(LuminousIntensityUnits), "Candela")]
    [InlineData(typeof(AngularVelocityUnits), "RadianPerSecond,DegreePerSecond")]
    [InlineData(typeof(AngularAccelerationUnits), "RadianPerSecondSquared,DegreePerSecondSquared")]
    public void TheUnits_ShouldCoverTheBaseline(Type unitClass, string expected)
    {
        string[] declared = unitClass
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(expected.Split(','),
            $"{unitClass.Name} has to match the baseline's unit list exactly");
    }

    /// <summary>**平方度是 `(π/180)²`** / A square degree is `(π/180)²`.</summary>
    /// <remarks>
    /// **`0.00030461741978670857` 这个数不是"大约 0.0003"**：它是**一度转弧度再平方**。
    /// **写成 0.0003 会让全天区覆盖从 41252.96 平方度变成 41887.9，差 1.5%。**
    ///
    /// **`0.00030461741978670857` is not "about 0.0003"**: it is **one degree in radians, squared**.
    /// **Writing 0.0003 turns the whole sky from 41252.96 square degrees into 41887.9, off by 1.5%.**
    /// </remarks>
    [Fact]
    public void TheSquareDegree_ShouldBeTheSquaredRadianOfOneDegree()
    {
        double expected = (System.Math.PI / 180.0) * (System.Math.PI / 180.0);

        FactorOf(SolidAngleUnits.SquareDegree).Should().BeApproximately(expected, expected * 1e-12,
            "a square degree is one degree converted to radians, squared");
    }

    /// <summary>**酶单位是六千万分之一开特** / An enzyme unit is one sixty-millionth of a katal.</summary>
    /// <remarks>
    /// **1 U = 1 µmol/min，而 1 katal = 1 mol/s**——
    /// **所以换算里同时有 10⁻⁶ 与 60，合起来是 6×10⁷。**
    /// **把两个换算漏掉任何一个，答案会差 60 倍或 100 万倍，而两者看起来都"像那么回事"。**
    ///
    /// **1 U = 1 µmol/min while 1 katal = 1 mol/s** —
    /// **so the conversion carries both 10⁻⁶ and 60, together 6×10⁷.**
    /// **Drop either and the answer is off by 60 or by a million, and both look plausible.**
    /// </remarks>
    [Fact]
    public void TheEnzymeUnit_ShouldBeOneSixtyMillionthOfAKatal()
    {
        double katal = FactorOf(CatalyticActivityUnits.Katal);
        double enzymeUnit = FactorOf(CatalyticActivityUnits.EnzymeUnit);

        (katal / enzymeUnit).Should().BeApproximately(60000000.0, 1.0,
            "one enzyme unit is one micromole per minute, and a katal is one mole per second");
    }

    /// <summary>**度每秒不是弧度每秒的 1/360** / Degrees per second is not a 360th of radians per second.</summary>
    /// <remarks>
    /// **`Degree` 是 `π/180` 弧度**，**所以度每秒与弧度每秒的比值是 `π/180 ≈ 0.01745`，不是 `1/360 ≈ 0.00278`。**
    /// **两者差 6.28 倍，而 6.28 恰好是 `2π`——把它当成"一个圆周 360 度"的直觉产物就会错。**
    ///
    /// **`Degree` is `π/180` radians**, **so degrees per second over radians per second is `π/180 ≈ 0.01745`, not `1/360 ≈ 0.00278`.**
    /// **They differ by 6.28, which is exactly `2π` — the error you get from reasoning "a circle is 360 degrees".**
    /// </remarks>
    [Fact]
    public void TheDegreePerSecond_ShouldUseTheRadianConversion()
    {
        double radians = FactorOf(AngularVelocityUnits.RadianPerSecond);
        double degrees = FactorOf(AngularVelocityUnits.DegreePerSecond);

        (degrees / radians).Should().BeApproximately(System.Math.PI / 180.0, 1e-12,
            "one degree is pi over 180 radians, not a 360th of a radian");
    }

    /// <summary>**坎德拉就是 SI 基本单位本身** / The candela is the SI base unit itself.</summary>
    /// <remarks>
    /// **不是"另一个等于坎德拉的单位"，而是同一个值**——
    /// **否则"基本单位的坎德拉"与"发光强度的坎德拉"会不相等，而它们在 SI 里必须是同一个。**
    ///
    /// **Not "another unit equal to the candela" but the same value** —
    /// **otherwise "the base unit candela" and "the luminous-intensity candela" would differ, while in SI they must be one.**
    /// </remarks>
    [Fact]
    public void TheCandela_ShouldBeTheBaseUnitItself()
    {
        LuminousIntensityUnits.Candela.Should().BeSameAs(SIBaseUnits.Candela,
            "there is one candela in SI, and this is an entry point to it rather than a copy");
    }

    /// <summary>**psi 用的是磅力，不是磅** / psi uses pound force, not pound mass.</summary>
    /// <remarks>
    /// **`PoundForce` 是 4.4482216152605 N**，**而 `Pound`（质量）是 0.45359237 kg**——
    /// **两者数值接近、含义完全不同，用错了 psi 会差 9.8 倍。**
    ///
    /// **`PoundForce` is 4.4482216152605 N**, **while `Pound` (mass) is 0.45359237 kg** —
    /// **close in number, entirely different in meaning, and using the wrong one makes psi off by 9.8×.**
    /// </remarks>
    [Fact]
    public void ThePsi_ShouldUsePoundForce()
    {
        PhysicalUnit expected = ForceUnits.PoundForce.Divide(AreaUnits.SquareInch);

        FactorOf(StressUnits.PoundForcePerSquareInch).Should()
            .BeApproximately(FactorOf(expected), FactorOf(expected) * 1e-12,
                "psi is pounds force per square inch, and pound mass would be off by 9.8");
    }

    /// <summary>**波数是米的倒数** / A wavenumber is the reciprocal of the metre.</summary>
    [Fact]
    public void TheReciprocalMeter_ShouldBeTheReciprocalOfTheMetre()
    {
        FactorOf(WavenumberUnits.ReciprocalMeter).Should().BeApproximately(1.0, 1e-12,
            "a reciprocal metre is one per metre");
    }

    /// <summary>取一个单位相对自身基准的换算因子 / Read one unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit)
    {
        UnitConversionRule.Linear linear = unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject;
        return linear.Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
    }
}
