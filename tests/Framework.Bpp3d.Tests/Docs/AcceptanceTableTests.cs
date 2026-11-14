// P4—P9 验收对照表：点名的测试类必须真的存在 / The acceptance table's cited test classes must really exist.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **验收表点名的每一条证据都必须能被核对** / Every piece of evidence the acceptance table names must be checkable.
/// </summary>
/// <remarks>
/// 这张表存在的理由：P4—P9 的验收标准散在一万多行计划里，"哪一条真的核过"从来没人能一屏看完。
/// 但**一张写着不存在的测试名的表比没有表更糟**——它把"没核对"伪装成"有证据"。所以这里逐个核对：
/// 表里以 `\`XxxTests\`` 点名的地方，`tests/` 下必须真有一个同名类型文件。
///
/// 这条判据只证明"名字存在"，**不证明那条判据覆盖了这个验收项**。表尾自己也这么写：
/// ✅ 只说明点名的那条判据在跑，不说明模块的每一处行为都验证过。**判据的能力边界必须写进判据自己身上。**
///
/// Why this table exists: the P4-P9 acceptance criteria are scattered across tens of thousands of plan lines and nobody could see in one screen what had actually
/// been checked. But **a table naming tests that do not exist is worse than no table** — it disguises "not checked" as "evidence exists". So this verifies every
/// `\`XxxTests\`` name in the table really has a file under `tests/`. This proves **the name exists**, not that the criterion covers the acceptance item — the table's
/// own closing paragraph says the same, and **a criterion's limits belong on the criterion itself**.
/// </remarks>
public sealed class AcceptanceTableTests {
    /// <summary>**4.10 不许退回"无法核对"** / Row 4.10 must not fall back to "cannot be checked".</summary>
    /// <remarks>
    /// **这一行原来是 ❌，理由是"基线 fixture 里没有调度样本 ⇒ 无法逐字节核对"。**
    /// **两个半边结论相反，而它挂在 ❌ 上很久：**
    ///
    /// - **调度模型那一半是凭空要求**：`ActivitySchedulingProblem` 在基线里**从不经过线格式**，
    ///   **"跨语言往返"这件事不存在，不是本仓缺。**
    /// - **能力摘要那一半早就有判据**：`RemoteSolverCapabilities` 逐字段对应，`ProbeCapabilitiesAsync` 走真实 HTTP 客户端断言六个字段。
    ///
    /// **这条判据钉两件事**：本行是 ✅，且它必须点名那两半——**否则下一个人照着旧理由再改回 ❌。**
    ///
    /// **This row was ❌ because "the baseline fixtures hold no scheduling sample ⇒ no byte-level check".**
    /// **The two halves reach opposite conclusions, and the row sat on ❌ for a long time:**
    ///
    /// - **The scheduling-model half is a requirement nobody made**: `ActivitySchedulingProblem` **never goes over a wire format** in the baseline,
    ///   **so "cross-language round trip" does not exist there — it is not missing here.**
    /// - **The capability-summary half has had criteria all along**: `RemoteSolverCapabilities` matches field by field and `ProbeCapabilitiesAsync` asserts six fields through a real HTTP client.
    ///
    /// **This pins two things**: the row is ✅, and it names both halves — **or the next reader restores ❌ from the stale reason.**
    /// </remarks>
    [Fact]
    public void TheSchedulingRoundTripRow_ShouldStayResolvedWithItsReasoning()
    {
        string table = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "kotlin-sync-acceptance.md"));
        string[] lines = table.Split('\u000a');
        string row = lines.First(line => line.StartsWith("| 4.10 |", StringComparison.Ordinal));

        row.Should().EndWith("| ✅ |", "the row was resolved in B1114 and must stay resolved");
        row.Should().Contain("从不经过线格式",
            "the scheduling half is not a gap: the baseline never serialises it");
        row.Should().Contain("ProbeCapabilitiesAsync",
            "the capability half is measured, and the row has to say by what");
        row.Should().NotContain("无法逐字节核对",
            "the old reason treated an absent requirement as missing evidence");
    }

    /// <summary>**交接入口不能是空标题** / The handoff entry must not be an empty heading.</summary>
    /// <remarks>
    /// B1123 做复核时发现：`### 下一批（B11xx）的唯一入口` **这个标题下面本来是空的。**
    ///
    /// **一个空标题会让下一批从"重新盘点一遍"开始**——**而盘点结论就在上一批的正文里，只是没人把它搬过来。**
    ///
    /// **这条判据钉的是"入口有内容"**：标题之后、下一个二级标题之前，**必须至少有 3 行非空内容**。
    /// **它不检查内容对不对**（那由其他判据管），**只检查"有人接了这个班"。**
    ///
    /// B1123, while doing a handoff review, found that the `### 下一批（B11xx）的唯一入口` heading **had nothing under it.**
    ///
    /// **An empty heading makes the next batch start by re-inventorying** — **while the inventory conclusion sits in the previous
    /// batch's body, just never carried over.**
    ///
    /// **This pins "the entry has content"**: between the heading and the next level-two heading there must be **at least 3 non-empty lines**.
    /// **It does not check that the content is right** (other criteria do), **only that somebody took the shift.**
    /// </remarks>
    [Fact]
    public void TheHandoffEntry_ShouldNotBeAnEmptyHeading()
    {
        string plan = File.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-plan.md"));
        string[] lines = plan.Split('\u000a');

        int heading = Array.FindIndex(lines,
            line => line.StartsWith("### 下一批（", StringComparison.Ordinal));
        heading.Should().BeGreaterThanOrEqualTo(0, "the plan always carries a next-batch entry");

        int content = 0;
        for (int index = heading + 1; index < lines.Length; ++index)
        {
            string line = lines[index];
            if (line.StartsWith("## ", StringComparison.Ordinal) ||
                line.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }
            if (!string.IsNullOrWhiteSpace(line))
            {
                ++content;
            }
        }

        content.Should().BeGreaterThanOrEqualTo(3,
            "an empty handoff heading makes the next batch re-inventory work whose conclusion is already written down");
    }

    private static string RepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }

    private static string ReadTable() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "docs", "kotlin-sync-acceptance.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>**表里点名的测试类都必须存在** / Every test class named in the table must exist.</summary>
    [Fact]
    public void AcceptanceTable_ShouldOnlyCiteExistingTestClasses() {
        var existing = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.Ordinal);

        var cited = Regex.Matches(ReadTable(), "`([A-Z][A-Za-z0-9_]*Tests?)`")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        cited.Should().NotBeEmpty("the table must cite evidence; an empty table would pass vacuously");
        var missing = cited.Where(name => !existing.Contains(name)).ToList();
        missing.Should().BeEmpty("a cited test class that does not exist disguises unchecked evidence as evidence");
    }

    /// <summary>**三个阶段（P4—P9）都要有行，且四种状态口径必须写明** / All six phases must have rows and the four statuses must be defined.</summary>
    /// <remarks>
    /// 只有 P9 有行的表会让人以为 P4—P8 不需要核对。而状态记号没有定义时，"◐"到底是"部分完成"还是"我看过但没测"没人知道。
    ///
    /// A table with only P9 rows suggests P4-P8 need no checking, and without defined status markers nobody knows whether "◐" means "partly done" or "looked at it".
    /// </remarks>
    [Fact]
    public void AcceptanceTable_ShouldCoverEveryPhaseAndDefineItsStatuses() {
        string table = ReadTable();

        table.Should().Contain("## 一、P4");
        table.Should().Contain("## 二、P5");
        table.Should().Contain("## 三、P6");
        table.Should().Contain("## 四、P7");
        table.Should().Contain("## 五、P8");
        table.Should().Contain("## 六、P9");

        table.Should().Contain("| ✅ | **实测通过**");
        table.Should().Contain("| ◐ | **部分**");
        table.Should().Contain("| ❌ | **没做**");
        table.Should().Contain("| ⊘ | **当前环境不可达**");
    }

    /// <summary>**不可达项必须写"缺什么"** / An unreachable item must say what is missing.</summary>
    /// <remarks>
    /// "不可达"是最容易变成托词的一句话。所以要求每一行都点名**缺的具体东西**（许可、SDK、服务、独占环境），
    /// 而不是笼统的"环境不支持"。
    ///
    /// "Unreachable" is the easiest phrase to turn into an excuse, so every row must name the **concrete** missing thing (licence, SDK, service, isolated environment)
    /// rather than a vague "the environment does not support it".
    /// </remarks>
    [Fact]
    public void AcceptanceTable_ShouldNameWhatEachUnreachableItemLacks() {
        string table = ReadTable();

        table.Should().Contain("## 七、当前环境不可达（不是代码缺口）");
        table.Should().Contain("**许可**");
        // **B956 修**：原判据要求写着"无服务"，而 B950 探到 9092/27017 端口关闭、
        // 3306/5432/6379 **端口开着但凭据被拒**——两种情况必须分开写，"无服务"把前者盖住了。
        // **B956 fix**: the criterion demanded "no service", but B950 found 9092/27017 closed while 3306/5432/6379 **answer and reject credentials**;
        // the two cases have to be distinguished, and "no service" buried the first one.
        table.Should().Contain("端口关闭");
        table.Should().Contain("无可用凭据");
        table.Should().Contain("无独占测量环境");
        table.Should().Contain("没有独占测量环境时不宣称性能提升");
    }

    /// <summary>**表本身不得被当成验收结论** / The table must not present itself as the verdict.</summary>
    /// <remarks>
    /// 一张全是 ✅ 的表最容易被读成"模块都没问题"。所以表尾必须写明它只是**索引**，并点明 ✅ 的能力边界。
    ///
    /// An all-✅ table is easily read as "every module is fine", so the closing paragraph must say it is only an **index** and state what ✅ does not prove.
    /// </remarks>
    [Fact]
    public void AcceptanceTable_ShouldDeclareItsOwnLimits() {
        string table = ReadTable();

        table.Should().Contain("**这张表本身不是验收结论**");
        table.Should().Contain("✅ 的行仍然只说明**点名的那条判据在跑**");
        table.Should().Contain("表替不了", "the closing paragraph must say the table cannot replace reading the criteria");
    }
}
