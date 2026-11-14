// Demo4 服务层对齐的钉子（B755）/ Pins on Demo4's service-layer alignment (B755).

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **Demo4 服务层对齐的钉子（B755）** / Pins on Demo4's service-layer alignment (B755).
/// </summary>
/// <remarks>
/// 文件 stem 扫对 `service/` 无效——基线的 `CostCalculator.kt` 在 C# 侧整个合进了 `RuleServices.cs`。于是这一层只能**逐条人工核对**，
/// 而人工结论必须钉住，否则它下一次就又变成"凭印象说齐了"。
///
/// 三条钉子各管一件事：**缺的那些确实还缺**（补上时必须回来改表）、**已经齐的那些确实在**（不能靠删文件假装齐）、
/// **两个 17 行空壳确实已删**（它们的名字会让"乘客域/货物域"看起来已经在）。
///
/// A file-stem scan is useless for `service/`: the baseline's `CostCalculator.kt` is folded into C#'s `RulesServices.cs`. This layer can only be **checked by hand**, and a manual
/// conclusion has to be pinned or it degrades back into "it looked complete last time".
///
/// The three pins cover three things: **the missing ones are still missing** (the table must be updated when they land), **the aligned ones are really there** (you cannot fake it by
/// deleting files), and **the two 17-line empty shells are really gone** (their names made "passenger/cargo domain" look implemented).
/// </remarks>
public class Demo4ServiceInventoryTests {
    /// <summary>
    /// **清单里的"缺"必须真的还缺** / What the table calls missing must still be missing.
    /// </summary>
    /// <remarks>
    /// 三点成对：清单写着"服务层现缺 **0** 个" / 货物域那节**说明它无可移植**（空壳，不是已完成）/ **C# 侧没有 `Cargo` 目录**。
    /// 第二条是这个文件里最容易被误读的地方：**"没有缺口"与"已实现"不是一回事**。
    ///
    /// Three paired points: the table says the service layer is missing **nothing**; the cargo section **explains that there is nothing to port** (empty shells, not "done"); and **the `Cargo`
    /// directory holds no `.cs` on the C# side**. The second point is what is easiest to misread here: **"no gap" and "implemented" are not the same thing**.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldStillListTheServiceGaps() {
        string text = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));

        text.Should().Contain("### 服务层对齐（B755，Demo4 逐条）");
        text.Should().Contain("### 货物域的两个基线文件是空壳（B769）");
        text.Should().Contain("现缺 0 个");

        File.Exists(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "BunchGeneration", "Service", "Diagnostics.cs"))
            .Should().BeTrue("B757 ported it");
        File.Exists(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "Passenger", "Service", "PipelineListGenerator.cs"))
            .Should().BeTrue("B768 ported it");
        // **判"没有 C# 文件"而不是"目录不存在"**：空目录不进版本库，磁盘上有残留也不代表有人写过东西。
        // **Assert "no C# files" rather than "no directory"**: an empty directory is not tracked, and its leftover on disk does not mean anybody wrote anything.
        string cargo = Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "Cargo");
        Directory.EnumerateFiles(cargo, "*.cs", SearchOption.AllDirectories).Should().BeEmpty(
            "the cargo shells were deleted and nothing replaced them");
    }

    /// <summary>
    /// **清单里的"已移植"必须真的在** / What the table calls ported must really be there.
    /// </summary>
    /// <remarks>
    /// 三点成对：两条限制与 `RuleServices` 三处都在 / 四个规则类真的合并在那一个文件里 / 两条限制是 B753/B754 的产物（名字对得上）。
    ///
    /// Three paired points: both limits and `RuleServices` are present; the four rule classes really live in that one file; and both limits are B753/B754's output (the names match).
    /// </remarks>
    [Fact]
    public void Inventory_ShouldMatchTheFilesThatExist() {
        string text = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));

        text.Should().Contain("`bunch_compilation/service/limits/FleetBalanceLimit.kt` | `Service/Limits/FleetBalanceLimit.cs` | **B753** 移植");
        text.Should().Contain("`bunch_compilation/service/FreeAircraftSelector.kt` | `Service/FreeAircraftSelector.cs` | **B756** 移植");
        File.Exists(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "BunchCompilation", "Service", "FreeAircraftSelector.cs"))
            .Should().BeTrue("B756 ported it");
        text.Should().Contain("`bunch_compilation/service/limits/FlightLinkLimit.kt` | `Service/Limits/FlightLinkLimit.cs` | **B754** 移植");

        string limits = Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "BunchCompilation", "Service", "Limits");
        File.Exists(Path.Combine(limits, "FleetBalanceLimit.cs")).Should().BeTrue();
        File.Exists(Path.Combine(limits, "FlightLinkLimit.cs")).Should().BeTrue();

        string rules = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "Rule", "Service", "RuleServices.cs"));
        rules.Should().Contain("class ConnectionTimeCalculator");
        rules.Should().Contain("class CostCalculator");
        rules.Should().Contain("class FeasibilityJudger");
        rules.Should().Contain("class MinimumDepartureTimeCalculator");
    }

    /// <summary>
    /// **两个空壳已经删掉** / The two empty shells are gone.
    /// </summary>
    /// <remarks>
    /// 两点成对：`PassengerDomain.cs` 与 `CargoDomain.cs` **都不存在** / 清单里写明了为什么删。
    /// **"名称还在、能力没有"是最容易被当成已完成的形态**——留着它们，下一个读代码的人会以为乘客域和货物域已经有了。
    ///
    /// Two paired points: neither `PassengerDomain.cs` nor `CargoDomain.cs` exists; and the table records why they were deleted. **"The name is there, the capability is not" is the
    /// form most easily mistaken for done** — leaving them lets the next reader believe the passenger and cargo domains already exist.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheDeletedShells() {
        string text = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));
        text.Should().Contain("#### 顺手删掉两个空壳");

        File.Exists(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "Passenger", "PassengerDomain.cs"))
            .Should().BeFalse("the empty passenger shell was deleted in B755");
        File.Exists(Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo4", "Domain", "Cargo", "CargoDomain.cs"))
            .Should().BeFalse("the empty cargo shell was deleted in B755");
    }

    /// <summary>仓库根目录 / The repository root.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string RepoRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }
}
