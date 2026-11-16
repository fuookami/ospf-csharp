// 二次 IIS 缺口的因果链判据 / Criteria pinning the causal chain of the quadratic-IIS gap.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Plugin.Gurobi;

/// <summary>
/// **这条因果链的中间两环已经变了，但链尾没变** / Two middle links changed; the end of the chain did not.
/// </summary>
/// <remarks>
/// 记下的因果链是这样的：
///
/// | 事实 | 当时的证据 |
/// | --- | --- |
/// | **有**生产调用 `model.elastic` | 另一份实现的源码 |
/// | C# 的 `Elastic` **只有测试调用** | `grep '\.Elastic' src` 零命中 |
/// | C# **没有** `ComputeLegacyIISAsync` 的二次版本 | `src/Core/Solver/Iis/` 只有 `LinearLegacyIisCompute.cs` |
/// | 于是 `DiagnosticAnalyzers` 二次版**返回空** | 接口默认方法 `Array.Empty` |
///
/// **重查之后，四行里有两行不成立了：**
///
/// | 行 | 现在 |
/// | --- | --- |
/// | 第 2 行 | **仍成立**：`Elastic` 只被 `QuadraticLegacyIisCompute` 调用，而后者**没有 src 调用方** |
/// | 第 3 行 | **不成立了**：`QuadraticLegacyIisCompute.cs` 已存在，有三个入口 |
/// | 第 4 行 | **不成立了**：二次 `DiagnosticAnalyzers` 现在返回原生 IIS 分析器 |
///
/// **链尾那个观察还在：`Elastic` 依旧没有生产入口。** 但原因不再是"二次 IIS 未实现"——
/// 二次 IIS 走的是**后端原生 `computeIIS`**，不是弹性化。**弹性化这条路仍然只有测试在走。**
///
/// **这份文件原先把"缺口"的可观测形式钉成"分析器列表为空"。** 那个判据按设计红了，
/// 但它**只钉了第 4 行**：第 3 行早就变了、第 2 行的原因也换了，**判据一句话都没说**。
/// 这是缺口标记的通病——**钉住一个现象，却以为钉住了整条推理。**
///
/// This file recorded this chain (table one). **After the earlier re-check, two of its four rows no longer hold**: `QuadraticLegacyIisCompute.cs` now exists with three entry points, and the
/// quadratic `DiagnosticAnalyzers` now returns a native IIS analyzer. **Row 2 still holds**: `Elastic` is only called by `QuadraticLegacyIisCompute`, which has **no src
/// caller**. **The observation at the end of the chain survives** — `Elastic` still has no production entry — but the reason is no longer "quadratic IIS is unimplemented":
/// quadratic IIS goes through the backend's native `computeIIS`, not through elasticization. **That path still has only tests walking it.** The old criterion pinned the gap's
/// observable form as "an empty analyzer list". It went red as designed, **but it only ever pinned row 4**: row 3 had already changed and row 2's reason had been replaced,
/// **and the criterion said nothing about either.** That is the usual failure of a gap marker — **it pins one symptom and is mistaken for pinning the whole argument.**
/// </remarks>
public class QuadraticIisGapTests {
    /// <summary>**缺口已闭，判据记录闭合** / The gap is closed, and the criterion records the closure.</summary>
    /// <remarks>
    /// 原来的判据断言 `DiagnosticAnalyzers` 为空，并在注释里写着"若将来实现二次 IIS，判据会红，提醒同步更新文档"。
    /// **它确实红了。** 这里把它换成方向相反的断言，**但不 weaken 成"列表非空就算完"**：
    /// 分析器必须真的是原生 IIS 那个，否则"闭合"只是换了个空壳。
    ///
    /// The old criterion asserted an empty `DiagnosticAnalyzers` and noted that it would go red when quadratic IIS landed, forcing a doc update. **It went red.** This replaces it
    /// with the opposite assertion **without weakening it to "the list is non-empty, done"**: the analyzer has to be the native IIS one, or the "closure" is just a hollow shell.
    /// </remarks>
    [Fact]
    public void TheQuadraticAnalyzers_ShouldNoLongerBeEmpty() {
        IAbstractQuadraticSolver solver = new GurobiQuadraticSolver();
        IReadOnlyList<IInfeasibilityAnalyzer<IQuadraticTetradModelView>> analyzers =
            solver.DiagnosticAnalyzers(new IisConfig());

        analyzers.Should().ContainSingle(
            "the quadratic side has exactly one analyzer, and the old criterion's emptiness is gone");
        analyzers[0].Source.Should().Be(InfeasibilityEvidenceSource.NativeIis,
            "a non-empty list filled with something other than a native IIS would not be a closure");
    }

