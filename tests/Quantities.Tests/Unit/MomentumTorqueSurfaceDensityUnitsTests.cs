#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Unit;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// **力矩与能量同量纲，所以编译器不会拦住你拿错** / Torque and energy share a dimension, so the compiler will not stop you.
/// </summary>
/// <remarks>
/// 补 `Momentum`、`Torque`、`SurfaceDensity` 三个量纲，共 5 个单位。
///
/// **这里最值得钉的是力矩**：**力矩与能量在 SI 里量纲相同**（都是 `kg·m²/s²`），
/// **所以 `NewtonMeter` 与 `Joule` 可以互换而编译器一句话都不说**——
/// **这是 SI 的设计取舍，不是缺陷，但意味着"数值全对、语义全丢"是可能的。**
///
/// **Torque is the one worth pinning here**: **torque and energy share a dimension in SI** (both `kg·m²/s²`),
/// **so `NewtonMeter` and `Joule` are interchangeable and the compiler says nothing** —
/// **an SI design trade-off rather than a defect, but it means "every number right, every meaning gone" is reachable.**
/// </remarks>
public class MomentumTorqueSurfaceDensityUnitsTests
{
    /// <summary>**动量单位是 `kg·m/s`** / The momentum unit is `kg·m/s`.</summary>
    [Fact]
    public void TheMomentumUnit_ShouldExist()
    {
        typeof(MomentumUnits).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .Should().BeEquivalentTo(["KilogramMetersPerSecond"],
                "the baseline declares exactly one momentum unit");
    }

