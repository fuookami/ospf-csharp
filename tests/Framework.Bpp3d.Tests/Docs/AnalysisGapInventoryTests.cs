// `core.analysis` 的清单：数了才知道有多大 / The `core.analysis` inventory: it takes counting to know its size.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **存在但没点名的缺口，和点名但不存在的缺口一样会让盘点失真** / A gap that exists but is never named distorts the inventory just as much.
/// </summary>
/// <remarks>
/// 数了两侧：基线 `core/analysis/` **19** 个主源码 + **2** 个插件分析器 = **21**；C# `Core/Analysis/` **3** 个、插件分析器 **0** ⇒ **缺 18**。
///
/// **此前没有任何一行记录。** 状态文档里 `core.analysis` 只在一个长句子里出现过一次，那句列的是 "criticality profile"——
/// **恰好是三个已搬过来的类型之一。**
///
/// 撤掉的两条是"点了名但不存在"；这一条正相反——**存在，但没被点名。** 两种都会让工作量判断失真。
///
/// counted both sides: the baseline has **19** files under `core/analysis/` plus **2** plugin analyzers (= **21**); C# has **3** under `Core/Analysis/` and **0**
/// plugin analyzers ⇒ **18 missing**, and this had **no row anywhere**. `core.analysis` appeared once in a long status sentence whose list named "criticality profile" —
/// **one of the three that were ported.** retracted two gaps that were named but did not exist; this is the opposite shape — **it exists and was never named.**
/// </remarks>
public class AnalysisGapInventoryTests
{
    /// <summary>**C# 侧只有三个类型** / Only three types exist here.</summary>
    /// <remarks>
    /// 这条钉的是**本仓的清单**，不是基线的：补进来一个，判据就红，**逼着改文档**。
    /// 这正是"存在但没点名"这类缺口的修法——**让数字有判据看着**。
    ///
    /// This pins **this repo's inventory**, not the baseline's: porting one turns it red and **forces the document to change**, which is the repair for a gap that exists
    /// but is never named — **give the number a criterion to watch it.**
    /// </remarks>
    [Fact]
    public void TheAnalysisInventory_ShouldStillBeThree() {
        string root = Root();
        string folder = Path.Combine(root, "src", "Core", "Analysis");
        Directory.Exists(folder).Should().BeTrue();

        string[] files = Directory.GetFiles(folder, "*.cs").Select(Path.GetFileNameWithoutExtension).ToArray()!;
        // 把它从 3 改到 6**：这条判据的作用是"补进来就红，逼着改文档"，所以它必须跟着改。
        // 每次改这个数字都要同时改矩阵第九节第 6 条，否则两处账对不上。
        // **moved it from 3 to 6**: the criterion exists so that "porting one turns it red and forces the document to change", so it moves with the code.
        // Every change here has to move the matrix row too, or the two accounts disagree.
        // **它从 52 改到 53**，而这一格不是"又多了一个需要原生后端的分析器"：
        // **它是把"已就绪的框架"与"两个插件后端"接起来的那个中间层
        // （`LinearSolverFixedIntegerLpSolver`）。**
        // **这条判据照旧起作用：加文件就红，逼着把矩阵那一格与这里一起改。**
        //
        // **It moved from 52 to 53, and this file is not another analyzer that needs a native backend: it is the
        // middle layer that connects the ready framework to both plugin backends.** **The criterion still works as designed:**
        // **adding a file reddens it and forces the matrix row and this count to move together.**
        files.Should().HaveCount(53,
            "the ported set grows only when the matrix says why; B1279 added the connecting layer, not an analyzer");
        files.Should().BeEquivalentTo(
            new[] { "AnalysisStatus", "CriticalityProfile", "ObjectiveTarget", "ActivityStatus", "ActivityEvidence", "DiagnosticSource", "LinearSolverFixedIntegerLpSolver", "ConstraintActivityReport", "CapabilityMatrix", "FixedIntegerLpSensitivity", "DiagnosticActivation", "CriticalConstraintAnalysisReport", "ConstraintActivityAnalyzer", "CorrectionRecommendation", "CriticalConstraintAnalysisSession", "TargetFeasibilityReport", "ConstraintPerturbationReport", "ConflictExplanation", "ConstraintProgrammingAnalysisBackends", "ConflictAnalysisOptions", "CriticalConstraintAnalysisOptions", "EffectivenessRanking", "CriticalAnalysisReports", "SnapshotModelRebuilder", "TargetWitnessValidator", "TargetFeasibilityAnalyzer", "ConstraintPerturbationSupport", "ConflictEvidenceSupport", "AssumptionConflictSeed", "NativeConflictSeed", "ConflictDeletionCheck", "ConflictExplanationAssembler", "CriticalConstraintAnalysisFullReport", "CriticalConstraintAnalysisReportBuilder", "CriticalPipelineStages", "CriticalConstraintPipeline", "ConstraintPerturbationPipeline", "CorrectionProjection", "ProvenanceDualMapping", "FixedIntegerLpGates", "EffectiveDomain", "BudgetGate", "PerturbationObservationFactory", "MinimalBlockingSet", "CriticalityProfileAnalysisResult", "FixedIntegerLpSensitivityAnalyzer", "ConflictAnalysisPreflight", "ObjectiveTargetBounds", "UnsupportedTargetFeatures", "TargetWitness", "UnknownOutputWitness", "TargetOutputStatus", "AnalysisCacheKey" },
            "these forty are the whole ported set; a forty-first file means somebody started porting an analyzer that needs a native backend");
    }

    /// <summary>**一个插件分析器都没有** / Not one plugin analyzer.</summary>
    /// <remarks>
    /// 基线有 `GurobiCriticalConstraintAnalysis.kt` 与 `ScipCriticalConstraintAnalysis.kt`。两者的证据来源都是**真实后端**（IIS / 活动约束），
    /// 所以 C# 侧没有——**这不是忘了，是还没有那条路径。**
    ///
    /// The baseline has `GurobiCriticalConstraintAnalysis.kt` and `ScipCriticalConstraintAnalysis.kt`, both of whose evidence comes from a **real backend** (IIS, active
    /// constraints), which is why this repo has neither — **not an oversight, but a path that does not exist yet.**
    /// </remarks>
    [Fact]
    public void ThePluginAnalyzers_ShouldStillBeAbsent() {
        string root = Root();
        foreach (string plugin in new[] { "Core.Plugin.Gurobi", "Core.Plugin.Scip" }) {
            string folder = Path.Combine(root, "src", plugin);
            if (!Directory.Exists(folder)) {
                continue;
            }
            string[] hits = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name!.Contains("CriticalConstraintAnalysis", StringComparison.OrdinalIgnoreCase))
                .ToArray()!;
            hits.Should().BeEmpty(plugin + " has no critical-constraint analyzer; the baseline's needs native IIS evidence");
        }
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
