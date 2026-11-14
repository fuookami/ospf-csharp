#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Flt64 = Fuookami.Ospf.Math.Algebra.Number.Flt64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>施加到一个原始约束的扰动操作 / Perturbation operation applied to one original constraint.</summary>
public enum ConstraintPerturbationKind
{
    /// <summary>修改整数比较约束 RHS / Change an integer comparison RHS.</summary>
    Rhs,

    /// <summary>从派生 snapshot 删除一个原始约束 / Remove one original constraint from a derived snapshot.</summary>
    Removal,
}

/// <summary>一次目标比较的结果 / Outcome of one objective comparison.</summary>
public enum PerturbationOutcome
{
    /// <summary>扰动模型产生了已证明的目标改善 / The perturbed model produced a proven objective improvement.</summary>
    Effective,

    /// <summary>已证明扰动模型且未观察到目标改善 / The perturbed model was proven and no objective improvement was observed.</summary>
    NoObservedEffect,

    /// <summary>求解未能支持目标结论 / The solve did not support an objective conclusion.</summary>
    Unknown,
}

/// <summary>自适应扰动序列的结果分类 / Result classification for an adaptive perturbation sequence.</summary>
public enum AdaptivePerturbationOutcome
{
    /// <summary>所有尝试的 delta 均无观察到效果 / Every attempted delta had no observed effect.</summary>
    NoObservedEffect,

    /// <summary>观察到首次有效 delta / The first effective delta was observed.</summary>
    FirstEffective,

    /// <summary>建立了有效阈值上下区间 / A lower/upper effective threshold interval was established.</summary>
    ThresholdInterval,

    /// <summary>预算耗尽且无法形成已证明结论 / The budget ended before a proven conclusion was possible.</summary>
    UnknownDueToBudget,
}

/// <summary>自适应 delta 增长及可选阈值细化策略 / Policy for adaptive delta growth and optional threshold refinement.</summary>
/// <param name="InitialDelta">第一个正 RHS delta / First positive RHS delta.</param>
/// <param name="GrowthFactor">乘法增长因子 / Multiplicative growth factor.</param>
/// <param name="MaxDelta">允许尝试的最大 delta / Largest delta to try.</param>
/// <param name="MaxSteps">增长阶段最多尝试次数 / Maximum number of growth attempts.</param>
/// <param name="RefineThreshold">是否细化首次有效区间 / Whether to refine the first effective interval.</param>
/// <param name="RefinementIterations">二分细化最多调用次数 / Maximum binary refinement calls.</param>
public sealed record AdaptivePerturbationPolicy(
    Flt64? InitialDelta = null,
    double GrowthFactor = 2.0,
    Flt64? MaxDelta = null,
    int MaxSteps = 8,
    bool RefineThreshold = false,
    int RefinementIterations = 4)
{
    /// <summary>第一个正 RHS delta，缺省为 1 / First positive RHS delta, one by default.</summary>
    public Flt64 InitialDeltaValue => InitialDelta ?? new Flt64(1.0);

    /// <summary>允许尝试的最大 delta，缺省为 16 / Largest delta to try, sixteen by default.</summary>
    public Flt64 MaxDeltaValue => MaxDelta ?? new Flt64(16.0);

    /// <summary>
    /// 校验策略 / Validate the policy.
    /// </summary>
    /// <returns>问题描述，合法时为 <c>null</c> / The problem, or <c>null</c> when well formed.</returns>
    /// <remarks>
    /// **增长因子必须严格大于 1**：等于 1 时每一步的 delta 不变，序列会在预算内反复试同一个点，
    /// **而报告只会说"没有观察到效果"。**
    ///
    /// **The growth factor has to be strictly greater than one**: at exactly one the delta never changes and the sequence retries the same point until the budget runs out,
    /// **while the report simply says "no effect observed".**
    /// </remarks>
    public string? Validate() {
        if (!IsFinite(InitialDeltaValue) || InitialDeltaValue.ToDouble() <= 0.0) {
            return "Adaptive initial delta must be finite and positive";
        }
        if (double.IsNaN(GrowthFactor) || double.IsInfinity(GrowthFactor) || GrowthFactor <= 1.0) {
            return "Adaptive growth factor must be finite and greater than one";
        }
        if (!IsFinite(MaxDeltaValue) || MaxDeltaValue.ToDouble() < InitialDeltaValue.ToDouble()) {
            return "Adaptive max delta must be finite and no smaller than initial delta";
        }
        if (MaxSteps <= 0) {
            return "Adaptive max steps must be positive";
        }
        if (RefinementIterations < 0) {
            return "Adaptive refinement iterations must not be negative";
        }
        return null;
    }

    private static bool IsFinite(Flt64 value) =>
        !double.IsNaN(value.ToDouble()) && !double.IsInfinity(value.ToDouble());
}

