#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"还缺"清单不许再列已经存在的东西** / The "still missing" list may not name what already exists.
/// </summary>
/// <remarks>
/// **B1292 读 `docs/kotlin-sync-status.md` 的"Summary of what is left in P9"，头一条写着：**
/// **"Still missing: the other modules' READMEs, a migration guide, and the capability matrix."**
///
/// **三项现在<b>全部存在**：** 其余模块都带 `README.md`/`README_ch.md`、指南是 `docs/kotlin-migration-guide.md`、
/// 矩阵是 `docs/kotlin-capability-matrix.md`。**
///
/// ### 而这份文档<b>自己</b>在往下三条里写过这句话
///
/// **"a stale 'cannot' is more expensive than a stale 'done', because it stops the work"** —
/// **而它<b>自己的第一条**正是这样一句过期的"还缺"。**
///
/// **这不是巧合：** **"已完成"会被下一批的条目带走，"还缺"却没人回去划掉**——
/// **于是清单只会越列越长，而读者按它去做的第一件事，往往是把做完的事再做一遍。**
///
/// **B1292 read the "Summary of what is left in P9" and found its first bullet saying "Still missing: the other modules'
/// READMEs, a migration guide, and the capability matrix" — all three exist.** **And the same document, three bullets down,
/// already states the lesson: "a stale 'cannot' is more expensive than a stale 'done', because it stops the work" — while its own
/// first bullet was exactly such a stale cannot.** **Not a coincidence: a "done" gets carried away by the next batch's entry, while a
/// "missing" never gets struck out — so the list only grows, and the first thing a reader does with it is the work again.**
/// </remarks>
public class StatusDocLeftoverTests
{
    /// <summary>**清单里那三项必须真实存在** / The three named artifacts must really exist.</summary>
    /// <remarks>
    /// **判据分两半：** **① 文档里不再出现那句过期的"Still missing"；② 那三项<b>在磁盘上确实在**。**
    ///
    /// **第二半是关键：** **只查文档文字的话，有人删掉迁移指南时判据不会红——而那时文档的说法反而又"对"了，
    /// 却没人知道该补回来。** **两半合起来才让"还缺"这个词有意义。**
    ///
    /// **Two halves: the stale sentence is gone, and the three artifacts really are on disk.** **The second half matters: with
    /// text-only checking, deleting the migration guide would leave the criterion green — the sentence would become true again
    /// without anyone knowing it needed restoring.**
    /// </remarks>
    [Fact]
    public void TheThreeArtifacts_ShouldExistAndNotBeCalledMissing() {
        string root = RepositoryRoot();
        string status = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-sync-status.md"));

        // **① 那句过期的"还缺"不许回来。**
        // **The stale "still missing" sentence must not come back.**
        status.Should().NotContain(
            "Still\n  missing: the other modules' READMEs",
            "the modules' READMEs, the migration guide and the matrix all exist; naming them as missing stops the work");
        status.Should().NotContain(
            "Still\r\n  missing: the other modules' READMEs",
            "the same sentence with CRLF line endings");

        // **② 三项必须真实存在。**
        // **The three artifacts must really exist.**
        File.Exists(System.IO.Path.Combine(root, "docs", "kotlin-migration-guide.md"))
            .Should().BeTrue("the migration guide is one of the three");
        File.Exists(System.IO.Path.Combine(root, "docs", "kotlin-capability-matrix.md"))
            .Should().BeTrue("the capability matrix is one of the three");

        string[] moduleReadmes = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "README.md", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        moduleReadmes.Length.Should().BeGreaterThanOrEqualTo(
            4,
            "the other modules' READMEs are the third artifact; the baseline's own root README is not among them");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}