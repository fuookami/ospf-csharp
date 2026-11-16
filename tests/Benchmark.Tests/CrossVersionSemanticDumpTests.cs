#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **跨版本转储必须诚实：** **已实现段、待办段、可比性限制三件事都要与输出对得上** /
/// The cross-version dump must be honest: what it implements, what it defers and what cannot be compared must match its output.
/// </summary>
/// <remarks>
/// **这是一个 diff 工具，所以它最大的风险不是"算错"，而是<b>说得比做得多**：** **待办段被当成已转储、
/// 或者输出了跨语言不可比的值（比如 Kotlin 的 `hashCode`）而被当成可比。** **四类判据分别钉：**
///
/// **① 输出只含已实现段的前缀；② 每行都有段前缀且不含大写布尔（小写）；③ 同一进程内两次运行<b>逐字符相同**；
/// **④ 待办清单与已实现段不重叠，且每条都写了理由。**
///
/// **It is a diff tool, so its greatest risk is not a wrong number but claiming more than it does: a deferred section presented as dumped, or a
/// value emitted that cannot be compared across languages yet read as comparable.** **Four kinds of criteria pin that: only implemented prefixes
/// appear; every line carries a prefix and no capitalised boolean; two runs in one process agree character for character; and the pending list never
/// overlaps the implemented sections and always states a reason.**
/// </remarks>
public sealed class CrossVersionSemanticDumpTests
{
    private static string[] Lines() =>
        CrossVersionSemanticDump.Run().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>**输出只含已实现段的段名** / The output only carries implemented section names.</summary>
    [Fact]
    public void TheOutput_ShouldOnlyCoverImplementedSections() {
        string[] lines = Lines();
        lines.Should().NotBeEmpty("the dump emits at least one line");

        string[] prefixes = lines.Select(line => line.Split('|')[0]).Distinct()
            .OrderBy(prefix => prefix, StringComparer.Ordinal).ToArray();
        prefixes.Should().BeEquivalentTo(
            CrossVersionSemanticDump.Implemented.OrderBy(section => section, StringComparer.Ordinal),
            "every emitted prefix must be one of the sections this file claims to implement, and every claimed section must actually appear");
    }

    /// <summary>**每行都有段前缀，且布尔量是小写的** / Every line has a prefix and booleans are lowercase.</summary>
    /// <remarks>
    /// **大写布尔是真实的跨语言陷阱：** **`bool.ToString` 给 `True`，基线给 `true`——逐行 diff 会把每一行都算成差异。**
    ///
    /// **A capitalised boolean is a real cross-language trap: `bool.ToString` gives `True` while the code gives `true`, so a line-by-line diff
    /// reports every line as different.**
    /// </remarks>
    [Fact]
    public void TheOutput_ShouldBeStableText() {
        foreach (string line in Lines()) {
            line.Should().MatchRegex(@"^[A-Z]{3,5}\|\S.*$", "each line starts with a three-letter section prefix and a field");
            line.Should().NotMatchRegex(
                @"=(True|False)\b",
                "booleans must render lowercase to stay comparable with the baseline's `true`/`false`");
            line.Should().NotContain(
                "\r", "a carriage return would make every line differ from a Unix-side dump");
        }
    }

