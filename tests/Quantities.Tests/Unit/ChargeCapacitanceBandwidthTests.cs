#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// 法拉在电荷文件里，但它是电容 / The farad sits in the charge file while being capacitance.
/// </summary>
/// <remarks>
/// 补 `ElectricCharge`(6) / `Bandwidth`(5) / `Frequency.CyclePerHour`(1)，
/// 并新建 `CapacitanceUnits`(5)。
///
/// **法拉系列写在 里，却声明 `Capacitance` 量纲**——
/// **跟着文件名会把它建成电荷单位，跟着声明的量纲才对。**
///
/// Adds and creates `CapacitanceUnits` (5).
///
/// **This implementation writes the farad family while declaring `Capacitance`** —
/// **following the file name builds charge units; following the declared quantity is right.**
/// </remarks>
public class ChargeCapacitanceBandwidthTests
{
    /// <summary>**法拉的量纲是电容，不是电荷** / The farad is capacitance, not charge.</summary>
    /// <remarks>
    /// **这条判据钉的正是"文件名与量纲不一致时该听谁的"。**
    /// **把法拉放进 `ElectricChargeUnits` 并沿用它的 `DerivedUnit`，量纲会变成电荷，
    /// 而"1 F = 1 C"这样的比较仍会通过**——**因为数值恰好都是 1。**
    ///
    /// **This pins exactly "when the file name and the declared quantity disagree, which wins".**
    /// **Putting the farad in `ElectricChargeUnits` and reusing its `DerivedUnit` makes the quantity charge,**
    /// **while a comparison like "1 F = 1 C" still passes** — **because the numbers happen to both be 1.**
    /// </remarks>
    [Fact]
    public void TheFarad_ShouldHaveTheCapacitanceQuantity()
    {
        CapacitanceUnits.Farad.Quantity.Should().Be(Dimensions.Capacitance,
            "a farad is a coulomb per volt, not a coulomb");

        // 用 Be（值相等）而不是 BeSameAs：**`Coulomb.Divide(Volt)` 算出的量纲对象与
        // `Dimensions.Capacitance` 不是同一个实例，但量纲向量相同。**
        // **BeSameAs 会因此变红——而那是判据过严，不是实现错。**
        //
        // Be (value equality) rather than BeSameAs: **the quantity `Coulomb.Divide(Volt)` computes is not the
        // same instance as `Dimensions.Capacitance`, while the dimension vector matches.**
 // **BeSameAs goes red on that, and the criterion is too strict rather than the implementation wrong.**
        ElectricChargeUnits.Coulomb.Quantity.Should().Be(Dimensions.ElectricCharge,
            "the coulomb stays a charge unit");

        CapacitanceUnits.Farad.Quantity.Should().NotBe(
            ElectricChargeUnits.Coulomb.Quantity,
            "capacitance and charge must not collapse into one quantity");
    }

    /// <summary>**法拉在数值上是库仑除以伏特** / The farad is numerically a coulomb over a volt.</summary>
    [Fact]
    public void TheFarad_ShouldEqualACoulombPerVolt()
    {
        (FactorOf(CapacitanceUnits.Farad) / FactorOf(ElectricChargeUnits.Coulomb)).Should().Be(
            1.0,
            "one farad is one coulomb per volt, and with a one-volt base the ratio is unity");
    }

    /// <summary>**电容阶梯每级 1000 倍，直到皮法** / The capacitance ladder steps by a thousand down to picofarad.</summary>
    /// <remarks>
    /// **pF 与 nF 是电容最常用的量级**（射频与寄生电容几乎全在 pF），
    /// **所以 nano 与 pico 之间差一个数量级是最贵的错误。**
    ///
    /// **pF and nF are the common scales in capacitance** (RF and stray capacitance live almost entirely in pF),
    /// **so a slip between nano and pico is the most expensive one.**
    /// </remarks>
    [Fact]
    public void TheCapacitanceLadder_ShouldStepByAThousand()
    {
        PhysicalUnit[] ladder = {
            CapacitanceUnits.Farad,
            CapacitanceUnits.Millifarad,
            CapacitanceUnits.Microfarad,
            CapacitanceUnits.Nanofarad,
            CapacitanceUnits.Picofarad,
        };

        for (int level = 1; level < ladder.Length; ++level) {
            (FactorOf(ladder[level - 1]) / FactorOf(ladder[level])).Should().BeApproximately(
                1000.0, 1e-9,
                $"capacitance level {level} should be a thousandth of level {level - 1}");
        }
    }

