#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// 那 28 个"未出现"的名字，全是拼写差异 / The twenty-eight absent names are all spellings.
/// </summary>
/// <remarks>
/// **扫描器把"名字是否作为子串出现"当作"能力是否具备"，于是 `BitPerSecond`（基线）
/// 与 `BitsPerSecond`（本仓）被判成缺失。**
///
/// **这个数字（28）此前一直躺在清单里，看起来像 28 个待办。实际上它们是 0 个待办。**
///
/// **"一个具体数字会让陈旧缺口看起来已经被检查过"——这里就是把那个数字拆开。**
///
/// **The scanner treats "does the name appear as a substring" as "is the capability present", so the code
/// `BitPerSecond` against this repo `BitsPerSecond` reads as missing.**
///
/// **That number (28) had been sitting in the inventory looking like twenty-eight items of work. It is zero.**
///
/// **"A specific number makes a stale gap look already checked" — takes the number apart.**
/// </remarks>
public class BaselineNamingVariantsTests
{
    /// <summary>
    /// 基线名字与本仓拼写的对应表 / Baseline names and the spellings this repo uses.
    /// </summary>
    /// <remarks>
    /// **每一行都是一个需要核对的声明，而不是一个"已知缺失"。**
    /// **若某天扫描器报出第 29 个名字，本类不会发现它**——
    /// **所以这个数字怎么来的（"扫描器报 28 个未出现，逐个核对后全部是拼写差异"）必须与表一起读。**
    ///
    /// **Every row is a claim to verify, not a known gap.**
    /// **If the scanner ever reports a twenty-ninth name this class will not notice** —
    /// **so how the number arose (scanner reported 28 absent, each checked to be a spelling) has to be read with it.**
    /// </remarks>
    private static readonly (string Baseline, string Ours)[] Variants = {
        ("BitPerSecond", "BitsPerSecond"),
        ("BytePerSecond", "BytesPerSecond"),
        ("CentimeterPerSecond", "CentimetersPerSecond"),
        ("CentimeterPerSecondSquared", "CentimetersPerSecondSquared"),
        ("CubicMeterPerSecond", "CubicMetersPerSecond"),
        ("ExabitPerSecond", "ExabitsPerSecond"),
        ("FootPerSecond", "FeetPerSecond"),
        ("FootPerSecondSquared", "FeetPerSecondSquared"),
        ("GigabitPerSecond", "GigabitsPerSecond"),
        ("InchPerSecond", "InchesPerSecond"),
        ("InchPerSecondSquared", "InchesPerSecondSquared"),
        ("KilobitPerSecond", "KilobitsPerSecond"),
        ("KilobytePerSecond", "KilobytesPerSecond"),
        ("KilogramMeterPerSecond", "KilogramMetersPerSecond"),
        ("KilogramPerCubicCentimeter", "KilogramsPerCubicCentimeter"),
        ("KilogramPerLiter", "KilogramsPerLiter"),
        ("KilometerPerHour", "KilometersPerHour"),
        ("KilometerPerSecond", "KilometersPerSecond"),
        ("KilometerPerSecondSquared", "KilometersPerSecondSquared"),
        ("LiterPerMinute", "LitersPerMinute"),
        ("LiterPerSecond", "LitersPerSecond"),
        ("MegabitPerSecond", "MegabitsPerSecond"),
        ("MegabytePerSecond", "MegabytesPerSecond"),
        ("MeterPerSecondSquared", "MetersPerSecondSquared"),
        ("MilePerHour", "MilesPerHour"),
        ("MillimeterMercury", "MillimeterOfMercury"),
        ("PetabitPerSecond", "PetabitsPerSecond"),
        ("TerabitPerSecond", "TerabitsPerSecond"),
    };

    /// <summary>**每一个对照名的目标单位都必须真的存在** / Every counterpart really exists.</summary>
    /// <remarks>
    /// **这条判据在有人删掉或改名一个单位时变红**——**而那正是这张表唯一的用处：
    /// 它把"28 个未出现"从一句结论变成了 28 个可核对的声明。**
    ///
    /// **Goes red when a unit is deleted or renamed** — **and that is the table's only job: turning "28 absent" from
    /// a conclusion into twenty-eight checkable claims.**
    /// </remarks>
    [Fact]
    public void EveryCounterpart_ShouldExistAsAUnit()
    {
        HashSet<string> units = DeclaredUnitNames();

        foreach ((string baseline, string ours) in Variants) {
            units.Should().Contain(ours,
                $"the baseline's {baseline} is this repo's {ours}, so that unit has to exist");
        }
    }

