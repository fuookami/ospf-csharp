#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **九个能力布尔是"描述"，不是"决策"** / The nine capability booleans describe; they do not decide.
/// </summary>
/// <remarks>
/// **B1268 在计划里留了一句"13 项原生能力申报：零写入、零读取"。B1290 去把它量清楚，而量出来的东西与那句话不同：**
///
/// | 测量 | 结果 |
/// | --- | --- |
/// | `SolverCapabilities` 上的布尔成员 | **9 个** |
/// | 写入方（后端申报） | **有，很多** |
/// | 读取方 | **只有 `SolveFingerprinting.cs`，`Dual`／`WarmStart` 另加 `CapabilityMatrix.cs`** |
/// | 分支点（`if`／`&&`／`\|\|`／`?:`） | **0** |
///
/// **所以"零写入、零读取"是错的，而更精确的说法是：** **这些布尔<b>被申报、被抄进指纹、被抄进能力矩阵，
/// 而没有任何代码根据它们改变行为**。**
///
/// **这个区别很重要：** **读者看到 `Capabilities.Dual = true` 会以为"求解器会检查它"，
/// 而实际上它只是一个描述——** **一旦有人据此写分支，判据会红，逼着回来把这句话重新写。**
///
/// **B1268 left "13 native capability declarations: zero writers, zero readers". B1290 measured it, and the measurement differs:
/// nine booleans, plenty of writers, and readers limited to the fingerprinting file plus the capability matrix — with <b>zero</b>
/// branch sites.** **So the precise statement is: they are declared, copied into the fingerprint and the matrix, and no code changes
/// behaviour because of them.** **The distinction matters because `Capabilities.Dual = true` reads like "the solver checks this",
/// while it is only a description — and the moment someone branches on one, the criterion reddens and forces the sentence to be
/// rewritten.**
/// </remarks>
public class CapabilityFlagsAreDescriptiveTests
{
    /// <summary>
    /// **九个布尔成员只在指纹与能力矩阵里被读** / The nine booleans are read only by the fingerprint and the matrix.
    /// </summary>
    /// <remarks>
    /// **允许清单来自实测，而不是猜：** **每个名字只允许出现在它今天真实出现的那几个文件里。**
    /// **加一处分支就是加一个文件，判据红——这正是我们要它做的。**
    ///
    /// **The allow-list is measured, not guessed: each name may appear only in the files where it appears today.** **One new branch
    /// is one new file, and the criterion reddens — which is exactly what we want it to do.**
    /// </remarks>
    [Fact]
    public void TheNineBooleans_ShouldStayDescriptive() {
        Dictionary<string, string[]> allowList = new(StringComparer.Ordinal) {
            ["NativeIis"] = ["SolveFingerprinting.cs"],
            ["Dual"] = ["SolveFingerprinting.cs", "CapabilityMatrix.cs"],
            ["Farkas"] = ["SolveFingerprinting.cs"],
            ["WarmStart"] = ["SolveFingerprinting.cs", "CapabilityMatrix.cs"],
            ["SolutionPool"] = ["SolveFingerprinting.cs"],
            ["Callback"] = ["SolveFingerprinting.cs"],
            ["Interrupt"] = ["SolveFingerprinting.cs"],
            ["Checkpoint"] = ["SolveFingerprinting.cs"],
            ["Resume"] = ["SolveFingerprinting.cs"],
        };

        string root = RepositoryRoot();
        string[] sources = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        foreach ((string member, string[] allowed) in allowList) {
            Regex pattern = new(@"\b[Cc]apabilities\." + member + @"\b");
            string[] readers = sources
                .Where(path => pattern.IsMatch(File.ReadAllText(path)))
                .Select(path => System.IO.Path.GetFileName(path)!)
                .Distinct()
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            readers.Should().BeEquivalentTo(
                allowed.OrderBy(static name => name, StringComparer.Ordinal),
                $"{member} must stay a description: it may be read by the fingerprint (and the matrix) and by nothing else, "
                + "because a solver-level branch would make it a decision and the documented statement would stop being true");
        }
    }

