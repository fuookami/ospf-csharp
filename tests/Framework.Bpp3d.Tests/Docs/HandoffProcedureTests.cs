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
/// **B1057 没有新增源码。** 它修的是交接清单本身。
///
/// 6.4 那三条是"下一会话最该读的三句"，而它们的计数**停在第 8 次和第 6 次**，
/// **实测已到第 12 次。** 一个写着"出现过 8 次"的清单，读起来像一个已经收敛的问题——
/// **而它其实还在以每三四批一次的频率发生。**
///
/// **B1057 added no source code.** It fixed the handoff checklist itself. Those three sentences are "what the next session most needs to read", and their counts **stopped at eight and six** while **the
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

    /// <summary>**计数必须写明它是哪一批更新的** / The count has to say which batch updated it.</summary>
    /// <remarks>
    /// **一个裸数字没有时间戳，读的人无法判断它是不是最新的。**
    /// 写成"实测已到第 N 次"加批次号，**下一批的人才知道该不该再数一遍。**
    ///
    /// **A bare number carries no timestamp, so a reader cannot tell whether it is current.** Writing "the measurement reached N" plus the batch id **tells the next session whether to recount.**
    /// </remarks>
    [Fact]
    public void TheCriterionFailureCount_ShouldCarryItsBatch()
    {
        string ledger = Ledger();

        ledger.Should().Contain("实测已到第 12 次",
            "the measurement reached twelve, and the checklist still said eight");
        ledger.Should().Contain("B1040 / B1043 / B1051 / B1054",
            "the four later occurrences have to be named, or the new count is as unsourced as the old one");
    }

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

    /// <summary>**已被推翻的说法不能再出现在待办表里** / A refuted claim may not stay on the to-do table.</summary>
    /// <remarks>
    /// 6.3 的第一行原本写"需要真实 conflict / perturbation 后端"。
    /// **B1041/1049/1050 已证明编排不需要后端**，那一行让下一会话以为这条路被许可证挡着。
    ///
    /// **这一条与 `BackendClaimHonestyTests` 互补**：那边查"类本体需要真实"这句，
    /// **这边查 6.3 的待办表本身**——两处是同一个低估的两个出口。
    ///
    /// Row one of 6.3 used to say "needs a real conflict / perturbation backend". **B1041/1049/1050 showed the orchestration needs none**, and that row made the next session believe a licence was the blocker.
    /// **This complements `BackendClaimHonestyTests`**: that one checks the sentence, **this one checks the to-do table itself** — two exits of the same underestimate.
    /// </remarks>
    [Fact]
    public void TheTodoTable_ShouldNotClaimTheOrchestrationIsMissing()
    {
        string ledger = Ledger();

        ledger.Should().NotContain("需要真实 conflict / perturbation 后端",
            "the orchestration is ported and measured against stubs; only the evidence source is missing");
        ledger.Should().Contain("只为**取到证据**，不是为写编排",
            "the replacement has to say what the real backend is actually for");
    }
}
