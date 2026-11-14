#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **状态文档的抬头也必须跟着批次走** / The status document's opening lines must move with the batch.
/// </summary>
/// <remarks>
/// **B1291 打开 `docs/kotlin-sync-status.md`，第一屏写着：**
///
/// | 位置 | 原文 | 差多少批 |
/// | --- | --- | --- |
/// | 标题 | `## Current Phase (as of Batch 265)` | **差 1025 批** |
/// | 正文 | `P9 is in progress (Batches 242–918)` | **差 372 批** |
///
/// **而同一份文档的 `上次更新 Batch N` 一直是准的**（B1281 给它做过判据）。
///
/// ### 为什么这次特别值得记
///
/// **B1281 的病是"收口脚本 `replace` 静默不生效"；B1284 的病是"交接表比计划旧九批"。**
/// **两次修的都<p>同一份文档里被引用的数字**——**而没有一次去看那份文档<b>自己第一屏**。**
///
/// **所以这一批的教训不是"又一个数字过期了"，而是：** **我们一直在修"引用"，没修"抬头"。**
/// **抬头是读者第一眼看到的东西，而它恰恰是最不受任何判据保护的。**
///
/// **B1291 opened the status document and its first screen said `as of Batch 265` (1025 batches stale) and
/// `P9 is in progress (Batches 242–918)` (372 stale) — while the same document's `上次更新 Batch N` marker was accurate all
/// along (B1281 added a criterion for it).** **B1281's disease was a silently no-op `replace`; B1284's was a hand-off table nine
/// batches behind.** **Both fixed numbers that <b>referenced</b> the status — and neither looked at the status document's own first
/// screen.** **So the lesson is not "another stale number" but: we kept fixing references and never the masthead, which is the first
/// thing a reader sees and the last thing any criterion protected.**
/// </remarks>
public class StatusDocMastheadTests
{
    /// <summary>**抬头两处的批次号必须等于 `上次更新 Batch N`** / Both masthead numbers must equal the marker.</summary>
    [Fact]
    public void TheMasthead_ShouldMoveWithTheMarker() {
        string status = File.ReadAllText(StatusPath());

        Match marker = Regex.Match(status, @"上次更新 Batch (\d+)");
        marker.Success.Should().BeTrue("the status document carries its batch marker");
        int newest = int.Parse(marker.Groups[1].Value);

        Match heading = Regex.Match(status, @"^## Current Phase \(as of Batch (\d+)\)", RegexOptions.Multiline);
        heading.Success.Should().BeTrue("the opening heading states which batch it describes");
        int.Parse(heading.Groups[1].Value).Should().Be(
            newest,
            "the first line a reader sees must name the batch the document is actually at");

        Match range = Regex.Match(status, @"P9 is in progress\*\* \(Batches \d+–(\d+)\)");
        range.Success.Should().BeTrue("the opening paragraph states the P9 batch range");
        int.Parse(range.Groups[1].Value).Should().Be(
            newest,
            "the stated range must end at the newest batch, or the first screen describes a document that no longer exists");
    }

    private static string StatusPath() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            string candidate = System.IO.Path.Combine(current.FullName, "docs", "kotlin-sync-status.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("docs/kotlin-sync-status.md not found");
    }
}