#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **注释里不得残留迁移叙述** / No migration narration may remain in comments.
/// </summary>
/// <remarks>
/// **判据只认一件事：`scripts/audit-comment-hygiene.py` 的命中数为零。** **那个脚本是这条规则的唯一定义，** '
/// **`docs/comment-hygiene-audit.md` 说明它把哪些形态判为叙述、哪些判为技术语境。** **判据与脚本分开，** '
/// **是因为"哪些算叙述"需要一张能被人读的表，而"有没有残留"需要一次能跑的扫描。**
///
/// **The criterion recognises one thing: the audit script reports zero hits.** That script is the single definition of the rule, and the audit document states which forms count as
/// narration and which as technical context. **The criterion and the script are separate because "what counts" needs a table a person can read, while "what remains" needs a scan that can be
/// run.**
/// </remarks>
public class CommentHygieneTests
{
    /// <summary>**审计脚本零命中** / The audit script reports zero hits.</summary>
    [Fact]
    public void Comments_ShouldCarryNoMigrationNarration() {
        string root = RepositoryRoot();
        string script = Path.Combine(root, "scripts", "audit-comment-hygiene.py");
        File.Exists(script).Should().BeTrue("the audit script is the definition of this rule");

        var start = new ProcessStartInfo("python", "scripts/audit-comment-hygiene.py") {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // **脚本以退出码表态：0 表示零命中，1 表示还有残留。** **把输出一并带上，** **红了就能直接看到是哪几行。**
        //
        // **The script speaks through its exit code: 0 for no hits, 1 for leftovers.** The output travels with the assertion, so a red shows the exact lines.
        process.ExitCode.Should().Be(0, "no migration narration may remain:\n" + output);
        // **不逐字比对中文**：Windows 控制台编码不一定是 UTF-8，比中文字符串会让判据因编码而红。
        // **改成断言"没有任何命中标记"——脚本每命中一行就打一个 `[`。**
        //
        // **Do not compare Chinese text verbatim**: the console encoding is not necessarily UTF-8 on Windows, so a text comparison would redden over encoding. **Assert instead that no
        // hit marker appears — the script prints a `[` for every hit.**
        output.Should().NotContain("[", "a hit marker means narration survived: " + output);
    }

    private static string RepositoryRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("the repository root was not found");
    }
}