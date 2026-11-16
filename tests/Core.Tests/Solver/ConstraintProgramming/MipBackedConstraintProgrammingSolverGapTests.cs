#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

/// <summary>
/// **CP 能力目前只有 SCIP 有——而底座其实是通用的** /
/// Only SCIP has CP today, yet the foundation underneath is generic.
/// </summary>
/// <remarks>
/// 逐项对照里唯一"真的要写实现"的块（基线 `MipBackedConstraintProgrammingSolver.kt`，503 行）。
/// **这里不写它，而是把缺口量化、并把依赖关系钉住——因为这块比前五块大一个量级，
/// 而半成品比不做更糟。**
///
/// **调查结果有一件事与预期相反：** **编解码那一层<b>已经有了</b>
/// （`ConstraintProgrammingToLinearModelLowerer`），而且 SCIP 的 CP 支持<b>正是</b>由它驱动的。**
///
/// **所以 SCIP 的 CP 能力实际上也是"MIP 驱动"的——只是那个能力被写死在
/// `ScipConstraintProgrammingSolver` 里，绑在 `ScipLinearSolver` 上，而不是抽成一个
/// 接受任意 `IAbstractLinearSolver` 的核心类。**
///
/// **缺口因此是：** **Gurobi／Cplex／Copt 没有 CP 能力，而它们本来只差"把 lowerer 接到自己的线性求解器上"。**
///
/// The only "a real implementation is needed" block of the survey (baseline
/// `MipBackedConstraintProgrammingSolver.kt`, 503 lines). ** does not write it; it sizes the gap and pins the
/// dependency relation — because this block is an order of magnitude larger than the previous five, and a half-finished one
/// would be worse than none.**
///
/// **One survey result runs against expectation: the encoding layer <b>already exists</b>
/// (`ConstraintProgrammingToLinearModelLowerer`) and SCIP's CP support is driven <b>by it</b>.** **So SCIP's CP capability is
/// itself MIP-backed — it is merely hard-wired inside `ScipConstraintProgrammingSolver` against `ScipLinearSolver` instead
/// of being factored into a core class that accepts any `IAbstractLinearSolver`.** **The gap is therefore: Gurobi/Cplex/Copt
/// have no CP, and all they lack is "connect the lowerer to their own linear solver".**
/// </remarks>
public class MipBackedConstraintProgrammingSolverGapTests
{
    /// <summary>
    /// **现在是两条后端：SCIP 与 MIP 驱动** / There are now two backends: SCIP and the MIP-backed one.
    /// </summary>
    /// <remarks>
    /// **这条判据钉的是"只有一条后端"，并在注释里要求：接上第二个时必须列全它们。**
    /// **第二个也接上了，于是它红了——而它红的那一刻，正好是它该被改写的那一刻。**
    ///
    /// **这条判据现在钉的是<b>名单</b>，不是数量：** **再出现第三条，它会红并要求点名。**
    ///
    /// **It once pinned "only one backend" and asked, in its own note, that a second one be enumerated.** **It added the
    /// second, so it went red — and going red is exactly when it should be rewritten.** **It now pins the <b>roll call</b>,
    /// not the count: a third one makes it red and demands to be named.**
    /// </remarks>
    [Fact]
    public void TheKnownBackends_ShouldBeExactlyTheOnesListed() {
        string root = RepositoryRoot();
        string[] implementations = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("FakeConstraintProgrammingSolver", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(": IConstraintProgrammingSolver", StringComparison.Ordinal))
            .Select(static path => System.IO.Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        implementations.Should().BeEquivalentTo(
            ["MipBackedConstraintProgrammingSolver.cs", "ScipConstraintProgrammingSolver.cs"],
            "a third backend appearing means the matrix's CP row must name it");
    }

    /// <summary>
    /// **那个通用类现在在，而且它不认识任何一个具体后端** /
    /// The generic class now exists, and it knows no concrete backend.
    /// </summary>
    /// <remarks>
    /// **这条判据断言的是"它不在"；此后那句不再成立，于是它红了。**
    ///
    /// **而替代它的这条更硬：** **它不认识 `ScipLinearSolver`、也不认识任何别的具体求解器——**
    /// **因为它的全部意义就是"接受任意 `IAbstractLinearSolver`"。**
    ///
    /// **如果有一天有人在里面写死了一个后端，它就退化成了 SCIP 那份代码的复制品，
    /// 而复制品不会跟着后端一起修。**
    ///
    /// **An earlier criterion asserted "it is absent"; that stopped being true, so it went red.** **Its replacement is harder:
    /// the class must not know `ScipLinearSolver` or any other concrete solver — its whole point is accepting any
    /// `IAbstractLinearSolver`.** **If someone hard-codes a backend in it, it degrades into a copy of SCIP's code, and copies
    /// do not get fixed along with the backend.**
    /// </remarks>
    [Fact]
    public void TheGenericClass_ShouldNotKnowAnyConcreteBackend() {
        string root = RepositoryRoot();
        string source = File.ReadAllText(
            System.IO.Path.Combine(
                root, "src/Core/Solver/ConstraintProgramming/MipBackedConstraintProgrammingSolver.cs"));

        source.Should().Contain(
            "IAbstractLinearSolver",
            "the seam is the abstraction; without it the class is one backend's private copy");

        // **只检查代码行，忽略注释。** **这条判据第一版是全文件搜，结果被自己的说明文字弄红了：**
        // **注释里正当地提到了 `GurobiLinearSolver`（说明"多数后端不填 `Descriptor`"）。**
        // **一条会因为解释自己而变红的判据，会训练人少写解释——那是反向的激励。**
        //
        // **Only code lines, comments excluded.** **The first version searched the whole file and went red on its own prose:
        // a comment legitimately names `GurobiLinearSolver` to explain that most backends leave `Descriptor` null.**
        // **A criterion that reddens when you explain it trains people to explain less — the wrong incentive.**
        var code = new System.Text.StringBuilder();
        foreach (string line in source.Split('\n')) {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", System.StringComparison.Ordinal)
                || trimmed.StartsWith("///", System.StringComparison.Ordinal)
                || trimmed.StartsWith("*", System.StringComparison.Ordinal)
                || trimmed.StartsWith("/*", System.StringComparison.Ordinal)) {
                continue;
            }
            code.Append(line).Append('\n');
        }

        string codeOnly = code.ToString();
        codeOnly.Should().NotContain(
            "ScipLinearSolver",
            "naming a concrete backend in code would silence every other one");
        codeOnly.Should().NotContain(
            "GurobiLinearSolver",
            "and likewise for the next backend someone is tempted to special-case");
    }

    private static string ReadAllSources(string root) {
        var text = new System.Text.StringBuilder();
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)) {
            text.Append(File.ReadAllText(path)).Append('\n');
        }
        return text.ToString();
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
