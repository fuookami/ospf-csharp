#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **警告预算：最近一次 Release 重建不得越过 1316 行** / The warning budget: the newest Release rebuild must not cross 1316 lines.
/// </summary>
/// <remarks>
/// **这个数字是"我们没在制造新警告"的唯一证据。** **它原来记在一份人工维护的状态文档里，** '
/// **而人工维护的数字会过期（曾经差 30 行、抄了很多批没人核）；** **现在它直接从门禁归档的重建日志里数出来，** '
/// **所以它只有一个来源：真的跑过的那一次。**
///
/// **口径是"不越过"而不是"等于"：** **上限是 1316，** **而每次重建的实测可能在 1312–1316 之间摆动——** '
/// **那 4 条诊断只在被重新分析的编译单元上重发，** **"少几条"不是改进，"多几条"才是退步。**
///
/// **This figure is the only evidence that no new warnings are being created.** It used to live in a hand-maintained status document, and hand-maintained figures go stale (one was thirty lines
/// off for many batches). **It is now counted straight from the archived gate rebuild log, so it has exactly one source: the run that actually happened.**
///
/// **The rule is "does not exceed" rather than "equals": the ceiling is 1316, and a given rebuild may land anywhere in 1312–1316 —** those four diagnostics only re-fire on compilation units
/// that are re-analysed, **so "a few fewer" is not an improvement and "a few more" is a regression.**
/// </remarks>
public class WarningBudgetTests
{
    /// <summary>**最新归档重建的警告行数不超过 1316** / The newest archived rebuild stays within 1316 warning lines.</summary>
    [Fact]
    public void TheNewestRebuild_ShouldNotExceedTheWarningBudget() {
        string logs = Path.Combine(RepositoryRoot(), "logs");
        List<string> rebuilds = Directory.GetFiles(logs, "p8-batch*-rebuild.log").ToList();
        rebuilds.Should().NotBeEmpty("the gate archives its Release rebuild log");

        string newest = rebuilds
            .OrderByDescending(ArchiveNumber, Comparer<int>.Default)
            .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
            .First();
        int lines = File.ReadAllLines(newest).Count(line => line.Contains(": warning ", StringComparison.Ordinal));

        lines.Should().BeLessThanOrEqualTo(1316,
            "1316 is the ceiling for the Release rebuild; the newest archive is " + Path.GetFileName(newest));
        lines.Should().BeGreaterThan(0, "a rebuild log with no warning lines means the log was not captured");
    }

    private static int ArchiveNumber(string path) {
        // 文件名形如 `p8-batch1410-rebuild.log`：取 `batch` 与紧跟的数字。
        string name = Path.GetFileName(path);
        int index = name.IndexOf("batch", StringComparison.Ordinal);
        if (index < 0) {
            return 0;
        }
        int cursor = index + "batch".Length;
        int start = cursor;
        while (cursor < name.Length && char.IsDigit(name[cursor])) {
            ++cursor;
        }
        return cursor > start && int.TryParse(name[start..cursor], out int number) ? number : 0;
    }

    private static string RepositoryRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (Directory.Exists(Path.Combine(directory, "logs"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("the repository root (with a logs directory) was not found");
    }
}