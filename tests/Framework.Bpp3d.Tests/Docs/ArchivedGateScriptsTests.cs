#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 归档的门禁脚本必须自洽 / An archived gate script has to agree with its own number.
/// </summary>
/// <remarks>
/// **纯文档，无新增源码。** 它钉住归档脚本的编号自洽性。
///
/// **`g1070.ps1` 曾被改坏**——补丁写进了此前的脚本，**而它已经跑过、日志已经归档。**
/// 发现它靠的是"我记得 g1070 不该有那段"，**而不是任何判据。**
/// **下一个会话不会有这段记忆。**
///
/// **No source change.** It pins the numbering consistency of the archived gate scripts.
/// **`g1070.ps1` was once corrupted** — the patch landed in the previous step's script, **which had already run and been archived.** I caught it by **remembering that g1070 should not have that block**, **not
/// by any criterion.** **The next session will not have that memory.**
/// </remarks>
public class ArchivedGateScriptsTests
{
    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }
        return directory;
    }

    private static (int Number, string Path, string Text)[] Scripts()
    {
        DirectoryInfo logs = new(Path.Combine(Root().FullName, "logs"));
        return logs.GetFiles("g*.ps1")
            .Select(file => (
                Number: int.TryParse(file.Name.Substring(1, file.Name.Length - 5), out int number) ? number : -1,
                Path: file.Name,
                Text: File.ReadAllText(file.FullName)))
            .Where(entry => entry.Number > 0)
            .OrderBy(entry => entry.Number)
            .ToArray();
    }

    /// <summary>**临时日志名必须是自己的批次号** / The temp log has to carry the script's own number.</summary>
    /// <remarks>
    /// 一个从此前复制过来、只改了部分数字的脚本，**会把这里的日志写进此前的名字里**——
    /// **于是两批的证据混在一起，而两条记录都仍然读得通顺。**
    ///
    /// A script copied from an earlier script with only some numbers bumped **writes its own logs under the other run's names** — **the two runs' evidence then mixes, and both records still read
    /// plausibly.**
    /// </remarks>
    [Fact]
    public void TheTempLogNames_ShouldMatchTheScriptNumber()
    {
        (int Number, string Path, string Text)[] scripts = Scripts();

        scripts.Should().NotBeEmpty("at least one gate script is archived");
        foreach ((int number, string path, string text) in scripts)
        {
            int[] found = Regex.Matches(text, @"tmp(\d{4})")
                .Select(match => int.Parse(match.Groups[1].Value))
                .Distinct()
                .ToArray();

            found.Should().Equal(new[] { number },
                $"{path} may only write logs named after its own batch");
        }
    }

    /// <summary>**归档目标必须是"批次号 = 脚本号 − 2"** / The archive target is the script number minus two.</summary>
    /// <remarks>
 /// **门禁脚本叫 `gNNNN`，归档叫 `p8-batch(NNNN−2)`**。
    /// **这个偏移量是约定，而约定只在不被写错时成立。**
    ///
    /// **The gate script is `gNNNN` and the archive is `p8-batch(NNNN−2)`** (since then the two differ by two). **The offset is a convention, and a convention holds only while nobody mistypes it.**
    /// </remarks>
    [Fact]
    public void TheArchiveTarget_ShouldBeTwoBelowTheScriptNumber()
    {
        (int Number, string Path, string Text)[] scripts = Scripts();

        foreach ((int number, string path, string text) in scripts)
        {
            int[] found = Regex.Matches(text, @"p8-batch(\d{4})")
                .Select(match => int.Parse(match.Groups[1].Value))
                .Distinct()
                .ToArray();

            found.Should().Equal(new[] { number - 2 },
                $"{path} has to archive into the batch it actually covers");
        }
    }

    /// <summary>**每个跑完的归档脚本都要有对应的归档日志** / Every finished script has to have its archives.</summary>
    /// <remarks>
    /// **脚本留着而日志不在，说明那一批的"跑过了"没有任何证据**；
 /// **日志留着而脚本不在，说明这些数字怎么算出来的已经不可复核**。
    ///
    /// **最新那一份必须跳过，否则这条判据会否证它自己**：它要求脚本留下日志，
    /// **而那些日志正是这次运行最后才写出来的。** 第一版没有跳过，
    /// **于是在 `g1072` 自己的运行里红了**——**一条判据在自己的运行中必然为假，那不是发现，是设计错误。**
    ///
 /// **A script with no logs means that batch's "it ran" has no evidence**; **logs with no script mean how those numbers were produced is no longer checkable**.
    /// **The newest one has to be skipped, or this criterion refutes itself**: it demands logs that **that very run writes at the end.** The first version did not skip it, **so it went red inside `g1072`'s own run** — **a criterion that is necessarily false while its own run is in flight is a design error, not a finding.**
    /// </remarks>
    [Fact]
    public void TheArchivedScripts_ShouldAllHaveTheirLogs()
    {
        DirectoryInfo logs = new(Path.Combine(Root().FullName, "logs"));
        (int Number, string Path, string Text)[] scripts = Scripts();

        // **跳过最新一份：它的日志由它自己这一趟写出。**
        // **Skip the newest: its logs are written by the very run that asks.**
        int newest = scripts.Max(entry => entry.Number);
        foreach ((int number, string Path, string _) in scripts.Where(entry => entry.Number < newest))
        {
            string pattern = $"p8-batch{number - 2:D4}-*";
            logs.GetFiles(pattern).Should().NotBeEmpty(
                $"{Path} has to leave logs named {pattern} behind, or its run is unverifiable");
        }
    }
}
