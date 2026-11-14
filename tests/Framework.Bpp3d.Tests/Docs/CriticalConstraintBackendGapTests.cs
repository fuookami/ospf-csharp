#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **关键约束分析的框架齐了，两个后端的 6 个函数一个都没搬** /
/// Critical-constraint analysis has its framework, and none of the two backends' six functions.
/// </summary>
/// <remarks>
/// **B1278 逐层查 P9 的分析能力，撞上的形状与 B1268／B1270 同一族，但方向相反：**
///
/// - **B1268 是"申报了 13 项原生能力，零写入零读取"——申报在前，实现没有**
/// - **这一批是"<b>框架全齐、后端全无**"——接口在前，实现没有**</b>
///
/// **实测的两侧：**
///
/// | 侧 | 内容 |
/// | --- | --- |
/// | **已就绪** | `CriticalConstraintAnalysisOptions`／`…Report`／`…ReportBuilder`／`…FullReport`／`CriticalAnalysisReports`／`ConstraintPerturbationPipeline`／`…Report`／`…Support`（8 个文件） |
/// | **缺失** | `GurobiCriticalConstraintAnalysis.kt`（3 个公开函数）与 `ScipCriticalConstraintAnalysis.kt`（3 个公开函数），<b>一个都没有** |
///
/// **这 6 个函数是：** **固定整数 LP 灵敏度采集、扰动后端、以及把两者编排成 pipeline 的那个入口。**
/// **它们<b>全部要调真实求解器的对偶值／边界重解**——所以缺它们不等于"少抄了几个函数"，**
/// **而是"这套框架目前<b>没有任何一个后端接得上</b>"。**
///
/// ### 为什么这条值得单独钉住
///
/// **矩阵把"C# 插件分析器"那一格记作 **0**——那个数字是对的，而它<b>看起来像"这个模块还没开始"**。</b>
/// **实际上框架已经写了 8 个文件、能力枚举有 `FixedIntegerLpSensitivity`、报告结构有
/// `FixedIntegerLpSensitivityReport` 字段——<b>只差后端那 6 个函数**。**</b>
///
/// **"0"和"还差 6 个函数"对工作量的含义完全不同，而前者把后者藏起来了。**
///
/// **B1278 walked the P9 analysis layers and hit the same shape as B1268/B1270, inverted: B1268 declared thirteen native
/// capabilities with zero writers and zero readers; this one has the <b>framework complete and both backends absent</b> —
/// interface first, implementation missing.**
///
/// **Measured: eight framework files are in place; `GurobiCriticalConstraintAnalysis.kt` (3 public functions) and
/// `ScipCriticalConstraintAnalysis.kt` (3 public functions) are <b>entirely absent</b>.** **Those six collect fixed-integer LP
/// sensitivity, provide the perturbation backend, and compose the two into a pipeline — all of them call a real solver's duals
/// or re-solve with bounds, so missing them is not "a few functions left un-copied" but "no backend can connect to this
/// framework yet".**
///
/// **The matrix records "C# plugin analyzers: 0", which is true and <b>reads as "this module has not started"</b> — while eight
/// framework files, the capability enum member and the report field all exist, leaving only those six functions.** **"0" and
/// "six functions short" mean very different amounts of work, and the former hides the latter.**
/// </remarks>
public class CriticalConstraintBackendGapTests
{
    /// <summary>
    /// **框架侧八件都在** / The eight framework pieces are present.
    /// </summary>
    [Fact]
    public void TheFramework_ShouldBeComplete() {
        string root = RepositoryRoot();

        string[] files = [
            "CriticalConstraintAnalysisOptions.cs", "CriticalConstraintAnalysisReport.cs",
            "CriticalConstraintAnalysisReportBuilder.cs", "CriticalConstraintAnalysisFullReport.cs",
            "CriticalAnalysisReports.cs", "ConstraintPerturbationPipeline.cs",
            "ConstraintPerturbationReport.cs", "ConstraintPerturbationSupport.cs",
        ];

        foreach (string file in files) {
            File.Exists(System.IO.Path.Combine(root, "src", "Core", "Analysis", file))
                .Should().BeTrue($"{file} is framework that already exists; its presence is what makes the gap narrow");
        }

        string capability = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Analysis/CapabilityMatrix.cs"));
        capability.Should().Contain(
            "FixedIntegerLpSensitivity",
            "the capability is declared; declaring it is exactly what makes an absent backend misleading");
    }