/// <summary>一个约束的 RHS 与删除分析策略 / Policy for one constraint's RHS and removal analysis.</summary>
/// <param name="Deltas">显式正 RHS delta 列表 / Explicit positive RHS deltas.</param>
/// <param name="Adaptive">可选自适应序列 / Optional adaptive sequence.</param>
/// <param name="RemovalTest">是否执行单约束删除测试 / Whether to run the single-constraint removal test.</param>
/// <param name="ObjectiveTolerance">目标改善判定阈值 / Objective improvement threshold.</param>
/// <param name="MaxSolves">后端求解调用总次数上限 / Maximum backend solve calls.</param>
/// <param name="TimeBudget">此候选的墙钟时间预算 / Wall-clock budget for this candidate.</param>
/// <param name="WarmStart">是否传递上次可行解提示 / Whether to pass the previous feasible solution as a hint.</param>
public sealed record ConstraintPerturbationPolicy(
    IReadOnlyList<Flt64>? Deltas = null,
    AdaptivePerturbationPolicy? Adaptive = null,
    bool RemovalTest = false,
    double ObjectiveTolerance = 1e-9,
    int MaxSolves = 32,
    TimeSpan? TimeBudget = null,
    bool WarmStart = true)
{
    /// <summary>显式正 RHS delta 列表，缺省为单个 1 / Explicit positive RHS deltas, a single one by default.</summary>
    public IReadOnlyList<Flt64> DeltaList => Deltas ?? [new Flt64(1.0)];

    /// <summary>
    /// 校验策略 / Validate the policy.
    /// </summary>
    /// <returns>问题描述，合法时为 <c>null</c> / The problem, or <c>null</c> when well formed.</returns>
    /// <remarks>
    /// **至少要有一个操作**：没有显式 delta、没有自适应、也不做删除测试的策略，跑起来**什么也不会发生**，
    /// 而报告会是一份"分析完成、无发现"。**那是最坏的一种空跑。**
    ///
    /// **At least one operation is required**: a policy with no explicit deltas, no adaptive sequence and no removal test **does nothing at all**, and the report comes back as
    /// "analysis complete, nothing found". **That is the worst kind of no-op.**
    /// </remarks>
    public string? Validate() {
        if (DeltaList.Any(delta => !IsFinite(delta) || delta.ToDouble() <= 0.0)) {
            return "Perturbation deltas must be finite and positive";
        }
        if (DeltaList.Count == 0 && Adaptive is null && !RemovalTest) {
            return "At least one explicit, adaptive, or removal operation is required";
        }
        if (double.IsNaN(ObjectiveTolerance) || double.IsInfinity(ObjectiveTolerance) || ObjectiveTolerance < 0.0) {
            return "Objective tolerance must be finite and non-negative";
        }
        if (MaxSolves <= 0) {
            return "Perturbation max solves must be positive";
        }
        if (TimeBudget is { } budget && budget <= TimeSpan.Zero) {
            return "Perturbation time budget must be positive";
        }
        return Adaptive?.Validate();
    }

    private static bool IsFinite(Flt64 value) =>
        !double.IsNaN(value.ToDouble()) && !double.IsInfinity(value.ToDouble());
}

/// <summary>一次派生模型求解请求 / One derived-model solve request.</summary>
/// <param name="BaselineSnapshot">不可变基线 snapshot / Immutable baseline snapshot.</param>
/// <param name="DerivedSnapshot">不可变派生 snapshot，连续后端自行派生时可为 <c>null</c> / Immutable derived snapshot, or null when a continuous backend derives it itself.</param>
/// <param name="ConstraintId">候选原始约束 ID / Candidate original constraint ID.</param>
/// <param name="Kind">施加的操作 / Applied operation.</param>
/// <param name="Delta">RHS 请求的 delta / RHS delta for an RHS request.</param>
/// <param name="PerturbedRhs">可表示时的结果 RHS / Resulting RHS when representable.</param>
/// <param name="BaselineSolution">可用时的基线 incumbent / Baseline incumbent, when available.</param>
/// <param name="WarmStart">启用时使用的上一次可行解 / Previous feasible solution used as a warm start.</param>
public sealed record ConstraintPerturbationRequest(
    ConstraintProgrammingModelSnapshot BaselineSnapshot,
    ConstraintProgrammingModelSnapshot? DerivedSnapshot,
    ConstraintId ConstraintId,
    ConstraintPerturbationKind Kind,
    Flt64? Delta = null,
    Flt64? PerturbedRhs = null,
    ConstraintProgrammingSolution? BaselineSolution = null,
    ConstraintProgrammingSolution? WarmStart = null)
{
    /// <summary>期望 snapshot 名称的调用方兼容别名 / Compatibility alias for consumers expecting a derived snapshot named snapshot.</summary>
    public ConstraintProgrammingModelSnapshot? Snapshot => DerivedSnapshot;

    /// <summary>此请求是否删除候选约束 / Whether this request removes the candidate.</summary>
    public bool IsRemoval => Kind == ConstraintPerturbationKind.Removal;
}

