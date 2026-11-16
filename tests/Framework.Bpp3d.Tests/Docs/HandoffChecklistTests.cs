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
/// 之前 `0.2` 是一长串 1–17，中间夹着重复的 2/3/4，**已撤回的旧结论和仍缺的能力混在同一个编号里**。
/// 之前 `0.3` 更糟：一条 bullet 把**一个已撤回的诊断**和**两个仍然存在的缺口**挤在一起。
/// 读的人无法判断哪条还在。
///
/// 清单是最容易腐烂的文档：**没有任何代码依赖它，所以没有任何测试会因为它的错而失败**。
///
/// Before，`0.2` was one long run of 1–17 with duplicate 2/3/4 in the middle, **mixing retracted conclusions with still-missing capabilities**. Before
///，`0.3` was worse: one bullet packed **a retracted diagnosis** together with **two live gaps**. A reader could not tell which items were live. A
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

}
