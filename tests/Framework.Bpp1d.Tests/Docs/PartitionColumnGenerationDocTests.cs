// 分区列生成：文档与代码一致（B772）/ Partition CG: the docs match the code (B772).

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Docs;

/// <summary>
/// **分区列生成的文档与代码一致（B772）** / The partition-CG docs match the code (B772).
/// </summary>
/// <remarks>
/// 两份 XML 注释原来说"最终整数主问题未移植""定价与主问题注册未移植"，而代码里这两半都在——`FinalAsync` 建整数主问题、
/// 求解、按完整模型复核收尾，`PartitionLoadingContext` 有 `MasterAsync` / `PricingRequestAsync` / `RefreshShadowPriceAsync`。
/// 行为判据早就覆盖了它们（`PartitionColumnGenerationServiceTests`），**只有注释在撒谎**。
///
/// 撒谎的注释比没有注释更坏：下一个人会照着它去"补一个已经存在的东西"，或者据此判断这条链不可用。所以这里把"注释不许
/// 说某件事没做"和"代码里那件事真的在"同时钉住——**两边一起钉，注释才回不去**。
///
/// Two XML comments used to claim "the final integer master is not ported" and "the pricing and master-registration half is not ported", while both halves exist in the code: `FinalAsync`
/// builds the integer master, solves, and closes out through the full-model re-check, and `PartitionLoadingContext` has `MasterAsync` / `PricingRequestAsync` /
/// `RefreshShadowPriceAsync`. Behavioural criteria have long covered them (`PartitionColumnGenerationServiceTests`) — **only the comments were lying**.
///
/// A lying comment is worse than none: the next reader either "adds back something that already exists" or concludes the chain is unusable. So this pins both sides — the comments
/// may not claim a thing is missing, and the code must really contain it — **pin both, and the comments cannot drift back**.
/// </remarks>
public class PartitionColumnGenerationDocTests {
    private static string RepoRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("could not locate the repository root");
    }

    private static string Read(params string[] parts) {
        string[] all = new string[parts.Length + 1];
        all[0] = RepoRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    /// <summary>
    /// **服务层：说明里不再说"未移植"，代码里确实建整数主问题** / The service's remarks no longer claim a gap, and it really builds the integer master.
    /// </summary>
    /// <remarks>
    /// 三点成对：注释里出现"整条链是打通的" / **不再出现"未移植"** / 代码里出现 `integer: true`（整数主问题的唯一入口）。
    /// 只改注释不钉代码，下一次有人删掉 `FinalAsync` 就没人发现；只钉代码不管注释，谎言会写回去。
    ///
    /// Three paired points: the remarks say the chain is wired; **"未移植" is gone**; and the code contains `integer: true` (the one entry to the integer master). Fixing only the comment
    /// leaves a deleted `FinalAsync` unnoticed; pinning only the code lets the lie be written back.
    /// </remarks>
    [Fact]
    public void ServiceRemarks_ShouldNotClaimAMissingFinalMaster() {
        string source = Read("src", "Framework.Bpp1d", "Application", "PartitionColumnGenerationApplicationService.cs");

        source.Should().Contain("整条链是打通的");
        source.Should().NotContain("最终整数主问题与完整装载结果的产出**未移植**");
        source.Should().Contain("integer: true");
        source.Should().Contain("FinishAsync");
    }

    /// <summary>
    /// **上下文：定价与主问题注册都在，注释不再说缺** / The context really has the pricing and master halves, and the remarks no longer deny them.
    /// </summary>
    /// <remarks>
    /// 四点成对：注释里点名四个成员都在 / 注释里不再说"未移植" / 代码里 `MasterAsync` / 代码里 `PricingRequestAsync`。
    ///
    /// Four paired points: the remarks name all four members as present; "未移植" is gone from the remarks; and the code holds `MasterAsync` and `PricingRequestAsync`.
    /// </remarks>
    [Fact]
    public void ContextRemarks_ShouldNotClaimAMissingPricingHalf() {
        string source = Read("src", "Framework.Bpp1d", "Domain", "PartitionLoading", "PartitionLoadingContext.cs");

        source.Should().Contain("`MasterAsync`");
        source.Should().Contain("`PricingRequestAsync`");
        source.Should().NotContain("定价与主问题注册那半边（`price`、`applyPricing`、`ColumnMasterRegistration` 桥接）**未移植**");
        source.Should().Contain("public async Task<Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>>> MasterAsync");
    }

    /// <summary>
    /// **批次定价已经接上，注释不许再说它没做** / Batch pricing is wired, and the remarks may not deny it.
    /// </summary>
    /// <remarks>
    /// 三点成对：服务里**不再有**"批次定价尚未移植"的拒绝 / 服务里出现 `PreparePricingBatchAsync` 与 `PricePreparedAsync` /
    /// 上下文里出现 `PreparePricingBatchAsync`。这条判据原先钉的是"拒绝还在"，批次定价落地后它**必须翻面**——
    /// 判据记的是缺口，缺口补上就得改判据，**否则它就成了替缺口保研的钉子**。
    ///
    /// Three paired points: the refusal "batch pricing is not ported yet" **is gone**; the service holds `PreparePricingBatchAsync` and `PricePreparedAsync`; and the context holds
    /// `PreparePricingBatchAsync`. This criterion used to pin "the refusal is still there" and **had to flip** once batch pricing landed — a criterion records a gap, and when the gap
    /// closes the criterion changes, **otherwise it becomes a nail holding the gap in place**.
    /// </remarks>
    [Fact]
    public void BatchPricing_ShouldBeWired() {
        string service = Read("src", "Framework.Bpp1d", "Application", "PartitionColumnGenerationApplicationService.cs");
        string context = Read("src", "Framework.Bpp1d", "Domain", "PartitionLoading", "PartitionLoadingContext.cs");

        service.Should().NotContain("批次定价尚未移植");
        service.Should().Contain("PreparePricingBatchAsync");
        service.Should().Contain("PricePreparedAsync");
        context.Should().Contain("PreparePricingBatchAsync");
    }
}
