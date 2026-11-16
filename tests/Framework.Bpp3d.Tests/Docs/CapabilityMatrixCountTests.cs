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
/// **那张表写着 C# 只有 3 个 `Core/Analysis/` 文件、"缺 18 个"。**
/// 实际是 **52** 个，**而那张表在这中间的一百多批里从没被动过。**
///
/// **一个写着具体数字的缺口，看起来像已经核实过了**——**所以没人去核实它。**
/// **它比"缺多数分析器"这种模糊说法更危险，正是因为它更具体。**
///
/// **the earlier table said C# had 3 files under `Core/Analysis/` and "18 missing".** The real count is **52**, **and that table sat untouched for over a hundred batches.** **A gap carrying a specific
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

}
