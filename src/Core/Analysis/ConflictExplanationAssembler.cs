#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 冲突解释的组装 / Assembling a conflict explanation.
/// </summary>
/// <remarks>
/// `ConflictAnalyzer.analyze` 的收尾部分：把已求出的可行性结果与删除收缩结论拼成一份 `ConflictExplanation`。
/// **求解与复验由调用方完成**，这一段只负责"怎么说"。
///
/// Ported from the tail of `ConflictAnalyzer.analyze`: it turns the computed feasibility and the deletion-shrink conclusion into a `ConflictExplanation`. **The solve and the checks
/// belong to the caller**; this part decides how to say it.
/// </remarks>
public static class ConflictExplanationAssembler
{
    /// <summary>
    /// 拼出一份不可行冲突的解释 / Assemble an explanation for an infeasible target.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="targetFeasibility">初始可行性结果 / The initial feasibility result.</param>
    /// <param name="tier">提取路径 / The extraction path.</param>
    /// <param name="evidence">完整证据 / The complete evidence.</param>
    /// <param name="targetSource">目标来源，可为 <c>null</c> / The target source, or <c>null</c>.</param>
    /// <param name="shrink">删除收缩结果 / The deletion-shrink result.</param>
    /// <returns>冲突解释 / The conflict explanation.</returns>
    /// <remarks>
    /// **`AvailableEvidence` 是全集，`Members` 是收缩后的成员**，两者**不能混用**：
    /// 用 `Members` 当 `AvailableEvidence`，读者会以为"这些就是全部证据"，
    /// **于是那份"最小冲突"看起来像整个模型。**
    ///
    /// **`AvailableEvidence` is the whole universe and `Members` is what survived the shrink**, and the two **must not be confused**: using `Members` as `AvailableEvidence` makes the
    /// reader think "this is all the evidence there is", so **a minimal conflict looks like the entire model.**
    ///
    /// **三种消息互斥且有优先级**：背景阻塞 → 部分 → 无。先判"部分"的话，背景阻塞那份
    /// 会带着一条"复验未完成"的消息，**而真正的原因在别处。**
    ///
    /// **The three messages are mutually exclusive with a priority**: background blocking, then partial, then none. Checking "partial" first would leave the background-blocking case
    /// carrying a "some checks did not run" message, **while the real cause is elsewhere.**
    /// </remarks>
    public static ConflictExplanation AssembleInfeasible(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        TargetFeasibilityReport targetFeasibility,
        ConflictExtractionTier tier,
        IReadOnlyList<DiagnosticSource> evidence,
        DiagnosticSource? targetSource,
        ConflictDeletionCheck.ShrinkResult shrink) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetFeasibility);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(shrink);

        string? message = shrink.BackgroundBlocking
            ? "目标在没有原始约束后仍不可达，阻塞来自固定背景，不能形成约束级 MUS / "
                + "The target remains unreachable without an original constraint; blocking comes from fixed background, so no constraint-level MUS is claimed"
            : shrink.Partial
                ? "至少一个删除复验未知或未执行，MUS 未完全证明 / "
                    + "At least one deletion check was unknown or not executed; MUS is not fully proven"
                : null;

        return new ConflictExplanation(
            Target: target,
            TargetFeasibility: targetFeasibility,
            ExtractionTier: tier,
            Status: AnalysisStatus.Unreachable,
            AvailableEvidence: evidence,
            Members: shrink.Members,
            TargetSource: targetSource,
            Validity: ConflictValidity.Verified,
            Minimality: shrink.Minimality,
            Verifications: shrink.Verifications,
            GroupSummaries: ConflictEvidenceSupport.GroupSummaries(snapshot, shrink.Members),
            BackgroundBlocking: shrink.BackgroundBlocking,
            Message: message);
    }

    /// <summary>
    /// 拼出一份"目标可达、无需冲突分析"的解释 / Assemble an explanation for a reachable target, where no conflict analysis applies.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="targetFeasibility">初始可行性结果 / The initial feasibility result.</param>
    /// <param name="tier">提取路径 / The extraction path.</param>
    /// <param name="evidence">完整证据 / The complete evidence.</param>
    /// <param name="targetSource">目标来源，可为 <c>null</c> / The target source, or <c>null</c>.</param>
    /// <returns>冲突解释 / The conflict explanation.</returns>
    /// <remarks>
    /// **目标可达时 `Members` 为空、最小性是 `NotChecked`，而 `Validity` 仍是 `Verified`。**
    ///
    /// 这一条容易写反：**"没有冲突"不等于"没验证过"**。把 `Validity` 填成 `Unknown`，
    /// 读者会以为这份报告本身可疑——**而它恰恰是唯一被完全证明的那种结论。**
    ///
    /// **A reachable target has no members and `NotChecked` minimality, yet `Validity` stays `Verified`.** It is easy to flip: **"no conflict" does not mean "nothing was verified"**.
    /// Filling `Validity` with `Unknown` makes the reader doubt a report that is, in fact, the one kind of conclusion that was fully proven.
    /// </remarks>
    public static ConflictExplanation AssembleReachable(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        TargetFeasibilityReport targetFeasibility,
        ConflictExtractionTier tier,
        IReadOnlyList<DiagnosticSource> evidence,
        DiagnosticSource? targetSource = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targetFeasibility);

        return new ConflictExplanation(
            Target: target,
            TargetFeasibility: targetFeasibility,
            ExtractionTier: tier,
            Status: targetFeasibility.Status,
            AvailableEvidence: evidence,
            Members: [],
            TargetSource: targetSource,
            Validity: ConflictValidity.Verified,
            Minimality: ConflictMinimality.NotChecked,
            Verifications: [],
            GroupSummaries: [],
            BackgroundBlocking: false,
            Message: null);
    }

    /// <summary>
    /// 目标不是"不可达"时的解释 / The explanation when the target is not unreachable.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="targetFeasibility">可行性结果 / The feasibility result.</param>
    /// <param name="tier">提取路径 / The extraction path.</param>
    /// <param name="evidence">完整证据 / The complete evidence.</param>
    /// <returns>冲突解释 / The conflict explanation.</returns>
    /// <exception cref="ArgumentException">当状态是 `Unreachable` / When the status is `Unreachable`.</exception>
    /// <remarks>
    /// **三种状态里只有一种该给空成员：**
    ///
    /// | 目标状态 | `Members` | `Validity` | 消息 |
    /// | --- | --- | --- | --- |
    /// | `Reachable` | **空** | `Verified` | 目标可达，不存在阻塞 |
    /// | `Unknown` | **全部证据** | **`Unknown`** | 目标检查未知 |
    /// | `Unsupported` | **全部证据** | **`Unknown`** | 目标检查不受支持 |
    ///
    /// **后两行最容易写错成"和可达一样"**：那会给出 `Members` 为空 + `Verified` 的一份解释，
    /// **而它同时声称"没有冲突"和"已证明"——两句话都是从没做过的分析里编出来的。**
    ///
    /// `Unreachable` 不走这条路：它要真去做删除复验。**所以这里直接拒绝，而不是给一份凑合的解释。**
    ///
    /// **Only one of the three statuses deserves empty members.** **The last two are the easy mistake**: treating them like `Reachable` yields empty members with `Verified`, **claiming both
    /// "no conflict" and "proven" — two statements invented from an analysis that never ran.** `Unreachable` does not come through here; it does real deletion checks, so this refuses
    /// rather than inventing a plausible-looking explanation.
    /// </remarks>
    public static ConflictExplanation AssembleNotUnreachable(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        TargetFeasibilityReport targetFeasibility,
        ConflictExtractionTier tier,
        IReadOnlyList<DiagnosticSource> evidence) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targetFeasibility);
        ArgumentNullException.ThrowIfNull(evidence);

        if (targetFeasibility.Status == AnalysisStatus.Unreachable) {
            throw new ArgumentException(
                "不可达的目标要走删除复验 / an unreachable target has to go through the deletion checks",
                nameof(targetFeasibility));
        }

        bool reachable = targetFeasibility.Status == AnalysisStatus.Reachable;
        DiagnosticSource[] members = reachable ? [] : evidence.ToArray();
        string message = targetFeasibility.Status switch {
            AnalysisStatus.Reachable => "目标可达，不存在 target blocking conflict / Target is reachable; no blocking conflict exists",
            AnalysisStatus.Unsupported => "目标检查不受支持，保留全部原始证据 / Target check is unsupported; all original evidence is retained",
            _ => "目标检查未知，保留全部原始证据 / Target check is unknown; all original evidence is retained",
        };

        return new ConflictExplanation(
            Target: target,
            TargetFeasibility: targetFeasibility,
            ExtractionTier: tier,
            Status: targetFeasibility.Status,
            AvailableEvidence: evidence,
            Members: members,
            TargetSource: null,
            // **只有"可达"是被证过的**：未知与不受支持都只说明"没证出来"。
            // **Only reachability was proven**: unknown and unsupported both mean "nothing was established".
            Validity: reachable ? ConflictValidity.Verified : ConflictValidity.Unknown,
            Minimality: ConflictMinimality.NotChecked,
            Verifications: [],
            GroupSummaries: ConflictEvidenceSupport.GroupSummaries(snapshot, members),
            BackgroundBlocking: false,
            Message: message);
    }
}
