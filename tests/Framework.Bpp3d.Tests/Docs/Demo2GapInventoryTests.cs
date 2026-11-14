// Demo2 子域清点的钉子 / Pins on the Demo2 sub-domain inventory.

using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **Demo2 的缺口清单必须说清"数字不是结论"** / The Demo2 inventory must say its numbers are not a verdict.
/// </summary>
/// <remarks>
/// P9 的完成条件里有"补全 Demo2"。补之前得知道缺哪儿，而"数文件个数"这件事已经在本仓出过一次事：
/// B468 按工程名对齐，把 `framework-bpp` 整个模块误报成缺失。文件数差同样有两种解释——**文件被合并**，或**能力真缺**。
///
/// 所以钉四件事：两侧总数在、最小的整域缺口被点名、四条误差警告仍在、"部分"不许被读成清单。
/// 数字本身会随移植变化，改这张表是**预期动作**；坏掉的是"没改表却改了代码"。
///
/// P9's completion condition includes "finish Demo2". Before finishing it you have to know what is missing, and counting files has already gone wrong once in this repo: B468
/// aligned by project name and reported the whole `framework-bpp` module as missing. A file-count difference has two readings — **files merged** or **capability missing**. So four
/// things are pinned: both totals, the smallest whole-domain gap by name, the four known-error caveats, and that "partial" is not a list. The numbers change as porting proceeds and
/// editing this table is the **expected** action; what breaks things is changing code without the table.
/// </remarks>
public class Demo2GapInventoryTests {
    /// **两侧总数与最小整域缺口** / Both totals and the smallest whole-domain gap.
    [Fact]
    public void Inventory_ShouldCarryBothTotalsAndNameTheSmallestGap() {
        string text = ReadInventory();

        text.Should().Contain("## Demo2 子域清点（B572 实测）");
        text.Should().Contain("共 **185** 个 `.kt`", "the baseline total is pinned to 12fdb587 and does not move (B927 recount)");
        text.Should().Contain("共 **175** 个 `.cs`", "the C# total is remeasured (B927)");
        text.Should().Contain("`domain/redundancy` 是**最小的整域缺口**（8 个源码文件");
    }

    /// **整域缺失必须逐个点名** / Every whole-domain gap must be named.
    [Fact]
    public void Inventory_ShouldNameEveryWholeDomainGap() {
        string text = ReadInventory();

        foreach (string domain in new[] {
            "domain/loading_effectiveness", "domain/redundancy", "domain/recommended_weight_equalization",
            "domain/express_effectiveness", "infrastructure/dto",
        }) {
            text.Should().Contain("| `" + domain + "` |", "every whole-domain gap must appear as a row");
        }
        text.Should().Contain("| `domain/loading_effectiveness` | 21 | 0 | **整域缺失** |");
        // **B807 重数**：`infrastructure/dto` 不再是整域缺失——12 个记录合并进了三个文件，剩下那个在基线里没有引用。
        // **B807 recount**: `infrastructure/dto` is no longer a whole-domain gap — 12 records are merged into three files and the last one has no baseline reference.
        text.Should().Contain("| `infrastructure/dto` | 13 | 0 | 齐（**B808 复核**：3 个文件装 10 个整文件 + 3 个枚举；第 4 个枚举与那个记录都无读者） |");
        text.Should().Contain("**`LoadingGeometryInput` 不进缺口表**");
        text.Should().Contain("**`WeightUnitInput` 不算缺口（B808 撤回 B807 的判断）**",
            "B808 withdrew the B807 verdict on evidence; the document must keep the retraction, not the original claim");
        text.Should().Contain("声明了但没人引用的，不算缺口");
        text.Should().Contain("| `domain/soft_security` | 9 | 9 | 齐（**全是壳**，见下节） |");
    }

    /// **"数字不是能力结论"这句警告不许消失** / The "numbers are not a capability verdict" warning must stay.
    [Fact]
    public void Inventory_ShouldWarnThatTheNumbersAreNotAVerdict() {
        string text = ReadInventory();

        text.Should().Contain("**两个数字都不是能力结论**");
        text.Should().Contain("这张表只用来排序");
        text.Should().Contain("**这张表不证明\"部分\"的子域缺什么**");
    }