    /// <summary>
    /// **两次运行（同进程）除统计行外逐字符相同** / Two runs in one process agree, except the statistics lines.
    /// </summary>
    /// <remarks>
    /// **第一版要求整份输出相同，而它红了——这是真结论，不是判据太严：** **`GEN`／`TRUNC` 的 `stats=` 行带着生成器的缓存计数
    /// （`MaterialSliceTemplateCacheHits`、`QuantityCacheHits` 等），它们是进程级累计值，第二次运行自然是热的。**
    /// **实测：两个独立进程各跑一次 `--dump`，输出逐字节相同（差异 0 行）。** **所以"跨进程可比"成立、"同进程两次相同"不成立——
    /// 而逐行 diff 用的正是前者。** **判据钉前者，把 `stats=` 行显式排除，并断言它确实存在，免得这个例外被悄悄扩大到整份输出。**
    ///
    /// **The first version demanded the whole output be identical and it went red — a real finding, not an over-strict criterion: the `stats=` lines of
    /// `GEN`/`TRUNC` carry the generator's process-cumulative cache counters, so a second run in the same process is naturally warm.** **Measured: two
    /// separate processes each running `--dump` produce byte-identical output (zero diff lines).** **So "comparable across processes" holds and "identical
    /// twice in one process" does not — and line-by-line diffing only uses the former.** **The criterion pins the former, excludes the `stats=` lines
    /// explicitly, and asserts they exist so the exception cannot quietly grow to cover the whole output.**
    /// </remarks>
    [Fact]
    public void TheDump_ShouldBeStableBetweenRuns() {
        string[] first = CrossVersionSemanticDump.Run().Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        string[] second = CrossVersionSemanticDump.Run().Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

        string[] stableFirst = first.Where(line => !line.Contains("|stats=", StringComparison.Ordinal)).ToArray();
        string[] stableSecond = second.Where(line => !line.Contains("|stats=", StringComparison.Ordinal)).ToArray();
        stableFirst.Should().HaveSameCount(stableSecond, "both runs emit the same number of non-statistics lines");
        for (int index = 0; index < stableFirst.Length; ++index) {
            stableFirst[index].Should().Be(stableSecond[index], "line " + index + " drifted between two runs in one process");
        }
        first.Should().Contain(
            line => line.Contains("|stats=", StringComparison.Ordinal),
            "the excluded lines must really exist, otherwise the exclusion covers nothing and the criterion proves less than it claims");
    }

    /// <summary>
    /// **待办清单与已实现段不重叠；清单为空，说明六段齐备** /
    /// Pending sections never overlap implemented ones; an empty list means all six are in.
    /// </summary>
    /// <remarks>
    /// **此后清单是空的。** **这条判据因此多了一句断言"六段齐备"：** **否则"清单为空"与"某段悄悄消失"是同一个绿色——**
    /// **而后者正是这个文件最容易发生的事（少调一个 `Dump` 就少一整段，编译照样过）。**
    ///
    /// **the list is empty.** **The criterion therefore also asserts that all six sections are present: otherwise "the list is empty" and
    /// "a section quietly disappeared" are the same green — and the latter is the easiest thing to do to this file, since dropping one `Dump` call
    /// compiles just fine.**
    /// </remarks>
    [Fact]
    public void ThePendingList_ShouldBeDisjointAndExplained() {
        var implemented = new HashSet<string>(CrossVersionSemanticDump.Implemented, StringComparer.Ordinal);
        var pending = new HashSet<string>(CrossVersionSemanticDump.Pending.Select(entry => entry.Section), StringComparer.Ordinal);

        CrossVersionSemanticDump.Implemented.Should().BeEquivalentTo(
            new[] { "NET", "PRJ", "GEN", "TRUNC", "TOK", "PRM" },
            "the baseline dumps six sections and every one of them has to be listed");

        implemented.Overlaps(pending).Should().BeFalse(
            "a section cannot be both dumped and deferred; whichever it is, the list has to say so");
        CrossVersionSemanticDump.Pending.Should().OnlyHaveUniqueItems(entry => entry.Section, "a deferred section is listed once");
        foreach ((string section, string reason) in CrossVersionSemanticDump.Pending) {
            section.Should().MatchRegex(
                "^[A-Z]{3,5}$",
                "a pending entry names a section prefix, exactly like an emitted line (`TRUNC` is five letters)");
            reason.Length.Should().BeGreaterThan(
                40,
                section + " must say what is missing, not just that it is pending; a bare \"not done\" hangs forever");
            reason.Should().Contain(
                " / ",
                section + " carries both the Chinese and the English reason, like every other note in this repo");
        }
    }

    /// <summary>**已实现段的核心数字必须是真实测出来的** / The implemented sections carry real measured numbers.</summary>
    /// <remarks>
    /// **π(20000) = 2262、1000 以内环上强连通分量只有 1 个、4096 个 token 都能查到——这些是"生产入口算出来的真值"。**
    /// **判据钉住它们，是因为一个"转储了但内容为空"的实现会安静地通过前四条。**
    ///
    /// **π(20000) is 2262, a 256-node ring has exactly one strongly connected component, and 4096 tokens all resolve — these are values the production
    /// entries really produce.** **The criterion pins them because a dump that emits the right shape with empty contents would pass the first four.**
    /// </remarks>
    [Fact]
    public void TheImplementedSections_ShouldCarryRealValues() {
        string dump = CrossVersionSemanticDump.Run();

        dump.Should().Contain("PRM|isPrime0To20000|count=2262", "there are 2262 primes up to 20000");
        dump.Should().Contain("NET|256|scc|count=1", "a four-step ring is one strongly connected component");
        dump.Should().Contain("NET|256|reverseExhaustiveMismatch|outgoing=0|incoming=0",
            "the reverse view agrees with the original incoming arcs on every node, or the design premise is false");
        dump.Should().Contain("TOK|4096|missIsNull|true", "an unregistered variable must not resolve to an index");
        dump.Should().Contain("PRM|boundaries|0=false,1=false,2=true", "0 and 1 are not prime, 2 is");
    }

