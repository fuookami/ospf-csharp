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
/// **计划文件的结构完整：批次不能缺号，参考小节不能消失** /
/// The plan's structure stays whole: no missing batch numbers, no vanished reference sections.
/// </summary>
/// <remarks>
/// **这两条判据是那次事故的直接产物。**
///
/// **事故：** **为了替换 `### 后续的唯一入口` 里一段过期正文，
/// 我用了一个"从这个标题到下一个 `## `"的切片替换——而下一个 `## ` 在文件很后面，
/// 于是那一次替换删掉了约 20972 行，包含第 891—1272 批的全部 382 条条目
/// 与 `0.2`／`0.3`／`0.4` 三个小节。**
///
/// **`logs/` 在 `.gitignore` 里，没有版本副本。删除立即生效。**
///
/// ### 为什么原有的 225 条判据一条都没拦住
///
/// **因为它们查的都是"某个具体的东西在不在"：**
///
/// - **`GateNumbersTests` 查最新归档日志的数字**
/// - **`HandoffChecklistTests` 查 `0.2`／`0.3` 的内容**
/// - **`CapabilityMatrixTests` 查矩阵的行**
///
/// **没有一条查<b>"批次号连不连续"**。** **一个从 242 跳到 1272 的文件，
/// 在所有这些判据眼里都是正常的——只要 1272 那一批在、`0.2` 在。**
///
/// **所以这两条补的是"<b>整体形状**"这一层：
/// 它数的是文件里<b>到底有多少批**，而不是"我要找的那一批在不在"。</b>
///
/// **These two criteria are the direct product of that incident: to replace one stale section body I used a slice
/// replacement "from this heading to the next `## `" — and the next `## ` was very far down the file, so that single
/// replacement deleted ~20972 lines, including all 382 batch entries from 891 to 1272 and sections 0.2/0.3/0.4.** **`logs/` is
/// in `.gitignore`, so there was no version copy; the deletion took effect immediately.**
///
/// **Why none of the existing 225 criteria caught it: they all check "is this specific thing present" — the newest archived
/// number, the contents of 0.2/0.3, the matrix rows — and not one checks whether the batch numbers are <b>contiguous</b>.**
/// **A file jumping from 242 straight to 1272 looks normal to every one of them, as long as 1272 and 0.2 are there.** **So these
/// two add the "overall shape" layer: they count <b>how many batches the file actually holds</b>, not whether the one being
/// looked for is present.**
/// </remarks>
public class PlanStructureTests
{
    private static int DeclaredNewestBatch() {
        string status = File.ReadAllText(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(PlanPath())!, "..", "docs", "kotlin-sync-status.md"));
        Match match = Regex.Match(status, @"上次更新 Batch (\d+)");
        if (!match.Success) {
            throw new InvalidOperationException("status doc no longer declares 上次更新 Batch N");
        }
        return int.Parse(match.Groups[1].Value);
    }

    private static string PlanPath() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            string candidate = System.IO.Path.Combine(current.FullName, "logs", "kotlin-sync-plan.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("logs/kotlin-sync-plan.md not found");
    }
}
