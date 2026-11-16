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
/// **纯文档，无新增源码。** 它把那份逐名判定写进台账并钉住。
///
/// **那 16 个名字的判定原本只活在这几批的叙述里。** 下一会话读不到叙述，
/// **于是要么重做一遍扫描，要么——更糟——相信"已经全搬完了"。**
///
/// **此前会这么想是有道理的**：扫描确实跑过，**而"跑过"和"结论留下来"是两件事。**
///
/// **No source change.** It wrote the verdicts into the ledger and pinned them.
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
    /// **§三的行也含"非缺口"三个字**，全表扫会把 19 行基线表里的行也算进来。
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

}
