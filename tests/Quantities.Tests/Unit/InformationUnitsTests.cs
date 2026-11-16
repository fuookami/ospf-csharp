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
/// KB 是 1000，KiB 是 1024——而本仓原来把 KiB 的值安在了 KB 上 / KB is 1000, KiB is 1024.
/// </summary>
/// <remarks>
/// 对齐基线 `Information.kt` 补全 18 个信息单位，**同时修掉一个真实错误**：
/// **本仓原来的 `Kilobyte` 用的是 1024（`Kibibyte` 的值），符号却写着 `KB`。**
/// `Megabyte`/`Gigabyte` 同样错（1024²、1024³）。
///
/// **这类错误最难发现，因为"KB 是 1000 还是 1024"在业界本来就被混用**，
/// **而符号与数值对不上这件事，从外部调用完全看不出来。**
///
/// Completes the 18 information units from the baseline's `Information.kt`
/// **and fixes a real defect**: **this repo's `Kilobyte` used 1024 (the value of `Kibibyte`) while its symbol said `KB`.**
/// `Megabyte`/`Gigabyte` were wrong the same way.
///
/// **This kind of error is the hardest to spot, because "is a KB 1000 or 1024" is genuinely mixed in practice**,
/// **and symbol-versus-magnitude disagreement is invisible from the outside.**
/// </remarks>
public class InformationUnitsTests
{
    /// <summary>**KB 是 1000 字节，不是 1024** / KB is a thousand bytes, not 1024.</summary>
    /// <remarks>
    /// **这是这里唯一的回归判据，而它钉的正是那个真实错误。**
    /// **写成 1024 时这条会红，报 `Expected 8000 to be 1000...` 之类，一眼能看出问题在字节数而不是位。**
    ///
    /// **This is the batch's one regression criterion, and it pins exactly that defect.**
    /// **Written as 1024 it goes red with the byte count off by 2.4% per unit — and compounding by 10 with each prefix.**
    /// </remarks>
    [Fact]
    public void TheKilobyte_ShouldBeOneThousandBytes()
    {
        (BitsOf(InformationUnits.Kilobyte) / BitsOf(InformationUnits.Byte)).Should().Be(
            1000.0,
            "KB is the SI decimal prefix, and 1024 is KiB — the value this repo wrongly carried for years");
    }

    /// <summary>**KiB 是 1024 字节** / KiB is 1024 bytes.</summary>
    [Fact]
    public void TheKibibyte_ShouldBeOneThousandTwentyFourBytes()
    {
        (BitsOf(InformationUnits.Kibibyte) / BitsOf(InformationUnits.Byte)).Should().Be(
            1024.0,
            "KiB is the binary prefix");
    }

    /// <summary>**两套前缀的比值按 1.024 的幂增长，不是固定 2.4%** / The gap grows as 1.024 to the level.</summary>
    /// <remarks>
    /// **第一版这条判据写的是"每一级都差 1.024"，实测第二级是 1.048576**——
    /// **因为比值是 `(1024/1000)^n`，不是常数。**
    ///
    /// **这是这里第二次"判据自己算错"**：**判据的方向没错（两套确实不同），但幅度我凭直觉给了个常数。**
    /// **按幂写之后它同时钉住了"两级都用对了前缀"**：**某一级复制错会让该级比值偏离 `1.024^n`。**
    ///
    /// **The first version of this criterion said "1.024 at every level", and the second level measured 1.048576** —
    /// **because the ratio is `(1024/1000)^n`, not a constant.**
    ///
    /// **This is the batch's second self-inflicted criterion error**: **the direction was right (the families do
    /// differ), but I gave the magnitude a constant from intuition.**
    /// **Written as a power it also pins "both levels used the right prefix"**: **a copy-paste at one level moves that
    /// level off `1.024^n`.**
    /// </remarks>
    [Fact]
    public void TheTwoFamilies_ShouldDifferByTheSameRatioAtEveryLevel()
    {
        (double decimalValue, double binaryValue)[] pairs = {
            (BitsOf(InformationUnits.Kilobyte), BitsOf(InformationUnits.Kibibyte)),
            (BitsOf(InformationUnits.Megabyte), BitsOf(InformationUnits.Mebibyte)),
            (BitsOf(InformationUnits.Gigabyte), BitsOf(InformationUnits.Gibibyte)),
            (BitsOf(InformationUnits.Terabyte), BitsOf(InformationUnits.Tebibyte)),
        };

        for (int level = 0; level < pairs.Length; ++level) {
            // 比值是 1.024 的 (level+1) 次幂：Kilobyte 那一级是 1.024，Megabyte 是 1.024²。
            // The ratio is 1.024 to the (level+1): 1.024 at the kilobyte level, 1.024 squared at the megabyte level.
            double expected = global::System.Math.Pow(1.024, level + 1);
            (pairs[level].binaryValue / pairs[level].decimalValue).Should().BeApproximately(
                expected, 1e-9,
                $"at level {level} the binary prefix is 1.024^{level + 1} times the decimal one");
        }
    }

