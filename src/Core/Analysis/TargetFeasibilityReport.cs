#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 一个目标条件的可行性结果 / Feasibility result for one objective target.
/// </summary>
/// <remarks>
/// 与基线 `TargetFeasibilityAnalyzer.kt`（915 行）里的 `TargetFeasibilityReport` 对应。
///
/// 只搬报告，不搬分析器**：分析器第 673 行有一处真正的 `solver.solve(model, options)`，
/// 而它之前的 `ConstraintProgrammingSnapshotBuilder` 又要重建可变模型。**那部分需要真实 CP 后端，
/// 这里不搬，也不写空跑版本。**
///
/// **`Reachable` / `Unreachable` / `Unknown` / `Unsupported` 四个状态必须分开**：前两个是结论，
/// 后两个是"没结论"的两种原因。**把 `Unsupported` 当成 `Unreachable`，会让"换个后端就能解"看起来像"目标不可能达成"。**
///
/// Mirrors `TargetFeasibilityReport` from the baseline's `TargetFeasibilityAnalyzer.kt` (915 lines). **The report and not the analyzer is ported**: line 673 has a real
/// `solver.solve(model, options)` and the `ConstraintProgrammingSnapshotBuilder` before it rebuilds a mutable model. **That part needs a real CP backend, so it was neither ported
/// nor stubbed.** **The four statuses have to stay apart**: the first two are conclusions, the last two are two different reasons for having none. **Reading `Unsupported` as
/// `Unreachable` makes "another backend could solve this" look like "the target cannot be met".**
/// </remarks>
/// <param name="Target">请求的目标条件 / Requested target.</param>
/// <param name="EffectiveIntegerBound">CP 目标约束使用的精确整数 RHS / Exact integer RHS used by the CP target constraint.</param>
/// <param name="Source">稳定目标证据来源 / Stable target evidence source.</param>
/// <param name="Status">可达性结论 / Reachability conclusion.</param>
/// <param name="Solution">目标可达时的见证解 / Witness solution when target is reachable.</param>
/// <param name="ExactObjective">CP 输出中的精确目标值 / Exact objective value from the CP output.</param>
/// <param name="ObjectiveValue">兼容浮点目标值 / Compatibility float objective value.</param>
/// <param name="ProofStatus">目标结论的证明状态 / Proof status of the target conclusion.</param>
/// <param name="TerminationReason">后端终止原因 / Backend termination reason.</param>
/// <param name="SolveTime">后端求解耗时 / Backend solve duration.</param>
/// <param name="TargetFixed">目标是否已插入派生模型 / Whether the target was inserted into a derived model.</param>
/// <param name="Message">面向诊断的详情 / Human-readable diagnostic detail.</param>
public sealed record TargetFeasibilityReport(
    ObjectiveTarget Target,
    Int64? EffectiveIntegerBound = null,
    DiagnosticSource? Source = null,
    AnalysisStatus Status = AnalysisStatus.Unknown,
    ConstraintProgrammingSolution? Solution = null,
    Int64? ExactObjective = null,
    Flt64? ObjectiveValue = null,
    ProofStatus ProofStatus = ProofStatus.None,
    TerminationReason? TerminationReason = null,
    TimeSpan? SolveTime = null,
    bool TargetFixed = true,
    string? Message = null)
{
    /// <summary>稳定目标证据来源，缺省从目标条件派生 / Stable target evidence source, derived from the target when omitted.</summary>
    public DiagnosticSource EvidenceSource =>
        Source ?? new DiagnosticSource.ObjectiveTargetSource(Target);

    /// <summary>
    /// 可行性调用方使用的兼容别名 / Compatibility alias used by feasibility callers.
    /// </summary>
    /// <remarks>
    /// **只有 `Reachable` / `Unreachable` 给出布尔值**；`Unknown` 与 `Unsupported` 都给 `null`。
    /// 让它们各自返回 `false`，等于**把"没结论"报成"不可达"**。
    ///
    /// **Only `Reachable` and `Unreachable` produce a boolean**; `Unknown` and `Unsupported` both give `null`. Returning `false` for them would **report "no conclusion" as
    /// "unreachable".**
    /// </remarks>
    public bool? Reachable => Status switch
    {
        AnalysisStatus.Reachable => true,
        AnalysisStatus.Unreachable => false,
        _ => null,
    };

    /// <summary>
    /// 结论是否为已证明的目标结果 / Whether the conclusion is a proven target result.
    /// </summary>
    /// <remarks>
    /// **`Reachable` 不等于已证明**：一个求解器在时间上限内找到一个解，只说明目标**可能**可达。
    /// 证明与否由 `ProofStatus` 说，不由 `Status` 说。
    ///
    /// **`Reachable` is not the same as proven**: a solver that finds a solution within a time limit only shows the target **may** be reachable. Whether it is proven is said by
    /// `ProofStatus`, not by `Status`.
    /// </remarks>
    public bool IsProven => Status.IsProven() && ProofStatus != ProofStatus.None;
}
