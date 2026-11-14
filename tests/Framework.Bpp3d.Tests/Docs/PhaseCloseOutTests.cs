#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 阶段收口必须有证据 / A phase close-out must carry evidence.
/// </summary>
/// <remarks>
/// **"已关闭"是一句可以被抄下去的话。** 它没有说谁关的、依据是什么、最后一条是什么——
/// 于是下一任维护者只能选择相信或不相信。
///
/// **"Closed" is a sentence that can be copied forward.** It does not say who closed it, on what basis, or what the last entry was, leaving the next maintainer to either believe it or not.
/// </remarks>
public class PhaseCloseOutTests {
    private static string StatusPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", "kotlin-sync-status.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("docs/kotlin-sync-status.md not found");
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

    /// <summary>**状态文档点名的阶段，都必须有收口条目** / Every phase the status calls closed needs a close-out entry.</summary>
    /// <remarks>
    /// 实测发现：**P4、P5、P7 有收口条目；P6、P8 没有。** 而状态文档写的是"P4—P8 已关闭"。
    ///
    /// **这两句话不可能同时为真。** 要么 P6/P8 缺一条收口，要么"已关闭"是抄下来的。
    /// 判据不能替人决定是哪种，**但可以拒绝让这个矛盾继续安静地待着。**
    ///
    /// Measured: **P4, P5 and P7 have close-out entries; P6 and P8 do not**, while the status says "P4 through P8 are closed". **Both cannot be true.** Either P6/P8 lack a close-out
    /// or "closed" was copied forward. The criterion cannot decide which, **but it can refuse to let the contradiction sit there quietly.**
    /// </remarks>
    [Fact]
    public void ThePhasesCalledClosed_ShouldEachHaveACloseOutEntry() {
        string plan = File.ReadAllText(PlanPath());

        // **"收口"必须指向阶段本身，不能是某个子模块。**
        // **收口 has to name the phase itself, not some sub-module.**
        //
        // 第一版判据只查"标题里有没有收口"，于是 `P6 Batch 71：QuantityCore（几何地基收口）`
        // 把一个**子模块**的收口当成了阶段收口——**判据被它蒙混过关。**
        // 把判据收紧成"必须出现 `P6 收口` / `P6：收口` / `P6 收口盘点`"之后，它当场红。
        //
        // **The first version only looked for 收口 anywhere in a title**, so `P6 Batch 71: QuantityCore (geometry foundation close-out)` passed a **sub-module** close-out off as
        // the phase one — **the criterion was fooled.** Tightening it to the three exact forms made it go red.
        for (int phase = 4; phase <= 8; phase++) {
            string[] phaseForms = [
                $"P{phase} 阶段收口",
                $"P{phase} 收口",
                $"P{phase}：收口",
                $"P{phase} 收口盘点",
            ];

            bool closed = plan
                .Split('\n')
                .Where(line => line.StartsWith("### ", StringComparison.Ordinal))
                .Any(line => phaseForms.Any(form => line.Contains(form, StringComparison.Ordinal)));

            closed.Should().BeTrue(
                $"docs/kotlin-sync-status.md calls P{phase} closed, so a P{phase} entry has to close the phase itself — a sub-module close-out does not count");
        }
    }

    /// <summary>**"已关闭"必须点名依据** / "Closed" must name its basis.</summary>
    /// <remarks>
    /// 状态文档的那句话后面必须跟着**区间**（Batch 起点到终点），
    /// **否则读者无法核对"关闭"覆盖了哪些批次。**
    ///
    /// The sentence in the status has to carry a **range** (first to last batch), **or the reader cannot check which batches the closure covers.**
    /// </remarks>
    [Fact]
    public void TheClosedClaim_ShouldNameItsRange() {
        string status = File.ReadAllText(StatusPath());

        int index = status.IndexOf("Phases P4 through P8 are closed", StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, "the status states the closed phases");

        // **区间必须紧邻那句声明**：同一段里要出现 Batch 编号。
        // **The range must sit next to the claim**: a batch number in the same paragraph.
        int paragraphEnd = status.IndexOf("\n## ", index, StringComparison.Ordinal);
        string paragraph = paragraphEnd > index ? status[index..paragraphEnd] : status[index..];

        paragraph.Should().MatchRegex(@"Batch\s*\d+",
            "the closed claim names no batch range, so nobody can check what it covers");
    }
}
