#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 门禁脚本必须真的能发现失败 / The gate script has to be able to notice a failure.
/// </summary>
/// <remarks>
/// **纯文档，无新增源码。** 它给"门禁脚本"本身加了一道自检。
///
/// 门禁脚本每批都跑，而它的输出是**五个数字**。**五个数字全对不代表脚本在数东西**——
/// 一个把 `Passed:` 正则写错的脚本会永远数出 0，**而 0 看起来像"这批没有测试"。**
///
/// **No source change.** It gave the gate script a self-check. The script runs every batch and its output is **five numbers**. **Five correct numbers do not mean the script is counting anything** — a
/// script with a broken `Passed:` regex counts zero forever, **and zero reads as " had no tests".**
///
/// **判据钉的是脚本的"结构与能力"，不是它的输出。** 输出每批都变，结构不该变。
///
/// **The criteria pin the script's structure and capability, not its output.** The output changes every batch; the structure should not.
/// </remarks>
public class GateScriptContractTests
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

    /// <summary>最近一份归档的门禁脚本 / The most recently archived gate script.</summary>
    /// <remarks>
    /// **按批次号取最大的一份**，而不是按修改时间——**修改时间会被 checkout 打乱，
    /// 而批次号是台账里写着的那个数。**
    ///
    /// **The highest batch number, not the newest modification time** — **a checkout rewrites mtimes, while the batch number is the one written in the ledger.**
    /// </remarks>
    private static (string Path, string Text) LatestGate()
    {
        DirectoryInfo logs = new(Path.Combine(Root().FullName, "logs"));
        FileInfo? latest = logs.GetFiles("g*.ps1")
            .Where(file => int.TryParse(file.Name.Substring(1, file.Name.Length - 5), out _))
            .OrderByDescending(file => int.Parse(file.Name.Substring(1, file.Name.Length - 5)))
            .FirstOrDefault();

        latest.Should().NotBeNull("at least one gate script has to be archived, or nothing here is measured");
        return (latest!.Name, File.ReadAllText(latest.FullName));
    }

    /// <summary>**必须同时数 `: error ` 与 `: warning `** / Both error and warning counts have to be taken.</summary>
    /// <remarks>
    /// **只数 warning 会让一次编译失败看起来像"0 警告"** ——
    /// **而"0 警告"是好消息的样子，这正是它危险的地方。**
    ///
    /// **Counting only warnings makes a failed build look like "zero warnings"** — **and "zero warnings" looks like good news, which is exactly what makes it dangerous.**
    /// </remarks>
    [Fact]
    public void TheGate_ShouldCountBothErrorsAndWarnings()
    {
        (string name, string text) = LatestGate();

        text.Should().Contain("': error '", $"{name} has to count compile errors");
        text.Should().Contain("': warning '", $"{name} has to count warnings");
    }

    /// <summary>**必须对 `^Failed!` 与 `^Test Run Aborted` 行首锚定** / Both have to be line-anchored.</summary>
    /// <remarks>
    /// **不锚定会把日志正文里提到的 `Failed!` 也数进去**，于是**一个通过的批次被报成有失败**——
    /// 那种假警报会让人开始忽略这个数字。
    ///
    /// **Without anchoring, a `Failed!` mentioned in the log body is counted too**, **reporting a passing batch as failing** — and false alarms teach people to ignore the number.
    /// </remarks>
    [Fact]
    public void TheGate_ShouldAnchorItsFailurePatterns()
    {
        (string name, string text) = LatestGate();

        text.Should().Contain("'^Failed!'", $"{name} has to anchor the failure summary");
        text.Should().Contain("'^Test Run Aborted'", $"{name} has to anchor the abort line");
    }

    /// <summary>**必须覆盖五个测试运行** / Five test runs have to be covered.</summary>
    /// <remarks>
    /// 集成、默认、Gurobi、Cplex、Copt。**少跑一个不会报错，只会让那一行数字消失**——
    /// **而消失的数字比错的数字更难发现。**
    ///
    /// Integration, default, Gurobi, Cplex, Copt. **Dropping one raises nothing; its number simply disappears** — **and a missing number is harder to notice than a wrong one.**
    /// </remarks>
    [Fact]
    public void TheGate_ShouldCoverEveryTestRun()
    {
        (string name, string text) = LatestGate();

        // **不能只查文件名**：`g1059-copt.log` 出现在 build 行和 test 行两处，
        // **删掉 test 那一处，文件名仍然在。** 所以查的是汇总循环那一行本身。
        // **Do not grep for the filename alone**: `g1059-copt.log` appears on both the build line and the test line, **so deleting the test one leaves the name present.** The check is for the summary loop line itself.
        string loop = text
            .Split((char)10)
            .FirstOrDefault(line => line.Contains("foreach ($f in @(", StringComparison.Ordinal))
            ?? string.Empty;

        loop.Should().NotBeEmpty($"{name} has to have a summary loop at all");

        foreach (string log in new[]
                 {
                     "-test.log", "-default-test.log", "-gurobi.log", "-cplex.log", "-copt.log",
                 })
        {
            loop.Should().Contain(log,
                $"{name} has to feed the {log} run into the summary, not only into a build line");
        }
    }

    /// <summary>**必须把临时日志归档** / The temporary logs have to be archived.</summary>
    /// <remarks>
    /// **"跑过了"和"留得下证据"是两件事。** 只有归档名带批次号，
    /// **下一会话才能在台账里对上一个数字。**
    ///
    /// **"It ran" and "the evidence survives" are two different things.** Only an archive named after the batch **lets the next session match a number against the ledger.**
    /// </remarks>
    [Fact]
    public void TheGate_ShouldArchiveWithTheBatchNumber()
    {
        (string name, string text) = LatestGate();

        text.Should().Contain("logs/p8-batch",
            $"{name} has to archive into a batch-numbered name, or the evidence cannot be matched");
    }

    /// <summary>**门禁必须先跑扫描脚本** / The gate has to scan the code first.</summary>
    /// <remarks>
    /// **"少搬了一个类型"和"编译不过"是两类问题**：前者编译是过得去的。
    /// **所以扫描必须在编译之前，而且它自己的失败不能被后面的成功掩盖。**
    ///
    /// **A missing type and a failed compile are different problems**: the former compiles cleanly. **So the scan has to run before the build, and its failure must not be buried under later successes.**
    /// </remarks>
    [Fact]
    public void TheGate_ShouldRunTheBaselineScanFirst()
    {
        (string name, string text) = LatestGate();

        int scan = text.IndexOf("scan-kotlin-baseline.py", StringComparison.Ordinal);
        int build = text.IndexOf("dotnet build", StringComparison.Ordinal);

        scan.Should().BeGreaterThanOrEqualTo(0, $"{name} has to run the baseline scan");
        build.Should().BeGreaterThanOrEqualTo(0, $"{name} has to build");
        scan.Should().BeLessThan(build,
            "the scan comes first: a missing type compiles cleanly, so a later build cannot catch it");

        // **`$LASTEXITCODE` 检查不能少。** 没有它，扫描非零退出后脚本会继续跑，
        // **而后面每一步都成功，整个门禁就报了绿。**
        // **The `$LASTEXITCODE` check is not optional.** Without it the script runs on after a non-zero scan,
        // **every later step succeeds, and the whole gate reports green.**
        text.Should().Contain("if ($LASTEXITCODE -ne 0)",
            "a non-zero scan has to stop the gate, or its finding is lost");
    }
}
