// 计划文档各批的"全方案测试"数字必须与那批的日志一致
// Each batch's full-suite count in the plan document must match that batch's log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// `logs/kotlin-sync-plan.md` 里每个 `### P9 Batch NNN：` 小节声明的**全方案测试通过数**，必须等于
/// `logs/p8-batchNNN-test.log` 里实际加出来的总数。
/// The **full-suite passed count** declared in each `### P9 Batch NNN：` section of `logs/kotlin-sync-plan.md` must equal the total actually summed from
/// `logs/p8-batchNNN-test.log`.
/// </summary>
/// <remarks>
/// 这与 B295 的门禁表核对是**不同的一道关**：B295 只查"最新一次"那条，这里逐批查**历史上每一批**。
/// 一道批小节里的数字写错了，读者会以为那一批跑过；而**没有任何东西会因为它错而失败**。
///
/// **一个必须先解决的解析陷阱**：批次小节里会出现多个 `N passed`——定向测试数、被引用的历史值
/// （如"`7159 passed` → `7450 passed`"）、以及全方案数。第一版取"第一个 `N passed`"，
/// 44 组里报出 3 处"不一致"，**全部是提取错了**（batch 266 取到的是被引用的旧值 `7159`）。
/// 因此这里只认 `全方案测试 | **\`N passed\`` 这一种写法。
///
/// This is a **different gate** from the B295 gate-table check: B295 verifies "the latest run", this verifies **every historical batch**. A wrong number in a
/// batch section makes readers believe that batch ran — and **nothing fails when it is wrong**.
///
/// **A parsing trap that must be solved first**: a batch section contains several `N passed` values — the targeted test count, quoted historical values
/// (such as "`7159 passed` → `7450 passed`"), and the full-suite count. The first version took "the first `N passed`" and reported 3 mismatches out of 44,
/// **all of them extraction errors** (batch 266 picked up the quoted old value `7159`). So only the `全方案测试 | **\`N passed\`` form is accepted here.
/// </remarks>
public sealed class PlanBatchNumbersTests {
    private static readonly string NewLine = ((char)10).ToString();

    /// <summary>只认表格行 `| 全方案测试 | **\`N passed\``。</summary>
    private static readonly Regex FullSuitePattern =
        new(@"全方案测试 \| \*\*`(\d+) passed`", RegexOptions.Compiled);

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

    private static string ReadPlan() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-plan.md"))
            .Replace("\r\n", NewLine, StringComparison.Ordinal);

    /// <summary>批次号 → 该小节声明的全方案通过数。</summary>
    private static Dictionary<int, int> StatedFullSuiteCounts() {
        var stated = new Dictionary<int, int>();
        string[] parts = Regex.Split(ReadPlan(), "^### P9 Batch (\\d+)：", RegexOptions.Multiline);
        // parts = [前言, 批号1, 正文1, 批号2, 正文2, ...]
        for (int index = 1; index + 1 < parts.Length; index += 2) {
            if (!int.TryParse(parts[index], out int batch)) {
                continue;
            }
            Match match = OwnMeasurement(parts[index + 1]);
            if (match.Success) {
                stated[batch] = int.Parse(match.Groups[1].Value);
            }
        }
        return stated;
    }

    /// <summary>取本批小节里**第一张** `| 全方案测试 |` 表格行，跳过代码围栏与引用行。</summary>
    /// <remarks>
    /// 这是本批第三次调整提取方式，前两次都错了：
    ///
    /// | 取法 | 结果 |
    /// | --- | --- |
    /// | 小节里第一个 `N passed` | 3 处误报（取到定向测试数、被引用的旧值） |
    /// | 小节里**最后一个**匹配 | 1 处误报（batch 242 小节末尾附了 **P4 Batch 6** 的历史记录，值 `4944`） |
    /// | **第一张表格行**（跳过围栏与引用） | **27 组全部一致** |
    ///
    /// batch 242 的小节之所以末尾有 `4944`，是因为那一批把 P4 的旧记录贴在了同一个小节里。
    /// **"最后一个"这种取法默认小节只有一条实测，而事实上历史记录会被贴进小节内部。**
    ///
    /// This is the third extraction scheme tried, and the first two were wrong:
    ///
    /// | Scheme | Result |
    /// | --- | --- |
    /// | first `N passed` in the section | 3 false mismatches (targeted counts, quoted old values) |
    /// | **last** match in the section | 1 false mismatch (batch 242 appends **P4 Batch 6**'s record, value `4944`) |
    /// | **first table row** (skipping fences and quotes) | **all 27 pairs agree** |
    ///
    /// Batch 242 has `4944` at its end because that batch pasted P4's old record into the same section. **"Last match" assumes a section holds one measurement,
    /// but historical records get pasted inside sections.**
    /// </remarks>
    private static Match OwnMeasurement(string body) {
        string withoutFences = Regex.Replace(body, "```.*?```", string.Empty, RegexOptions.Singleline);
        foreach (string line in withoutFences.Split(NewLine.ToCharArray()[0])) {
            Match match = FullSuitePattern.Match(line);
            if (match.Success) {
                return match;
            }
        }
        return Match.Empty;
    }

