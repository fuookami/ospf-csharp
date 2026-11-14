// 交接清单的结构测试 / Structural tests for the handoff checklist.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// `logs/kotlin-sync-plan.md` 的两份清单（`0.2` P9 剩余、`0.3` 已知缺口）必须**分组清楚、编号不重复、撤回项有标记**。
/// The two lists in `logs/kotlin-sync-plan.md` (`0.2` P9 remaining, `0.3` known gaps) must be **grouped, without duplicate numbers, and with retractions
/// marked**.
/// </summary>
/// <remarks>
/// B293 之前 `0.2` 是一长串 1–17，中间夹着重复的 2/3/4，**已撤回的旧结论和仍缺的能力混在同一个编号里**。
/// B294 之前 `0.3` 更糟：一条 bullet 把**一个已撤回的诊断**和**两个仍然存在的缺口**挤在一起。
/// 读的人无法判断哪条还在。
///
/// 清单是最容易腐烂的文档：**没有任何代码依赖它，所以没有任何测试会因为它的错而失败**。
///
/// Before B293, `0.2` was one long run of 1–17 with duplicate 2/3/4 in the middle, **mixing retracted conclusions with still-missing capabilities**. Before
/// B294, `0.3` was worse: one bullet packed **a retracted diagnosis** together with **two live gaps**. A reader could not tell which items were live. A
/// checklist is the easiest document to rot: **no code depends on it, so no test fails when it is wrong**.
/// </remarks>
public sealed class HandoffChecklistTests {
    private static readonly string NewLine = ((char)10).ToString();

    private static string PlanPath()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            string candidate = Path.Combine(directory, "logs", "kotlin-sync-plan.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException("logs/kotlin-sync-plan.md not found from the test output directory");
    }

    private static string ReadPlan() =>
        DocsFile.ReadAllText(PlanPath()).Replace("\r\n", NewLine, StringComparison.Ordinal);

    /// <summary>截取某个 `### 0.x` 小节，直到下一个同级标题。</summary>
    private static string Section(string heading, string nextHeading) {
        string plan = ReadPlan();
        int start = plan.IndexOf(heading, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "section must exist: " + heading);
        int end = plan.IndexOf(nextHeading, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "section must end before: " + nextHeading);
        return plan[start..end];
    }

    private static string ChecklistSection() => Section("### 0.2 ", "### 0.3 ");

    private static string GapSection() => Section("### 0.3 ", "### 0.4 ");

    private static IEnumerable<string> GroupsOf(string section) =>
        Regex.Split(section, "^#### ", RegexOptions.Multiline).Skip(1);

    private static string TitleOf(string group) =>
        group.Split(NewLine.ToCharArray()[0])[0].Trim();

