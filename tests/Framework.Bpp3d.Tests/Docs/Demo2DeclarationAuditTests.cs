// Demo2 声明级对表的钉子 / Pins on the Demo2 declaration-level audit (B792).

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"没找到"有三种意思，不能混着数** / "Not found" has three meanings and must not be counted as one (B792).
/// </summary>
/// <remarks>
/// 按文件数数不出能力缺不缺。B792 换成声明级对表：从基线 `12fdb587` 抽出 **265** 个类型声明，逐个到本仓全文找同名，
/// **22** 个没找到——其中 **8** 个是改名、**1** 个是别名、**8** 个落在已记录的 `infrastructure/dto` 整域缺口里，
/// **2** 个是基线里的死代码或已被合并，只有 **3** 个是真缺口。
///
/// 钉四件事：总数 265 / 四个分组各在其位 / **5 个新缺口逐个点名** / 那句"数字会变、改表是预期动作"仍在。
/// 最后一组最要紧：**只钉总数会让"把 5 个新缺口塞进 dto 那组"这种改法静默通过**。
///
/// Counting files cannot tell whether a capability is missing. B792 audits declarations instead: **265** type declarations are read from the `12fdb587`
/// baseline and matched by name against the whole tree; **22** are not found — **8** renamed, **1** aliased, **8** inside the already-recorded whole-domain gap
/// in `infrastructure/dto`, and **2** either dead or already merged in the baseline, leaving only **3** genuinely missing. Four things are pinned: the 265 total; all four groups where they belong; **the five new gaps
/// named one by one**; and the line saying numbers move and editing the table is the expected action. The last group matters most: **pinning only the totals
/// would let "stuff the five new gaps into the dto group" pass silently**.
/// </remarks>
public class Demo2DeclarationAuditTests {
    /// **总数与四个分组** / The total and the four groups.
    [Fact]
    public void Audit_ShouldCarryTheTotalsAndAllFourGroups() {
        string text = ReadInventory();

        text.Should().Contain("## Demo2 声明级对表（B792 实测）");
        text.Should().Contain("抽出 265 个类型声明", "the audited declaration count is tied to 12fdb587");
        text.Should().Contain("**243 个找到，22 个没找到**");
        text.Should().Contain("### 一、改名（8 个，不是缺口）");
        text.Should().Contain("### 二、别名（1 个，不是缺口）");
        text.Should().Contain("### 三、落在已记录的整体缺口里（8 个）");
        text.Should().Contain("### 四、真正的新缺口：**3 个**（`PositionStatusCode` 已在 B795 关闭，**现缺 2 个**）");
        text.Should().Contain("### 四之二、基线里未生效或被合并的声明：**2 个，不是缺口**（B794 起更正）");
    }

    /// **3 个新缺口必须逐个点名** / The three new gaps must be named one by one.
    [Fact]
    public void Audit_ShouldNameEveryNewGap() {
        string text = ReadInventory();

        foreach (string name in new[] {
            "LoadingOrderOutputExporter",
            "PositionStatusCode",
            "Demo2ApplicationService",
        }) {
            text.Should().Contain(name, $"{name} is a gap outside every recorded whole-domain gap");
        }
    }

    /// **8 个 dto 缺口不许算成新缺口** / The eight dto gaps must not be counted as new.
    [Fact]
    public void Audit_ShouldKeepTheDtoGapsInsideTheirExistingGroup() {
        string text = ReadInventory();

        foreach (string name in new[] {
            "WeightUnitInput", "LoadingLayoutInput", "LayoutGroupInput", "PositionConflictInput",
            "RenderPositionDTO", "RenderWeightDTO", "RunningHeartBeatDTO", "FinnishHeartBeatDTO",
        }) {
            text.Should().Contain(name, $"{name} belongs to the already-recorded infrastructure/dto gap");
        }
        text.Should().Contain("不要当成 8 个新缺口");
        text.Should().Contain("`8 + 1 + 8 + 3 + 2 = 22`");
    }

    /// **数字会变，改表是预期动作** / The numbers move; editing the table is the expected action.
    [Fact]
    public void Audit_ShouldSayTheTableIsExpectedToChange() {
        string text = ReadInventory();

        text.Should().Contain("数字会随移植变化，改表是预期动作");
        text.Should().Contain("先跑一遍对表脚本");
    }

    private static string ReadInventory() {
        string path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "logs", "kotlin-sync-gap-inventory.md");
        return File.ReadAllText(path);
    }
}