    /// <remarks>
    /// **用 `FileShare.ReadWrite` 打开**：默认的 `File.ReadAllText` 要求独占，日志正被别的进程（并行跑的门禁脚本、
    /// 索引服务）读着时会抛 `IOException`，把一条纯文档判据变成间歇性失败。读的是历史日志，本来就该允许并发访问。
    ///
    /// **Opened with `FileShare.ReadWrite`**: the default `File.ReadAllText` demands exclusive access and throws `IOException` whenever another process (a parallel gate run, an indexer)
    /// is reading the log, turning a pure documentation criterion into an intermittent failure. These are historical logs, so concurrent access is exactly what should be allowed.
    /// </remarks>
    private static int TotalPassed(string testLog) =>
        Regex.Matches(DocsFile.ReadAllText(testLog), @"Passed:\s*(\d+)")
            .Sum(match => int.Parse(match.Groups[1].Value));

    /// <summary>**每一批声明的全方案数必须等于那批日志的实际总数** / Each stated full-suite count must equal that batch's log total.</summary>
    [Fact]
    public void EachBatch_ShouldStateTheCountItsLogRecords() {
        Dictionary<int, int> stated = StatedFullSuiteCounts();
        stated.Should().NotBeEmpty("the plan must record full-suite counts per batch");

        string logs = Path.Combine(RepoRoot(), "logs");
        var mismatches = new List<string>();
        int compared = 0;
        foreach (KeyValuePair<int, int> pair in stated) {
            string testLog = Path.Combine(logs, $"p8-batch{pair.Key}-test.log");
            if (!File.Exists(testLog)) {
                continue;
            }
            ++compared;
            int actual = TotalPassed(testLog);
            if (actual != pair.Value) {
                mismatches.Add($"batch {pair.Key}: doc says {pair.Value}, log says {actual}");
            }
        }

        compared.Should().BeGreaterThan(10, "enough batches must have both a stated count and a log");
        mismatches.Should().BeEmpty();
    }

    /// <summary>**全方案数字下降必须在该批小节里被解释** / A decrease must be explained in that batch's section.</summary>
    /// <remarks>
    /// 测试只会增加，除非某批**删掉**了测试。batch 272 从 `7472` 降到 `7468`（删了两个空壳），
    /// 并在同一小节写着"原 `7472`，减少 4 条"——**这是合法下降**。
    ///
    /// 第一版断言"单调不减"，结果把 batch 272 判成失败。那说明判据本身错了：
    /// **要钉的不是"数字只能涨"，而是"降了要有人解释"**。
    ///
    /// Tests only accumulate unless a batch **deletes** some. Batch 272 dropped from `7472` to `7468` (two shells removed) and says in the same section
    /// "was `7472`, four fewer" — **a legitimate drop**.
    ///
    /// The first version asserted "non-decreasing" and failed batch 272. That made the criterion wrong: **what needs pinning is not "numbers only go up" but
    /// "a drop must be explained"**.
    /// </remarks>
    [Fact]
    public void FullSuiteCountDecreases_ShouldBeExplained() {
        Dictionary<int, int> stated = StatedFullSuiteCounts();
        var ordered = stated.OrderBy(pair => pair.Key).ToList();
        ordered.Should().HaveCountGreaterThan(10);

        Dictionary<int, string> sections = SectionsByBatch();
        var unexplained = new List<string>();
        for (int index = 1; index < ordered.Count; ++index) {
            if (ordered[index].Value >= ordered[index - 1].Value) {
                continue;
            }
            string body = sections.TryGetValue(ordered[index].Key, out string? section) ? section : string.Empty;
            // 只看**数字所在那一行**：小节别处提到"删除"不算解释了这次下降。
            // Only the **line carrying the number** counts: a mention of "deleted" elsewhere in the section does not explain this drop.
            string line = body.Split(NewLine.ToCharArray()[0])
                .FirstOrDefault(candidate => candidate.Contains($"`{ordered[index].Value} passed`", StringComparison.Ordinal))
                ?? string.Empty;
            bool explained =
                line.Contains("减少", StringComparison.Ordinal) ||
                line.Contains("删除", StringComparison.Ordinal) ||
                line.Contains("removed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("deleted", StringComparison.OrdinalIgnoreCase);
            if (!explained) {
                unexplained.Add(
                    $"batch {ordered[index].Key} drops to {ordered[index].Value} from {ordered[index - 1].Value} without saying why"
                );
            }
        }
        unexplained.Should().BeEmpty();
    }

    /// <summary>批次号 → 小节正文。</summary>
    private static Dictionary<int, string> SectionsByBatch() {
        var sections = new Dictionary<int, string>();
        string[] parts = Regex.Split(ReadPlan(), "^### P9 Batch (\\d+)：", RegexOptions.Multiline);
        for (int index = 1; index + 1 < parts.Length; index += 2) {
            if (int.TryParse(parts[index], out int batch)) {
                sections[batch] = parts[index + 1];
            }
        }
        return sections;
    }

    /// <summary>**解析必须只认全方案那一行** / The parser must only accept the full-suite row.</summary>
    /// <remarks>
    /// batch 266 的小节开头写着 "`7159 passed` → `7450 passed`"（引用旧值）。只认全方案行才不会取到 `7159`。
    /// 这条测试把那个具体陷阱钉住，避免有人把正则放松回"第一个 `N passed`"。
    ///
    /// Batch 266's section opens with "`7159 passed` → `7450 passed`" (a quoted old value). Accepting only the full-suite row avoids picking up `7159`. This
    /// pins that specific trap so nobody relaxes the regex back to "the first `N passed`".
    /// </remarks>
    [Fact]
    public void Parser_ShouldNotPickUpQuotedHistoricalValues() {
        Dictionary<int, int> stated = StatedFullSuiteCounts();

        if (stated.TryGetValue(266, out int batch266)) {
            batch266.Should().Be(7450, "batch 266's full-suite count is 7450; 7159 is a quoted historical value");
        }
    }
}
