#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 台账里那句"跨语言"必须与搬进来的文件对得上 / The ledger's interop sentence must match the files behind it.
/// </summary>
/// <remarks>
/// **纯文档，无新增源码。** 它改的是台账里那一行——**"只做了一部分"这句话现在是可数的了。**
///
/// 行里写了"用例表 6 节共 56 行"。**56 是算出来的，不是数出来的**，
/// 所以它会**随契约一起变**：加上一节而台账不改，这句话就变成假话。
///
/// **一个写着"只做了一部分"的格子，读起来像一个已经交代过的欠账。**
/// **而它既没说哪部分是做的，也没说哪部分不是。** 现在两样都有了。
///
/// **No source change.** It changed one row in the ledger — **"only partly measured" is now countable.** The row says "56 rows in six sections", and **56 is computed, not eyeballed**, so it
/// **moves with the contract**: add a section without touching the ledger and the sentence becomes false. **A cell reading "only partly measured" reads like a debt already accounted for** — **while it
/// says neither which part is done nor which is not.** Now it says both.
/// </remarks>
public class InteropLedgerTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";
    private const string CasesPath = "tests/Core.Tests/Analysis/Fixtures/analysis-cases.tsv";
    private const string ContractPath = "tests/Core.Tests/Analysis/Fixtures/analysis-contract.tsv";
    private const string AcceptancePath = "docs/kotlin-sync-acceptance.md";

    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return directory;
    }

    private static string[] Lines(string relative) =>
        File.ReadAllLines(Path.Combine(Root().FullName, relative));

    /// <summary>台账里"跨语言互通实测"那一行 / The interop row of the ledger.</summary>
    private static string InteropRow() => Lines(LedgerPath)
        .Single(line => line.StartsWith("| 跨语言互通实测 |", StringComparison.Ordinal));

    /// <summary>用例表的数据行总数 / The total number of case rows.</summary>
    private static int CaseRowCount() => Lines(CasesPath)
        .Where(line => !line.StartsWith("[", StringComparison.Ordinal))
        .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
        .Count(line => line.Length > 0);

    /// <summary>用例表的节数 / The number of case sections.</summary>
    private static int CaseSectionCount() => Lines(CasesPath)
        .Count(line => line.StartsWith("[", StringComparison.Ordinal));

}
