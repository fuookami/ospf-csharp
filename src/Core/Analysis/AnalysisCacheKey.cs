#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 分析缓存的键 / The analysis cache key.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.CacheKey` 与 `ConflictAnalyzer.CacheKey` 的移植。**
///
/// 基线的键是三段：**分析器实例 + 目标 + 选项**。
///
/// | 段 | 少了它会怎样 |
/// | --- | --- |
/// | 目标 | 换一个目标就命中上一个目标的报告 |
/// | 选项 | 换求解选项就命中旧选项下的结论 |
/// | **分析器实例** | **两个分析器（不同后端 / 不同选项）在同一会话里互相命中对方的结论** |
///
/// **第三段最容易漏，也最贵。** `CriticalConstraintAnalysisSession` 的缓存是
/// **一个以 `object` 为键的字典**——它不会检查键的形状，**只比较相等性。**
/// 一个不含分析器身份的键，在两台后端上看起来完全一样。
///
/// **The third segment is the easiest to omit and the most expensive to omit.** The session cache is **a dictionary keyed by `object`** — it never inspects the shape of a key, **only equality.** A key
/// without the analyzer identity looks identical across two different backends.
///
/// **这里刻意不把 `activeSources` 放进键。** 基线的缓存只在**全模型**那条路径上读写，
/// **而删除复验从不碰缓存**——所以键里没有它是对的。
/// **但这条"对"依赖一个前提：谁都不许从收缩路径去读缓存。**
///
/// **`activeSources` is deliberately absent.** The baseline cache is read and written only on the **full-model** path, **and deletion checks never touch it** — so leaving it out is correct. **But that correctness rests
/// on one premise: nobody may read the cache from a shrink path.**
/// </remarks>
public sealed record AnalysisCacheKey
{
    private AnalysisCacheKey(object analyzer, ObjectiveTarget target, object options) {
        Analyzer = analyzer;
        Target = target;
        Options = options;
    }

    /// <summary>产生这个键的分析器 / The analyzer that produced the key.</summary>
    public object Analyzer { get; }

    /// <summary>目标 / The target.</summary>
    public ObjectiveTarget Target { get; }

    /// <summary>选项 / The options.</summary>
    public object Options { get; }

    /// <summary>
    /// 目标可行性分析的键 / The key for target-feasibility analysis.
    /// </summary>
    /// <param name="analyzer">分析器实例 / The analyzer instance.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="options">求解选项 / The solve options.</param>
    /// <returns>缓存键 / The cache key.</returns>
    public static AnalysisCacheKey ForTarget(
        object analyzer,
        ObjectiveTarget target,
        ConstraintProgrammingSolveOptions options) {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return new AnalysisCacheKey(analyzer, target, options);
    }

    /// <summary>
    /// 冲突分析的键 / The key for conflict analysis.
    /// </summary>
    /// <param name="analyzer">分析器实例 / The analyzer instance.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="options">冲突选项 / The conflict options.</param>
    /// <returns>缓存键 / The cache key.</returns>
    public static AnalysisCacheKey ForConflict(
        object analyzer,
        ObjectiveTarget target,
        ConflictAnalysisOptions options) {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return new AnalysisCacheKey(analyzer, target, options);
    }
}
