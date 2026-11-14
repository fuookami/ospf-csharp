// 缺口清单的钉子 / Pins on the gap inventory.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **缺口清单必须说清"按什么对齐"和"谁真的缺"** / The inventory must state what it aligns by and who is really missing.
/// </summary>
/// <remarks>
/// 这张表出过错：B468 按**工程名**对齐，把 `framework-bpp` 的 10 个主源码整模块误报成"不存在"，
/// 而它们其实住在名为 `Framework.Bpp1d` 的工程里、命名空间是 `Fuookami.Ospf.Framework.Bpp.*`。
/// B469 改成按**命名空间**对齐后，`framework-bpp` 与 `framework-bpp1d` 都是 0 缺口。
///
/// 所以钉三件事：方法必须写明按命名空间对齐、0 缺口的结论必须在、误判这件事本身必须留档。
/// **一个会误报的清单比没有清单更危险**，因为它会让人去重做已经做完的模块。
///
/// The table got this wrong once: B468 aligned by **project name** and reported the whole `framework-bpp` module as missing, while its 10 main sources live in the project
/// named `Framework.Bpp1d` under the `Fuookami.Ospf.Framework.Bpp.*` namespace. B469 aligned by **namespace** and both `framework-bpp` and `framework-bpp1d` have zero
/// gaps. So three things are pinned: the method must say it aligns by namespace, the zero-gap conclusion must stay, and the misjudgement itself must stay on record.
/// **A table that misreports is worse than no table**, because it sends people to redo finished modules.
/// </remarks>
public class GapInventoryTests {
    /// **清单必须写明"按命名空间对齐"，并保住 0 缺口的结论** / The inventory must say it aligns by namespace and keep the zero-gap verdict.
    [Fact]
    public void Inventory_ShouldAlignByNamespaceAndKeepTheZeroGapVerdict() {
        string text = ReadInventory();

        text.Should().Contain("按**命名空间**而不是工程名", "aligning by project name is what produced the false gap");
        text.Should().Contain("`framework-bpp` 的 10 个主源码全部落在 `Framework.Bpp1d`",
            "the zero-gap verdict for framework-bpp is the corrected conclusion");
        text.Should().Contain("| `framework-bpp` | 10 | 0 |");
        text.Should().Contain("| `framework-csp2d` | 18 | 0 |");
    }

    /// **清单必须自陈"粗判不等于结案"，并点名 plugin 的命名差异** / The inventory must declare its coarseness and name the plugin naming artefact.
    [Fact]
    public void Inventory_ShouldDeclareItsOwnCoarseness() {
        string text = ReadInventory();

        text.Should().Contain("只用于排序，不用于结案");
        text.Should().Contain("按文件名扫不出能力缺口", "the plugin module's hits are a naming artefact");
    }

    /// <summary>
    /// **"下一块"里的数字必须与上表一致** / The counts in the "what next" section must agree with the table above.
    /// </summary>
    /// <remarks>
    /// B484 修的就是这件事：那段原本硬编码 `bpp3d（18 个）`/`bpp2d（14 个）`/`gantt（24 个）`，
    /// 而同一份文件的表里早已是 `15`/`9`/`21`——**一份自相矛盾的表比没有表更危险**，
    /// 因为它既让人去重做、又让人相信数字。
    /// 现在数字从扫描结果推导，并由这条判据逐项比对；将来谁再写死数字，这里会红。
    ///
    /// This is what B484 fixed: that section hard-coded `bpp3d (18)` / `bpp2d (14)` / `gantt (24)` while the table in the same file already said `15` / `9` / `21` —
    /// **a self-contradicting table is worse than none**, because it both sends people to redo work and makes them trust the numbers. The counts now derive from the scan
    /// and this criterion compares them item by item; anyone hard-coding a number again turns it red.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldKeepTheNextBlockCountsConsistentWithTheTable() {
        string text = ReadInventory();

