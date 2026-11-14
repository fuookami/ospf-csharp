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
            .Where(entry => TotalPassed(entry.Test) == stated)
            .Select(entry => entry.Batch)
            .ToList();

        reproductions.Should().NotBeEmpty($"the doc claims {stated} passed, which no gate log reproduces");
    }

    /// <summary>**文档不得声称有失败，且它引用的那次运行必须零失败** / The doc must not claim failures, and the run it quotes must have none.</summary>
    [Fact]
    public void Doc_ShouldMatchTheMeasuredFailureCount() {
        ReadDoc().Should().Contain("`0 failed`");

        int stated = StatedPassedCount();
        foreach ((int Batch, string Rebuild, string Test) entry in CompleteGateLogs().Where(entry => TotalPassed(entry.Test) == stated)) {
            FailedSummaries(entry.Test).Should().Be(0, $"batch {entry.Batch} is the run the doc quotes");
        }
    }

    /// <summary>**门禁表不得再写死某个历史批次号** / The gate table must not pin a historical batch number.</summary>
    [Fact]
    public void Doc_ShouldNotLabelTheGateTableWithAStaleBatchRange() {
        string doc = ReadDoc();

        doc.Should().Contain("每次收口后在此更新");
        doc.Should().NotContain("实测结果（P4 Batch 7—11）");
    }
}
