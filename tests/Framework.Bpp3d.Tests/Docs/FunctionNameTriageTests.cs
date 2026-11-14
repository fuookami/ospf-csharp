#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 逐名判定表不能变成传说 / The name-by-name verdicts must not become folklore.
/// </summary>
/// <remarks>
/// **B1064 没有新增源码。** 它把 B1059—B1063 那份逐名判定写进台账并钉住。
///
/// **那 16 个名字的判定原本只活在这几批的叙述里。** 下一会话读不到叙述，
/// **于是要么重做一遍扫描，要么——更糟——相信"已经全搬完了"。**
///
/// **第二批会这么想是有道理的**：扫描确实跑过，**而"跑过"和"结论留下来"是两件事。**
///
/// **B1064 added no source code.** It wrote the B1059—B1063 verdicts into the ledger and pinned them.
/// **Those sixteen verdicts lived only in the narration of those batches.** The next session cannot read narration, **so it either redoes the sweep or — worse — believes everything is already ported.**
/// **That second belief is reasonable**: the sweep did run, **and "it ran" is not "the conclusion survived".**
/// </remarks>
public class FunctionNameTriageTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";

    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }
        return directory;
    }

    private static string Ledger() => File.ReadAllText(Path.Combine(Root().FullName, LedgerPath));

    /// <summary>只看 §6.5 那一段 / Only the 6.5 slice.</summary>
    /// <remarks>
    /// **§三 的行也含"非缺口"三个字**，全表扫会把 19 行基线表里的行也算进来。
    /// **判据必须框在自己的段落里，否则它在数别的东西。**
    ///
    /// **Rows in §3 also contain the words "非缺口"**, so scanning the whole ledger counts rows from the 19-row baseline table. **A criterion has to stay inside its own section, or it is counting something else.**
    /// </remarks>
    private static string[] SectionSixFiveRows() {
        string ledger = Ledger();
        int start = ledger.IndexOf("### 6.5 ", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the triage table has to exist");
        int end = ledger.IndexOf("### 6.4", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? ledger.Substring(start) : ledger.Substring(start, end - start);

        return section
            .Split((char)10)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>判定表必须存在且行数对得上 / The table has to exist with the right number of rows.</summary>
    /// <remarks>
    /// **16 行是扫描数出来的，不是估的。** 少一行意味着某个名字的判定被删掉了，
    /// **而删掉的那行恰恰会是最难判的那个。**
    ///
    /// **Sixteen is what the sweep counted, not an estimate.** A missing row means one verdict was deleted, **and the deleted one is always the hardest.**
    /// </remarks>
    [Fact]
    public void TheTriageTable_ShouldCarryEveryScannedName()
    {
        string[] rows = SectionSixFiveRows();

        rows.Should().HaveCount(12,
            "the sweep produced sixteen names, and the table groups them into twelve verdict rows");
    }

    /// <summary>每个"缺口"都必须点名补它的批次 / Every gap row has to name the batch that closed it.</summary>
    /// <remarks>
    /// **一个写着"缺口"而没有批次号的行是死路**：读的人知道缺，
    /// **却不知道是已经补了还是仍在缺——而这两种状态需要的动作完全相反。**
    ///
    /// **A gap row without a batch id is a dead end**: the reader learns something is missing, **but not whether it was since closed — and those two states call for opposite actions.**
    /// </remarks>
    [Fact]
    public void TheGapRows_ShouldNameTheBatchThatClosedThem()
    {
        string[] gapRows = SectionSixFiveRows()
            .Where(line => line.Contains("**缺口**", StringComparison.Ordinal))
            .ToArray();

        gapRows.Should().NotBeEmpty("the sweep found real gaps");
        foreach (string row in gapRows)
        {
            row.Should().MatchRegex(@"\*\*B10\d\d\*\*",
                "a gap row has to say which batch closed it");
        }
    }

    /// <summary>有意分歧必须写出来 / A deliberate divergence has to be printed.</summary>
    /// <remarks>
    /// `BlockingSummary()` 的英文半句在基线里插了中文值，**输出会是"Minimal blocking set (已验证最小)"。**
    /// C# 按语言分开取词。**不写下来，下次就会被当成"不一致"再"修回去"。**
    ///
    /// The baseline interpolates a Chinese value into the English half of `BlockingSummary()`, **producing "Minimal blocking set (已验证最小)".** C# picks the word per language. **Unwritten, the next session reads it as an
    /// inconsistency and "fixes" it back.**
    /// </remarks>
    [Fact]
    public void TheDeliberateDivergence_ShouldBeStated()
    {
        string ledger = Ledger();

        ledger.Should().Contain("一条已知的有意分歧",
            "a divergence from the baseline has to be labelled as one, not buried");
        ledger.Should().Contain("Minimal blocking set (已验证最小)",
            "the exact broken output has to be quoted, or the note reads as a style preference");
    }
}
