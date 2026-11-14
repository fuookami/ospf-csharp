#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 声明的 CP 特性不被支持时的目标报告 / The target report for declared-but-unsupported CP features.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.unsupportedTargetReport` 与 `analyzeActiveSources` 第一道门的移植。**
///
/// **`TargetFixed` 必须是 `false`。** 这个字段的含义是"目标值已经被固定下来了"，
/// **而这里恰恰相反：根本没算，目标值未知。** 把它留成默认的 `true` 会让下游以为
/// **有一个已经确定的结论**，而实际上一个数都没有。
///
/// **`TargetFixed` has to be `false`.** The field means "the objective value has been pinned down", **and here the opposite is true: nothing was computed. Leaving it at the default `true` lets downstream
/// believe there is a settled conclusion when there is not a single number.**
///
/// **状态是 `Unsupported`，不是 `Unknown`。** `Unknown` 是"试过了没试出来"，
/// **`Unsupported` 是"这条路本来就给不出答案"**——区别决定调用方是重试还是换后端。
///
/// **The status is `Unsupported`, not `Unknown`.** `Unknown` is "tried and did not get it"; **`Unsupported` is "this route cannot give an answer"** — the difference decides whether the caller retries or
/// swaps backends.
/// </remarks>
public static class UnsupportedTargetFeatures
{
    /// <summary>
    /// 构造一份"特性不支持"的目标报告 / Build an unsupported-feature target report.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <param name="features">不被支持的特性 / The unsupported features.</param>
    /// <returns>一份 `Unsupported` 报告 / An `Unsupported` report.</returns>
    /// <remarks>
    /// **特性名字必须进消息。** "当前求解器不支持"这句话对调用方没有用——
    /// **他要知道的是"哪一条不支持"，否则唯一的动作就是把整个后端换掉。**
    ///
    /// **The feature names have to be in the message.** "The current solver does not support this" is useless to the caller — **they need to know which one, or their only move is to swap the entire
    /// backend.**
    /// </remarks>
    public static TargetFeasibilityReport Report(
        ObjectiveTarget target,
        IReadOnlySet<ConstraintProgrammingFeature> features) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(features);

        string names = string.Join(", ", features.Select(feature => feature.ToString()).OrderBy(name => name, StringComparer.Ordinal));

        return new TargetFeasibilityReport(
            Target: target,
            Status: AnalysisStatus.Unsupported,
            TargetFixed: false,
            Message: $"当前求解器未声明 CP 特性支持：{names} / Solver explicitly declares these CP features unsupported: {names}");
    }

    /// <summary>
    /// 若声明的特性不被支持，给出报告；否则放行 / The report when declared features are unsupported, otherwise <c>null</c>.
    /// </summary>
    /// <param name="matrix">能力矩阵 / The capability matrix.</param>
    /// <param name="snapshot">模型快照 / The model snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="activeSources">激活的诊断来源 / The active diagnostic sources.</param>
    /// <returns>报告，或 <c>null</c> 表示继续 / The report, or <c>null</c> to continue.</returns>
    /// <remarks>
    /// **这是 `analyzeActiveSources` 的第一行。** 排在构造派生模型之前，
    /// **因为一个这个后端点名不支持的模型，连构造都不该开始。**
    ///
    /// **This is the first line of `analyzeActiveSources`.** It runs before the derived model is built, **because a model the backend explicitly names as unsupported should not even start being built.**
    /// </remarks>
    public static TargetFeasibilityReport? Gate(
        CapabilityMatrix matrix,
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        IReadOnlySet<DiagnosticSource> activeSources) {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(activeSources);

        IReadOnlySet<ConstraintProgrammingFeature> unsupported =
            matrix.UnsupportedDeclaredCpFeatures(snapshot, activeSources);

        return unsupported.Count == 0 ? null : Report(target, unsupported);
    }
}