        text.Should().Contain("## 下一块该做哪一块", "a list without an order is not a plan");

        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in
                 Regex.Matches(text, "\\| `(?<module>[a-z0-9-]+)` \\| (?<total>\\d+) \\| (?<missing>\\d+) \\|")) {
            table[match.Groups["module"].Value] = int.Parse(match.Groups["missing"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        table.Should().NotBeEmpty("the results table must be parsed for the comparison to mean anything");

        // **只比对当前段，不比对历史段**：文末保留 B629 原文以备追溯，那里也有一份排序列表。
        // 整篇扫会把历史副本算进来——当前结论写 0、历史写着 8，这条判据就报 8；反过来更糟：按历史"通过"（B716/B717 撞过）。
        // **Only the current region is compared, never the historical one**: the tail keeps the B629 original for traceability and it carries its own ranking list.
        // Scanning the whole file counts that copy too, so the criterion reports the historical 8 — or worse, "passes" against history (B716 and B717 both ran into it).
        int history = text.IndexOf("### 历史口径", StringComparison.Ordinal);
        string current = history > 0 ? text[..history] : text;

        var section = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in
                 Regex.Matches(current, "\\*\\*`(?<module>[a-z0-9-]+)`（(?<count>\\d+) 个）\\*\\*")) {
            section[match.Groups["module"].Value] = int.Parse(match.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        section.Should().NotBeEmpty("the next-block section must carry the counts");

        foreach (KeyValuePair<string, int> entry in section) {
            table.Should().ContainKey(entry.Key, "the next-block section may only name modules the table knows");
            table[entry.Key].Should().Be(entry.Value, "the count for " + entry.Key + " must match the table rather than be hard-coded");
        }
    }

    /// **"文件 stem 不等于类型名"这条误差必须留档** / The "a file stem is not a type name" error must stay on record.
    [Fact]
    public void Inventory_ShouldRecordTheFileStemCaveat() {
        string text = ReadInventory();

        text.Should().Contain("文件 stem 不等于类型名",
            "B484 found `BunchSchedulingSolution.cs` declaring `BunchSolution`, and reported it missing");
        text.Should().Contain("`BunchSchedulingSolution`、`GanttSchedulingErrors` 已完整",
            "the concrete files that the stem scan falsely reported must be named");
        text.Should().Contain("`IterativeAggregation`（基线 629 行 / C# 52 行）",
            "and the ones that really are incomplete must be distinguished from them");
        text.Should().Contain("`CapacitySolverValue` 在基线里是**空文件**",
            "an empty baseline file is not a capability");
        text.Should().Contain("`SolverRunId` 已被 `Core` 的 `SolveRunId` 覆盖",
            "duplicating a covered identifier would be a second source of truth");
    }

    /// <summary>
    /// **B629 复核：bpp3d 的 15 个里 8 个是误报** / The B629 review: 8 of bpp3d's 15 were false positives.
    /// </summary>
    /// <remarks>
    /// 这张表按**文件名子串**扫，凡是"已有但换了名字/换了语言形态"的都会被算成缺失。B629 逐条核对 bpp3d 那 15 个，
    /// 8 个不是缺口，7 个是——而其中一个还归属 bpp2d。
    ///
    /// 钉四件事：总体结论、四类误报各自的理由、`QuantityContainerCore` 那条"换了名字"的特例、以及两条约束的阻塞点
    /// 必须写成 `PreciseAssignment` 而不是含糊的"未实现"。
    ///
    /// The table scans by **filename substring**, so anything that exists under a different name or language shape counts as missing. B629 checked bpp3d's fifteen one by one: eight are
    /// not gaps and seven are, one of which belongs to bpp2d. Four things are pinned: the overall verdict, each of the four false-positive reasons, the `QuantityContainerCore`
    /// "renamed" special case, and the two constraints' blocker, which must name `PreciseAssignment` rather than a vague "not implemented".
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheBpp3dFalsePositives() {
        string text = ReadInventory();

        text.Should().Contain("**B629 复核：原列 15 个，其中 8 个是误报，真缺口 7 个（另 1 个归属 bpp2d）。**");
        // 四类误报，各有各的理由，不能合并成一句"已存在"。
        // Four false-positive classes, each with its own reason rather than one blanket "already exists".
        text.Should().Contain("基线里是个**空 object**", "ProjectivePlaneGeometryMapping has literally nothing to port");
        text.Should().Contain("Kotlin 协程运行时产物", "the four *Async files are coroutine scopes, not capabilities");
        text.Should().Contain("`IContainer3Geometry` / `IContainer2Geometry`", "QuantityContainerCore exists under C# interface names");
        text.Should().Contain("按文件名扫不到", "the reason a renamed port is invisible to this table's method");
        // 阻塞点点名，而不是"未实现"。
        // The blocker is named rather than left as "not implemented".
        text.Should().Contain("`PreciseAssignment.v[]` / `.tail[]`");
        text.Should().Contain("C# 的 `LayerAssignment` **没有这两个数组**");
        // 归属纠正：QuantityDomainModels 属于 bpp2d。
        // Ownership correction: QuantityDomainModels belongs to bpp2d.
        text.Should().Contain("**归属 `framework-bpp2d`**");
        text.Should().Contain("  3. `framework-bpp2d`：**10 个**");
    }

    /// <summary>
    /// **B716 复核：bpp3d 真缺口 0 个** / The B716 review: bpp3d has no real gaps.
    /// </summary>
    /// <remarks>
    /// B629 记了 7 个"真缺口"，其中两条还写着"被 `PreciseAssignment.v[]` / `.tail[]` 阻塞"。B716 逐条重核，
    /// **7 个全部已在 C# 落地**，而那条阻塞理由今天也不成立——`PreciseAssignment` 已经有 `V(int)` 与 `Tail`。
    ///
    /// 三点钉住：
    ///
    /// 1. 表行改成 `105 | 0`，且 §bpp3d 的开头写明"真缺口 0 个"。
    /// 2. 五条落地路径逐条写在表里——只说"已落地"而不给位置，下一个人还得再考古一次。
    /// 3. **过期的那两条旧结论只允许出现在历史段里**：本文档保留 B629 原文以备追溯，
    ///    于是"钉字符串"这种判据可以被历史段满足而假通过（本批就撞上了）。所以这里断言它们的首次出现位置**晚于**历史段标题。
    ///
    /// Three things are pinned: the table row becomes `105 | 0` and the bpp3d section states "no real gaps"; each of the five landing paths is written into the table, because saying
    /// "already landed" without a location forces the next reader to dig again; and **the two stale conclusions may only appear inside the historical section** — this document keeps the B629
    /// original for traceability, so a string-pin criterion can be satisfied by that copy and pass falsely (this batch ran into exactly that), hence the assertion that their first occurrence
    /// comes **after** the historical section's heading.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheBpp3dReaudit() {
        string text = ReadInventory();

        text.Should().Contain("| `framework-bpp3d` | 105 | 0 |", "the machine row must agree with the review");
        text.Should().Contain("B716 复核：机器段报的 8 个全部不是缺口，本模块真缺口 0 个");
        text.Should().Contain("### 真缺口 7 个（B629 口径，**B716 复核后全部落地**）");

        // 五条落地路径，逐条给位置。
        // Five landing paths, each with a location.
        text.Should().Contain("`Domain/LayerAssignment/Service/Limits/BinLoadingOrderConstraint.cs`");
        text.Should().Contain("同目录 `TailBinAssignmentConstraint.cs`");
        text.Should().Contain("`Infra/PWLRadiusApproximation.cs`");
        text.Should().Contain("`Domain/Item/Model/ContinuousRadiusSelectionExtractor.cs`");
        text.Should().Contain("`Framework.Bpp2d/Domain/Item/Model/QuantityDomainModels.cs`");

        // **过期结论只许留在历史段**：否则"钉原文"这种判据会被历史副本满足而假通过。
        // **Stale conclusions are confined to the historical section**: otherwise a pin-the-original criterion is satisfied by the historical copy and passes falsely.
        int history = text.IndexOf("### 历史口径（B629，保留原文以备追溯）", StringComparison.Ordinal);
        history.Should().BeGreaterThan(0, "the historical section must exist for traceability");
        foreach (string stale in new[] { "`PreciseAssignment.v[]` / `.tail[]`", "C# 的 `LayerAssignment` **没有这两个数组**" }) {
            int first = text.IndexOf(stale, StringComparison.Ordinal);
            if (first >= 0) {
                first.Should().BeGreaterThan(history, "a stale blocker may only survive inside the historical section");
            }
        }
    }

    /// <summary>
    /// **B717 复核：`framework` 真缺口 0 个** / The B717 review: `framework` has no real gaps.
    /// </summary>
    /// <remarks>
    /// B673 把机器段的 8 个收到 3 个"真缺口"，B674/B675/B676 逐个处理。B717 重核代码：三个都有对应物。
    ///
    /// 三点钉住：表行改成 `70 | 0`、三条落地路径逐条给位置、以及 §framework 开头写明"真缺口 0 个"。
    ///
    /// 顺带钉住一条结论上的分寸：`BooleanExpressionDsl` 当年被判"机制不同、不算缺口"——**这个判断没错，但它掩盖了
    /// "其实也移植了一份"**（`ExpressionDsl.cs`）。"不算缺口"与"已经存在"是两个不同的结论，写下来要分清，
    /// 否则下一轮复核还得把同一行再解释一遍。
    ///
    /// Three things are pinned: the table row becomes `70 | 0`, each of the three landing paths gets a location, and the framework section states "no real gaps". One further point is pinned about
    /// how to word a verdict: `BooleanExpressionDsl` was judged "a different mechanism, not a gap" — **true, but it hid the fact that a port exists too** (`ExpressionDsl.cs`). "Not a gap" and
    /// "already present" are different conclusions, and mixing them up forces the next review to explain the same row again.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheFrameworkReaudit() {
        string text = ReadInventory();

        text.Should().Contain("| `framework` | 70 | 0 |", "the machine row must agree with the review");
        text.Should().Contain("B717 复核：机器段报的 8 个全部不是缺口，本模块真缺口 0 个");
        text.Should().Contain("### B717 复核：三个");
        text.Should().Contain("src/Framework/Solver/CombinatorialSolveSupport.cs");
        text.Should().Contain("src/Framework/Model/TypedModelBuilder.cs");
        text.Should().Contain("src/Math/Symbol/Expression/Dsl/ExpressionDsl.cs");
        text.Should().Contain("是两个不同的结论，写下来的时候要分清");
    }

    /// <summary>
    /// **B718 复核：`framework-gantt-scheduling` 真缺口 0 个** / The B718 review: gantt has no real gaps.
    /// </summary>
    /// <remarks>
    /// B679 曾判定 `ActivitySchedulingApplicationService` "暂不可开工"，理由是 `ActivitySchedulingContext` 只有 `Choices` + `ValidateNamespace`。
    /// B718 直接读代码：三个转发方法都在（`Register` / `Validate` / `ExtractSolution`），聚合层也真的在取解值，应用服务本身也已在
    /// `Application/Service/Activity/` 下。**阻塞理由又一次过期了**——它记的是当时的仓。
    ///
    /// 钉四件事：表行改成 `158 | 0`、开头写明"真缺口 0 个"、四条逐条结论、以及那两条过期判断（"暂不可开工"与"4 个"）只许留在历史段。
    ///
    /// B679 once judged `ActivitySchedulingApplicationService` "not startable yet" because `ActivitySchedulingContext` only had `Choices` + `ValidateNamespace`. B718 reads the code
    /// directly: all three forwarding methods are there (`Register` / `Validate` / `ExtractSolution`), the aggregation really does read solution values, and the application service itself
    /// already lives under `Application/Service/Activity/`. **The blocking reason expired again** — it recorded the repo of that moment. Four things are pinned: the table row becomes
    /// `158 | 0`, the section states "no real gaps", the four per-item verdicts, and the two stale conclusions, confined to the historical section.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheGanttReaudit() {
        string text = ReadInventory();

        text.Should().Contain("| `framework-gantt-scheduling` | 158 | 0 |", "the machine row must agree with the review");
        text.Should().Contain("B718 复核：机器段报的 4 个全部不是缺口，本模块真缺口 0 个");
        text.Should().Contain("### B718 复核：gantt 那 4 个也全部落地");

        // 四条逐条结论，各给位置或理由。
        // Four per-item verdicts, each with a location or a reason.
        text.Should().Contain("`Application/Service/Activity/ActivitySchedulingApplicationService.cs`");
        text.Should().Contain("**空文件**：6 行");
        text.Should().Contain("`Infrastructure/Dto/RenderTaskDto.cs`");
        text.Should().Contain("`Core/Solver/Report/SolveReport.cs`");

        // **过期判断只许留在历史段**：整篇查字符串会被历史副本满足。
        // **Stale verdicts stay inside the historical section**: a whole-file string check would be satisfied by that copy.
        int history = text.IndexOf("### 历史口径", StringComparison.Ordinal);
        history.Should().BeGreaterThan(0);
        int stale = text.IndexOf("**暂不可开工**", StringComparison.Ordinal);
        if (stale >= 0) {
            stale.Should().BeGreaterThan(history, "the 'not startable yet' verdict may only survive inside the historical section");
        }
    }

    /// <summary>
    /// **B719 复核：bpp2d 与 network 的"1 个"都归零** / The B719 review zeroes bpp2d and network.
    /// </summary>
    /// <remarks>
    /// 这两条各自只挂着 1 个疑似未同步，B677/B631 已经核过，但**表行一直没跟着改**，于是"下一块"里它们仍按 1 个排着。
    ///
    /// 这条判据的重点不是"结论段里有这句话"（那种查法 B716/B717/B718 三次都没抓到"表行没跟上"），
    /// 而是**逐模块比对表行与结论段**：表里每个模块的缺口数，必须与 §B719 表里写的结论一致。
    ///
    /// This criterion is not about the conclusion paragraph containing a phrase — that kind of check missed "the table row never caught up" three times in B716/B717/B718. It compares the
    /// table row against the conclusion paragraph module by module: every module whose count the B719 table states must agree with the results table.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldZeroTheBpp2dAndNetworkRows() {
        string text = ReadInventory();

        text.Should().Contain("| `framework-bpp2d` | 56 | 0 |", "the machine row must agree with the review");
        text.Should().Contain("| `framework-network-scheduling` | 114 | 0 |");
        text.Should().Contain("### B719 复核：bpp2d 与 network 两个");
        // **位置要在本批那一节里**：同一个路径在旧的 network 段也出现过，整篇查会被那份副本满足（这条变异存活过一次）。
        // **The location must sit in this batch's section**: the same path also appears in the older network section, so a whole-file check is satisfied by that copy (this mutation
        // survived once).
        int section = text.IndexOf("### B719 复核：bpp2d 与 network 两个", StringComparison.Ordinal);
        section.Should().BeGreaterThan(0);
        string b719 = text[section..];
        b719.Should().Contain("`src/Framework.NetworkScheduling/Application/Service/NetworkApplicationService.cs`");
        b719.Should().Contain("`Framework.Bpp1d/Domain/DiscreteLoading/`");
        text.Should().Contain("是这份清单最常见的失效方式");

        // **表行与结论段必须一致**：结论说 0 个，表行就不能还挂着 1。
        // **The table row must agree with the conclusion paragraph**: a conclusion of zero cannot sit next to a row still carrying one.
        var table = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in
                 Regex.Matches(text, "\\| `(?<module>[a-z0-9-]+)` \\| (?<total>\\d+) \\| (?<missing>\\d+) \\|")) {
            table[match.Groups["module"].Value] = int.Parse(match.Groups["missing"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        foreach (string module in new[] { "framework-bpp2d", "framework-network-scheduling" }) {
            table.Should().ContainKey(module);
            table[module].Should().Be(0, "B719 found no real gap in " + module + ", so its row must say so");
        }
    }

    /// <summary>
    /// **B720 复核：plugin 的 48 个按能力分组后名称级缺口 0 个，但查出三个翻译器没接上** / The B720 review groups plugin's 48 files, finds no name-level gap, and catches one capability that exists but is never called.
    /// </summary>
    /// <remarks>
    /// 48 个不是 48 件事。它们按方言 / 翻译器 / 绑定器 / 仓储 / 后端入口 / 查询编译 / 记录 / 谓词 schema / 杂项九组分布，
    /// 其中方言、后端入口、查询编译三层是 Ktorm/JdbcClient/MyBatis 的机制产物，C# 走 EF Core provider 因而不需要。
    ///
    /// 真正值得记的不是"48 个没同步"，而是 **`EfCore` 的三个翻译器只有测试在用**——能力写好了、测试也绿了，却没有生产路径调用它。
    /// 这一整轮同步反复抓的就是这一类："存在"不等于"生效"。
    ///
    /// 钉三件事：表行归零、九组分组结论、以及那条"只有测试在用"的待办必须写明。
    ///
    /// Forty-eight files are not forty-eight capabilities. They fall into nine groups — dialect, translators, binders, repositories, backend entry, query compilation, records, predicate
    /// schema, and misc — and the dialect, backend-entry and query-compilation layers are Ktorm/JdbcClient/MyBatis mechanisms that C# does not need because it goes through EF Core
    /// providers. What is worth recording is not "48 unsynced" but that **the three `EfCore` translators are called only from their own tests** — the capability is written and green with no
    /// production caller. "Exists" is not "in effect", the very class this whole sync keeps catching. Three things are pinned: the row is zeroed, the nine-group verdict, and that pending item.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordThePluginReaudit() {
        string text = ReadInventory();

        text.Should().Contain("| `framework-plugin` | 61 | 0 |", "the machine row must agree with the review");
        text.Should().Contain("本模块名称级缺口 0 个，但查出 1 条");
        text.Should().Contain("### 真正值得记的一条：三个翻译器只有测试在用");
        text.Should().Contain("**B721 待办：把三个翻译器接进仓储路径");
        // 机制不同的那三层，理由要具体到"靠什么替代"，不能只写"不需要"。
        // The three mechanism-different layers must name what replaces them, not just say "not needed".
        int plugin = text.IndexOf("## `framework-plugin`", StringComparison.Ordinal);
        plugin.Should().BeGreaterThan(0);
        string pluginSection = text[plugin..];
        pluginSection.Should().Contain("DbContext");
        pluginSection.Should().Contain("EF Core 自己把 LINQ 编译成 SQL");
        text.Should().Contain("C# 用源生成器而不是 KSP 处理器");
    }

    /// <summary>读取清单 / Read the inventory.</summary>
    /// <returns>文件内容 / The file content.</returns>
    private static string ReadInventory() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));

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
