#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 基线的摘要 / A summary of the baseline solve.
/// </summary>
/// <param name="ObjectiveId">目标标识 / Objective identity.</param>
/// <param name="Sense">优化方向 / Optimisation sense.</param>
/// <param name="ObjectiveValue">基线目标值，可为 <c>null</c> / The baseline objective value, or <c>null</c>.</param>
/// <param name="Status">基线状态 / Baseline status.</param>
/// <param name="ProvenOptimal">基线是否已证明最优 / Whether the baseline was proven optimal.</param>
public sealed record AnalysisBaselineSummary(
    string ObjectiveId,
    string Sense,
    Flt64? ObjectiveValue,
    AnalysisStatus Status,
    bool ProvenOptimal);

/// <summary>
/// 关键约束分析的完整报告 / The full critical-constraint analysis report.
/// </summary>
/// <remarks>
/// 移植自 `CriticalConstraintAnalysisReport`。**这是基线分析器里最后一个报告类型。**
///
/// Ported from `CriticalConstraintAnalysisReport`. **This is the last report type among the baseline analysers.**
/// </remarks>
/// <param name="Baseline">基线摘要 / The baseline summary.</param>
/// <param name="Activity">约束活跃度报告 / The constraint activity report.</param>
/// <param name="LocalSensitivity">局部敏感度，可为 <c>null</c> / Local sensitivity, or <c>null</c>.</param>
/// <param name="Funnel">候选漏斗 / The candidate funnel.</param>
/// <param name="Effectiveness">有效性排名，可为 <c>null</c> / The effectiveness ranking, or <c>null</c>.</param>
/// <param name="Target">目标可行性，可为 <c>null</c> / Target feasibility, or <c>null</c>.</param>
/// <param name="Conflict">冲突解释，可为 <c>null</c> / The conflict explanation, or <c>null</c>.</param>
/// <param name="GroupSummaries">按组统计 / Per-group summaries.</param>
/// <param name="Status">聚合状态 / The aggregate status.</param>
/// <param name="UnavailableReasons">未执行阶段的原因 / Why some stages did not run.</param>
/// <param name="SchemaVersion">报告 schema 版本 / The report schema version.</param>
public sealed record CriticalConstraintAnalysisReport(
    AnalysisBaselineSummary Baseline,
    ConstraintActivityReport Activity,
    FixedIntegerLpSensitivityReport? LocalSensitivity,
    CandidateFunnelRanking Funnel,
    EffectivenessRanking? Effectiveness,
    TargetFeasibilityReport? Target,
    ConflictExplanation? Conflict,
    IReadOnlyList<AnalysisGroupSummary> GroupSummaries,
    AnalysisStatus Status,
    IReadOnlyList<string> UnavailableReasons,
    string SchemaVersion = CriticalAnalysisSchema.Version)
{
    /// <summary>是否给出了已验证的最小组 / Whether a verified minimal blocking set is available.</summary>
    /// <remarks>
    /// **`null` 的冲突与"冲突未证明最小"必须都返回 `false`。** 写成 `Conflict?.Minimality == Irreducible`
    /// 也可以，但那样一来**新增一个最小性状态就会悄悄变成"没有最小集"**。
    ///
    /// **A `null` conflict and an unproven minimality both have to give `false`.** Writing it as `Conflict?.Minimality == Irreducible` would work too, but then **a newly added
    /// minimality status would silently become "no minimal set".**
    /// </remarks>
    public bool HasMinimalBlockingSet =>
        Conflict is not null && Conflict.Minimality == ConflictMinimality.Irreducible;

    /// <summary>
    /// 面向诊断的阻塞摘要 / A human-readable blocking summary.
    /// </summary>
    /// <returns>摘要文本 / The summary text.</returns>
    /// <remarks>
    /// **三种情况分开说**：没跑、背景阻塞、有最小集。**第二种最容易漏**——
    /// 背景阻塞时 `Members` 里没有约束，**若走"最小集"那条分支，摘要会声称"0 个原始证据"，
    /// 而真实原因是固定背景。**
    ///
    /// **Three cases are kept apart**: not run, background blocking, and a minimal set. **The second is the easy omission** — with background blocking there is no constraint
    /// among the members, and **taking the "minimal set" branch would report "0 original evidence items" while the real cause is fixed background.**
    /// </remarks>
    public string BlockingSummary() {
        if (Conflict is null) {
            return "阻塞分析未执行 / Blocking analysis was not run";
        }
        if (Conflict.BackgroundBlocking) {
            return "目标在没有原始约束后仍不可达，阻塞来自固定背景 / "
                + "The target remains unreachable without an original constraint; blocking comes from fixed background";
        }

        string detail = string.Join(", ", GroupSummaries.Select(entry => $"{entry.Group ?? "<ungrouped>"}: {entry.BlockingInstances}"));
        string minimality = HasMinimalBlockingSet ? "已验证最小" : "未证明最小";
        return $"最小阻塞集合（{minimality}）：{Conflict.Members.Count} 个原始证据 [{detail}] / "
            + $"Minimal blocking set ({(HasMinimalBlockingSet ? "verified minimal" : "not proven minimal")}): "
            + $"{Conflict.Members.Count} original evidence items [{detail}]";
    }

    /// <summary>
    /// 校验这份报告自洽 / Validate that the report is self-consistent.
    /// </summary>
    /// <returns>问题清单，空表示通过 / The list of problems; empty means it passed.</returns>
    /// <remarks>
    /// **四条不变量，每条都对应一种"看起来正常但说不通"的报告：**
    ///
    /// | 不变量 | 违反后报告会说什么 |
    /// | --- | --- |
    /// | schema 版本非空 | 一份无法追溯格式的报告 |
    /// | 基线有目标身份与方向 | 一份不知道在优化什么的报告 |
    /// | **已证明最优必须带目标值** | **"已证明最优"但报不出最优值** |
    /// | **背景阻塞不得同时声称最小集** | **"最小集是空的"与"阻塞来自背景"同时成立** |
    ///
    /// **第三条最关键**：`ProvenOptimal = true` 而 `ObjectiveValue == null` 是一份自相矛盾的报告，
    /// **而它的两个字段单独看都合法。**
    ///
    /// **Four invariants, each matching a report that looks fine but does not add up.** **The third is the important one**: `ProvenOptimal = true` with `ObjectiveValue == null` is
    /// self-contradictory, **while either field alone is legitimate.**
    /// </remarks>
    public IReadOnlyList<string> Validate() {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(SchemaVersion)) {
            problems.Add("报告 schema 版本不得为空 / Report schema version must not be blank");
        }
        if (string.IsNullOrWhiteSpace(Baseline.ObjectiveId) || string.IsNullOrWhiteSpace(Baseline.Sense)) {
            problems.Add("基线需要目标身份与优化方向 / Baseline requires an objective identity and sense");
        }
        if (Baseline.ProvenOptimal && Baseline.ObjectiveValue is null) {
            problems.Add("已证明的基线必须带目标值 / A proven baseline requires an objective value");
        }
        if (Conflict is { BackgroundBlocking: true } && HasMinimalBlockingSet) {
            problems.Add("背景阻塞时不能同时声称约束级最小集 / "
                + "Background blocking cannot be claimed together with a constraint-level minimal set");
        }

        return problems;
    }

    /// <summary>
    /// 取两个状态里更弱的一个 / Take the weaker of two statuses.
    /// </summary>
    /// <param name="current">当前状态 / The current status.</param>
    /// <param name="next">下一个状态，可为 <c>null</c> / The next status, or <c>null</c>.</param>
    /// <returns>更弱的状态 / The weaker status.</returns>
    /// <remarks>
    /// **顺序是 `Reachable` &gt; `Unreachable` &gt; `Unknown` &gt; `Unsupported`，取最弱者。**
    ///
    /// 这条规则的含义是：**整体结论不能说成比它最弱的那个环节更确定。**
    /// 一个阶段没跑出结论，整份报告就不能说"目标可达"——
    /// **哪怕其余六个阶段都跑出了确定结论。**
    ///
    /// **`Unreachable` 比 `Unknown` 强**：前者是"证出来了不可达"，后者是"没证出来"。
    /// 把这两个排反，一份"目标不可达"的报告会被一个没结论的阶段拉成"未知"——**丢掉了已有的证明。**
    ///
    /// **The order is `Reachable` &gt; `Unreachable` &gt; `Unknown` &gt; `Unsupported`, weakest wins.** The rule says **the overall conclusion may not be more certain than its
    /// weakest stage.** One stage without a conclusion stops the report from saying "the target is reachable" **even when the other six concluded firmly.** **`Unreachable` outranks
    /// `Unknown`**: the first was proven, the second was not; swapping them would let an inconclusive stage drag a proven "unreachable" down to "unknown", **discarding a proof that
    /// already exists.**
    /// </remarks>
    public static AnalysisStatus CombineStatus(AnalysisStatus current, AnalysisStatus? next) {
        if (next is null) {
            return current;
        }

        static int Rank(AnalysisStatus status) => status switch {
            AnalysisStatus.Reachable => 3,
            AnalysisStatus.Unreachable => 2,
            AnalysisStatus.Unknown => 1,
            _ => 0,
        };

        return Rank(next.Value) < Rank(current) ? next.Value : current;
    }
}