/// <summary>一次扰动模型的后端结果 / Backend result for one perturbed model.</summary>
/// <param name="Status">求解结论 / Solve conclusion.</param>
/// <param name="ObjectiveValue">可用时的新最优目标值 / New optimal objective value, when available.</param>
/// <param name="Solution">可用时的新 incumbent / New incumbent, when available.</param>
/// <param name="SolveTime">后端求解耗时 / Backend solve duration.</param>
/// <param name="Message">保留用于诊断的后端详情 / Backend detail retained for diagnostics.</param>
public sealed record ConstraintPerturbationSolveResult(
    AnalysisStatus Status,
    Flt64? ObjectiveValue = null,
    ConstraintProgrammingSolution? Solution = null,
    TimeSpan? SolveTime = null,
    string? Message = null)
{
    /// <summary>校验结果本身成立 / Validate that the result is well formed.</summary>
    /// <returns>问题描述，合法时为 <c>null</c> / The problem, or <c>null</c> when well formed.</returns>
    public string? Validate() =>
        ObjectiveValue is { } objective
            && (double.IsNaN(objective.ToDouble()) || double.IsInfinity(objective.ToDouble()))
            ? "Perturbation objective must be finite"
            : null;
}

/// <summary>一个扰动观测点 / One observed perturbation point.</summary>
/// <param name="ConstraintId">候选原始约束 ID / Candidate original constraint ID.</param>
/// <param name="Kind">RHS 或删除操作 / RHS or removal operation.</param>
/// <param name="Delta">RHS delta，删除时为 <c>null</c> / RHS delta, null for removal.</param>
/// <param name="PerturbedRhs">结果 RHS，无法派生时为 <c>null</c> / Resulting RHS, null when not derivable.</param>
/// <param name="Status">后端求解状态 / Backend solve status.</param>
/// <param name="BaselineObjective">基线目标值 / Baseline objective.</param>
/// <param name="NewObjective">扰动后的目标值 / Perturbed objective.</param>
/// <param name="ObjectiveImprovement">按优化方向计算的改善量 / Improvement in the optimization direction.</param>
/// <param name="IntegerPatternChanged">incumbent 整数结构是否变化 / Whether the incumbent integer pattern changed.</param>
/// <param name="SolveTime">后端耗时 / Backend duration.</param>
/// <param name="Outcome">区分有效、无效与未知 / Outcome distinguishing effective, no-effect, and unknown.</param>
/// <param name="Message">诊断详情 / Diagnostic detail.</param>
public sealed record ConstraintPerturbationObservation(
    ConstraintId ConstraintId,
    ConstraintPerturbationKind Kind,
    Flt64? Delta,
    Flt64? PerturbedRhs,
    AnalysisStatus Status,
    Flt64? BaselineObjective,
    Flt64? NewObjective,
    Flt64? ObjectiveImprovement,
    bool? IntegerPatternChanged,
    TimeSpan? SolveTime,
    PerturbationOutcome Outcome,
    string? Message = null)
{
    /// <summary>目标差异兼容别名 / Compatibility alias for the objective difference.</summary>
    public Flt64? ObjectiveDifference => ObjectiveImprovement;

    /// <summary>
    /// 删除有效性兼容别名 / Compatibility alias for removal effectiveness.
    /// </summary>
    /// <remarks>
    /// **只有删除操作才有"删除是否有效"**：给一个 RHS 扰动返回 `false`，会被读成"扰动无效"，
    /// 而实际含义是"这个字段不适用于本次操作"。所以非删除操作返回 `null`。
    ///
    /// **Only a removal has a "removal effective" answer**: returning `false` for an RHS perturbation reads as "the perturbation did nothing", when the real meaning is
    /// "this field does not apply here". A non-removal therefore returns `null`.
    /// </remarks>
    public bool? RemovalEffective => Kind != ConstraintPerturbationKind.Removal
        ? null
        // **`Unknown` 必须给 `null`，不能给 `false`。** B994 的删除测试判据当场抓到这个：
        // 一次因预算耗尽而没跑成的删除测试，`Outcome` 是 `Unknown`，而这里返回 `false`——
        // **"没测"被写成了"删除无效"，正是本仓反复记录的那类失效。**
        //
        // **`Unknown` has to give `null`, not `false`.** B994's removal criterion caught this on the spot: a removal test skipped for budget reasons has `Outcome == Unknown`,
        // and this returned `false` — **"not measured" written down as "removal ineffective", the exact failure this repository keeps recording.**
        : Outcome switch {
            PerturbationOutcome.Effective => true,
            PerturbationOutcome.NoObservedEffect => false,
            _ => null,
        };
}

