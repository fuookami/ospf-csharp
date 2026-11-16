// 能力矩阵的存在性与内容测试 / Existence and content tests for the capability matrix.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 能力矩阵必须存在且不丢掉关键结论 / The capability matrix must exist and keep its key conclusions.
/// </summary>
/// <remarks>
/// 矩阵的价值全在**它敢说"缺什么"**。若后续批次把"缺失"悄悄改成"完整"，矩阵就退化成一张好看的假表，
/// 而这不会让任何代码失败。因此这里钉住的是**负面结论**。
///
/// The matrix's value is entirely in **daring to say what is missing**. If a later batch quietly turns "missing" into "complete", the matrix degrades
/// into a good-looking lie — and no code fails. So these tests pin the **negative** conclusions.
/// </remarks>
public sealed class CapabilityMatrixTests {
    private static string MatrixPath()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            string candidate = Path.Combine(directory, "docs", "kotlin-capability-matrix.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException("docs/kotlin-capability-matrix.md not found from the test output directory");
    }

    private static string ReadMatrix() =>
        DocsFile.ReadAllText(MatrixPath()).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void CapabilityMatrix_ShouldExist() {
        File.Exists(MatrixPath()).Should().BeTrue();
        ReadMatrix().Trim().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>**BPP1D / CSP2D 工程存在，缺的是 starter** / The projects exist; the starters are what is missing.</summary>
    /// <remarks>
    /// 这两条判据原本断言"项目不存在"。**量到它们早就存在**（`Framework.Bpp1d` 25 个源文件、`Framework.Csp2d` 21 个，
    /// 缺口清单对 `framework-bpp` 报 0 真缺口）。判据本身成了过期文档的守卫——**一条钉住错误事实的判据比没有判据更糟**。
    ///
    /// These two criteria used to assert "the projects do not exist". **measured that they have existed for a long time** (`Framework.Bpp1d` 25 sources,
    /// `Framework.Csp2d` 21, zero real gaps for `framework-bpp`). The criteria had become guards of stale documentation — **a criterion pinning a wrong fact is worse
    /// than none**.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldSayTheProjectsExistAndOnlyStartersAreMissing() {
        string matrix = ReadMatrix();
        matrix.Should().Contain("| BPP 1D | `Framework.Bpp1d` | ✅ |");
        matrix.Should().Contain("| CSP 2D | `Framework.Csp2d` | ✅ |");
        matrix.Should().NotContain("`Framework.Bpp1d` 项目不存在");
        matrix.Should().NotContain("**C# 侧没有 `Framework.Bpp1d` / `Framework.Csp2d` 项目**");
        matrix.Should().Contain("缺的只是 **starter 包**本身");
    }

