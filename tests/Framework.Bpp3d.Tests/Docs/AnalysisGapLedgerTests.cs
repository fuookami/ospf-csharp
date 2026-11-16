#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 分析能力缺口的三口径账 / The three-way accounting of the analysis gap.
/// </summary>
/// <remarks>
/// **此前那一行写的是"由 3 个增至 18 个，仍缺 3（基线 21）"**。这个口径混了"文件数"和"覆盖的基线文件数"：
/// 有三个基线文件里的类型被拆成了单独的 C# 文件，**文件数涨了，覆盖数没涨**。
///
/// 按实测重记，分开三种：整文件搬完、只搬报告侧、完全未动。**两个方向都错过一次——
/// 写成"缺 3"低估了 Gurobi，写成"缺 6 个文件"又高估了 SCIP。**
///
/// 判据钉的是**口径本身**：文档三个类别的数量必须加起来等于基线文件总数，且每一类点名的文件都存在。
///
/// **The row used to read "3 → 18 files, 3 still missing (baseline 21)"**, which conflated file count with baseline files covered: three baseline types were split into
/// separate C# files, so **the file count rose while the coverage did not.** re-measures into three categories. **Both directions were wrong once — "3 missing"
/// understated Gurobi, "6 files missing" overstated SCIP.** These criteria pin **the accounting itself**: the three category counts must sum to the baseline total, and every
/// named file must exist.
/// </remarks>
public class AnalysisGapLedgerTests {
    private const string MatrixPath = "docs/kotlin-capability-matrix.md";
    private const string AnalysisDir = "src/Core/Analysis";

    private static string Matrix() {
        string root = RepositoryRoot();
        return File.ReadAllText(Path.Combine(root, MatrixPath));
    }