    /// <summary>
    /// **成员恰好九个** / Exactly nine members.
    /// </summary>
    /// <remarks>
    /// **数错成员的个数会让上面那条判据悄悄变松：** **如果记录里加到十个布尔而清单还是九个，
    /// 新增的那个从未被检查过。**
    ///
    /// **A miscounted member list quietly loosens the criterion above: a tenth boolean would never be checked.**
    /// </remarks>
    [Fact]
    public void TheRecord_ShouldHoldExactlyNineBooleans() {
        string root = RepositoryRoot();
        string report = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Core", "Solver", "Report", "SolveReport.cs"));

        Match declaration = Regex.Match(
            report,
            @"public sealed record SolverCapabilities\((?<parameters>.*?)\)\s*(?:\{|=>)",
            RegexOptions.Singleline);
        declaration.Success.Should().BeTrue("the capabilities record is declared in one place");

        int booleans = Regex.Matches(declaration.Groups["parameters"].Value, @"\bbool\s+[A-Z]\w*").Count;
        booleans.Should().Be(
            9,
            "the documented statement speaks about nine booleans; a new one must be added to the allow-list above, "
            + "not silently escape the check");
    }

    /// <summary>
    /// **13 项函数接口申报仍然没有生产读取方** / The thirteen function-interface claims still have no production reader.
    /// </summary>
    /// <remarks>
    /// **这是 B1268 那一项，与上面那 9 个布尔<b>是两件事**。
    ///
    /// **B1290 先把它们混成一件：** **量了 `SolverCapabilities` 的布尔，就拿那个结果去改计划里"13 项原生能力申报"那条注记**——
    /// **而那条注记说的其实是 `*FunctionCapabilities`（从 SDK 反射出的函数接口能力）。**
    /// **这是我的<b>第四次找错地方**（前三：B1276 找错层、B1277 名不等、B1283 找错文件）。**
    ///
    /// **差点造成的后果是：** **一条<b>仍然成立**的缺口被改写掉，而新的测量结果被塞进它的位置。**
    /// **判据把两件事分开钉：这一条钉"没有读取方"。**
    ///
    /// **This is B1268's item, and it is <b>not</b> the nine booleans above.** **B1290 first conflated them: it measured the
    /// `SolverCapabilities` booleans and then rewrote the plan's "thirteen capability declarations" note with that result — while that
    /// note speaks about `*FunctionCapabilities`, the function-interface capabilities reflected from the SDK.** **That is the fourth
    /// wrong-place lookup (after B1276 wrong layer, B1277 name equality, B1283 wrong file).** **What it nearly caused: a gap that is
    /// <b>still true</b> being overwritten, with a fresh measurement put in its place.** **This criterion keeps the two apart.**
    /// </remarks>
    [Fact]
    public void TheThirteenFunctionClaims_ShouldStillHaveNoReader() {
        string root = RepositoryRoot();
        string[] sources = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        // **产出方：三个插件 + 类型本身。除此之外不许有人读它。**
        // **Producers: the three plugins and the type itself. Nobody else may read it.**
        string[] allowed = [
            "FunctionSolverCapabilities.cs",
            "CoptFunctionCapabilities.cs",
            "CplexFunctionCapabilities.cs",
            "GurobiFunctionCapabilities.cs",
        ];
        string[] readers = sources
            .Where(path => File.ReadAllText(path).Contains("FunctionSolverCapabilities", StringComparison.Ordinal))
            .Select(path => System.IO.Path.GetFileName(path)!)
            .Distinct()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        readers.Should().BeEquivalentTo(
            allowed.OrderBy(static name => name, StringComparer.Ordinal),
            "the documented gap is that the function-interface capability claims have no production consumer; "
            + "the moment one appears that gap is closed and the note must say so instead");
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