    /// <summary>**安秒与库仑数值相同，但不是同一个对象** / Ampere-second equals the coulomb but is its own unit.</summary>
    /// <remarks>
    /// **这里必须用比值而不是 `BeSameAs`**——**因为它要有自己的名字。
    /// 若做成别名，按名字查 A·s 会查到 "coulomb"，符号也就跟着错了。**
    ///
    /// **A ratio here rather than `BeSameAs`** — **because it needs its own name. As an alias, looking up A·s would
    /// return "coulomb" and the symbol would be wrong with it.**
    /// </remarks>
    [Fact]
    public void TheAmpereSecond_ShouldEqualTheCoulombUnderItsOwnName()
    {
        (FactorOf(ElectricChargeUnits.AmpereSecond) / FactorOf(ElectricChargeUnits.Coulomb)).Should().Be(1.0);
        ElectricChargeUnits.AmpereSecond.Name.Should().NotBe(ElectricChargeUnits.Coulomb.Name,
            "a caller looking up A*s must not be handed the coulomb");
    }

    /// <summary>**安时阶梯与库仑的换算** / The ampere-hour ladder against the coulomb.</summary>
    [Fact]
    public void TheAmpereHourLadder_ShouldConvertToCoulombs()
    {
        (FactorOf(ElectricChargeUnits.AmpereHour) / FactorOf(ElectricChargeUnits.Coulomb)).Should().Be(3600.0);
        (FactorOf(ElectricChargeUnits.MilliampereHour) / FactorOf(ElectricChargeUnits.Coulomb)).Should().BeApproximately(3.6, 1e-12);
        (FactorOf(ElectricChargeUnits.MicroampereHour) / FactorOf(ElectricChargeUnits.Coulomb)).Should().BeApproximately(0.0036, 1e-15);
        (FactorOf(ElectricChargeUnits.KiloampereHour) / FactorOf(ElectricChargeUnits.Coulomb)).Should().Be(3600000.0);
    }

    /// <summary>**带宽的 KB/s 是 1000 B/s，不是 1024** / KB/s is a thousand bytes per second.</summary>
    /// <remarks>
    /// **这与那个 KB/KiB 错误同源**：**如果 `InformationUnits.Kilobyte` 又被写成 1024，
    /// 这里会跟着错十倍进位的偏差**——**而带宽单位是从它派生的，所以一条判据同时护住两处。**
    ///
    /// **The same root as the KB/KiB defect fixed**: **if `InformationUnits.Kilobyte` were 1024 again,
    /// this would inherit a decade-shaped error** — **bandwidth derives from it, so one criterion guards both.**
    /// </remarks>
    [Fact]
    public void TheKilobytePerSecond_ShouldBeOneThousandBytesPerSecond()
    {
        (FactorOf(BandwidthUnits.KilobytesPerSecond) / FactorOf(InformationUnits.Byte)).Should().Be(
            1000.0,
            "KB/s is 1000 B/s, and the binary variant would be KiB/s");
    }

    /// <summary>**带宽阶梯每级 1000 倍** / The bandwidth ladder steps by a thousand.</summary>
    [Fact]
    public void TheBandwidthLadder_ShouldStepByAThousand()
    {
        (FactorOf(BandwidthUnits.KilobitsPerSecond) / FactorOf(BandwidthUnits.BitsPerSecond)).Should().Be(1000.0);
        (FactorOf(BandwidthUnits.MegabitsPerSecond) / FactorOf(BandwidthUnits.KilobitsPerSecond)).Should().Be(1000.0);
        (FactorOf(BandwidthUnits.GigabitsPerSecond) / FactorOf(BandwidthUnits.MegabitsPerSecond)).Should().Be(1000.0);
        (FactorOf(BandwidthUnits.TerabitsPerSecond) / FactorOf(BandwidthUnits.GigabitsPerSecond)).Should().Be(1000.0);
        (FactorOf(BandwidthUnits.PetabitsPerSecond) / FactorOf(BandwidthUnits.TerabitsPerSecond)).Should().Be(1000.0);
        (FactorOf(BandwidthUnits.ExabitsPerSecond) / FactorOf(BandwidthUnits.PetabitsPerSecond)).Should().Be(1000.0);

        (FactorOf(BandwidthUnits.MegabytesPerSecond) / FactorOf(BandwidthUnits.KilobytesPerSecond)).Should().Be(1000.0);
    }

    /// <summary>**每小时一次比每秒一次慢 3600 倍** / Once per hour is 3600 times slower than once per second.</summary>
    /// <remarks>
    /// **方向必须是除**：**`Hertz * 3600` 会让"每小时一次"变成 3600 Hz，
    /// 而那条判据仍会通过"是个频率"这种弱检查。**
    ///
    /// **The direction has to be a division**: **`Hertz * 3600` would make "once per hour" 3600 Hz,**
    /// **and a weak check like "it is a frequency" would still pass.**
    /// </remarks>
    [Fact]
    public void TheCyclePerHour_ShouldBeSlowerThanTheHertz()
    {
        (FactorOf(FrequencyUnits.CyclePerHour) / FactorOf(FrequencyUnits.Hertz)).Should().BeApproximately(
            1.0 / 3600.0, 1e-15,
            "one cycle per hour is one three-thousand-six-hundredth of a hertz");
    }

    /// <summary>取一个单位相对其基准的换算因子 / Read a unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