    /// <summary>**投影段带着真结构，而不是空壳** / The projection section carries real structure, not a shell.</summary>
    /// <remarks>
    /// **这段的待办理由原来是"快照类型名不同、字段需对齐"，而实测两边的入口一个不缺。** **判据钉住三件可证的实事：**
    /// **行数与变量数等于构造时给的规模、定价系数数与决策数成正比、预热与冷启的系数逐项相同。**
    ///
    /// **The pending reason used to be "the snapshot type name differs, the fields need aligning", while measurement shows every entry point exists on
    /// both sides.** **Three provable facts are pinned: rows and variables match the constructed size, the pricing coefficient count scales with the
    /// decisions, and warm and cold pricing agree item by item.**
    /// </remarks>
    [Fact]
    public void TheProjectionSection_ShouldCarryRealStructure() {
        string dump = CrossVersionSemanticDump.Run();

        dump.Should().Contain("PRJ|256|snapshot|rows=128|vars=256", "half of 256 rows and 256 variables, exactly as constructed");
        dump.Should().Contain("PRJ|1024|snapshot|rows=512|vars=1024", "the larger size scales the same way");
        dump.Should().Contain("PRJ|256|pricing|block=b0|coeffCount=64", "one block owns a quarter of the 256 decisions");
        dump.Should().Contain("PRJ|256|warmSameAsCold|coeffs=true|convexity=true",
            "a second pricing request over an unchanged model has to agree with the first, or the cache is lying");
        dump.Should().MatchRegex(@"PRJ\|256\|snapshotRow0\|source=r0\|rhs=2\|coeffs=v\d+:");
        dump.Should().Contain("PRJ|256|master|rows=128|convexities=4|columnVars=4",
            "the master problem holds the source rows, one convexity per block and one variable per registered column");
    }

    /// <summary>
    /// **生成段：预算截断真的截断了，且规范键不是同一串** /
    /// The generation section: the budget really truncates, and the canonical keys are not all the same string.
    /// </summary>
    /// <remarks>
    /// **第二条是这里实测抓到的缺陷：** **第一版用 `record.ToString` 渲染规范键，而那个键的两个集合成员都是 `IReadOnlyList`，
    /// `ToString` 打印的是集合的类型名——于是前三个方案的摘要完全相同。** **一个"看起来有摘要、实际没有信息"的输出比没有摘要更糟：**
    /// **它会安静地通过"格式正确"这类判据。**
    ///
    /// **The second assertion pins a defect measured: the first version rendered the canonical key through `record.ToString`, and both of
    /// that key's collection members are `IReadOnlyList`, so `ToString` printed the collection's type name — the first three plans hashed identically.**
    /// **A digest that looks informative and carries none is worse than no digest: it passes "the format is right" criteria quietly.**
    /// </remarks>
    [Fact]
    public void TheGenerationSection_ShouldShowTheBudgetBite() {
        string dump = CrossVersionSemanticDump.Run();

        dump.Should().MatchRegex(@"GEN\|equivalent\|dfs\|plans=\d+\|stop=Exhausted",
            "a budget of a hundred thousand is not reached, so the generator exhausts its space instead of truncating");
        dump.Should().Contain("TRUNC|equivalent|dfs|plans=200|stop=MaxPlans",
            "a budget of two hundred stops the generator at two hundred plans");

        string[] heads = dump.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("GEN|equivalent|dfs|orderHead=", StringComparison.Ordinal))
            .Select(line => line["GEN|equivalent|dfs|orderHead=".Length..])
            .ToArray();
        heads.Should().HaveCount(1, "one order-head line per generator");
        heads[0].Split('|').Distinct().Should().HaveCountGreaterThan(
            1,
            "the first three plans must not hash to the same string; identical digests mean the key text carries no content");
    }
}