    /// <summary>**没有两个基线名字指向同一个单位** / No two baseline names share one counterpart.</summary>
    /// <remarks>
    /// **如果 `FootPerSecond` 与 `MilePerHour` 都指向 `MilesPerHour`，上一条判据仍然全绿**——
    /// **而那就是"一个单位冒充两个概念"，是这张表能犯的最严重的错。**
    ///
    /// **If `FootPerSecond` and `MilePerHour` both pointed at `MilesPerHour`, the criterion above would stay green** —
    /// **and that is one unit impersonating two concepts, the worst mistake this table can make.**
    /// </remarks>
    [Fact]
    public void NoTwoBaselineNames_ShouldShareACounterpart()
    {
        string[] duplicated = Variants
            .GroupBy(pair => pair.Ours)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        duplicated.Should().BeEmpty("one unit must not stand in for two different baseline concepts");
    }

    /// <summary>**28 这个数字必须与表长一致** / The number 28 matches the table.</summary>
    /// <remarks>
    /// **这条挡的是"表被改了一半"**：**删掉一行不会让任何单位消失，所以前两条都不会红。**
    /// **而清单里的数字仍然是 28，于是它继续看起来是完整的。**
    ///
    /// **This blocks a half-edited table**: **deleting a row removes no unit, so neither criterion above goes red.**
    /// **The inventory keeps saying 28 and the table goes on looking complete.**
    /// </remarks>
    [Fact]
    public void TheTable_ShouldHoldAllTwentyEightVariants()
    {
        Variants.Should().HaveCount(28,
            "the scanner reported twenty-eight absent names, and each one is accounted for here");
        Variants.Select(pair => pair.Baseline).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// **归一化之后两个名字必须相等** / Normalised, the two names must be equal.
    /// </summary>
    /// <remarks>
    /// **这条是那张表真正的判据**：**归一化规则（去掉 `Of`、按 CamelCase 分词、每词去尾部 `s`、
    /// 并把 `feet`/`inches` 两个不规则复数折回）正是这 28 对被挑出来的原因。**
    ///
    /// **它挡的是"错配"**：**把 `FootPerSecond` 指向 `MilesPerHour`，前三条判据全绿；
    /// 因为它们只查"这个名字存在"与"没有重复指向"。归一化之后两者不相等，这条才红。**
    ///
    /// **This is the table's real criterion**: **the normalisation (drop `Of`, split on CamelCase, strip a trailing
    /// `s` per word, and fold the two irregular plurals `feet`/`inches` back) is exactly what produced these
    /// twenty-eight pairs.**
    ///
    /// **It blocks mismapping**: **pointing `FootPerSecond` at `MilesPerHour` leaves the first three criteria green,
    /// because they only ask "the name exists" and "no duplicate". Normalised, the two differ, and this goes red.**
    /// </remarks>
    [Fact]
    public void TheNormalisedNames_ShouldBeEqual()
    {
        foreach ((string baseline, string ours) in Variants) {
            Normalise(ours).Should().Be(Normalise(baseline),
                $"{ours} has to be the same words as {baseline} once plural, irregular and Of are folded");
        }
    }

    /// <summary>按文档化的规则归一化一个名字 / Normalise a name by the documented rule.</summary>
    private static string Normalise(string name) {
        System.Text.RegularExpressions.MatchCollection words =
            System.Text.RegularExpressions.Regex.Matches(name, "[A-Z][a-z0-9]*|[a-z0-9]+");
        var parts = new List<string>();

        foreach (System.Text.RegularExpressions.Match match in words) {
            string word = match.Value.ToLowerInvariant();
            if (word == "of") {
                continue;
            }

            // 两个不规则复数先折回单数，再去尾部 s。
            // Fold the two irregular plurals back first, then strip a trailing s.
            word = word switch {
                "feet" => "foot",
                "inches" => "inch",
                _ => word,
            };

            if (word.Length > 1 && word.EndsWith("s", StringComparison.Ordinal)) {
                word = word[..^1];
            }

            parts.Add(word);
        }

        return string.Concat(parts);
    }

    /// <summary>收集装配件里所有 `PhysicalUnit` 字段名 / Gather every PhysicalUnit field name.</summary>
    private static HashSet<string> DeclaredUnitNames() {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Type type in typeof(InformationUnits).Assembly.GetTypes()) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (field.FieldType == typeof(PhysicalUnit)) {
                    names.Add(field.Name);
                }
            }
        }

        return names;
    }
}