/// <summary>自适应探测汇总 / Summary of adaptive probing.</summary>
/// <param name="Outcome">自适应结论 / Adaptive conclusion.</param>
/// <param name="FirstEffectiveDelta">观察到时的首次有效 delta / First effective delta, if observed.</param>
/// <param name="LowerBound">阈值区间已证明无效的下端点 / Proven no-effect lower endpoint.</param>
/// <param name="UpperBound">阈值区间已证明有效的上端点 / Proven effective upper endpoint.</param>
/// <param name="Attempts">自适应观测数量 / Number of adaptive observations.</param>
public sealed record AdaptivePerturbationResult(
    AdaptivePerturbationOutcome Outcome,
    Flt64? FirstEffectiveDelta = null,
    Flt64? LowerBound = null,
    Flt64? UpperBound = null,
    int Attempts = 0)
{
    /// <summary>阈值下界兼容别名 / Compatibility alias for the threshold lower endpoint.</summary>
    public Flt64? ThresholdLower => LowerBound;

    /// <summary>阈值上界兼容别名 / Compatibility alias for the threshold upper endpoint.</summary>
    public Flt64? ThresholdUpper => UpperBound;
}

/// <summary>一个候选约束的扰动报告 / Report for one candidate constraint.</summary>
/// <param name="ConstraintId">候选原始约束 ID / Candidate original constraint ID.</param>
/// <param name="Status">聚合分析状态 / Aggregate analysis status.</param>
/// <param name="BaselineObjective">基线目标值 / Baseline objective.</param>
/// <param name="Observations">显式及自适应 RHS 观测 / Explicit and adaptive RHS observations.</param>
/// <param name="Adaptive">自适应策略汇总 / Adaptive summary.</param>
/// <param name="Removal">可选单约束删除观测 / Optional single-removal observation.</param>
/// <param name="Message">诊断详情 / Diagnostic detail.</param>
public sealed record ConstraintPerturbationReport(
    ConstraintId ConstraintId,
    AnalysisStatus Status,
    Flt64? BaselineObjective = null,
    IReadOnlyList<ConstraintPerturbationObservation>? Observations = null,
    AdaptivePerturbationResult? Adaptive = null,
    ConstraintPerturbationObservation? Removal = null,
    string? Message = null)
{
    /// <summary>观测列表，未给出时为空 / The observations, empty when absent.</summary>
    public IReadOnlyList<ConstraintPerturbationObservation> ObservationList =>
        Observations ?? Array.Empty<ConstraintPerturbationObservation>();

    /// <summary>RHS 观测兼容别名 / Compatibility alias for RHS observations.</summary>
    public IReadOnlyList<ConstraintPerturbationObservation> Perturbations => ObservationList;

    /// <summary>使用观测点术语的调用方兼容别名 / Compatibility alias for point terminology.</summary>
    public IReadOnlyList<ConstraintPerturbationObservation> Points => ObservationList;

    /// <summary>
    /// 是否至少有一个点证明全局改善 / Whether at least one point proved a global improvement.
    /// </summary>
    /// <remarks>
    /// **只数 `Effective`，不数 `Unknown`**：一次没有结论的求解不是改善。
    /// **Only `Effective` counts, never `Unknown`**: a solve that reached no conclusion is not an improvement.
    /// </remarks>
    public bool GloballyEffective =>
        ObservationList.Any(observation => observation.Outcome == PerturbationOutcome.Effective)
        || Removal?.Outcome == PerturbationOutcome.Effective;

    /// <summary>删除测试是否完成且证明改善 / Whether the removal test completed and proved an improvement.</summary>
    public bool? RemovalEffective => Removal?.RemovalEffective;
}
