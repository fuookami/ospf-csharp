#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 一个原始约束或变量值域成员的活动性结果 / Activity result for one original constraint or variable-domain member.
/// </summary>
/// <remarks>
/// **变量边界和稀疏值域的 `ConstraintId` 为 `null`**；
/// 所有记录都该用 `Source` 当稳定身份——**用一个可能为 `null` 的 id 当键，正是"两条记录撞成一条"的来源。**
///
/// **`ConstraintId` is `null` for variable bounds and sparse domains**; every record should use `Source`
/// as its stable identity — **keying on something that may be `null` is how two records collapse into one.**
/// </remarks>
public sealed class ConstraintActivity
{
    /// <summary>构造一条活动性记录 / Construct one activity record.</summary>
    /// <param name="constraintId">原始约束 ID，变量或值域成员为 <c>null</c> / Original constraint ID, or <c>null</c> for variable/domain members.</param>
    /// <param name="group">约束所属组名称 / Constraint group name.</param>
    /// <param name="status">活动性结论 / Activity conclusion.</param>
    /// <param name="evidence">原始模型证据 / Original-model evidence.</param>
    /// <param name="slack">有符号标量松弛 / Signed scalar slack.</param>
    /// <param name="normalizedSlack">归一化松弛 / Slack normalized by the relevant magnitude.</param>
    /// <param name="tolerance">绝对容差 / Absolute tolerance used for the metric.</param>
    /// <param name="source">稳定来源身份，缺省取证据的来源 / Stable source identity, defaulting to the evidence source.</param>
    public ConstraintActivity(
        ConstraintId? constraintId,
        string? group,
        ActivityStatus status,
        ActivityEvidence evidence,
        double? slack = null,
        double? normalizedSlack = null,
        double? tolerance = null,
        DiagnosticSource? source = null) {
        ArgumentNullException.ThrowIfNull(evidence);
        ConstraintId = constraintId;
        Group = group;
        Status = status;
        Evidence = evidence;
        Slack = slack;
        NormalizedSlack = normalizedSlack;
        Tolerance = tolerance;
        // **缺省必须回落到证据来源，而且必须在构造时定下来**：留成"用的时候再算"会让一条记录的身份
        // 随 `Evidence` 的可变状态漂移——**而它是缓存键。**
        // **The fallback has to be resolved at construction**: computing it lazily would let a record's identity drift with `Evidence` — **and this is a cache key.**
        Source = source ?? evidence.Source;
    }

    /// <summary>原始约束 ID，变量或值域成员为 <c>null</c> / Original constraint ID, or <c>null</c> for variable/domain members.</summary>
    public ConstraintId? ConstraintId { get; }

    /// <summary>约束所属组名称 / Constraint group name.</summary>
    public string? Group { get; }

    /// <summary>活动性结论 / Activity conclusion.</summary>
    public ActivityStatus Status { get; }

    /// <summary>有符号标量松弛，无自然度量时为 <c>null</c> / Signed scalar slack, or <c>null</c> without a natural metric.</summary>
    public double? Slack { get; }

    /// <summary>按相关量级归一化的松弛 / Slack normalized by the relevant magnitude.</summary>
    public double? NormalizedSlack { get; }

    /// <summary>此度量使用的绝对容差 / Absolute tolerance used for the metric.</summary>
    public double? Tolerance { get; }

    /// <summary>原始模型证据 / Original-model evidence.</summary>
    public ActivityEvidence Evidence { get; }

    /// <summary>稳定来源身份 / Stable source identity.</summary>
    public DiagnosticSource Source { get; }
}

