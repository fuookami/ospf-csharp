// P4—P9 验收对照表：点名的测试类必须真的存在 / The acceptance table's cited test classes must really exist.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **验收表点名的每一条证据都必须能被核对** / Every piece of evidence the acceptance table names must be checkable.
/// </summary>
/// <remarks>
/// 这张表存在的理由：P4—P9 的验收标准散在一万多行计划里，"哪一条真的核过"从来没人能一屏看完。
/// 但**一张写着不存在的测试名的表比没有表更糟**——它把"没核对"伪装成"有证据"。所以这里逐个核对：
/// 表里以 `\`XxxTests\`` 点名的地方，`tests/` 下必须真有一个同名类型文件。
///
/// 这条判据只证明"名字存在"，**不证明那条判据覆盖了这个验收项**。表尾自己也这么写：
/// ✅ 只说明点名的那条判据在跑，不说明模块的每一处行为都验证过。**判据的能力边界必须写进判据自己身上。**
///
/// Why this table exists: the P4-P9 acceptance criteria are scattered across tens of thousands of plan lines and nobody could see in one screen what had actually
/// been checked. But **a table naming tests that do not exist is worse than no table** — it disguises "not checked" as "evidence exists". So this verifies every
/// `\`XxxTests\`` name in the table really has a file under `tests/`. This proves **the name exists**, not that the criterion covers the acceptance item — the table's
/// own closing paragraph says the same, and **a criterion's limits belong on the criterion itself**.
/// </remarks>
public sealed class AcceptanceTableTests {
    private static string RepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }

    private static string ReadTable() =>
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "docs", "kotlin-sync-acceptance.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

}
