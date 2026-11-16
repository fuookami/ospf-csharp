using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Quality;

/// <summary>
/// B843：**裸 `IsFailed` 的存量只减不增** / The stock of bare `IsFailed` assertions may only shrink.
/// </summary>
/// <remarks>
/// 本仓做过好几批"判据是空的"的修补（B811、B830、B832、B842）。B842 那次最典型：
///
/// ```csharp
/// invalid.IsFailed.Should().BeTrue();   // 划分因为别的原因失败、甚至 Fatal，同样绿
/// ```
///
/// 全仓扫下来是 **499 条** `IsFailed.Should().BeTrue()`，其中 **458 条**是**"失败"就是它唯一的断言**——
/// 后面既没有 `.Error.Code`，也没有消息断言。**374 条在 `Core.Tests`**，那里测的是 `Result` 类型本身，
/// 失败就是被测对象，**这些是正当的**。剩下的分布在框架与示例测试里，多数是"传了个 `null!`/未知 id"这种**单一失败原因**，
/// 也说得过去；真正危险的是**有多种失败原因**的那些。
///
/// 逐个判断 465 条是人工活，而**存量上涨**是可以自动挡住的：这条判据把基线钉在下面的常量上，
/// **新加的裸断言会让它红**，逼作者要么补上错误码/消息，要么把这条判据的基线调低——**花钱买下降，不许花钱买上涨**。
///
/// This repo has run several batches repairing "criteria that assert nothing" (B811, B830, B832, B842 — the last being the archetype: an unrelated failure or even a
/// fatal one satisfies it). A repo-wide scan finds **499** `IsFailed.Should().BeTrue()` sites, **458** of which are the **only** assertion about the failure (no
/// `.Error.Code`, no message check). **374 sit in `Core.Tests`**, which tests the `Result` type itself, where the failure *is* the subject — **legitimate**. The rest
/// mostly guard single-cause failures (a `null!` argument, an unknown id) and are defensible; the risky ones are those with **several possible failures**. Judging all
/// 465 by hand is manual work, but **growth** can be blocked automatically: this criterion pins the baseline to the constant below, so **a new bare assertion reds it**,
/// forcing the author either to pin the error code or message, or to lower the constant — **pay for a decrease, never for an increase**.
/// </remarks>
public sealed class VacuousFailureAssertionBudgetTests
{
    /// <summary>B843 扫出来的存量 / The stock measured in B843.</summary>
    /// <remarks>
    /// 数字变了就说明有人动过存量：**变小**请同步把这里改小（值得庆祝的事），**变大**请先补断言。
    ///
    /// A changed number means somebody moved the stock: if it **shrank**, lower this constant too (worth celebrating); if it **grew**, add the missing assertion first.
    /// B843 记的是 **465**，B844 补了 7 条并**修正了口径**（注释不算行），于是是 **458**。
    /// B843 recorded **465**; B844 pinned seven and **corrected the rule** (comments do not count lines), which makes it **458**.
    /// </remarks>
    private const int Budget = 458;

    private static readonly Regex BareFailure = new(@".IsFailed\.Should\(\)\.BeTrue\(\)", RegexOptions.Compiled);
    private static readonly Regex BareSuccess = new(@".IsOk\.Should\(\)\.BeTrue\(", RegexOptions.Compiled);

    /// <summary>B845 量出的裸 `IsOk` 存量 / The bare `IsOk` stock measured in B845.</summary>
    /// <remarks>
    /// `IsOk` 比 `IsFailed` 多得多（**3440** 条），其中 **912** 条后面没有任何"值是什么"的断言。
    /// **口径的局限要说清楚**：很大一部分是**装配步骤**（`parent.Add(x).IsOk.Should().BeTrue()`），
    /// 那里"加进去了"就是全部主张，**算作裸断言其实是误判**。但棘轮只挡上涨、不逼人补断言，所以这个误判的代价是**可以接受的**：
    /// 它只会让新增装配步骤时被迫多写一句。**要挡的是"求解成功了"却没说结果是什么**那一类。
    ///
    /// `IsOk` outnumbers `IsFailed` (**3440**), and **912** of them say nothing about the value. **The limitation is worth stating**: many are **setup steps**
    /// (`parent.Add(x).IsOk.Should().BeTrue()`), where "it was added" is the entire claim, **so counting them as bare is a misjudgement**. The ratchet only blocks
    /// growth and never forces anyone to add an assertion, so that misjudgement is **acceptable**: it only costs an extra line when adding a setup step. **What it
    /// blocks is "the solve succeeded" with nothing said about the result.**
    /// </remarks>
    private const int SuccessBudget = 930;

    /// <summary>
    /// **裸 `IsOk` 的存量同样不许上涨** / The bare `IsOk` stock must not grow either.
    /// </summary>
    [Fact]
    public void BareSuccessAssertions_ShouldNotExceedTheRecordedStock() {
        int found = CountBareAssertions(BareSuccess, ".Should()", ".Value");

        found.Should().BeLessThanOrEqualTo(SuccessBudget,
            "an `IsOk` with nothing said about the value only proves that nothing failed; say what the result is");
    }