    /// <summary>**`PreciseAssignment` 已落地，两条约束随之完成** / `PreciseAssignment` exists and both constraints landed with it.</summary>
    /// <remarks>
    /// 原判据断言"该模型整体缺失"。**量到它在 `Framework.Bpp2d` / `Framework.Bpp3d` 的 `LayerAssignment/Model/` 下都有**，
    /// `BinLoadingOrderConstraint` 与 `TailBinAssignmentConstraint` 也都在 `LayerAssignment/service/limits/` 下。
    /// 这条长期挂着的"缺失"是**文档没跟上代码**，而不是代码缺口。
    ///
    /// The original criterion asserted "the model is entirely missing". **measured it exists** under `LayerAssignment/Model/` in both `Framework.Bpp2d` and
    /// `Framework.Bpp3d`, with `BinLoadingOrderConstraint` and `TailBinAssignmentConstraint` under `LayerAssignment/service/limits/`. The long-standing "missing" note was
    /// **documentation lagging behind code**, not a code gap.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldSayPreciseAssignmentLanded() {
        string matrix = ReadMatrix();
        matrix.Should().Contain("| 加载顺序约束 | ✅ | `BinLoadingOrderConstraint`");
        matrix.Should().Contain("| 尾箱分配约束 | ✅ | `TailBinAssignmentConstraint`");
        matrix.Should().NotContain("**该模型整体缺失**");
        matrix.Should().Contain("已撤回");
    }

    /// <summary>**必须把前置校验与模型约束分开** / It must separate a pre-check from a model constraint.</summary>
    [Fact]
    public void CapabilityMatrix_ShouldSeparatePreCheckFromModelConstraint() {
        string matrix = ReadMatrix();

        matrix.Should().Contain("前置校验，不是模型约束");
        matrix.Should().Contain("在优化结果上不同");
    }

    /// <summary>**跨语言验证范围只能写三处** / The cross-language scope must list exactly the three measured items.</summary>
    [Fact]
    public void CapabilityMatrix_ShouldLimitTheMeasuredScope() {
        string matrix = ReadMatrix();

        matrix.Should().Contain("SerializedSolution");
        matrix.Should().Contain("RemoteTaskSubmitRequest");
        matrix.Should().Contain("除此之外没有任何跨语言互通或性能结论");
    }

    /// <summary>**starter 包的空壳历史与缺失项必须写明** / The starter packages' shell history and missing entries must be stated.</summary>
    [Fact]
    public void CapabilityMatrix_ShouldStateTheStarterPackageGap() {
        string matrix = ReadMatrix();

        // **改了标题**（补上"补齐四个"），所以这里跟着改。
        // 下面两句历史断言**不动**：` 之前这五个 starter 是逐字节相同的 csproj` 说的是当年那五个，
        // 和 新补的四个不冲突。
        //
        // **changed the heading** (adding "filled in four"), so this follows. The two history assertions below stay: the sentence about the five byte-identical
        // starters describes what those five were, and does not conflict with the four added.
        matrix.Should().Contain("## 四之三、Starter 包（**曾经是空壳，现已补齐四个**）");
        matrix.Should().Contain("**同名程序集互相覆盖、装上去不传递任何模块**");
        // 断言**历史那句**：只断言后果会放过"把'曾经是空壳'改成'一直正确'"。 / Assert the history sentence: asserting only the consequence lets "always correct" replace "used to be a shell".
        matrix.Should().Contain("**这五个 starter 曾经是逐字节相同的 csproj**");

    }

    /// <summary>**Demo2 的逐项状态必须与实测一致** / Demo2's per-capability states must match the measurement.</summary>
    /// <remarks>
    /// 把这条判据翻面了**。它原本断言两句 的结论：
    ///
    /// | 原断言 | 实测 |
    /// | --- | --- |
    /// | "**这是全表里\"P9 明文要求、实际做得最少\"的地方**" | 逐项实测：**每个能力都有实现、0 个 `TODO`、都有生产引用** |
    /// | "**没有任何生产调用方**" | 引用数 25 / 23 / 19 / 17 / 16 / 13 / 9 / 6 / 5 / 4 / 2 / 1 |
    ///
    /// **一条钉住错误结论的判据比没有判据更糟**。
    /// 现在它钉的是：文件数与空壳数（实测）、唯一的空壳被点名、以及**那张逐项表不许再退回成"空壳"**。
    ///
    /// **flipped this criterion.** It used to assert two conclusions, and the measurement contradicts both: every capability has an implementation, zero
    /// `TODO`s and production references. **A criterion pinning a wrong conclusion is worse than none** — the same lesson recorded for "the project does not
    /// exist". It now pins the measured file and shell counts, names the one remaining shell, and **forbids the per-capability table from reverting to "shells"**.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldStateTheDemo2Gap() {
        string matrix = ReadMatrix();

        matrix.Should().Contain("## 四之二、Demo2 细项");
        // 文件数与空壳数改为实测值**（脚本 `logs/b927-recount.py`）。旧的两行（`231`/`101`、`10 个纯空壳`）
        // 是 的读数，实测是 175 个 `.cs`、1 个空壳，而且那些"空壳"已经有 43—251 行、一个 `TODO` 都没有。
        // the counts are measured values now**; the old lines were readings, and the supposed shells already held 43-251 lines with no `TODO`.
        matrix.Should().Contain("| 文件数 | **185** 个 `.kt`（`demo2/src/main/`） | **175** 个 `.cs`（**实测**");
        // **撤回"1 个空壳"这个结论**，因为它本身是错的：那个文件是
        // `public enum FlightPhase { ZeroFuel, TakeOff, Landing }`——三个成员一个不缺、能编译、被引用。
        // **它不是空壳，是本仓新增的枚举。** 钉住错误结论的判据比没有判据更糟，
        // 所以这里改成钉更正后的结论与更正说明。
        //
        // **withdraws "one shell"** because that conclusion was wrong: the file is `public enum FlightPhase { ZeroFuel, TakeOff, Landing }` — three members,
        // none missing, compiles and is referenced. **It is not a shell but an enum added here.** A criterion pinning a wrong conclusion is worse than none,
        // so this now pins the corrected conclusion and its note.
        matrix.Should().Contain("| 空壳 | — | **0** 个（**更正**");
        matrix.Should().Contain("**更正**：这里原来写",
            "the correction has to say what it replaces, or the old row gets copied back");
        // 逐项表不许退回成"空壳"**。逐个点名，因为只断言整节还在会放过某一行被改回去。
        // the table must not revert to "shells"**, named row by row, since asserting the section alone lets a single row slip back.
        matrix.Should().Contain("| `Flight` / `Payload` / `TotalWeight` / `MaxLoadWeight` | ✅ | 50 / 213 / 159 / 105 行，**0 个 `TODO`**");
        matrix.Should().Contain("| `Torque` / `MacModel` / `MacAggregation` | ✅ | 237 / 89 / 148 行，**0 个 `TODO`**");
        matrix.Should().Contain("| `SolutionAnalyzer` | ✅ | 252 行、2 处引用 |");
        matrix.Should().Contain("| `PipelineListGenerator` | ✅ | 149 行、13 处引用 |");
        // **分区列生成必须标 ✅ 并带真后端实测**：它是 四批的产出。
        // **Partition column generation must be ✅ with the real-backend measurement**: it is the output of.
        matrix.Should().Contain("| **分区列生成分派** | ✅ | 分区配置 + 分派 + 目标展平");
        // **旧结论必须明说作废**，否则它会被后续原样抄回去。
        // **The old conclusion must be marked void**, or the next step copies it straight back.
        matrix.Should().Contain("**整张旧表已撤回**");
        matrix.Should().NotContain("**没有任何生产调用方**",
            "the measurement shows production references everywhere; this sentence is no longer true");
    }
    /// <summary>**CSP1D 的空壳缺口必须写明** / The CSP1D shell gap must be stated.</summary>
    /// <remarks>
    /// 删掉了恒返回空的 `DFSGenerator`。这一节存在的意义是：**接口、实现、统计字段、KPI 键四样都齐，
    /// 唯独串起来的那段是空壳**——这是全表最容易被误读为"已完成"的地方。
    ///
    /// deleted the always-empty `DFSGenerator`. This section exists because **the interface, the implementations, the statistics fields and the KPI
    /// keys are all present, and the one piece wiring them together was a shell** — the place in this table most easily misread as complete.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldStateTheCsp1dShellGap() {
        string matrix = ReadMatrix();

        // **翻转这条判据**：它原来钉的四条里，三条钉的是错结论。
        //
        // | 原来钉 | 实测 |
        // | --- | --- |
        // | `DFSGenerator` 是个空壳 | **153 行实现** |
        // | 已删除 | **文件在**，`FullSumGenerator` 调用它的 `GenerationSumSearch` |
        // | 但没有任何使用者 | **有测试在用**，只是没有生产实例化 |
        //
        // **没有生产调用方是接线缺口，不是空壳。** 把前者写成后者会让人以为工作量比实际大得多。
        //
        // **flips this criterion**: three of its four pins asserted wrong conclusions — the file is **153 lines** of real implementation, `FullSumGenerator`
        // calls its `GenerationSumSearch`, and **tests do use it**; only production instantiation is missing. **No production caller is a wiring gap, not a shell.**
        matrix.Should().Contain("## 四之一、CSP1D 细项");
        // **要的是"上一版整节被撤回"这件事被写明，而不是某个批次号。** '
        // **What this needs is the withdrawal being stated, not a batch id.**
        matrix.Should().Contain("**上一版整节已撤回。**",
            "the withdrawal has to be stated, or the old conclusion gets copied back");
        matrix.Should().Contain("**153 行实现**", "the measurement is the correction");
        matrix.Should().Contain("没有生产调用方是一个**接线缺口**",
            "the distinction between a wiring gap and a shell is the whole point");
        matrix.Should().NotContain("**已删除**",
            "that claim was false; leaving it in lets the next reader repeat it");
    }

    /// <summary>**外部未验收项必须单列** / The externally-unverified items must be listed separately.</summary>
    /// <remarks>
    /// P9 的验收标准要求"外部未验收项单列"。若这一节被删掉，上表的"✅"就会被当成"已经真跑过"，
    /// 而实际上那些只证明"代码在"。
    ///
    /// The P9 acceptance criteria require the externally-unverified items to be listed separately. Without this section the ✅ marks above read as
    /// "actually ran it", while they only prove "the code is there".
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldListTheExternallyUnverifiedItems() {
        string matrix = ReadMatrix();

        // 断言**标题与首段连在一起**：只断言标题会放过"标题留下、内容被删"的实现。
        // Assert the **heading and its opening paragraph together**: asserting the heading alone lets "heading kept, body deleted" pass.
        matrix.Should().Contain("## 八、外部未验收项（**单列，不计入上表定级**）\n\n上表的定级依据是");
        // **撤回了 `**许可与真实求解未验收**` 这条钉子**：它把四行本来能验的项目写成了验不了，
        // 而实测四个后端都真跑过（Gurobi 829 / CPLEX 26 / COPT 37 / SCIP 46 个门内文件）。
        // **判据跟着改成钉"已验收的那四个 + 仍然没验的那几个"**——两边都要在，只钉一边会放过另一边被悄悄改。
        //
        // **withdraws the "licence and real solving unverified" pin**: it wrote four verifiable rows as unverifiable, while the measurement shows four backends
        // really running. The criterion now pins **both** the four that were verified **and** the ones still unverified — pinning one side lets the other slip.
        matrix.Should().Contain("**✅ 已验收**", "the four that really ran must say so");
        matrix.Should().Contain("**SDK 在，licence 缺**", "and the ones that could not run must say why");
        // SQLite 那条已经不再是"没有往返"**——`SqliteRoundTripTests` 关连接再开，真的落到文件库。
        // 但 Kafka 那条**仍然是**。所以两句分开钉：**只钉"有往返"会放过 Kafka 被悄悄写成已测**。
        //
        // SQLite is no longer "no round trip"** — `SqliteRoundTripTests` closes the connection and reopens it against a real file database. Kafka **still
        // is**. So both are pinned separately: **pinning only the positive would let Kafka be quietly written up as tested.**
        matrix.Should().Contain("**SQLite 有真实往返**", "the one backend that really round-trips must say so");
        matrix.Should().Contain("**没有真实 broker 往返测试**", "and the one that does not must keep saying so");
        matrix.Should().Contain("**两个问题的答案不一样**");
    }

    /// <summary>**未实测的原生后端不得标成"完整"** / An unverified native backend must not be marked complete.</summary>
    /// <remarks>
    /// 所有求解器插件只验证了"无 SDK 时的显式不可用"。若有人把 Gurobi 标成 ✅，那句"没有验证有 SDK 时的行为"就自相矛盾——
    /// 但**只断言那句话还在**抓不住这个矛盾：把它标成 ✅ 之后，那句话仍然躺在文件里。
    /// 因此这里直接断言**每一行求解器插件都不带 ✅**。
    ///
    /// Every solver plugin was only verified for "an explicit unavailable result when there is no SDK". If someone marks Gurobi ✅, the sentence about
    /// "behaviour with an SDK was never verified" contradicts itself — but **asserting only that the sentence is still there** cannot catch the
    /// contradiction: after marking it ✅ the sentence is still sitting in the file. So this asserts that **no solver-plugin row carries ✅**.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldNotClaimUnverifiedBackendBehaviour() {
        string matrix = ReadMatrix();
        // `Lingo` 与 `OptVerse` 也必须在这一列里**。它们此前不在断言表中，而文档里已经有它们的行——
        // 于是"把它们标成 ✅"这件事**没有任何判据会红**。这两个与其余八个还不同：**基线本身未实现**，
        // C# 侧是**不实现 `ILinearSolver` 的占位类型**，所以它们连"无 SDK 时显式失败"都不该假装有。
        //
        // `Lingo` and `OptVerse` belong in this list too.** They were missing while the document already had rows for them — so marking them ✅ would
        // **turn nothing red**. They also differ from the other eight: **the code itself does not implement them**, and the C# side is a **placeholder that does not
        // implement `ILinearSolver`**, so they must not even pretend to have "an explicit failure without an SDK".
        string[] plugins = {
            "| Gurobi |", "| Gurobi 11 |", "| SCIP |", "| CPLEX |",
            "| COPT |", "| Mosek |", "| MindOpt |", "| Hexaly |",
            "| Lingo |", "| OptVerse |",
        };
        foreach (string plugin in plugins) {
            int lineStart = matrix.IndexOf(plugin, StringComparison.Ordinal);
            lineStart.Should().BeGreaterThan(-1, $"{plugin} must be listed");
            int lineEnd = matrix.IndexOf('\n', lineStart);
            string row = matrix[lineStart..lineEnd];
            row.Should().NotContain("✅", $"{plugin} has unverified native behaviour and must not be marked complete");
        }
        matrix.Should().Contain("没有验证有 SDK 时的实际求解行为");

        // **两个占位后端必须自己说出来**：它们的行要写"基线本身未实现 … 不声明求解能力"，
        // 而不是混在"◐ 接口在、能力子集"里——**"没实现"和"实现了但没验证"是两件事**，混在一起等于把前者说成后者。
        //
        // **The two placeholder backends must say so themselves**: their rows must read "the code itself does not implement this … declares no solving capability",
        // not blend into "◐ interface present, capability subset" — **"not implemented" and "implemented but unverified" are different**, and blending them upgrades the former
        // into the latter.
        foreach (string placeholder in new[] { "| Lingo |", "| OptVerse |" }) {
            int lineStart = matrix.IndexOf(placeholder, StringComparison.Ordinal);
            lineStart.Should().BeGreaterThan(-1);
            string row = matrix[lineStart..matrix.IndexOf('\n', lineStart)];
            row.Should().Contain("➖", $"{placeholder} is a placeholder, not a backend with a verified subset");
            row.Should().Contain("不声明求解能力", $"{placeholder} must say it declares no solving capability");
        }
    }
    /// <summary>**已收口的阶段不得还挂着"项目不存在"** / A closed phase must not still say "the project does not exist".</summary>
    /// <remarks>
    /// 量到的矛盾：`docs/kotlin-sync-status.md` 说 **P4–P8 已收口**，而**同一套结论**在能力矩阵里还留着
    /// `Gap: … pending`、"后续 P6/P7/P8"、"**项目不存在**"。两份文档**互相矛盾**，而**没有任何判据会因此失败**。
    ///
    /// 更糟的是那些内容**已经过期**：`Framework.Csp2d`（21 个源文件）、`Framework.NetworkScheduling`（75 个）、
    /// `Core.Plugin.Heuristic`（29 个）、`Framework.Bpp1d`（25 个）都在，缺口清单对它们全报 **0 真缺口**。
    /// 留着"不存在"会让人去重做已完成的模块——与 按工程名误判是同一类伤害。
    ///
    /// **The contradiction measured**: `docs/kotlin-sync-status.md` says **P4-P8 are closed**, while **the same conclusions** in the capability matrix still
    /// read `Gap: … pending`, "next step P6/P7/P8" and "**the project does not exist**". Two documents **contradicting each other**, and **nothing failed because of
    /// it**. Worse, that content was **stale**: `Framework.Csp2d` (21 sources), `Framework.NetworkScheduling` (75), `Core.Plugin.Heuristic` (29) and
    /// `Framework.Bpp1d` (25) all exist with **0 real gaps**. Leaving "does not exist" sends people to redo finished modules — the same damage as.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldNotKeepClosedPhasesPending() {
        string matrix = ReadMatrix();

        // **工程存在就要写存在**：这四条覆盖 P6/P7/P8 三个已收口阶段里被写成"不存在"的那几个模块。
        // **An existing project must be described as existing**: these four are the modules a closed phase still described as missing.
        matrix.Should().NotContain("项目不存在", "every project named here exists; a closed phase must not still say otherwise");
        matrix.Should().NotContain("**C# 侧没有", "the Bpp1d/Csp2d projects exist; only their starter packages are missing");
        matrix.Should().Contain("| BPP 1D | `Framework.Bpp1d` | ✅ |");
        matrix.Should().Contain("| CSP 2D | `Framework.Csp2d` | ✅ |");
        matrix.Should().Contain("| 网络调度 | `Framework.NetworkScheduling` | ✅ |");
        matrix.Should().Contain("| 启发式算子 | `Core.Plugin.Heuristic` | ✅ |");
    }

    /// <summary>**系统性偏差那五条必须逐条实测** / The five systematic deviations must each carry a measurement.</summary>
    /// <remarks>
    /// **之前这一节从没被量过**，五条里有两条已经不成立：第 2 条早被 撤回，第 5 条的措辞（「所有求解器插件只验证了无 SDK 时的不可用」）
    /// 把「验证过」说成了「能验证」——**各插件其实都有真后端门，只是这个环境只跑得动 SCIP**。
    ///
    /// 判据钉三件事：**五条都在**、**每条带证据或带撤回标记**、**第 5 条不许退回成"全都没验证"**。
    /// 数值本身（11 个文件、5 个 `where`）会随代码变，所以判据钉**结论词**而不是数字——数字写在表里供人看。
    ///
    /// **This section had never been measured at first**, and two of its five entries no longer held: item 2 was withdrawn, and item 5's wording turned
    /// "verified" into "verifiable" — **every plugin does have a real-backend gate, only SCIP runs in this environment**. The criterion pins three things: **all five
    /// entries are present**, **each carries evidence or a withdrawal marker**, and **item 5 must not revert to "none of them verified"**. The numbers move with the
    /// code, so the criterion pins the **verdict words** rather than the figures; the figures sit in the table for humans.
    /// </remarks>
    [Fact]
    public void CapabilityMatrix_ShouldMeasureEverySystematicDeviation() {
        string matrix = ReadMatrix();

        matrix.Should().Contain("## 九、已知的系统性偏差");
        // **五条逐条点名**：少了任何一条，读者会以为那个偏差不存在。
        // **All five named**: a missing row reads as "that deviation does not exist".
        // **钉结论，不钉整行**： 在结论后面补了实测细节（"11 个校验点"、更正后的文件数），
        // 那是加信息，不是改结论。原来钉到行尾的 `|` 会因为一个括号就红——**判据该钉立场，不该钉格式。**
        //
        // **Pin the conclusion, not the whole row**: appended measured detail after it (11 validation sites, a corrected file count), which adds information
        // rather than changing the conclusion. Pinning to the closing `|` reddens over a parenthesis — **a criterion should pin the position, not the formatting.**
        matrix.Should().Contain("| 1 | **前置校验 vs 模型约束** | **仍然成立**");
        matrix.Should().Contain("| 2 | ~~`PreciseAssignment` 模型缺失~~ | **已撤回** |");
        matrix.Should().Contain("| 3 | **PWL 体积路径缺失** | **仍然成立**");
        matrix.Should().Contain("| 4 | **通配符泛型缺失** | **仍然成立**");
        matrix.Should().Contain("| 5 | **原生后端行为未验证** | **收窄了，但没消失** |");
        // **第 5 条的真实形状**：**门在、而且四个后端在这个环境里真跑过**。
        // 上一版写"只有一个后端真跑过"，逐个开门的实测推翻了它。
        //
        // **Item 5's real shape** (re-measured): the gates exist **and four backends really run in this environment**. The previous version said only one did;
        // opened each gate and the measurement overturned it.
        matrix.Should().Contain("四个后端在这个环境里真跑过");
        matrix.Should().Contain("829 通过", "the measurement has to carry its number");
        // **门符号要在表里逐行出现**：四个真跑的 + 三个没跑的都点名。
        // **Every gate symbol has to appear row by row** : the four that ran and the three that did not.
        foreach (string gate in new[] {
            "SCIP", "Gurobi", "Cplex", "Copt", "Mindopt", "Mosek", "Hexaly",
        }) {
            matrix.Should().Contain("| " + gate + " |", gate + " must have its own row, and the symbol names it");
        }
        matrix.Should().NotContain("所有求解器插件只验证了",
            "that wording turned 'verified' into 'verifiable'; every plugin has a real-backend gate");
        // **"不能在这个环境里补完"必须写明理由**：否则后续会去试。
        // **"cannot be completed here" must state why**, or the next step tries it.
        matrix.Should().Contain("**写一个\"假后端\"来验证真后端行为等于什么都没验证**");
    }
}
