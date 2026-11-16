#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 剩下 8 个缺口的记账 / The ledger for the remaining eight gaps.
/// </summary>
/// <remarks>
/// **没有新增源码**，它把剩下 8 个缺口逐条记账并钉住数字。
///
/// 判据钉三件事：**表格里每一项都在**、**数字与实测一致**、**口径的局限被写下来**。
/// **第三条最容易被后来的人删掉**——因为它读起来像自我批评，而它其实是这条表能被相信的唯一原因。
///
/// added no source code**; it booked the remaining eight gaps and pinned the numbers. The criteria pin three things: **every row is present**, **the numbers match the
/// measurement**, and **the limitation of the proxy is written down**. **The third is the one a later reader is most likely to delete**, because it reads as self-criticism —
/// and it is the only reason the table can be trusted at all.
/// </remarks>
public class RemainingGapLedgerTests {
    private const string MatrixPath = "docs/kotlin-capability-matrix.md";

    private static string Matrix() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, MatrixPath));
    }

    private static string Row(string document, string file) =>
        document.Split('\n').FirstOrDefault(line => line.StartsWith("| `" + file + "`", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("no ledger row for " + file);

    /// <summary>**8 个缺口每一条都在表里** / All eight gaps have a row.</summary>
    /// <remarks>
    /// 少一行意味着那条缺口**又回到了"不知道是啥"的状态**——而这正是这批要消除的东西。
    ///
    /// A missing row puts that gap **back into "nobody knows what it is"**, which is exactly what exists to remove.
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldListEveryRemainingGap() {
        string matrix = Matrix();
        string[] gaps = [
            "ConflictAnalyzer",
            "ConstraintPerturbationAnalyzer",
            "TargetFeasibilityAnalyzer",
            "CriticalConstraintAnalysisPipeline",
            "ConstraintProgrammingAnalysisBackends",
            "GurobiInfeasibilityAnalyzers",
            "ScipInfeasibilityAnalyzers",
        ];

        foreach (string gap in gaps) {
            Row(matrix, gap).Should().NotBeNull("every remaining gap needs a row, or it is unaccounted for again");
        }
        gaps.Should().HaveCount(7,
            "**B973 补掉了 `CriticalConstraintAnalysisSession`**，所以这张表跟着少一行——它记的是剩余项，不是历史");
    }

    /// <summary>**每一行都要写明缺哪个前置件** / Every row names what it is missing.</summary>
    /// <remarks>
    /// "缺"这一个字没有信息量：**它会让人以为再试一次就行**。写清缺哪个类型，才看得出是"要真实后端"还是"顺手就能补"。
    ///
    /// The bare word "missing" carries no information: **it suggests one more attempt would do**. Naming the prerequisite is what separates "needs a real backend" from
    /// "a quick fill-in".
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNameTheMissingPrerequisite() {
        string matrix = Matrix();
        string[] gaps = [
            "ConflictAnalyzer",
            "ConstraintPerturbationAnalyzer",
            "TargetFeasibilityAnalyzer",
            "CriticalConstraintAnalysisPipeline",
            "ConstraintProgrammingAnalysisBackends",
            "GurobiInfeasibilityAnalyzers",
            "ScipInfeasibilityAnalyzers",
        ];

        foreach (string gap in gaps) {
            string row = Row(matrix, gap);
            // 前置件栏非空：表格最后一列不能是 `| |`。
            row.TrimEnd().TrimEnd('|').Split('|').Last().Trim()
                .Should().NotBeEmpty(gap + " has to name the prerequisite it is missing");
        }
    }

    /// <summary>**数字与实测一致** / The numbers match the measurement.</summary>
    /// <remarks>
    /// 行数是这条表唯一可以核对的量。**它一旦和基线不符，整张表就只是印象。**
    ///
    /// The line counts are the only checkable quantity here. **Once they disagree with the baseline the whole table is just an impression.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldCarryTheMeasuredLineCounts() {
        string matrix = Matrix();

        Row(matrix, "ConflictAnalyzer").Should().Contain("918");
        Row(matrix, "ConstraintPerturbationAnalyzer").Should().Contain("959");
        Row(matrix, "TargetFeasibilityAnalyzer").Should().Contain("915");
        Row(matrix, "CriticalConstraintAnalysisPipeline").Should().Contain("359");
        Row(matrix, "ConstraintProgrammingAnalysisBackends").Should().Contain("270");
        Row(matrix, "GurobiInfeasibilityAnalyzers").Should().Contain("518");
        Row(matrix, "ScipInfeasibilityAnalyzers").Should().Contain("278");

        // 补掉 Session 后，剩余 core 是 3421 行**；总数与原因都要在。
        // **With the session ported the remaining core is 3421 lines**; the total and the reason both have to be there.
        matrix.Contains("3421 行", StringComparison.Ordinal)
            .Should().BeTrue("the total has to be stated, or nobody can size the remainder");
    }

    /// <summary>**口径的局限必须写下来** / The limitation of the proxy has to be written down.</summary>
    /// <remarks>
    /// "求解引用"是 grep 命中数，**不是后端依赖的证明**： 就是被它纠正的——命中 0，但真正的理由是
    /// "只读快照与赋值"。
    ///
    /// The "solver refs" column is a grep hit count, **not proof of a backend dependency**: was corrected by exactly that — a score of 0, while the real reason was
    /// "only reads a snapshot and an assignment".
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldAdmitTheProxyIsNotProof() {
        string matrix = Matrix();

        matrix.Contains("它是代理，不是证据", StringComparison.Ordinal)
            .Should().BeTrue("a grep count presented without that caveat becomes a measurement it is not");
        matrix.Contains("就是被它纠正的", StringComparison.Ordinal)
            .Should().BeTrue("the caveat needs a concrete case, or it reads as boilerplate");
    }

}
