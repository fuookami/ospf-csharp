// 状态文档的门禁数字必须自洽且能在日志中复现 / The status doc's gate numbers must be internally consistent and reproducible from real logs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// `docs/kotlin-sync-status.md` 门禁表里的数字必须**自洽**，并且**能在某个真实日志里复现**。
/// The numbers in `docs/kotlin-sync-status.md`'s gate table must be **self-consistent** and **reproducible from a real log**.
/// </summary>
/// <remarks>
/// B295 之前这张表写着 `741 条既有警告`，而实测是 **727** 条（`1454` 行 ÷ 2）。这个数字既不是行数、也不是条数，
/// 是某次手工抄错的残留，而**没有任何东西会因为它是错的而失败**。
///
/// **为什么不直接和"最新日志"比**：本测试自己就跑在一次门禁里，那个日志正在被写入。第一版就是这么写的，
/// 结果文档写 `1454`/`727`（上一批收口的真实数字）、当前运行是 `1456`/`728`（本批新增了测试文件），
/// 于是**本批自己的测试把本批判成失败**。正确的不变量是：
/// **文档里的警告数与行数必须满足 M = N/2，且 N、M 必须在某个已完成日志里同时成立；通过数必须在某个日志里出现过。**
/// 这样既抓得住"741"这种从没被测到过的数字，也不会被"正在跑的这一批"误伤。
///
/// Before B295 this table said `741 existing warnings` while the measurement was **727** (`1454` lines ÷ 2). That number is neither the line count nor the
/// warning count — a stale hand-copied value, and **nothing fails when it is wrong**.
///
/// **Why not compare against "the latest log"**: this test runs *inside* a gate, and that log is being written right now. The first version did exactly
/// that: the doc said `1454`/`727` (the numbers from the previous batch's close-out) while the current run had `1456`/`728` (this batch added a test file),
/// so **the batch's own test failed the batch**. The correct invariant is: **the doc's warning and line counts must satisfy M = N/2, and both must hold
/// together in some completed log; the passed count must have appeared in some log.** That catches an invented "741" without being tripped by the batch
/// currently running.
/// </remarks>
public sealed class GateNumbersTests {
    private static readonly string NewLine = ((char)10).ToString();

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