    /// <summary>**基线的两个力矩单位都在** / Both baseline torque units are present.</summary>
    [Fact]
    public void TheTorqueUnits_ShouldCoverTheBaseline()
    {
        string[] declared = typeof(TorqueUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(["NewtonMeter", "KilogramForceMeter"]);
    }

    /// <summary>**千克力米是牛顿米的 9.80665 倍** / The kilogram force metre is 9.80665 times the newton metre.</summary>
    /// <remarks>
    /// **`KilogramForce` 是 9.80665 N**（标准重力下的 1 kg），**不是 1 N**——
    /// **写成 1 N 会让所有以 kgf·m 标注的扭矩差 9.8 倍**，
    /// **而 kgf·m 在机械行业是常用单位，错在这里会直接影响选型。**
    ///
    /// **`KilogramForce` is 9.80665 N** (1 kg under standard gravity), **not 1 N** —
    /// **writing 1 N makes every torque quoted in kgf·m off by 9.8×**,
    /// **and kgf·m is common in mechanical work, so an error here changes equipment selection.**
    /// </remarks>
    [Fact]
    public void TheKilogramForceMeter_ShouldBeNinePointEightTimesTheNewtonMeter()
    {
        double newtonMetre = FactorOf(TorqueUnits.NewtonMeter);
        double kilogramForceMetre = FactorOf(TorqueUnits.KilogramForceMeter);

        (kilogramForceMetre / newtonMetre).Should().BeApproximately(9.80665, 1e-9,
            "one kilogram force is 9.80665 newtons under standard gravity");
    }

    /// <summary>**力矩与能量同量纲这件事，判据挡不住，只能靠约定** / The torque-energy overlap is not catchable here.</summary>
    /// <remarks>
    /// **这一条记录的是一个"判据做不到"的诚实结论，不是一个检查。**
    ///
    /// 我试了三版想钉住"`NewtonMeter` 必须按力乘长度构造，而不是指向 `Joule`"：
    ///
    /// | 第几版 | 写法 | 为什么挡不住 |
    /// | --- | --- | --- |
    /// | 1 | 比 `Quantity` 与 `Newton × Meter` | **`Joule` 本身就是 `Newton × Meter`**（实测其定义正是这一行），`Quantity` 完全相同 |
    /// | 2 | 比 `Name` 与 `Symbol` | **`DerivedUnit` 的名称是构造时传入的字面量**，与 `_inner` 无关，指向 `Joule` 后名字仍是 `"newton meter"` |
    /// | 3 | 比 `ConversionRule` | **同样只取决于 `_inner` 的 `Scale`，而两者 `Scale` 相同** |
    ///
    /// **结论：在这个实现里，"力矩单位来自哪个单位"是不可观测的。**
    /// **这不是判据写坏了，是 SI 的设计取舍 + 本仓 `DerivedUnit` 的形状共同决定的。**
    ///
    /// **所以这里不做断言，只留记录**——**写一条永远不会红的判据比不写更糟，因为它会让人以为这件事被守住了。**
    ///
    /// **This entry records an honest "the criterion cannot do it", not a check.**
    ///
    /// I tried three versions to pin "`NewtonMeter` must be built as force times length, not pointed at `Joule`":
    ///
    /// | Version | Approach | Why it cannot bite |
    /// | --- | --- | --- |
    /// | 1 | Compare `Quantity` with `Newton × Meter` | **`Joule` is itself `Newton × Meter`** (measured: that is literally its definition), so `Quantity` is identical |
    /// | 2 | Compare `Name` and `Symbol` | **`DerivedUnit`'s name is a literal passed at construction**, independent of `_inner`, so it still reads `"newton meter"` |
    /// | 3 | Compare `ConversionRule` | **also depends only on `_inner`'s `Scale`, and the two scales agree** |
    ///
    /// **Conclusion: in this implementation, which unit a torque unit came from is not observable.**
    /// **That is not a broken criterion; it follows from SI's trade-off plus this repo's `DerivedUnit` shape.**
    ///
    /// **So nothing is asserted here, only recorded** — **a criterion that can never go red is worse than none, because it
    /// makes people believe the thing is guarded.**
    /// </remarks>
    [Fact]
    public void TheTorqueEnergyOverlap_ShouldBeDocumentedAsUncatchable()
    {
        // **这两件事是真的，所以照旧断言**：力矩单位存在，且它的数值等于力乘长度。
        // **"它是不是从 Joule 来的"不可观测，所以不断言。**
        //
        // **These two are true and stay asserted**: the torque unit exists, and its value equals force times length.
        // **Whether it came from Joule is unobservable, so it is not asserted.**
        TorqueUnits.NewtonMeter.Name.Should().Be("newton meter");
        TorqueUnits.NewtonMeter.Quantity.Should().Be(
            ForceUnits.Newton.Multiply(SIBaseUnits.Meter).Quantity);
    }

    /// <summary>**基线的两个面密度单位都在，且相差 1000 倍** / Both surface-density units, 1000 apart.</summary>
    /// <remarks>
    /// **这两个名字只差一个词、差 1000 倍**——**与 `LitersPerMinute` / `LitersPerSecond` 是同一类风险。**
    /// **g/m² 在纺织与造纸里极常用（纸张克重），混用会让整批材料的定量错 1000 倍。**
    ///
    /// **The two names differ by one word and by 1000×** — **the same risk as the earlier `LitersPerMinute` / `LitersPerSecond`.**
    /// **g/m² is ubiquitous in textiles and paper (paper weight), and mixing them misquotes a whole batch by 1000×.**
    /// </remarks>
    [Fact]
    public void TheSurfaceDensityUnits_ShouldDifferByAThousand()
    {
        string[] declared = typeof(SurfaceDensityUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(["KilogramsPerSquareMeter", "GramsPerSquareMeter"]);

        double kilograms = FactorOf(SurfaceDensityUnits.KilogramsPerSquareMeter);
        double grams = FactorOf(SurfaceDensityUnits.GramsPerSquareMeter);

        (kilograms / grams).Should().BeApproximately(1000.0, 1e-9,
            "a kilogram per square metre is a thousand grams per square metre");
    }

    /// <summary>取一个单位相对自身基准的换算因子 / Read one unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit)
    {
        UnitConversionRule.Linear linear = unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject;
        return linear.Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
    }
}