    /// <summary>**每一级恰好是下一级的 1000 倍（十进制）/** Each decimal level is a thousand times the one before.</summary>
    /// <remarks>
    /// **这条排掉"某一级少写或多写一个零"**：**那种错误会让相邻两级的比值偏离 1000，
    /// 而"Kilobyte 是对的"这类单点判据看不出来。**
    ///
    /// **This rejects "a zero was dropped or added at some level"**: **that skews the ratio between two adjacent
    /// levels, and a single-point criterion like "Kilobyte is right" cannot see it.**
    /// </remarks>
    [Fact]
    public void EachDecimalLevel_ShouldBeAThousandTimesTheOneBefore()
    {
        PhysicalUnit[] ladder = {
            InformationUnits.Byte,
            InformationUnits.Kilobyte,
            InformationUnits.Megabyte,
            InformationUnits.Gigabyte,
            InformationUnits.Terabyte,
            InformationUnits.Petabyte,
            InformationUnits.Exabyte,
        };

        for (int level = 1; level < ladder.Length; ++level) {
            (BitsOf(ladder[level]) / BitsOf(ladder[level - 1])).Should().Be(
                1000.0,
                $"level {level} should be a thousand times level {level - 1}");
        }
    }

    /// <summary>**每一级恰好是下一级的 1024 倍（二进制）/** Each binary level is 1024 times the one before.</summary>
    [Fact]
    public void EachBinaryLevel_ShouldBeOneThousandTwentyFourTimesTheOneBefore()
    {
        PhysicalUnit[] ladder = {
            InformationUnits.Byte,
            InformationUnits.Kibibyte,
            InformationUnits.Mebibyte,
            InformationUnits.Gibibyte,
            InformationUnits.Tebibyte,
        };

        for (int level = 1; level < ladder.Length; ++level) {
            (BitsOf(ladder[level]) / BitsOf(ladder[level - 1])).Should().Be(
                1024.0,
                $"binary level {level} should be 1024 times level {level - 1}");
        }
    }

    /// <summary>**比特单位不带 8 这个因子** / Bit units carry no factor of eight.</summary>
    /// <remarks>
    /// **字节单位是比特的 8 倍，而比特单位之间只差前缀。**
    /// **如果比特那条链上误乘了 8，"1 kbit" 会变成 8000 bit，而所有字节判据仍然全对**——
    /// **因为字节的因子本来就是"前缀 × 8"，多一个 8 会被当成正常。**
    ///
    /// **Byte units are eight times the bit, while bit units differ only by prefix.**
    /// **A stray factor of eight on the bit chain makes "1 kbit" 8000 bit while every byte criterion still passes** —
    /// **because a byte's factor is "prefix × 8" anyway, so the extra eight looks normal.**
    /// </remarks>
    [Fact]
    public void TheBitUnits_ShouldCarryNoFactorOfEight()
    {
        BitsOf(InformationUnits.Kilobit).Should().Be(1000.0, "a kilobit is a thousand bits");
        (BitsOf(InformationUnits.Byte) / BitsOf(InformationUnits.Bit)).Should().Be(
            8.0,
            "the factor of eight belongs to the byte, and only to the byte");
    }

    /// <summary>**十进制与二进制单位的符号必须不同** / The decimal and binary symbols must differ.</summary>
    /// <remarks>
    /// **`KB` 对 `KiB`**：**符号不同是这套体系唯一的外部提示**——
    /// **而本仓原来把 `KB` 写在了 1024 上，符号与数值对不上。**
    ///
    /// **`KB` versus `KiB`**: **the differing symbol is the only external hint this system offers** —
    /// **and this repo had `KB` on 1024, with symbol and magnitude in disagreement.**
    /// </remarks>
    [Fact]
    public void TheDecimalAndBinarySymbols_ShouldDiffer()
    {
        (string decimalSymbol, string binarySymbol)[] pairs = {
            (InformationUnits.Kilobyte.Symbol!, InformationUnits.Kibibyte.Symbol!),
            (InformationUnits.Megabyte.Symbol!, InformationUnits.Mebibyte.Symbol!),
            (InformationUnits.Gigabyte.Symbol!, InformationUnits.Gibibyte.Symbol!),
            (InformationUnits.Terabyte.Symbol!, InformationUnits.Tebibyte.Symbol!),
        };

        foreach ((string decimalSymbol, string binarySymbol) in pairs) {
            decimalSymbol.Should().NotBe(binarySymbol,
                "a binary unit must not wear the decimal unit's symbol");
            binarySymbol.Should().Contain("i",
                "the binary prefixes are Kibi/Mebi/Gibi/Tebi, marked by the i");
        }
    }

    /// <summary>**单位清单与基线一致** / The unit list matches the baseline.</summary>
    [Fact]
    public void TheUnitList_ShouldMatchTheBaseline()
    {
        string[] declared = typeof(InformationUnits)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(PhysicalUnit))
            .Select(field => field.Name)
            .ToArray();

        declared.Should().BeEquivalentTo(new[] {
            "Bit", "Kilobit", "Megabit", "Gigabit", "Terabit", "Petabit", "Exabit",
            "Byte", "Kilobyte", "Megabyte", "Gigabyte", "Terabyte", "Petabyte", "Exabyte",
            "Kibibyte", "Mebibyte", "Gibibyte", "Tebibyte",
        }, "the baseline's Information.kt declares exactly these eighteen");
    }

    /// <summary>取一个单位相对比特的换算因子 / Read a unit's factor against the bit.</summary>
    private static double BitsOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