    private static List<int> NumbersOf(string group) =>
        Regex.Matches(group, @"^(\d+)\. ", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToList();

    private static List<string> EntriesOf(string group) =>
        Regex.Matches(group, @"^(\d+)\. (.+)$", RegexOptions.Multiline)
            .Select(match => match.Groups[2].Value)
            .ToList();

    private static List<string> RepeatedNumbersIn(string section) {
        var duplicates = new List<string>();
        foreach (string group in GroupsOf(section)) {
            var repeated = NumbersOf(group).GroupBy(number => number)
                .Where(byNumber => byNumber.Count() > 1)
                .Select(byNumber => byNumber.Key)
                .ToList();
            if (repeated.Count > 0) {
                duplicates.Add($"{TitleOf(group)}: {string.Join(", ", repeated)}");
            }
        }
        return duplicates;
    }

    [Fact]
    public void Checklist_ShouldBeSplitIntoNamedGroups() {
        string section = ChecklistSection();

        foreach (string heading in new[] {
            "#### A. 仍缺",
            "#### B. 已撤回的旧结论",
            "#### C. 已知分歧",
            "#### D. 无法在本仓库达成",
            "#### E. 反复出现的形态",
        }) {
            section.Should().Contain(heading);
        }
    }

    /// <summary>**0.2 的编号在每个分组内不得重复** / Numbers must not repeat within a group in 0.2.</summary>
    /// <remarks>
    /// 这是 B293 重排的直接原因：旧清单里 2、3、4 各出现两次。
    ///
    /// This is the direct reason for the B293 rewrite: the old list had 2, 3 and 4 twice each.
    /// </remarks>
    [Fact]
    public void Checklist_ShouldNotRepeatNumbersWithinAGroup() =>
        RepeatedNumbersIn(ChecklistSection()).Should().BeEmpty();

    /// <summary>**每个分组都要有编号条目** / Every group must have numbered entries.</summary>
    [Fact]
    public void Checklist_ShouldNumberEveryGroup() {
        var groups = GroupsOf(ChecklistSection()).ToList();
        groups.Should().NotBeEmpty();
        foreach (string group in groups) {
            NumbersOf(group).Should().NotBeEmpty($"group {TitleOf(group)} must have numbered entries");
        }
    }

    /// <summary>**0.2 的撤回项必须带删除线** / Retracted items in 0.2 must carry a strikethrough.</summary>
    /// <remarks>
    /// 撤回项不删掉是有意的：**读者需要知道"这条曾经被当成缺口"**，否则会重新提出来。
    /// 但必须一眼能看出它已撤回，所以要求 `~~...~~` 标记。
    ///
    /// Retracted items are kept deliberately: **readers need to know "this was once treated as a gap"**, or they will raise it again. But it must be
    /// obvious at a glance that it is retracted, hence the `~~...~~` marker.
    /// </remarks>
    [Fact]
    public void Checklist_RetractedEntries_ShouldBeStruckThrough() {
        string section = ChecklistSection();
        int start = section.IndexOf("#### B. 已撤回的旧结论", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        int end = section.IndexOf("#### C. ", StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        string group = section[start..end];

        List<string> entries = EntriesOf(group);
        entries.Should().NotBeEmpty();
        foreach (string entry in entries) {
            entry.Should().Contain("~~", "retracted entry must be struck through: " + entry);
        }
    }

    /// <summary>**不得再把已撤回的结论写成待办** / Refuted conclusions must not reappear as todos.</summary>
    [Fact]
    public void Checklist_ShouldNotReopenRefutedConclusions() {
        string section = ChecklistSection();
        int aStart = section.IndexOf("#### A. ", StringComparison.Ordinal);
        int aEnd = section.IndexOf("#### B. ", StringComparison.Ordinal);
        string groupA = section[aStart..aEnd];

        groupA.Should().NotContain("YAML/TOML 未实现");
        groupA.Should().NotContain("重量尚未参与任何约束");
    }

    /// <summary>**0.3 也必须分组** / Section 0.3 must be grouped too.</summary>
    /// <remarks>
    /// B294 之前它有一条 bullet 把**一个已撤回的诊断**和**两个仍然存在的缺口**挤在一起，
    /// 读的人会以为那三个都已处理。
    ///
    /// Before B294 one bullet packed **a retracted diagnosis** together with **two live gaps**, so a reader would think all three were handled.
    /// </remarks>
    [Fact]
    public void GapList_ShouldBeSplitIntoNamedGroups() {
        string section = GapSection();

        foreach (string heading in new[] {
            "#### A. 仍然存在的判据弱点",
            "#### B. 已撤回的旧诊断",
            "#### C. 跨语言与性能的红线",
        }) {
            section.Should().Contain(heading);
        }
    }

    /// <summary>**0.3 的编号在组内不得重复** / Section 0.3 must not repeat numbers within a group.</summary>
    [Fact]
    public void GapList_ShouldNotRepeatNumbersWithinAGroup() =>
        RepeatedNumbersIn(GapSection()).Should().BeEmpty();

    /// <summary>**0.3 的撤回项必须单独成条且带删除线** / Retracted entries in 0.3 must stand alone and be struck through.</summary>
    [Fact]
    public void GapList_RetractedEntries_ShouldStandAloneAndBeStruck() {
        string section = GapSection();
        int start = section.IndexOf("#### B. 已撤回的旧诊断", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        int end = section.IndexOf("#### C. ", StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        string group = section[start..end];

        List<string> entries = EntriesOf(group);
        entries.Should().NotBeEmpty();
        foreach (string entry in entries) {
            entry.Should().Contain("~~", "retracted entry must be struck through: " + entry);
        }

        // 逐条只查首行会放过"续行里又塞回一个仍存缺口"的写法——那正是 B294 之前的状态。
        // Checking only the first line of each entry lets "a live gap stuffed back into a continuation line" pass — exactly the pre-B294 shape.
        group.Should().NotContain("Batch 189");
        group.Should().NotContain("Batch 208");
    }

    /// <summary>**跨语言红线必须写明"不得写进结论"** / The cross-language red line must say "must not be claimed".</summary>
    [Fact]
    public void GapList_ShouldKeepTheNoUnmeasuredClaimRule() {
        string section = GapSection();

        section.Should().Contain("跨语言验证只做过三处");
        section.Should().Contain("不得写进结论");
    }
}
