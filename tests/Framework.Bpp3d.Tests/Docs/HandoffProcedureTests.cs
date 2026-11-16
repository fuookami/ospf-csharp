#nullable enable

using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 交接流程里那几条数字不能落后于实测 / The handoff numbers must not fall behind the measurement.
/// </summary>
/// <remarks>
/// **纯文档，无新增源码。** 它修的是交接清单本身。
///
/// 6.4 那三条是"下一会话最该读的三句"，而它们的计数**停在第 8 次和第 6 次**，
/// **实测已到第 12 次。** 一个写着"出现过 8 次"的清单，读起来像一个已经收敛的问题——
/// **而它其实还在以每三四批一次的频率发生。**
///
/// **No source change.** It fixed the handoff checklist itself. Those three sentences are "what the next session most needs to read", and their counts **stopped at eight and six** while **the
/// measurement reached twelve.** A list saying "this happened eight times" reads like a settled problem — **while it is still happening once every three or four batches.**
/// </remarks>
public class HandoffProcedureTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";

    /// <summary>仓库根目录 / The repository root.</summary>
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

    /// <summary>**那四批必须真的记在缺口台账里** / Those four batches have to be recorded in the inventory.</summary>
    /// <remarks>
    /// **清单点名了四批，而台账必须能对上。** 一个只在清单里出现的批次号，
    /// **和一句"我记得有过"没有区别。**
    ///
    /// **The checklist names four batches, and the inventory has to agree.** A batch id that appears only in the checklist **is indistinguishable from "I remember it happening".**
    /// </remarks>
    [Fact]
    public void TheNamedBatches_ShouldAppearInTheInventory()
    {
        string inventory = File.ReadAllText(
            Path.Combine(Root().FullName, "logs/kotlin-sync-gap-inventory.md"));

        foreach (string batch in new[] { "B1040", "B1043", "B1051", "B1054" })
        {
            inventory.Should().Contain(batch,
                $"{batch} is named as an occurrence, so the inventory has to carry it too");
        }
    }

}