    /// <summary>
    /// **两个后端仍然一个函数都没有** / Both backends still have none of their functions.
    /// </summary>
    /// <remarks>
    /// **这条断言的是"不在"——而它必须是<b>一条会因为新增而变红的判据**：
    /// 谁把后端接上了，这条立刻红，逼着把矩阵那一格从 0 改成真实数。**
    ///
    /// **This asserts absence, and it must be <b>a criterion that reddens when the thing is added</b>: whoever wires a backend
    /// makes it red, forcing the matrix's 0 to become a real number.**
    /// </remarks>
    [Fact]
    public void TheTwoEnds_ShouldExistAndStillLackRanges() {
        string root = RepositoryRoot();

        // **B1281 把这条判据整个换掉了，因为它的题材变了。**
        //
        // **B1278 写它的时候，插件侧一个后端都没有，所以判据断言"不存在"是对的。**
        // **而 B1280／B1281 各接上一端之后，那条"不存在"仍然<b>绿着**——
        // 因为新文件叫 `GurobiFixedIntegerLpBackend.cs`，而判据找的是 `CriticalConstraintAnalysis` 这个子串。
        //
        // **这正是本仓反复记的那类失败：<b>判据搜索的字符串与被实现的东西不是一回事**。
        // 如果不换掉它，它会一直绿着，而它断言的那件事已经半真半假了。**
        //
        // **换成的版本不再问"有没有文件"，而是问两件真实的事：**
        // **① 两端都在（不然就是退步）；② 区间与扰动<b>都没接**（常量必须这么说）。**
        //
        // **B1281 replaced this criterion entirely, because its subject changed.** **When B1278 wrote it no plugin-side backend
        // existed, so asserting absence was right.** **After B1280/B1281 connected one end each, that absence assertion was
        // <b>still green</b> — because the new file is named `GurobiFixedIntegerLpBackend.cs` while the criterion searched for the
        // substring `CriticalConstraintAnalysis`.** **This is the failure mode this repo keeps recording: <b>the string a criterion
        // searches and the thing that got implemented are not the same thing</b>.** **Replaced with a version that stops asking
        // "is there a file" and asks two real things: both ends exist (or it is a regression), and neither ranges nor perturbation
        // is attached (the constants must say so).**
        string gurobiFactory = System.IO.Path.Combine(
            root, "src", "Core.Plugin.Gurobi", "GurobiFixedIntegerLpBackend.cs");
        string scipFactory = System.IO.Path.Combine(
            root, "src", "Core.Plugin.Scip", "ScipFixedIntegerLpBackend.cs");

        File.Exists(gurobiFactory).Should().BeTrue(
            "B1280 connected the Gurobi end; its absence would be a regression, not a gap to document");
        File.Exists(scipFactory).Should().BeTrue(
            "B1281 connected the SCIP end");

        string gurobi = File.ReadAllText(gurobiFactory);
        string scip = File.ReadAllText(scipFactory);

        // **B1282 之后 Gurobi 这一端有区间了，所以这一格从"必须说 false"翻成"必须说 true 并且真有那条路径"。**
        //
        // **这正是这条判据的用处：** **它不是在记录事实，而是在<b>逼着事实与文档同时改**。**
        //
        // **After B1282 the Gurobi end does supply ranges, so this assertion flips from "must say false" to "must say true and the
        // path must exist".** **That is the point of the criterion: it does not record a fact, it forces the fact and the document to
        // move together.**
        gurobi.Should().Contain(
            "SuppliesSensitivityRanges = true",
            "B1282 captured the native ranging attributes; the constant has to say so");
        File.ReadAllText(System.IO.Path.Combine(root, "src", "Core.Plugin.Gurobi", "GurobiLinearSolver.cs"))
            .Should().Contain("SARHSLow", "a true constant without a ranging path would be a lie");
        scip.Should().Contain(
            "SuppliesSensitivityRanges = false",
            "SCIP has no ranging capability yet either");
        // **B1283 把这一格翻成 true：两个插件现在都给出可被流水线扰动的 CP 求解器。**
        // **B1283 flips this to true: both plugins now hand back a solver the pipeline can perturb with.**
        // **这条常量在另一个文件里：`ScipPerturbationBackend.cs`。**
        // **第一版在这里读 `ScipFixedIntegerLpBackend.cs`，判据当场红——**
        // **又一次"在错误的文件里找东西"，而这正是 B1276 记过的那类错。**
        //
        // **The constant lives in another file: `ScipPerturbationBackend.cs`.** **The first version read
        // `ScipFixedIntegerLpBackend.cs` and went red at once — looking in the wrong file again, the very mistake B1276 recorded.**
        foreach (string relative in new[] {
            "src/Core.Plugin.Scip/ScipPerturbationBackend.cs",
            "src/Core.Plugin.Gurobi/GurobiPerturbationBackend.cs",
        }) {
            string path = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            File.Exists(path).Should().BeTrue(relative + " must exist; a true constant without it would be a lie");
            File.ReadAllText(path).Should().Contain(
                "SuppliesPerturbationBackend = true",
                relative + " claims the capability, so it has to say so");
        }

        // **两端都必须是"包一层共享层"，而不是各写一遍求解。**
        // **Both ends must wrap the shared layer rather than write solving again.**
        foreach (string source in new[] { gurobi, scip }) {
            source.Should().Contain(
                "new LinearSolverFixedIntegerLpSolver(linearSolver)",
                "the baseline duplicates the lower/solve/dual chain per plugin; this repo must not copy that duplication");
        }
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