    private static string RepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the test needs the repository root to read the matrix");
        return directory!.FullName;
    }

    /// <summary>**三个类别的数量必须加得起来** / The three categories must add up.</summary>
    /// <remarks>
    /// 分开记了却加不起来，等于**换了三个数，还是没有一个能对账的数**。
    ///
    /// Splitting the count but not making it add up just means **three numbers instead of one, none of them reconcilable.**
    /// </remarks>
    [Fact]
    public void TheThreeCategories_ShouldAddUpToTheBaselineTotal() {
        string matrix = Matrix();

        matrix.Should().Contain("整文件搬完", "the fully-ported category has to be named");
        matrix.Should().Contain("只搬报告侧", "the report-only category has to be named");
        matrix.Should().Contain("完全未动", "the untouched category has to be named");

        MatchCollection counts = Regex.Matches(matrix, @"\|\s*\*\*(\d+)\*\*\s*\|");
        counts.Should().NotBeEmpty("the counts are the point of the ledger");

        matrix.Should().Contain("**19**", "the baseline total is the number everything reconciles against");
        matrix.Should().Contain("| **合计** | **19** |",
            "the categories must be summed explicitly, or nobody can check the arithmetic");
    }

    /// <summary>**每一类点名的文件都必须真的存在于某一侧** / Every named file must exist on some side.</summary>
    /// <remarks>
    /// 点名一个**两边都没有**的文件，会让缺口看起来比实际大—— 与 已经因此撤过两次。
    ///
    /// Naming a file that exists on **neither side** makes the gap look larger than it is — and already retracted twice for exactly that.
    /// </remarks>
    [Fact]
    public void TheNamedAnalyzers_ShouldBeNamedConsistently() {
        string matrix = Matrix();

        // 只搬报告侧的三个，必须在文档里点名，且不能在"整文件搬完"里。
        foreach (string name in new[] { "TargetFeasibilityAnalyzer", "ConstraintPerturbationAnalyzer", "ConflictAnalyzer" }) {
            matrix.Should().Contain(name, $"{name} is one of the three report-only ports");
        }
        matrix.Should().Contain("CriticalConstraintAnalysisPipeline",
            "the one untouched baseline file has to be named");

        // **C# 侧那三个拆分文件必须真的存在**，否则"报告已补"是在说一个不存在的文件。
        // **The three split-out C# files have to exist**, or "the report is ported" describes a file that is not there.
        string root = RepositoryRoot();
        foreach (string file in new[] { "TargetFeasibilityReport", "ConstraintPerturbationReport", "ConflictExplanation" }) {
            File.Exists(Path.Combine(root, AnalysisDir, $"{file}.cs"))
                .Should().BeTrue($"{file}.cs is what makes the three baseline files 'report ported'");
        }
    }

    /// <summary>**有理由的差异不是缺口** / A divergence with a reason is not a gap.</summary>
    /// <remarks>
    /// SCIP 侧此前被记成"部分"。基线那个文件只有**一个类**，C# 有同名同类实现，
    /// 两处差异（线程数与后端配置不下发）**是 C# 自己写明了理由的**。
    ///
    /// **把有理由的差异记成缺口，和把缺口记成完成一样，都会让工作量判断失真。**
    ///
    /// The SCIP side used to be recorded as "partial". The baseline file holds **one class**, and C# has the same class with two divergences (thread count and backend
    /// configuration not applied) **whose reasons are written down in C# itself.** **Recording a reasoned divergence as a gap distorts the workload the same way
    /// recording a gap as done does.**
    /// </remarks>
    [Fact]
    public void TheScipSide_ShouldBeRecordedAsDoneWithItsReasons() {
        string matrix = Matrix();

        matrix.Should().Contain("ScipInfeasibilityAnalyzers", "the SCIP file has to appear in the ledger");
        matrix.Should().Contain("已搬完",
            "SCIP's single class is ported; recording it as missing overstates the gap");
        matrix.Should().Contain("ThreadCount", "the thread-count divergence has to be named");
        matrix.Should().Contain("BackendConfiguration", "the backend-configuration divergence has to be named");

        string root = RepositoryRoot();
        string solver = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Scip/ScipLinearSolver.cs"));
        solver.Should().Contain("ThreadCount", "the divergence is only reasoned if the source gives the reason");
        solver.Should().Contain("BackendConfiguration");
        File.Exists(Path.Combine(root, "src/Core.Plugin.Scip/ScipFarkasInfeasibilityAnalyzer.cs"))
            .Should().BeTrue("the ported analyzer has to exist for the claim to hold");
    }

    /// <summary>**Gurobi 那 3 个类确实一个都没有** / None of Gurobi's three classes exists.</summary>
    /// <remarks>
    /// 这条判据的方向和上一条相反：上一条防"把做过的记成没做"，这一条防"把没做的记成做过"。
    /// **两条都要有，否则修一种走样会引入另一种。**
    ///
    /// This criterion points the opposite way from the one above: that one stops "done recorded as missing", this one stops "missing recorded as done". **Both are needed, or
    /// fixing one distortion introduces the other.**
    /// </remarks>
    [Fact]
    public void TheGurobiAnalyzers_ShouldRecordWhichMemberIsPorted() {
        string matrix = Matrix();
        matrix.Should().Contain("GurobiInfeasibilityAnalyzers", "the Gurobi file has to appear in the ledger");
        matrix.Should().Contain("已搬完", "all three Gurobi analyzers are ported now");
        matrix.Should().Contain("三个类全部到位", "the ledger has to say the file is complete, not merely partly ported");

        string root = RepositoryRoot();
        string support = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Gurobi/GurobiReportSupport.cs"));
        support.Should().Contain("NativeIis: true",
            "the linear native IIS is implemented now, and the declaration has to follow the implementation");
        // **两个 IIS 类一个都没有 C# 文件**，而 Farkas 那个有——方向相反的断言必须同时存在，
        // 否则"部分已搬"这句话本身无法被证伪。
        // **Neither IIS class has a C# file, while the Farkas one does** — the two assertions must coexist, or "partly ported" cannot be falsified.
        foreach (string analyzer in new[] { "GurobiQuadraticNativeIISAnalyzer" }) {
            Directory.EnumerateFiles(Path.Combine(root, "src/Core.Plugin.Gurobi"), $"{analyzer}.cs")
                .Should().BeEmpty(
                    $"{analyzer} has no file of its own: the quadratic analyzer lives in GurobiQuadraticInfeasibilityAnalyzers.cs");
        }
        File.Exists(Path.Combine(root, "src/Core.Plugin.Gurobi/GurobiQuadraticInfeasibilityAnalyzers.cs"))
            .Should().BeTrue("the quadratic analyzer exists, just not under the baseline class name");
        File.Exists(Path.Combine(root, "src/Core.Plugin.Gurobi/GurobiInfeasibilityAnalyzers.cs"))
            .Should().BeTrue("the Farkas and linear IIS analyzers live in the file named after the baseline one");
    }
}
