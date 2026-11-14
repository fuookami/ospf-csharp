#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>临界分析报告 schema 版本 / Critical analysis report schema version.</summary>
public static class CriticalAnalysisSchema
{
    /// <summary>报告 schema 版本 / Report schema version.</summary>
    public const string Version = "1.0";
}

/// <summary>一个约束的有效性条目 / Effectiveness of one constraint.</summary>
/// <param name="ConstraintId">约束身份 / Constraint identity.</param>
/// <param name="Group">原始约束组 / Original constraint group.</param>
/// <param name="Tier">候选层级 / Candidate tier.</param>
/// <param name="Activity">活动性状态 / Activity status.</param>
/// <param name="DualValue">对偶值 / Dual value.</param>
/// <param name="LocalEffective">局部有效性；只在有对偶值时才有意义 / Local effectiveness; only meaningful when a dual value exists.</param>
/// <param name="RemovalEffective">单约束删除是否有效 / Whether a single removal is effective.</param>
/// <param name="FirstEffectiveDelta">首次有效的 RHS delta / First effective RHS delta.</param>
/// <param name="MaxImprovement">已证明的最大目标改善 / Proven maximum objective improvement.</param>
/// <param name="MarginalEffect">边际效应 / Marginal effect.</param>
/// <param name="Status">分析状态 / Analysis status.</param>
/// <param name="UnavailableReason">不可用原因 / Reason the value is unavailable.</param>
public sealed record EffectivenessEntry(
    ConstraintId ConstraintId,
    string? Group,
    CandidateTier Tier,
    ActivityStatus Activity,
    Flt64? DualValue,
    bool? LocalEffective,
    bool? RemovalEffective,
    Flt64? FirstEffectiveDelta,
    Flt64? MaxImprovement,
    Flt64? MarginalEffect,
    AnalysisStatus Status,
    string? UnavailableReason = null)
{
    /// <summary>
    /// 是否观察到全局改善 / Whether a global improvement was observed.
    /// </summary>
    /// <remarks>
    /// **必须同时满足"有值"和"大于零"**：`null > 0` 在这里会被读成"没有改善"，
    /// 而实际含义是**根本没有测出这个量**。**把"没测"报成"没改善"，会让一份缺失证据的报告看起来结论明确。**
    ///
    /// **Both "has a value" and "greater than zero" are required**: `null > 0` here would read as "no improvement" when the real meaning is **the quantity was never measured**.
    /// **Reporting "not measured" as "no improvement" makes a report with missing evidence look conclusive.**
    /// </remarks>
    public bool IsGloballyEffective => MaxImprovement is { } improvement && improvement.ToDouble() > 0.0;

    /// <summary>对应的诊断来源 / The matching diagnostic source.</summary>
    public DiagnosticSource Source => new DiagnosticSource.Constraint(ConstraintId);
}