    /// <summary>
    /// **链尾变了：弹性化那条路现在有生产入口** / The chain's end changed: the elastic path now has a production entry.
    /// </summary>
    /// <remarks>
    /// **这条判据被翻过来了——而它是<b>自己变红逼着改的</b>。**
    ///
    /// **它原来的写法是"`QuadraticLegacyIisCompute` 有实现，但没人调用它"，
    /// 并在注释里写明：** **"如果这条哪天变成非空，说明弹性化那条路有了生产调用方，文档必须跟上。"**
    ///
    /// **那个适配器正是那个调用方。**
    ///
    /// **值得记的是这句预言的准确度：** **它没有说"将来会有人调用"，
    /// 而是说"如果有人调用，这里会红，而那时文档要改"。**
    /// **前者是猜测，后者是一个可执行的提醒机制。**
    ///
    /// **This criterion flipped — and it was <b>the criterion going red that forced the edit</b>.**
    ///
    /// **It used to say "`QuadraticLegacyIisCompute` is implemented but nothing calls it", with a note: "if this ever becomes
    /// non-empty, the elastic path has gained a production caller and the docs must say so".**
    ///
    /// **That adapter is that caller.**
    ///
    /// **What is worth noting is how precise the prediction was: it did not say "someone will call it someday", it said "if
    /// someone calls it, this goes red and the docs must change".** **The former is a guess; the latter is an executable
    /// reminder.**
    /// </remarks>
    [Fact]
    public void TheQuadraticLegacyCompute_ShouldNowHaveAProductionCaller() {
        Type? type = typeof(IisConfig).Assembly
            .GetType("Fuookami.Ospf.Core.Solver.Iis.QuadraticLegacyIisCompute");

        type.Should().NotBeNull("the quadratic elastic path is implemented");
        type!.GetMethod("PerformElasticFilteringAsync").Should().NotBeNull(
            "the elastic filtering entry is what eventually calls `Elastic()`");

        string root = RepositoryRoot();
        string[] callers = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("QuadraticLegacyIisCompute.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("QuadraticLegacyIisCompute", StringComparison.Ordinal))
            .ToArray();

        callers.Should().ContainSingle(
            "exactly one production caller was added — the quadratic legacy IIS analyzer");
        callers[0].Should().EndWith(
            "LegacyElasticQuadraticInfeasibilityAnalyzer.cs",
            "if another caller appears, the docs need to name it too");
    }

    /// <summary>**线性侧则有实现**——反向对照 / The linear side does have an implementation — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**整个 IIS 都没实现**"的误解会掩盖真实状态。
    ///
    /// Without this, the misconception "**no IIS at all is implemented**" would hide the real state.
    /// </remarks>
    [Fact]
    public void LinearLegacyIisCompute_ShouldExist() {
        Type? type = typeof(IisConfig).Assembly
            .GetType("Fuookami.Ospf.Core.Solver.Iis.LinearLegacyIisCompute");

        type.Should().NotBeNull("linear legacy IIS is implemented and must stay implemented");
        type!.GetMethod("ComputeLegacyIISAsync").Should().NotBeNull(
            "the linear entry point is what the linear analyzer calls");
    }

    private static string RepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the scan needs the repository root");
        return directory!.FullName;
    }
}
