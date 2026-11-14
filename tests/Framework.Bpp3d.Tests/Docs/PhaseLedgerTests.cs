#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 阶段核算台账必须可回溯 / The phase ledger has to be traceable.
/// </summary>
/// <remarks>
/// **台账的价值全在"依据"上。** 一条结论如果不指向计划里能搜到的条目，
/// 它就和状态文档里那句"已关闭"没有区别——**可以被抄下去，不能被核对。**
///
/// **The ledger's whole value is its basis.** A conclusion that points at nothing searchable in the plan is no better than the status document's "closed": **it can be copied forward,
/// it cannot be checked.**
/// </remarks>
public class PhaseLedgerTests {
    private static string DocsPath(string name) {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", name);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException($"docs/{name} not found");
    }

    private static string PlanPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "logs", "kotlin-sync-plan.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("logs/kotlin-sync-plan.md not found");
    }

    /// <summary>**台账点名的每一个计划条目都必须真的存在** / Every plan entry the ledger names has to exist.</summary>
    /// <remarks>
    /// 台账里写了 `P4 Batch 54`、`P5 Batch 65`、`P7 Batch 151` 这些定位。
    /// **任何一个拼错，读者就会搜不到，而搜不到会被理解成"这条结论是编的"。**
    ///
    /// The ledger cites `P4 Batch 54`, `P5 Batch 65`, `P7 Batch 151`. **One typo and the reader finds nothing, and finding nothing reads as "this conclusion was made up".**
    /// </remarks>
    [Fact]
    public void TheLedgerCitations_ShouldAllResolve() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));
        string plan = File.ReadAllText(PlanPath());

        MatchCollection citations = Regex.Matches(ledger, @"`P(?<phase>[4-9]) Batch (?<number>\d+)");
        citations.Should().NotBeEmpty("the ledger cites the plan by batch number");

        string[] missing = citations
            .Select(match => $"P{match.Groups["phase"].Value} Batch {match.Groups["number"].Value}")
            .Distinct()
            .Where(citation => !plan.Contains($"### {citation}", StringComparison.Ordinal))
            .ToArray();

        missing.Should().BeEmpty("every citation has to point at a real plan entry:\n" + string.Join("\n", missing));
    }

    /// <summary>**六个阶段都要有一行核算** / All six phases get an accounting row.</summary>
    [Fact]
    public void TheLedger_ShouldCoverEveryPhase() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        for (int phase = 4; phase <= 9; phase++) {
            ledger.Should().Contain($"### P{phase}：", $"P{phase} needs its own accounting section");
        }
    }

    /// <summary>**"搬不了"必须分成三类** / "Cannot port" splits into exactly three kinds.</summary>
    /// <remarks>
    /// **三类原因的下一步动作完全不同**：环境所限换个环境就能验；语言所限根本不是缺口；
    /// 需真实后端要接上后端才能验。**混成一类，读者只能统一当成"做不到"。**
    ///
    /// **The three kinds have completely different next steps.** Mixed together, the reader can only treat them all as "impossible".
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldSeparateTheThreeKindsOfGaps() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        ledger.Should().Contain("环境所限");
        ledger.Should().Contain("语言所限");
        // **B1051 把这一类改了名。** 旧名"需真实后端"把话说大了：
        // B1041/1049/1050 已证明编排用假后端就能验，真正缺的是**证据的来源**。
        // **判据跟着改名走**，而不是留着旧名让台账继续低估。
        //
        // **B1051 renamed this category.** The old name overstated it: B1041/1049/1050 showed the orchestration is provable with a stub, and what is actually missing is **the source of the evidence**.
        // **The criterion follows the rename** rather than keeping the old name alive and the ledger underestimated.
        ledger.Should().Contain("需真实对偶 / assumption 来源");

        ledger.Should().Contain("`err_missing_license_file(1008)`",
            "an environment gap names its concrete failure string, not just the word 'missing'");
        ledger.Should().Contain("`28P01: password authentication failed`",
            "the database gap names the server's own message");
        ledger.Should().Contain("缺的是凭据，不是服务",
            "the reader has to know whether to install a database or ask for a password");
    }

    /// <summary>**补写的收口必须自报家门** / A retroactive close-out declares itself.</summary>
    /// <remarks>
    /// P6 与 P8 的收口条目是 B1015 补写的。**台账里如果只写"已收口"，那句"已关闭"就又变成无据的了。**
    ///
    /// The P6 and P8 close-outs were written retroactively in B1015. **If the ledger just says "closed", the "closed" claim becomes baseless again.**
    /// </remarks>
    [Fact]
    public void TheRetroactiveCloseOuts_ShouldSaySo() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        int p6 = ledger.IndexOf("### P6：", StringComparison.Ordinal);
        int p8 = ledger.IndexOf("### P8：", StringComparison.Ordinal);
        p6.Should().BeGreaterThanOrEqualTo(0);
        p8.Should().BeGreaterThanOrEqualTo(0);

        string p6Section = ledger[p6..p8];
        // **必须落在"状态"那一行里**，而不是段内任何位置——
        // 段里别处提到"补写"不能让这条判据通过。
        //
        // **It has to sit on the status row itself**, not anywhere in the section — a mention of 补写 elsewhere must not satisfy it.
        p6Section.Split('\n')
            .First(line => line.Contains("| **状态** |", StringComparison.Ordinal))
            .Should().Contain("补写", "the P6 close-out was written after the fact, and its status row says so");

        string p8Section = ledger[p8..ledger.IndexOf("### P9：", StringComparison.Ordinal)];
        p8Section.Split('\n')
            .First(line => line.Contains("| **状态** |", StringComparison.Ordinal))
            .Should().Contain("补写", "the P8 close-out was written after the fact, and its status row says so");
    }

    /// <summary>**19 个基线文件都要在对表里** / All nineteen baseline files appear in the mapping.</summary>
    /// <remarks>
    /// **文件数不等（19 vs 40）不是缺口**：C# 侧把一部分逻辑拆成了独立文件。
    /// **但每一个基线文件都必须有一行结论**，否则"搬完了"这句话就没法核对。
    ///
    /// **An unequal count (19 vs 40) is not a gap**: the C# side split some logic into separate files. **But every baseline file needs its own verdict row**, or "ported" cannot be checked.
    /// </remarks>
    [Fact]
    public void TheMapping_ShouldCoverEveryBaselineFile() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        string[] baseline = [
            "ActivityEvidence", "ActivityStatus", "AnalysisStatus", "CapabilityMatrix",
            "ConflictAnalyzer", "ConstraintActivityAnalyzer", "ConstraintActivityReport",
            "ConstraintPerturbationAnalyzer", "ConstraintProgrammingAnalysisBackends",
            "CorrectionRecommendation", "CriticalConstraintAnalysisPipeline",
            "CriticalConstraintAnalysisReport", "CriticalConstraintAnalysisSession",
            "CriticalityProfile", "DiagnosticActivation", "DiagnosticSource",
            "FixedIntegerLpSensitivity", "ObjectiveTarget", "TargetFeasibilityAnalyzer",
        ];

        baseline.Should().HaveCount(19, "the baseline directory holds nineteen analysis files");

        string[] missing = baseline
            .Where(name => !ledger.Contains($"`{name}.kt`", StringComparison.Ordinal))
            .ToArray();

        missing.Should().BeEmpty("every baseline file needs a verdict in the mapping table: " + string.Join(", ", missing));
    }

    /// <summary>**"19 / 19"这个总数必须写着** / The "19 / 19" total is written down.</summary>
    /// <remarks>
    /// 一行一行都齐了，读者还得自己数一遍才知道是不是齐了。**把结论也写出来。**
    ///
    /// Even with every row present the reader has to count. **Write the conclusion too.**
    /// </remarks>
    [Fact]
    public void TheMapping_ShouldStateItsTotal() {
        File.ReadAllText(DocsPath("kotlin-sync-ledger.md"))
            .Should().Contain("19 / 19 有对应", "the reader should not have to count the rows themselves");
    }

    /// <summary>**状态文档里的警告数必须与实测一致** / The warning count in the status matches the measurement.</summary>
    /// <remarks>
    /// 实测发现：状态文档写着 `1346` 行，而 Release 重建实测是 **1316**——**差 30 行，抄了好几批没人核。**
    ///
    /// **这个数字是门禁的一部分**：它是"我们没在制造新警告"的唯一证据。**一个抄错的基线数字让这句话失去意义。**
    ///
    /// Measured: the status said 1346 lines while the Release rebuild measured **1316** — **thirty lines off, copied for many batches unchecked.** **This number is part of the gate**: it is the
    /// only evidence that no new warnings are being created. **A stale baseline figure makes that sentence meaningless.**
    /// </remarks>
    [Fact]
    public void TheWarningCount_ShouldMatchTheMeasurement() {
        string status = File.ReadAllText(DocsPath("kotlin-sync-status.md"));

        MatchCollection counts = Regex.Matches(status, @"`(\d+)` 行既有警告");
        counts.Should().NotBeEmpty("the status states the warning line count");

        counts.Select(match => int.Parse(match.Groups[1].Value))
            .Should().OnlyContain(count => count == 1316,
                "1316 is the measured Release rebuild warning-line count as of Batch 1028");
    }

    /// <summary>**台账的章节号不得重复** / The ledger does not reuse a section number.</summary>
    /// <remarks>
    /// 实测发现台账里有**三个 `## 三、`**——写第三节、补第四节、再写第五节时，
    /// 后两次都顺手写了"三"。**读者按编号跳转时会落到错误的那一节。**
    ///
    /// Measured: the ledger had **three `## 三、` headings** — the later two were both written as "three". **A reader jumping by number lands on the wrong section.**
    /// </remarks>
    [Fact]
    public void TheLedgerSections_ShouldBeNumberedOnce() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        string[] numbers = Regex.Matches(ledger, @"^## ([一二三四五六七八九十]+)、", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        numbers.Should().NotBeEmpty("the ledger has numbered sections");
        numbers.Should().OnlyHaveUniqueItems(
            "a repeated number makes the section impossible to cite: " + string.Join(" ", numbers));
    }

    /// <summary>**轮次上限交接必须写下"还没做的事"** / The hand-off records what is not done.</summary>
    /// <remarks>
    /// 交接里最容易漏的不是"做完了什么"，而是**"什么没做、为什么没做"**——
    /// **没有这一段，下一个人会以为范围已经清空。**
    ///
    /// What a hand-off most often omits is not what was finished but **what was not, and why** — **without that section the next reader assumes the scope is empty.**
    /// </remarks>
    [Fact]
    public void TheHandoff_ShouldRecordWhatIsNotDone() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        ledger.Should().Contain("还没做的事", "the hand-off names the remaining work");
        ledger.Should().Contain("为什么没做", "and why each item is still open");
        ledger.Should().Contain("需要真实后端", "the backend-bound items are distinguished from the unwritten ones");
        ledger.Should().Contain("环境所限", "and the environment-bound items from both");
    }

    /// <summary>**"不是缺口"的结论必须三步都过** / A "not a gap" verdict needs all three steps.</summary>
    /// <remarks>
    /// **第 3 步（有判据）最容易被跳过**，因为前两步通过之后"看起来就没事了"——
    /// **而"存在、完整、但没人钉住"的实现在下一次改动里就能被悄悄改坏。**
    ///
    /// **Step three (has criteria) is the one most often skipped**, because passing the first two makes everything look fine — **and an implementation that exists, is complete and is pinned by nothing can be
    /// quietly broken by the next change.**
    /// </remarks>
    [Fact]
    public void TheNotAGapVerdict_ShouldRequireThreeSteps() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        ledger.Should().Contain("判\"不是缺口\"的三步法", "the method is written down, not just its results");
        ledger.Should().Contain("有判据吗", "step three is the one that gets skipped");
        ledger.Should().Contain("下一次改动就能悄悄改坏它",
            "and the reason step three matters has to be stated, or it reads as optional");
    }

    /// <summary>**假缺口要记下来，含它为什么像缺口** / A phantom gap is recorded, with why it looked real.</summary>
    /// <remarks>
    /// **Kotlin 的 `object` / `companion object` 在 C# 侧被压平成静态类**，所以容器名搜不到而成员还在。
    /// **不把这条写下来，下一个人会写同一份重复实现。**
    ///
    /// **Kotlin `object` and `companion object` are flattened into static classes on the C# side**, so the container name misses while the member remains. **Without this note the next person writes the
    /// same duplicate.**
    /// </remarks>
    [Fact]
    public void ThePhantomGap_ShouldSayWhyItLookedReal() {
        string ledger = File.ReadAllText(DocsPath("kotlin-sync-ledger.md"));

        ledger.Should().Contain("第 1 步要搜\"成员名\"，不是\"容器名\"",
            "the rule belongs next to the three steps it corrects");
        ledger.Should().Contain("压平成静态类",
            "the mechanism is the part that generalises; without it the rule reads as a one-off");
        ledger.Should().Contain("FixedValues", "and the concrete member is named so it is searchable");
    }
}