/// <summary>
/// 一个约束组的活动性状态计数 / Counts of activity states for one constraint group.
/// </summary>
/// <param name="Group">组名称，<c>null</c> 表示未分组约束 / Group name, or <c>null</c> for ungrouped constraints.</param>
/// <param name="Total">组内约束实例数 / Number of constraint instances in the group.</param>
/// <param name="Active">`Active` 状态数量 / Number of `Active` records.</param>
/// <param name="NearlyActive">`NearlyActive` 状态数量 / Number of `NearlyActive` records.</param>
/// <param name="Inactive">`Inactive` 状态数量 / Number of `Inactive` records.</param>
/// <param name="SatisfiedWithoutSlackMetric">无标量松弛度量但满足的数量 / Number satisfied without a scalar slack metric.</param>
/// <param name="Violated">`Violated` 状态数量 / Number of `Violated` records.</param>
/// <param name="Unknown">`Unknown` 状态数量 / Number of `Unknown` records.</param>
public sealed record ActivityGroupSummary(
    string? Group,
    int Total,
    int Active,
    int NearlyActive,
    int Inactive,
    int SatisfiedWithoutSlackMetric,
    int Violated,
    int Unknown)
{
    /// <summary>使用 `ConstraintCount` 的调用方的兼容名称 / Compatibility name for consumers that use `ConstraintCount`.</summary>
    public int ConstraintCount => Total;
}

/// <summary>
/// 一个基线赋值的不可变活动性报告 / Immutable activity report for one baseline assignment.
/// </summary>
/// <remarks>
/// **组汇总只统计约束实例**：变量边界和稀疏值域仍在 `Activities` 里，通过 `VariableActivities` 提供给需要完整证据的调用方。
/// 把它们混进组计数，会让"这个约束组里有 5 条活动"变成"有 5 条约束和 2 个变量边界"——**而后者的 5 没人能解释。**
///
/// **Group summaries count constraint instances only**: variable bounds and sparse domains stay in `Activities` and are exposed through `VariableActivities`. Mixing them into
/// the group counts turns "five active members in this group" into "five constraints and two variable bounds" — **and nobody can explain the five.**
/// </remarks>
/// <param name="Activities">按 snapshot 顺序排列的约束与变量活动性记录 / Constraint and variable activity records in snapshot order.</param>
/// <param name="GroupSummaries">原始约束的组级计数 / Group-level counts for original constraints.</param>
/// <param name="Tolerance">分析器使用的绝对容差 / Absolute tolerance used by the analyzer.</param>
/// <param name="NearlyActiveTolerance">`NearlyActive` 阈值 / Threshold for `NearlyActive`.</param>
public sealed record ConstraintActivityReport(
    IReadOnlyList<ConstraintActivity> Activities,
    IReadOnlyList<ActivityGroupSummary> GroupSummaries,
    double Tolerance,
    double NearlyActiveTolerance)
{
    /// <summary>仅约束记录 / Constraint-only records.</summary>
    public IReadOnlyList<ConstraintActivity> ConstraintActivities =>
        Activities.Where(activity => activity.ConstraintId is not null).ToArray();

    /// <summary>变量边界和稀疏值域记录 / Variable bounds and sparse-domain records.</summary>
    public IReadOnlyList<ConstraintActivity> VariableActivities =>
        Activities.Where(activity => activity.ConstraintId is null).ToArray();

    /// <summary>原始约束数量 / Number of original constraints.</summary>
    public int TotalConstraints => ConstraintActivities.Count;

    /// <summary>全部活动性记录数量 / Number of all activity records.</summary>
    public int TotalActivities => Activities.Count;

    /// <summary>组汇总别名 / Group summaries alias.</summary>
    public IReadOnlyList<ActivityGroupSummary> Groups => GroupSummaries;

    /// <summary>
    /// 按约束 ID 查找活动性 / Find a constraint activity by constraint ID.
    /// </summary>
    /// <param name="id">要查找的约束 ID / Constraint ID to find.</param>
    /// <returns>匹配的活动性记录，找不到时为 <c>null</c> / Matching activity record, or <c>null</c> when absent.</returns>
    public ConstraintActivity? Constraint(ConstraintId id) =>
        ConstraintActivities.FirstOrDefault(activity => activity.ConstraintId is { } candidate && candidate == id);
}