/// <summary>按已证明改善排序的有效性报告 / An effectiveness ranking ordered by proven improvement.</summary>
/// <remarks>
/// **不在 `GloballyEffective` 里不等于无关**：单独移除无收益的约束，仍可能是组合瓶颈的一部分。
/// 这正是 `UnavailableReason` 与 `MaxImprovement = null` 必须分开的理由。
///
/// **Absence from `GloballyEffective` does not mean irrelevant**: a constraint whose single removal shows no benefit can still be part of a combined bottleneck. That is exactly why
/// `UnavailableReason` and `MaxImprovement = null` have to stay distinct.
/// </remarks>
/// <param name="BaselineObjective">基线目标值 / Baseline objective value.</param>
/// <param name="Entries">按已证明的最大改善降序排列 / Entries ordered by proven maximum improvement.</param>
/// <param name="SchemaVersion">报告 schema / Report schema.</param>
public sealed record EffectivenessRanking(
    Flt64 BaselineObjective,
    IReadOnlyList<EffectivenessEntry>? Entries = null,
    string SchemaVersion = CriticalAnalysisSchema.Version)
{
    /// <summary>条目列表，未给出时为空 / The entries, empty when absent.</summary>
    public IReadOnlyList<EffectivenessEntry> EntryList => Entries ?? System.Array.Empty<EffectivenessEntry>();

    /// <summary>观察到全局改善的约束 / Constraints with an observed global improvement.</summary>
    public IReadOnlyList<EffectivenessEntry> GloballyEffective =>
        EntryList.Where(entry => entry.IsGloballyEffective).ToArray();

    /// <summary>局部有效的约束 / Locally effective constraints.</summary>
    public IReadOnlyList<EffectivenessEntry> LocallyEffective =>
        EntryList.Where(entry => entry.LocalEffective == true).ToArray();

    /// <summary>按约束身份查条目 / Look an entry up by constraint identity.</summary>
    /// <param name="id">约束身份 / Constraint identity.</param>
    /// <returns>条目，未找到时为 <c>null</c> / The entry, or <c>null</c> when absent.</returns>
    public EffectivenessEntry? Entry(ConstraintId id) =>
        EntryList.FirstOrDefault(entry => entry.ConstraintId == id);

    /// <summary>
    /// 校验排序报告 / Validate the ranking.
    /// </summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    /// <remarks>
    /// 这里挡的四条，**都是"报告看起来有数值，但那些数值撑不住"的形状**：
    ///
    /// | 检查 | 不查会怎样 |
    /// | --- | --- |
    /// | schema 非空、基线目标有限 | 一份没有出处的报告 |
    /// | 身份非空、不重复 | 同一条约束算两次，**改善看起来翻倍** |
    /// | 数值有限、delta 为正 | `NaN` 混进排序，**整个降序失去意义** |
    /// | **局部判决需要对偶值** | 没有对偶值却给出"局部有效"，**这是凭空断言** |
    ///
    /// Every check here blocks a shape where **the report looks numeric but the numbers do not hold up**: a report with no provenance; the same constraint counted twice, **making
    /// the improvement look doubled**; `NaN` slipping into the ordering, **which destroys the sort**; and **a local verdict without a dual value**, which is an assertion from
    /// nothing at all.
    /// </remarks>
    public Try Validate() {
        if (string.IsNullOrWhiteSpace(SchemaVersion)
            || double.IsNaN(BaselineObjective.ToDouble())
            || double.IsInfinity(BaselineObjective.ToDouble())) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "有效性排序报告无效 / Effectiveness ranking is invalid"));
        }

        var seen = new HashSet<ConstraintId>();
        foreach (EffectivenessEntry entry in EntryList) {
            if (string.IsNullOrWhiteSpace(entry.ConstraintId.Value)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "有效性条目必须带稳定约束身份 / Effectiveness entry requires a stable constraint identity"));
            }
            if (!seen.Add(entry.ConstraintId)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "有效性排序包含重复约束 / Effectiveness ranking contains duplicate constraints"));
            }

            bool finite = IsFiniteOrNull(entry.DualValue)
                && IsFiniteOrNull(entry.MaxImprovement)
                && IsFiniteOrNull(entry.MarginalEffect);
            bool deltaOk = entry.FirstEffectiveDelta is not { } delta
                || (IsFinite(delta) && delta.ToDouble() > 0.0);
            if (!finite || !deltaOk) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "有效性条目包含非有限或非正数值 / Effectiveness entry contains a non-finite or non-positive value"));
            }

            // **局部判决只在对偶值确实可用时才允许存在**：没有对偶值却给出"局部有效"，
            // 是凭空断言——**而它读起来完全像一条测出来的结论。**
            // **A local verdict is allowed only when a dual value really exists**: "locally effective" with no dual is an assertion from nothing — **and it reads exactly like a
            // measured conclusion.**
            if (entry.LocalEffective is not null && entry.DualValue is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "局部有效性需要可用的对偶值 / Local effectiveness requires an available dual value"));
            }
        }
        return Results.OkInstance;
    }

    private static bool IsFinite(Flt64 value) =>
        !double.IsNaN(value.ToDouble()) && !double.IsInfinity(value.ToDouble());

    private static bool IsFiniteOrNull(Flt64? value) => value is not { } present || IsFinite(present);
}