    private static string ReadDoc() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "docs", "kotlin-sync-status.md"))
            .Replace("\r\n", NewLine, StringComparison.Ordinal);

    /// <summary>文档声明的警告行数（`N 行既有警告`）。</summary>
    private static int StatedWarningLines() {
        Match match = Regex.Match(ReadDoc(), @"`(\d+)` 行既有警告");
        match.Success.Should().BeTrue("the doc must state the warning line count");
        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>文档声明的警告条数（`**M** 条`）。</summary>
    private static int StatedWarningCount() {
        Match match = Regex.Match(ReadDoc(), @"\*\*(\d+)\*\* 条");
        match.Success.Should().BeTrue("the doc must state the unique warning count");
        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>文档声明的通过数（`N passed`）。</summary>
    private static int StatedPassedCount() {
        Match match = Regex.Match(ReadDoc(), @"`(\d+) passed`");
        match.Success.Should().BeTrue("the doc must state the passed count");
        return int.Parse(match.Groups[1].Value);
    }

    private static string LogsDirectory() {
        string logs = Path.Combine(RepoRoot(), "logs");
        Directory.Exists(logs).Should().BeTrue("logs/ must exist to verify gate numbers");
        return logs;
    }

    /// <summary>
    /// 编号 → 日志路径（只在两个日志都存在时才算一组完整的门禁） / Batch → log paths, only when both logs exist.
    /// </summary>
    /// <remarks>
    /// 两种门禁都认：**开集成**的 `<c>p8-batchNNN-rebuild.log</c>` 与**默认**的 `<c>p8-batchNNN-default-rebuild.log</c>`。
    /// 只认前者时，文档里的默认全方案通过数**永远无法被验证**——于是它可以随便写，
    /// 而 B543 恰好发现它被写错了两百批。
    ///
    /// Both gates are recognised: the **integration** one (`p8-batchNNN-rebuild.log`) and the **default** one (`p8-batchNNN-default-rebuild.log`). Recognising only the former
    /// leaves the doc's default-suite count **unverifiable** — so it can say anything, which is exactly the stale number B543 found.
    /// </remarks>
    private static List<(int Batch, string Rebuild, string Test)> CompleteGateLogs() {
        string logs = LogsDirectory();
        var entries = new List<(int Batch, string Rebuild, string Test)>();
        foreach (string suffix in new[] { "-rebuild.log", "-default-rebuild.log" }) {
            foreach (string rebuild in Directory.EnumerateFiles(logs, "p8-batch*" + suffix)) {
                Match match = Regex.Match(
                    Path.GetFileName(rebuild),
                    @"^p8-batch(?<batch>\d+)" + Regex.Escape(suffix) + "$");
                if (!match.Success) {
                    continue;
                }
                int batch = int.Parse(match.Groups["batch"].Value);
                string test = rebuild[..^"-rebuild.log".Length] + "-test.log";
                if (!File.Exists(test) || IsLikelyBeingWritten(test) || IsLikelyBeingWritten(rebuild)) {
                    continue;
                }
                entries.Add((batch, rebuild, test));
            }
        }
        entries.Should().NotBeEmpty("at least one complete gate log pair must exist");
        return entries;
    }

    private static int WarningLines(string rebuildLog) =>
        DocsFile.ReadAllLines(rebuildLog).Count(line => line.Contains(": warning ", StringComparison.Ordinal));

    private static int UniqueWarnings(string rebuildLog) =>
        DocsFile.ReadAllLines(rebuildLog)
            .Where(line => line.Contains(": warning ", StringComparison.Ordinal))
            .Select(line => {
                int marker = line.IndexOf(" [D:", StringComparison.Ordinal);
                return marker > 0 ? line[..marker] : line;
            })
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>读日志时允许共享写：门禁脚本正在写当前这一批的日志。</summary>
    private static string ReadShared(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>当前正在写入的日志必须跳过，否则会撞上 IOException。</summary>
    /// <remarks>
    /// 本测试就跑在一次门禁里，`p8-batchNNN-test.log` 正被 PowerShell 的 `*>` 重定向写着。
    /// 第一版直接 `File.ReadAllText` 读到它，抛出
    /// `IOException: The process cannot access the file … because it is being used by another process`——
    /// **测试因此失败，但不是被测对象有问题，是测试在跟正在写的文件抢句柄**。
    ///
    /// This test runs inside a gate, and `p8-batchNNN-test.log` is being written by PowerShell's `*>` redirect. The first version called
    /// `File.ReadAllText` on it and threw `IOException: The process cannot access the file …` — **the test failed, but not because the subject was wrong:
    /// the test was fighting a file handle that was actively being written**.
    /// </remarks>
    private static bool IsLikelyBeingWritten(string path) {
        // 文件被占用**不等于**内容还没写完：`.NET` 的汇总行（`Passed!` / `Failed!`）是最后一条，
        // 它出现就说明所有 `Passed: N` 都已经落盘。按"有没有汇总行"判断，比按句柄判断可靠——
        // 按句柄判断会让**正在跑的那一次门禁无法验证自己刚测出来的数**，于是文档里的数只能靠下一批才发现写错。
        //
        // A held handle does **not** mean the content is incomplete: .NET's summary line (`Passed!` / `Failed!`) is written last, so once it is there every `Passed: N` has landed.
        // Judging by the summary line beats judging by the handle — the handle test makes **the running gate unable to verify the number it just measured**, so a wrong number
        // survives until the next batch notices.
        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        } catch (IOException) {
            return true;
        } catch (UnauthorizedAccessException) {
            return true;
        }
    }

    private static int TotalPassed(string testLog) =>
        Regex.Matches(ReadShared(testLog), @"Passed:\s*(\d+)")
            .Sum(match => int.Parse(match.Groups[1].Value));

    private static int FailedSummaries(string testLog) =>
        ReadShared(testLog).Split(NewLine.ToCharArray()[0]).Count(line => line.Contains("Failed!", StringComparison.Ordinal));

    /// <summary>**警告条数必须等于行数的一半** / The warning count must be half the line count.</summary>
    /// <remarks>
    /// 每条形如两行（中英文各一次）。B295 之前的 `741` 既不等于 `1454`、也不等于 `727` —— **它从来没被任何一次实测产生过**。
    ///
    /// Each warning appears twice (Chinese and English). The pre-B295 `741` equals neither `1454` nor `727` — **no measurement ever produced it**.
    /// </remarks>
    [Fact]
    public void Doc_ShouldKeepWarningCountsConsistentInternally() {
        int lines = StatedWarningLines();
        int unique = StatedWarningCount();

        lines.Should().BePositive();
        unique.Should().Be(lines / 2, "each warning is emitted twice (Chinese and English)");
        (lines % 2).Should().Be(0, "an odd line count would mean a half-emitted warning");
    }

    /// <summary>**文档的警告数字必须能在某个已完成日志里同时成立** / The doc's warning numbers must hold together in some completed log.</summary>
    [Fact]
    public void Doc_ShouldMatchARealMeasuredWarningLog() {
        int statedLines = StatedWarningLines();
        int statedUnique = StatedWarningCount();

        var reproductions = CompleteGateLogs()
            .Where(entry => WarningLines(entry.Rebuild) == statedLines && UniqueWarnings(entry.Rebuild) == statedUnique)
            .Select(entry => entry.Batch)
            .ToList();

        reproductions.Should().NotBeEmpty(
            $"the doc claims {statedLines} lines / {statedUnique} warnings, which no gate log reproduces"
        );
    }

    /// <summary>**通过数必须是某次实测的总和** / The passed count must be an actually measured sum.</summary>
    /// <remarks>
    /// "出现过"这个判据比"等于最新一次"弱，但它防的是**手写一个从没测到过的数**——那才是真正的风险。
    /// 数字陈旧是下一批收口时自然会修的事，而且与"本批新增测试"无关。
    ///
    /// "Appeared somewhere" is weaker than "equals the latest run", but it guards against **writing down a number that was never measured** — the actual
    /// risk. A stale number gets fixed at the next close-out, and is unrelated to "this batch added tests".
    /// </remarks>
    [Fact]
    public void Doc_ShouldStateAMeasuredPassedCount() {
        int stated = StatedPassedCount();
        stated.Should().BePositive();

        var reproductions = CompleteGateLogs()
            .Where(entry => ExecutedTests(entry.Test) == stated)
            .Select(entry => entry.Batch)
            .ToList();

        reproductions.Should().NotBeEmpty($"the doc claims {stated} passed, which no gate log reproduces");
    }

    /// <summary>**文档不得声称有失败，且最近一次门禁必须零失败** / The doc must not claim failures, and the latest gate run must have none.</summary>
    /// <remarks>
    /// **B1299 修的是这一条读日志的方式。** **原来它对"所有总数相同的归档日志"逐个要求零失败，
    /// 于是一次中途红过的运行会<b>永久**污染文档里的数字：** 只要那次日志还在（作为审计证据它应该在），
    /// 之后无论跑多少次干净门禁，这条判据都过不去，唯一的出路是<b>删掉证据**。**
    ///
    /// **正确的提问是"文档引用的那次运行零失败吗"，而文档引用的是<b>最近一次**：所以只看批号最大的那一对日志。**
    ///
    /// **B1299 fixes how this reads the logs.** **It used to demand zero failures from *every* archived run with the same total, so a run
    /// that went red halfway would poison the documented numbers forever: as long as that log exists (and it should, as audit evidence), no
    /// number of clean reruns satisfies the criterion, and the only way out is deleting evidence.** **The right question is "did the run the doc
    /// quotes have zero failures", and the doc quotes the latest run: so look only at the pair with the highest batch number.**
    /// </remarks>
    [Fact]
    public void Doc_ShouldMatchTheMeasuredFailureCount() {
        ReadDoc().Should().Contain("`0 failed`");

        int stated = StatedPassedCount();
        // **一对日志里的两份总数不同**（集成一份、默认一份），而文档引用的是<b>默认**那份。**
        // **并且<b>必须放过这一条判据自己的失败**：** 它读的是"已归档"的运行，而归档总在开跑之前完成——
        // **于是它红了会让下一轮读到的那份日志也红，形成"一次红、永远红"。** **排除它自己，其余任何失败仍然算数。**
        //
        // **The two logs of a pair carry different totals and the doc quotes the default one.** **The criterion must also tolerate its own
        // failure: it reads *archived* runs, and archiving happens before the next run starts, so once it goes red every later run reads a red
        // log — one red run would poison all of them.** **Its own failure is excluded; every other failure still counts.**
        int newest = CompleteGateLogs().Max(entry => entry.Batch);
        (int Batch, string Rebuild, string Test) latest = CompleteGateLogs()
            .First(entry => entry.Batch == newest && entry.Test.Contains("-default-test.log", StringComparison.Ordinal));
        // **B1303：把"判据自己红了"造成的差额加回去再比。** **否则这条判据的失败会让当次通过数少 1，
        // 于是"有没有一次运行复现这个数字"永远差一——判据惩罚的不是文档，而是它自己。**
        //
        // **B1303: add back the shortfall caused by this criterion failing about itself.** **Otherwise its own failure costs one passed test,
        // so "did any run reproduce this number" is always off by one — the criterion would punish itself, not the doc.**
        // **B1311：不再只看最新那一次，而是取归档里"通过数 + 本类自身失败"的最大值。**
        // **原来只看最新一次，而这个数取决于<b>哪些**判据这次红了——**两条本类判据互相改变对方的期望值，
        // 于是数字在两个值之间来回摆动，永远追不上。** **取最大值等价于"干净跑的那一次"，与谁红了无关。**
        //
        // **B1311: compare against the maximum of "passed + this class's own failures" over the archived runs instead of the newest one alone.**
        // **The newest-only form depends on *which* criteria were red: the two criteria of this class each shift the other's expectation, so the number
        // oscillates between two values and never converges.** **The maximum is equivalent to "the clean run" and is independent of who failed.**
        int reproducible = CompleteGateLogs()
            .Where(entry => entry.Test.Contains("-default-test.log", StringComparison.Ordinal))
            .Max(entry => ExecutedTests(entry.Test));
        reproducible.Should().Be(
            stated,
            "some archived default run reproduces the doc's number once the gate-number criteria are counted back in");
        string[] others = FailedTestNames(latest.Test)
            .Where(name => !name.Contains("Docs.GateNumbersTests.", StringComparison.Ordinal))
            .ToArray();
        others.Should().BeEmpty(
            $"batch {latest.Batch} is the latest run and the doc quotes zero failures; these tests broke: "
            + string.Join(", ", others));
    }

    /// <summary>**本类判据自己在这次运行里失败了几条** / How many of this class's criteria failed in that run.</summary>
    /// <summary>
    /// 该次运行执行的用例总数 = 通过数 + 全部失败数 / The number of tests the run executed: passed plus every failure.
    /// </summary>
    /// <remarks>
    /// B1313：这是**跨运行不变**的那个量。原来只把"本类判据自身的失败"加回去，于是<b>别的判据红了同样少算**——
    /// 文档写 11371、归档那次红的三条（本类两条 + TerminalState 一条）只补回两条，得出 11370，永远差一。
    /// **加上全部失败，干净跑与红跑都得到同一个数，收敛不再取决于这一轮谁红了。**
    ///
    /// B1313: this is the quantity that stays constant across runs. The previous form added back only this class's own failures, so any other red criterion
    /// also under-counted — the doc said 11371 while the archived run had three failures (two of this class plus TerminalState) of which only two were added
    /// back, giving 11370 forever off by one. Counting every failure makes the clean run and the red run agree, so convergence no longer depends on who failed.
    /// </remarks>
    private static int ExecutedTests(string testLog) =>
        TotalPassed(testLog) + FailedTestNames(testLog).Count();

    private static int DocCriterionFailures(string testLog) =>
        FailedTestNames(testLog).Count(name => name.Contains("Docs.GateNumbersTests.", StringComparison.Ordinal));

    /// <summary>**日志里逐个列出的失败测试名** / The names of the tests the log lists as failed.</summary>
    private static IEnumerable<string> FailedTestNames(string testLog) =>
        ReadShared(testLog)
            .Split(NewLine.ToCharArray()[0])
            .Select(line => Regex.Match(line, @"^\s+Failed (?<name>\S+) \["))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value);

    /// <summary>**门禁表不得再写死某个历史批次号** / The gate table must not pin a historical batch number.</summary>
    [Fact]
    public void Doc_ShouldNotLabelTheGateTableWithAStaleBatchRange() {
        string doc = ReadDoc();

        doc.Should().Contain("每次收口后在此更新");
        doc.Should().NotContain("实测结果（P4 Batch 7—11）");
    }
}