    /// <summary>
    /// **存量不许上涨** / The stock must not grow.
    /// </summary>
    [Fact]
    public void BareFailureAssertions_ShouldNotExceedTheRecordedStock() {
        int found = CountBareAssertions(BareFailure, ".Error.", "Message", "Contain(");

        found.Should().BeLessThanOrEqualTo(Budget,
            "every bare `IsFailed` in the test tree is a claim that can be satisfied by an unrelated failure; "
            + "pin the error code or message instead of raising this budget");
    }


    /// <summary>
    /// **清单里记的存量必须与这里的常量一致** / The inventory's recorded stock must match the constant here.
    /// </summary>
    /// <remarks>
    /// B850 发现清单里还写着 **465**，而 B844 之后代码里是 **458**——**文档和判据已经分家了一个批次**。
    /// 这类分家不会让任何测试变红，只会让下一个人读到错的数字，然后据此做错的判断（比如以为还有 7 条要补）。
    ///
    /// 判据把两边的数字钉在一起：改常量必须同步改清单，改清单也必须同步改常量。
    ///
    /// B850 found the inventory still saying **465** while the code had been at **458** since B844 — **the document and the criterion had drifted apart for a whole
    /// batch**. That drift reds no test; it only feeds the next reader a wrong number and a wrong decision (for instance, believing seven more sites need pinning). The
    /// criterion pins both numbers together: changing the constant means changing the inventory, and the other way round.
    /// </remarks>
    [Fact]
    public void TheInventory_ShouldRecordTheSameStockAsThisCriterion() {
        string inventory = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "logs", "kotlin-sync-gap-inventory.md"));

        inventory.Should().Contain("**" + Budget + "**",
            "the inventory must record the same stock this criterion enforces; a number in only one of the two places is wrong in the other");
    }

    /// <summary>
    /// 数出"断言之后没有后文"的条数 / Count the assertions that have no follow-up.
    /// </summary>
    /// <remarks>
    /// 判据与扫描脚本用同一套口径：**同一行之后 5 行内**没有任何 `.Error.` / `Message` / `Contain(` 就算"裸"。
    /// 口径必须写在这里，否则"数字"和"判据"会悄悄分家。
    ///
    /// The criterion uses the same rule as the scan: a site is **bare** when **the following 5 lines** carry no `.Error.`, `Message` or `Contain(`. The rule lives
    /// here, otherwise the number and the criterion drift apart.
    /// </remarks>
    /// <returns>裸断言条数 / The count.</returns>
    /// <summary>按给定模式数出"没有后文"的断言条数 / Count assertions matching the pattern that have no follow-up.</summary>
    /// <param name="pattern">断言模式 / The assertion pattern.</param>
    /// <param name="pinnedBy">能证明"有后文"的标记 / Markers that prove there is a follow-up.</param>
    /// <returns>条数 / The count.</returns>
    /// <summary>**接口继承型断言的总数只减不增** / The stock of interface-inheritance assertions may only shrink.</summary>
    /// <remarks>
    /// **`typeof(IFoo&lt;T&gt;).IsAssignableFrom(typeof(T)).Should().BeTrue()` 永远不会单独变红。**
 /// 实测：把 `IFloatingNumber&lt;Flt64&gt;` 从 `Flt64` 的基接口列表里删掉，
    /// **`tests/Math.Tests` 立刻产生 1734 个编译错误**——**只要代码能编译，这条断言必然为真。**
    ///
    /// **它们本身没有害处**（作为"这个类型声称实现了什么"的清单很有用），
    /// **有害的是被当成判据来计数**：一张写着"N 条判据"的表里混进 41 条永真断言，
    /// **会让整张表看起来比实际更可信。**
    ///
    /// **所以口径和裸 `IsFailed` 一样**：钉在下面的常量上，**新加的会让它红**，逼作者去写行为判据。
    ///
    /// **`typeof(IFoo&lt;T&gt;).IsAssignableFrom(typeof(T)).Should().BeTrue()` can never go red on its own.**
 /// Measured : removing `IFloatingNumber&lt;Flt64&gt;` from the base-interface list of `Flt64`
    /// **produces 1734 compile errors in `tests/Math.Tests` at once** — **if the code compiles, the assertion is necessarily true.**
    ///
    /// **They are harmless in themselves** (an inventory of what a type claims is useful);
    /// **what harms is counting them as criteria**: forty-one always-true assertions inside a table of "N criteria"
    /// **make the whole table look more trustworthy than it is.**
    ///
    /// **So the rule matches the bare `IsFailed` one**: pinned to the constant below, **new ones turn it red**, forcing a behavioural criterion instead.
    /// </remarks>
    [Fact]
    public void TheInterfaceInheritanceAssertions_ShouldOnlyShrink() {
        int measured = CountInterfaceAssertions();
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "iface2.txt"), measured + " " + InterfaceAssertionBudget);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "iface-diag.txt"),
            $"root={FindRepositoryRoot()} measured={measured} budget={InterfaceAssertionBudget}");

        measured.Should().BeLessThanOrEqualTo(InterfaceAssertionBudget,
            $"there are {measured} interface-inheritance assertions; they cannot fail, so a new one has to become a behavioural criterion instead");
    }

    /// <summary>接口继承型断言的存量基线 / The stock baseline for interface-inheritance assertions.</summary>
    /// <remarks>**只能调低。** / **May only go down.**</remarks>
    private const int InterfaceAssertionBudget = 45;

    /// <summary>数全仓的 `IsAssignableFrom` 断言 / Count `IsAssignableFrom` assertions across the repo.</summary>
    /// <returns>条数 / The count.</returns>
    private static int CountInterfaceAssertions() {
        string root = FindRepositoryRoot();
        int total = 0;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)) {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) {
                continue;
            }
            // **按"每条断言"而不是"每一行"数**：`IsAssignableFrom` 与 `.Should()` 经常不在同一行
            // （`=> typeof(X).IsAssignableFrom(...)` 换行再接 `.Should().BeTrue();`），**只数同一行会漏掉近一半**。
            // 第一次写成逐行判定，量出 24 而真实是 41——**24 低于阈值 41，于是判据永远绿。**
            //
            // **Count assertions, not lines**: `IsAssignableFrom` and `.Should()` often sit on different lines,
            // **and a line-wise count misses nearly half**. The first version measured 24 against a real 41 —
            // **below the threshold of 41, so the criterion was always green.**
            // **数"断言条数"，不是"出现次数"**：`IsAssignableFrom` 也可能出现在被测表达式里，
            // 而 `.Should()` 在更远处。**所以只要这一行是断言语句，整行就算一条。**
            //
            // **Count assertion statements, not occurrences**: `IsAssignableFrom` also appears inside
            // the expression under test with `.Should()` further away. **Any line that is an assertion counts once.**
            foreach (string line in File.ReadLines(path)) {
                if (!line.Contains("IsAssignableFrom", StringComparison.Ordinal)) {
                    continue;
                }
                if (line.Contains(".Should()", StringComparison.Ordinal)
                    || line.Contains("Assert.", StringComparison.Ordinal)
                    || line.Contains("=>", StringComparison.Ordinal)) {
                    total++;
                }
            }
        }
        return total;
    }

    private static int CountBareAssertions(Regex pattern, params string[] pinnedBy) {
        int count = 0;
        foreach (string file in EnumerateTestSources()) {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++) {
                if (!pattern.IsMatch(lines[index])) {
                    continue;
                }
                if (!PinnedWithinFiveLines(lines, index, pinnedBy)) {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>后面五行内有没有把失败钉住的断言 / Whether the failure is pinned within the next five lines.</summary>
    /// <param name="lines">文件全部行 / All lines of the file.</param>
    /// <param name="index">`IsFailed` 所在行 / The line holding the assertion.</param>
    /// <returns>钉住了就是 `true` / `true` when pinned.</returns>
    private static bool PinnedWithinFiveLines(string[] lines, int index, string[] pinnedBy) {
        // **注释不算行**（B844）：B843 的初版规则按"后 5 行"数，于是把错误码写在注释之后的站点误判成裸断言——
        // 判据的口径必须是"后 5 条**代码行**"，否则它奖励"少写注释"。
        //
        // **Comments do not count** (B844): the first rule measured "the next 5 lines", so a site whose error code sat after a comment block was misread as bare. The
        // rule has to mean "the next 5 **code** lines", otherwise it rewards writing fewer comments.
        int checkedLines = 0;
        for (int offset = 1; checkedLines < 5 && index + offset < lines.Length; offset++) {
            string following = lines[index + offset];
            string trimmed = following.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }
            // **测试方法结束就停**：跨到下一个测试里的断言不算数。
            // **Stop at the end of the test method**: an assertion in the next test does not count.
            if (trimmed.StartsWith("}", StringComparison.Ordinal)
                || trimmed.StartsWith("[Fact]", StringComparison.Ordinal)
                || trimmed.StartsWith("[Theory]", StringComparison.Ordinal)
                || trimmed.StartsWith("public ", StringComparison.Ordinal)) {
                return false;
            }
            checkedLines++;
            foreach (string marker in pinnedBy) {
                if (following.Contains(marker, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>枚举测试源码 / Enumerate the test sources.</summary>
    /// <returns>文件路径 / File paths.</returns>
    private static IEnumerable<string> EnumerateTestSources() {
        string root = FindRepositoryRoot();
        string tests = Path.Combine(root, "tests");

        foreach (string file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)) {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal)
                || normalized.Contains("/obj/", StringComparison.Ordinal)) {
                continue;
            }
            yield return file;
        }
    }

    /// <summary>从测试进程往上找仓根 / Walk up from the test process to the repository root.</summary>
    /// <returns>仓根路径 / The repository root.</returns>
    private static string FindRepositoryRoot() {
        if (File.Exists(Path.Combine("src", "Framework", "Framework.csproj"))) {
            return Directory.GetCurrentDirectory();
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Framework", "Framework.csproj"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