    /// <summary>
    /// **"文件数相等"不等于"能力在"：62/101 是壳** / Equal file counts do not mean the capability is there: 62 of 101 are shells.
    /// </summary>
    /// <remarks>
    /// B572 按文件数把 `soft_security`（9/9）、`mac`（6/6）、`payload_maximization`（4/4）判成"齐"。
    /// B573 按内容再查：这三个子域**一个真实实现都没有**，全是 `TODO: Port from Kotlin` + `return Ok`。
    ///
    /// 判据钉三件事：壳的总数、三个"齐"子域的壳数等于文件数、"壳比缺失更坏"这句判断在。
    /// 只钉第一条会让"某个子域又新增了壳"漏过去。
    ///
    /// B572 judged `soft_security` (9/9), `mac` (6/6) and `payload_maximization` (4/4) as complete by file count. B573 re-checked by content: those three have **not one real
    /// implementation**, all `TODO: Port from Kotlin` plus `return Ok`. The criterion pins three things: the shell total, the fact that the three "complete" domains have as many
    /// shells as files, and the verdict that a shell is worse than a gap. Pinning only the first would let "a new shell appeared in some domain" slip through.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldCountShellsNotJustFiles() {
        string text = ReadInventory();

        text.Should().Contain("## Demo2 壳文件清点（B573 实测，**推翻上一节的\"齐\"**）");
        text.Should().Contain("**B927 重测：175 个 `.cs`、1 个壳**", "the shell count is the headline, remeasured each batch");
        text.Should().Contain("| 合计 | 175 | **1** | 174 |");
        // **重测脚本必须留在仓库里**：这张表在 B610 之前连续多批带着过期数字（`Domain/Stowage` 写 22、实测 36），
        // 因为每批只改标题与合计、没有重跑。钉住脚本路径，让"重跑"成为可执行的动作而不是一句口号。
        // **The recount script must stay in the repository**: before B610 this table carried stale numbers for several batches (`Domain/Stowage` said 22, measured 36) because each
        // batch edited only the headline and the total without re-running. Pinning the script path makes "re-run it" an executable action rather than a slogan.
        text.Should().Contain("`logs/count-shells.py`", "the recount script is referenced so the next batch can re-run it");
        text.Should().Contain("B573 那张表是 `101 / 62 / 39`", "the historical number stays visible so the drift is auditable");
        text.Should().Contain("| `Domain/SoftSecurity` | 7 | 0 | 7 |");
        text.Should().Contain("| `Domain/Mac` | 6 | 0 | 6 |");
        text.Should().Contain("| `Domain/PayloadMaximization` | 4 | 0 | 4 |");
        text.Should().Contain("**壳比缺失更坏**");
        text.Should().Contain("先补**前置模型**");
    }

    /// <summary>读取缺口清单 / Read the gap inventory.</summary>
    /// <returns>文件内容 / The file content.</returns>
    /// <summary>**185 与 175 之间那 10 个，必须被算清楚而不是被数着** / The difference must be explained, not merely counted.</summary>
    /// <remarks>
    /// **"少十个"这句话在这份文档里挂了很久，而它从来不是结论。** B1100 用 `logs/b1100-demo2-diff.py` 把它拆开：
    /// **按主干多重集比（目录被重命名，路径比不了），38 个缺额里 33 个是"多个基线类型合并进一个 C# 文件"，5 个真没有对应物。**
    ///
    /// **而 5 个里有 2 个基线本来就不该抄**：`ItemAdjustmentLimit` 加的行与 `ItemAssignmentLimit` 完全相同且无人调用，
    /// **`LoadingGeometryInput` 零引用。** 它们"不存在"是**结论**，不是**待办**。
    /// **这条判据钉的是"差值被算过"这件事本身**：数字变了要改它，**而"没算就写少十个"会红。**
    ///
    /// **"Ten fewer" sat in this document for a long time and was never a conclusion.** B1100 splits it with `logs/b1100-demo2-diff.py`:
    /// **comparing stem multisets (directories were renamed, so paths cannot be compared), 38 slots are short, 33 of them because several baseline types merged into one C# file, and 5 with no counterpart at all.**
    /// **Two of those five should not be copied at all**: `ItemAdjustmentLimit` adds the same row as `ItemAssignmentLimit` and is never called,
    /// **and `LoadingGeometryInput` has zero references.** Their absence is a **conclusion**, not a **todo**.
    /// **What this pins is that the difference was worked out**: change the numbers and this must change too, **while an unexamined "ten fewer" goes red.**
    /// </remarks>
    [Fact]
    public void TheCountDifference_ShouldBeExplainedByName() {
        string acceptance = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "docs", "kotlin-sync-acceptance.md"));

        acceptance.Should().Contain("b1100-demo2-diff.py",
            "the row has to point at the measurement, or the numbers are unreproducible");
        acceptance.Should().Contain("**38**", "the shortfall in slots");
        acceptance.Should().Contain("**33**", "the slots explained by merging");
        acceptance.Should().Contain("**5 个真的没有对应物**", "the remainder that needs a judgement");

        foreach (string name in new[] {
            "ApplicationResponseMapper", "ItemAdjustmentLimit", "LoadingGeometryInput",
            "SemanticParameter", "TryHelpers",
        }) {
            acceptance.Should().Contain(name, name + " is one of the five and must be named");
        }

        // **脚本本身要在**：数字指向一个不存在的脚本，等于没有证据。
        // **The script has to exist**: numbers pointing at a missing script are not evidence.
        File.Exists(Path.Combine(RepoRoot(), "logs", "b1100-demo2-diff.py"))
            .Should().BeTrue("the measurement is a file, not a claim");
    }

    private static string ReadInventory() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));

    /// <summary>仓库根目录 / The repository root.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string RepoRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null
            && !File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new InvalidOperationException("找不到解题根目录 / solution root not found");
    }

    /// <summary>**文档里的数字必须与重算结果一致** / The numbers in the inventory must equal a fresh recount.</summary>
    /// <remarks>
    /// 前一条判据只钉"字符串在不在"，**钉不住数字对不对**：B927 实测发现同一份文档里两节写着 `201/101` 与 `167/0`，
    /// 而代码侧是 `173` 个文件、`1` 个壳——**三条判据全绿，因为都在检查字符串**。这一条改为**在测试里重算**。
    ///
    /// 口径写死：本仓只数 `Domain/`、`Application/`、`Infrastructure/` 下的 `.cs`（不含 `bin`/`obj`）；
    /// 壳的定义与 `logs/b927-recount.py` 一致——去掉注释与 `using` 后不足 200 个非空白字符。
    ///
    /// The previous criterion only checked "is this string present", which **cannot catch a wrong number**: B927 measured two sections of the same inventory stating
    /// `201/101` and `167/0` while the code held `173` files and `1` shell — **all three criteria green because they all checked strings**. This one recounts instead.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldMatchTheMeasuredCounts() {
        (int files, int shells) = Recount();

        string text = ReadInventory();
        text.Should().Contain($"共 **{files}** 个 `.cs`",
            "the C# total in the inventory must equal a fresh recount");
        text.Should().Contain($"**B927 重测：{files} 个 `.cs`、{shells} 个壳**",
            "the shell headline must equal a fresh recount");
        text.Should().Contain($"| 合计 | {files} | **{shells}** | {files - shells} |",
            "the shell table's total row must equal a fresh recount");
    }

    /// <summary>重算文件数与壳数 / Recount the files and shells.</summary>
    /// <returns>文件数与壳数 / The file and shell counts.</returns>
    private static (int Files, int Shells) Recount() {
        string root = Path.Combine(RepoRoot(), "src", "Example", "FrameworkDemo", "Demo2");
        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        int count = 0;
        int shells = 0;
        foreach (string file in files) {
            string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Contains("/bin/") || relative.Contains("/obj/")) {
                continue;
            }
            ++count;
            string body = DocsFile.ReadAllText(file);
            // 只用字符组，不写反斜杠：`//.*` 默认不跨行，`/[*].*?[*]/` 等价于块注释，`[ \t]+` 等价于 `\s+`。
            // Character classes only, no backslashes: `//.*` stops at the newline, `/[*].*?[*]/` is a block comment, `[ \t]+` stands in for `\s+`.
            body = Regex.Replace(body, "//.*", string.Empty);
            body = Regex.Replace(body, "/[*].*?[*]/", string.Empty, RegexOptions.Singleline);
            body = Regex.Replace(body, "^[ \t]*using .*$", string.Empty, RegexOptions.Multiline);
            if (Regex.Replace(body, "[ \t\r\n]+", string.Empty).Length < 200) {
                ++shells;
            }
        }
        return (count, shells);
    }

}
