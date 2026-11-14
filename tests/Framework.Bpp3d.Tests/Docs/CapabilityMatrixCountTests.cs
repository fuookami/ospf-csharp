#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 能力矩阵里的数字必须等于磁盘上的数字 / The matrix numbers have to equal the numbers on disk.
/// </summary>
/// <remarks>
/// **B957 那张表写着 C# 只有 3 个 `Core/Analysis/` 文件、"缺 18 个"。**
/// 到 B1079 实际是 **52** 个，**而那张表在这中间的一百多批里从没被动过。**
///
/// **一个写着具体数字的缺口，看起来像已经核实过了**——**所以没人去核实它。**
/// **它比"缺多数分析器"这种模糊说法更危险，正是因为它更具体。**
///
/// **B957's table said C# had 3 files under `Core/Analysis/` and "18 missing".** By B1079 the real count is **52**, **and that table sat untouched for over a hundred batches.** **A gap carrying a specific
/// number looks already checked** — **so nobody checks it.** **It is more dangerous than a vague "most analyzers missing" precisely because it is more specific.**
/// </remarks>
public class CapabilityMatrixCountTests
{
    private const string MatrixPath = "docs/kotlin-capability-matrix.md";
    private const string AnalysisDirectory = "src/Core/Analysis";

    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return directory;
    }

    /// <summary>磁盘上 `src/Core/Analysis/` 的 `.cs` 文件数 / The `.cs` file count on disk.</summary>
    private static int AnalysisFileCount() =>
        Directory.GetFiles(Path.Combine(Root().FullName, AnalysisDirectory), "*.cs").Length;

    /// <summary>矩阵里那一行"当前实测"的 C# 行 / The "current" C# row in the matrix.</summary>
    private static string CurrentAnalysisRow() => File
        .ReadAllLines(Path.Combine(Root().FullName, MatrixPath))
        .Single(line => line.StartsWith("| C# `src/Core/Analysis/` |", StringComparison.Ordinal));

    /// <summary>**旧的那组数学差集数字必须带上重测标记** / The old math-diff numbers have to carry a re-measurement.</summary>
    /// <remarks>
    /// **"91 个里只有 1 个是真的，误报率 98.9%"读起来像已经复核过。**
    /// **而它是 B961 的测量**，到 B1082 基线名数是 **284**、两级候选是 **61 / 25**。
    ///
    /// **这条只查"重测标记在不在"，不查那三个数本身**——数由 `logs/b1082-math-gap.py` 现算，
    /// **在单元测试里跑 Python 与 git 只是把可移植性换成一个更好看的绿。**
    ///
    /// **The sentence "one real gap out of 91, a 98.9% false-positive rate" reads as already checked.** **It is B961's measurement**; by B1082 the baseline has **284** names and the two levels give **61 / 25**.
    /// **This checks only that the re-measurement marker is present, not the three numbers** — those come from the script, **and running Python and git inside a unit test would trade portability for a better-looking green.**
    /// </remarks>
    [Fact]
    public void TheMathDiff_ShouldCarryItsRemeasurement()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Root().FullName, MatrixPath));
        int old = Array.FindIndex(lines, line => line.Contains("误报率 98.9%", StringComparison.Ordinal));

        old.Should().BeGreaterThanOrEqualTo(0, "the original audit stays in the document as history");
        lines.Skip(old)
            .Take(12)
            .Any(line => line.Contains("B1082 重测", StringComparison.Ordinal))
            .Should().BeTrue("a measured rate this precise has to say when it was measured");

        string text = string.Join(Environment.NewLine, lines);
        text.Should().Contain("b1082-math-gap.py",
            "the numbers have to name the script that produces them, or nobody can re-derive them");
    }

    /// <summary>**"短文件"与"空壳"必须是两个数** / Short files and empty shells have to be two numbers.</summary>
    /// <remarks>
    /// **同一个词两个测法，两个数字就永远对不上，而且没人看得出它们量的不是一件事。**
    /// 脚本曾把"去掉注释后不足 200 字符"叫空壳（长度），文档把"类型体里一个成员都没有"叫空壳（结构）。
    /// **于是脚本报 1、文档报 0，读者只能猜哪个是真的。**
    ///
    /// **Two meanings under one word never reconcile, and nobody can tell they measure different things.**
    /// The script called "under 200 non-comment characters" a shell (a length), the doc called "a type body with no members" a shell (a structure).
    /// **So the script said one and the doc said zero, and a reader could only guess which was true.**
    /// </remarks>
    [Fact]
    public void TheTwoMeasurements_ShouldBeNamedSeparately()
    {
        string matrix = File.ReadAllText(Path.Combine(Root().FullName, MatrixPath));

        matrix.Should().Contain("短文件", "the length measurement has to have its own word");
        matrix.Should().Contain("空壳", "the structural measurement keeps the word it always meant");
        matrix.Should().Contain("short=1 empty_types=0",
            "the row has to show both numbers side by side, or they will merge again");
    }

    /// <summary>**Demo2 里真的是 0 个空壳** / Demo2 really has zero empty type bodies.</summary>
    /// <remarks>
    /// **这条自己量，不调脚本**：脚本可能在另一台机器上没有 Python 或不在这条路径上，
    /// **而这条判据要守的是"文档里的 0 能不能被独立复核"。**
    ///
    /// **This measures by itself rather than calling the script**: the script may not run on another machine,
    /// **and what this guards is whether the doc's zero can be independently rechecked.**
    /// </remarks>
    [Fact]
    public void Demo2_ShouldHoldNoEmptyTypeBodies()
    {
        string demo2 = Path.Combine(Root().FullName, "src", "Example", "FrameworkDemo", "Demo2");
        Directory.Exists(demo2).Should().BeTrue("the Demo2 tree has to be there to be measured");

        string[] empty = Directory.GetFiles(demo2, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => HasEmptyTypeBody(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        empty.Should().BeEmpty("a type body with no members is the shell the document counts as zero");
    }

    /// <summary>文件里有没有一个成员都没有的类型体 / Whether the file holds a type body with no members.</summary>
    private static bool HasEmptyTypeBody(string source)
    {
        string stripped = Regex.Replace(source, @"//[^\n]*", string.Empty);
        stripped = Regex.Replace(stripped, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.IsMatch(
            stripped,
            @"\b(class|enum|record|struct|interface)\s+\w+[^{;]*\{\s*\}");
    }

    /// <summary>**矩阵里写的数必须等于磁盘上的数** / The number written down has to equal the number on disk.</summary>
    [Fact]
    public void TheCurrentAnalysisRow_ShouldMatchTheDisk()
    {
        Match match = Regex.Match(CurrentAnalysisRow(), @"\*\*(\d+)\*\*");

        match.Success.Should().BeTrue("the row has to state the count in bold");
        int.Parse(match.Groups[1].Value).Should().Be(AnalysisFileCount(),
            "the matrix may not report a file count the directory disagrees with");
    }

    /// <summary>**历史那张表必须带上"这是历史"的标记** / The historical table has to be marked as historical.</summary>
    /// <remarks>
    /// **不能删掉 B957 那张表**：它记的是"当时以为的缺口"，而**删掉它就删掉了"文档为什么会落后"的证据。**
    /// **但必须紧跟着一行说清哪个是现状**，**否则读者拿到的是两个互相矛盾的数字，而没有任何线索说明该信哪个。**
    ///
    /// **B957's table must stay**: it records what the gap was believed to be, and **deleting it deletes the evidence of how the document fell behind.** **But a line must follow saying which is current**, **or
    /// the reader is handed two contradictory numbers with no clue which to trust.**
    /// </remarks>
    [Fact]
    public void TheHistoricalAudit_ShouldBeFollowedByACurrentMeasurement()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Root().FullName, MatrixPath));
        int historical = Array.FindIndex(lines, line => line.Contains("缺 18 个", StringComparison.Ordinal));

        historical.Should().BeGreaterThanOrEqualTo(0, "the B957 audit stays in the document");
        lines.Skip(historical)
            .Take(20)
            .Any(line => line.Contains("B1079 更新", StringComparison.Ordinal))
            .Should().BeTrue("the historical audit needs a current measurement right after it");
    }
}
