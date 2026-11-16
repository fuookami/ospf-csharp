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
/// **这两条判据是 B1273 事故的直接产物。**
///
/// **事故：** **为了替换 `### 下一批的唯一入口` 里一段过期正文，
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
/// **These two criteria are the direct product of the B1273 incident: to replace one stale section body I used a slice
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
    /// <summary>
    /// **本批的缺口已被声明，不是"从来如此"** / This batch's loss is declared, not "always so".
    /// </summary>
    /// <remarks>
    /// **这一条<b>不</b>去断言"没有缺口**——因为缺口确实存在，而假装没有比承认更糟。</b>
    ///
 /// **`PhaseRangeTests.TheBatchNumbers_ShouldBeGloballyContiguous`已经在断言全局连续，
    /// 它现在红着，而那是对的：** **断号即丢失，这是那条判据的原意。**
    ///
    /// **本批没有放宽那条判据，也没有伪造条目去糊住它。**
    /// **本批新增的是这一条：** **缺口的存在必须由 `0.5` 事故记录<b>声明**，
    /// 并且声明里必须写明区间与条数。**
    ///
    /// **为什么要有它：** **如果哪天有人删掉那段声明，
    /// `PhaseRangeTests` 仍然红（缺口还在），但<b>再没有人知道为什么**。
    /// **红而没有理由，和绿而没有理由一样糟。**
    ///
    /// **This one deliberately does <b>not</b> assert "no gap" — the gap exists, and pretending otherwise would be worse than
    /// saying so.** **`PhaseRangeTests.TheBatchNumbers_ShouldBeGloballyContiguous` (standing since B1007) already asserts global
    /// contiguity, and it is red right now, which is correct: a hole means a lost conclusion, that is what it was written for.**
    ///
    /// **This batch neither relaxed that criterion nor invented entries to plug it.** **What it added is this one: the gap's
    /// existence must be <b>declared</b> by the 0.5 incident record, with its range and count.** **Why it is needed: if someone
    /// later deletes that declaration, `PhaseRangeTests` stays red (the hole is still there) but <b>nobody knows why</b> — and red
    /// without a reason is as bad as green without one.**
    /// </remarks>
    [Fact]
    public void TheDeclaredLoss_ShouldStayDeclared() {
        string plan = File.ReadAllText(PlanPath());

        plan.Should().Contain(
            "### 0.5 B1273 的事故记录",
            "the incident record is the only place the gap's reason lives");

        plan.Should().Contain(
            "891—1268",
            "the lost range must be named; a vague 'some entries were lost' cannot be checked against the hole");

        plan.Should().Contain(
            "378 条",
            "the count must be stated, or a reader cannot tell a 3-entry loss from a 378-entry one");

        // **而声明必须在，条目也必须真的不在**——如果哪天原文从别处找回来了，
        // **这条会红，逼着把声明改成"已恢复"。**
        //
        // **And the declaration must be there while the entries must really be absent: if the text is ever recovered from
        // somewhere, this goes red and forces the declaration to become "recovered".**
        plan.Should().Contain(
            "不再重建",
            "the declaration must say the text was not rebuilt; recovering it later means rewriting this line");
    }

    /// <summary>
    /// **四个参考小节都在** / All four reference sections exist.
    /// </summary>
    /// <remarks>
    /// **这四个小节分别回答：交接清单、判据弱点与红线、门禁怎么跑、事故记录。**
 /// **两次误删都发生在它们身上，所以逐个断言。**
    ///
    /// **The four answer, respectively: the hand-off checklist, criteria weaknesses and red lines, how to run the gate, and the
    /// incident record.** **Both deletions hit them (B1273 took three at once), so each is asserted individually.**
    /// </remarks>
    [Fact]
    public void TheReferenceSections_ShouldExist() {
        string plan = File.ReadAllText(PlanPath());

        foreach (string heading in new[] { "### 0.2 ", "### 0.3 ", "### 0.4 ", "### 0.5 " }) {
            plan.Should().Contain(
                heading,
                "these sections carry the hand-off state; losing one silently is how B1273 went unnoticed");
        }
    }

    /// <summary>
    /// **状态文档必须点名最新的那一批** / The status document must name the newest batch.
    /// </summary>
    /// <remarks>
    /// **B1281 收口时发现的：** **`docs/kotlin-sync-status.md` 里那句 `上次更新 Batch 1272`
    /// <b>已经九批没动过了**，而没有任何判据发现。**
    ///
    /// **原因有两层，第二层更值得记：**
    ///
    /// 1. **没有判据把状态文档与计划里的最新批次对上** —— `DeclaredNewestBatch()` 早就写好了，
    ///    却<b>从头到尾没有被任何判据调用**，是一段死代码。**
    /// 2. **收口脚本用 `replace(旧串, 新串)` 改它，而变化是<b>静默的**：
    ///    **字符串对不上就什么都不做，脚本照样打印完收尾信息。** **于是"我更新了文档"这件事
    ///    连续九批都是假的，而每一步都成功退出。**
    ///
    /// **这条判据把那段死代码用上，让第 2 层的沉默变成红：** **状态文档点名的批次必须等于计划里最新的批次。**
    ///
    /// **B1281 found this while closing: the line `上次更新 Batch 1272` in the status document had <b>not moved for nine
    /// batches</b>, and no criterion noticed.** **Two layers of cause, the second worth more:** **nothing compared the status
    /// document to the plan's newest batch — `DeclaredNewestBatch()` was written and <b>never called by any criterion</b>, dead
    /// code; and the closing script edited it with `replace(old, new)`, whose failure is <b>silent</b>: a mismatched string
    /// changes nothing and the script still prints its closing line.** **So "I updated the document" was untrue for nine batches
    /// while every step exited successfully.** **This criterion puts that dead helper to work and turns the silence red: the
    /// batch named by the status document must equal the newest batch in the plan.**
    /// </remarks>
    [Fact]
    public void TheStatusDoc_ShouldNameTheNewestBatch() {
        string plan = File.ReadAllText(PlanPath());
        int newest = Regex.Matches(plan, @"^### P[1-9]\d* Batch (\d+)", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Max();

        DeclaredNewestBatch().Should().Be(
            newest,
            "a status document that names an older batch is the shape of a closing script whose replacement silently did nothing");
    }

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
