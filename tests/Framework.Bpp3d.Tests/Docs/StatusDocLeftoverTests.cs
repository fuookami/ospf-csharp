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
/// **状态文档的"Summary of what is left in P9"，头一条写着：**
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
/// **这不是巧合：** **"已完成"会被后续的条目带走，"还缺"却没人回去划掉**——
/// **于是清单只会越列越长，而读者按它去做的第一件事，往往是把做完的事再做一遍。**
///
/// **The "Summary of what is left in P9" and found its first bullet saying "Still missing: the other modules'
/// READMEs, a migration guide, and the capability matrix" — all three exist.** **And the same document, three bullets down,
/// already states the lesson: "a stale 'cannot' is more expensive than a stale 'done', because it stops the work" — while its own
/// first bullet was exactly such a stale cannot.** **Not a coincidence: a "done" gets carried away by the next step's entry, while a
/// "missing" never gets struck out — so the list only grows, and the first thing a reader does with it is the work again.**
/// </remarks>
public class StatusDocLeftoverTests
{
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