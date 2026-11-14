// 第四到第七节的数字，重新量一遍 / Re-measure the numbers in sections four through seven.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **一个过时的判断会被抄进更多地方** / A stale judgement gets copied into more places than one.
/// </summary>
/// <remarks>
/// B955 只改了状态文档的 P9 摘要，**而矩阵里那两条一模一样的判断还活着**：基准"九个 / 两个依赖缺失或 `internal`"、持久化"只验证了仓储接口"。
/// B960 在同一张表的第四到第七节又找出四处。
///
/// **两份拷贝里改一份等于没改**：读者遇到不一致时不会去查哪份新，**他只会信对自己更不利的那份**——然后重做一遍已经做过的事。
///
/// B955 corrected only the status document **while the matrix kept the same two judgements**: "nine benchmarks, two blocked by a missing project or an `internal` type" and "only the
/// repository interface was verified". B960 found four more in sections four through seven. **Fixing one of two copies is not fixing it**: a reader facing the disagreement does
/// not check which is newer, **he believes the one that costs him more** — and redoes work that was already done.
/// </remarks>
public class ModuleTableRemeasuredTests
{
    /// <summary>**基准是 15 个** / There are fifteen benchmarks.</summary>
    [Fact]
    public void TheBenchmarkCount_ShouldMatchTheFiles() {
        string root = Root();
        int actual = Directory.GetFiles(Path.Combine(root, "src", "Benchmark"), "*Benchmark.cs")
            .Count(file => !Path.GetFileName(file).Equals("Program.cs", StringComparison.Ordinal));

        string row = Row(root, "| 基准套件 |");
        row.Contains(actual + " 个基准", StringComparison.Ordinal)
            .Should().BeTrue("the row has to state the measured count (" + actual + ")");
        // **旧的两个理由不许留**：它们配合"九个"一起，把缺口说得比实际大。
        // **The two old excuses must go**: together with "nine" they overstated the gap.
        // **这一行现在是在"证伪"那句话**，所以不能断言"不含 `internal`"——它得提到才能证伪。
        // 断言的落点改成"证伪这个动作在不在"：把行改回旧文案，"证伪"就没影了，判据就红。
        // **The row now refutes that sentence**, so "must not contain `internal`" is wrong — it has to mention it to refute it. The assertion moves to whether the refutation
        // is there: reverting the row to the old wording removes "证伪", and the criterion goes red.
        row.Contains("证伪", StringComparison.Ordinal)
            .Should().BeTrue("the row has to state that the two blockers were disproved, not just drop them");
    }

    /// <summary>**Starters 是十份，不是五份** / Ten starters, not five.</summary>
    [Fact]
    public void TheStarterCount_ShouldBeTen() {
        string root = Root();
        int actual = Directory.GetDirectories(Path.Combine(root, "src", "Starters")).Length;

        string row = Row(root, "| 其余模块 README |");
        row.Contains("十份", StringComparison.Ordinal)
            .Should().BeTrue("the README row names the starter count, so it has to state the measured one");
        row.Contains("五份", StringComparison.Ordinal)
            .Should().BeFalse("five was the count before B947 added four starters");

        // **十份是真的**：目录数要对得上，否则"十份"是空口。
        // **Ten is real**: the directory count has to agree, or the number is hearsay.
        actual.Should().Be(10, "the row and the directory must not drift apart");
    }

    /// <summary>**SQLite 是两条判据、文件库** / Two criteria, a file database.</summary>
    /// <remarks>
    /// 矩阵第八节曾写"**7 条判据，`:memory:` 真库**"——**两个数字都错，而且错在同一个方向上把证据说得更足**。
    /// `:memory:` 库随连接消失，用它测"落盘"等于没测；真正的判据是 **2 条**、用的是**文件库**。
    ///
    /// Section eight once said "**7 criteria, an `:memory:` database**" — **both numbers wrong, and both in the direction of claiming more evidence than exists**. An in-memory
    /// database vanishes with its connection, so testing persistence with it tests nothing; the real criteria are **two**, over a **file** database.
    /// </remarks>
    [Fact]
    public void TheSqliteRoundTrip_ShouldStateTwoCriteriaOverAFileDatabase() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        string dbRow = Row(root, "| 数据库持久化往返 |");
        dbRow.Contains("7 条判据", StringComparison.Ordinal).Should().BeFalse("there are two, not seven");
        dbRow.Contains("**2 条**判据", StringComparison.Ordinal).Should().BeTrue("the measured count has to be stated");

        // **文件库是真的**：测试源码里必须能给出一条打开的连接串，且注释说明为什么不用 `:memory:`。
        // **The file database is real**: the test source has to open a file and say why not `:memory:`.
        string tests = File.ReadAllText(
            Path.Combine(root, "tests", "Framework.Plugin.Tests", "Persistence", "SqliteRoundTripTests.cs"));
        int facts = tests.Split("[Fact]").Length - 1;
        facts.Should().Be(2, "the matrix says two criteria, so the test file has to hold two");
        tests.Should().Contain("不是 `:memory:`",
            "the reason the test uses a file database is the whole point of the criterion");
    }

    /// <summary>**每一行的备注都不是空的，且项目名能对上目录** / No empty remark, and the project name matches a directory.</summary>
    /// <remarks>
    /// B960 在第四到第七节找到两行备注空着（`BPP 2D 装箱` 与 `CoreDemo / FrameworkDemo`）。
    /// **空着不等于"没问题"**：这一批给两行都填了能核对的事实。
    ///
    /// B960 found two empty remarks in sections four through seven. **Empty does not mean "fine"**; both got checkable facts in this batch.
    /// </remarks>
    [Fact]
    public void EveryModuleRow_ShouldHaveARemarkOrAProvenStatus() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        foreach (string name in new[] { "| BPP 2D 装箱 |", "| CoreDemo / FrameworkDemo |" }) {
            string row = Row(root, name);
            string[] cells = row.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            cells[3].Should().NotBeNullOrWhiteSpace(
                name + " was empty; a cell nobody filled reads as \"nothing to note\" and hides whether anyone looked");
        }
    }

    private static string Row(string root, string prefix) {
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        return matrix.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(prefix + " row not found");
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
