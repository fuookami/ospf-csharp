#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>
/// 求解器类型 / Solver type.
/// </summary>
public enum SolverType {
    /// <summary>线性 / Linear</summary>
    Linear,
    /// <summary>二次 / Quadratic</summary>
    Quadratic,
    /// <summary>混合整数 / Mixed integer</summary>
    MixedInteger
}

/// <summary>
/// 执行句柄 / Execution handle.
/// </summary>
public sealed record ExecutionHandle(
    HandleId HandleId,
    TaskId TaskId,
    SliceId SliceId,
    NodeId NodeId,
    TenantId TenantId);

/// <summary>
/// 切片结果 / Slice result.
/// </summary>
/// <remarks>
/// 字段按 `toSliceResult` 的合并结果对齐：结果 artifact 优先，其次任务视图，最后按状态与
/// 可行性回退。未知语义下问题结论／解存在性／证明状态一律为 `UNKNOWN`。
/// Fields follow the `toSliceResult` merge: the result artifact wins, then the task view,
/// then status and feasibility fallbacks. Under unknown semantics the problem status, solution presence
/// and proof status are all `UNKNOWN`.
/// </remarks>
/// <param name="Completed">是否终态 / Whether terminal.</param>
/// <param name="Feasible">是否可行 / Whether feasible.</param>
/// <param name="ObjectiveValue">目标值 / Objective value.</param>
/// <param name="Gap">间隙 / Gap.</param>
/// <param name="Elapsed">已耗时 / Elapsed time.</param>
/// <param name="Message">结果消息 / Result message.</param>
/// <param name="Status">任务状态 / Task status.</param>
/// <param name="Outcome">切片结果 / Slice outcome.</param>
/// <param name="RunId">求解运行标识 / Solve run identifier.</param>
/// <param name="AttemptId">求解尝试标识 / Solve attempt identifier.</param>
/// <param name="Scheduling">有效调度信息 / Effective scheduling decision.</param>
/// <param name="ObjectiveValueInt64">精确 Int64 目标值 / Exact Int64 objective.</param>
/// <param name="ProblemStatus">问题结论 / Problem status.</param>
/// <param name="TerminationReason">终止原因 / Termination reason.</param>
/// <param name="SolutionPresence">解存在性 / Solution presence.</param>
/// <param name="ProofStatus">证明状态 / Proof status.</param>
/// <param name="CheckpointRef">检查点引用 / Checkpoint reference.</param>
/// <param name="IncumbentRef">incumbent 引用 / Incumbent reference.</param>
/// <param name="ResultRef">结果引用 / Result reference.</param>
/// <param name="ArtifactDigest">结果 artifact 摘要 / Result artifact digest.</param>
/// <param name="BestBound">最优界 / Best bound.</param>
/// <param name="Statistics">统计 / Statistics.</param>
/// <param name="Diagnostics">诊断 / Diagnostics.</param>
/// <param name="SchemaVersion">结果 schema 版本 / Result schema version.</param>
/// <param name="Fingerprints">身份指纹 / Identity fingerprints.</param>
/// <param name="FingerprintSchemas">指纹 schema / Fingerprint schemas.</param>
/// <param name="Provenance">执行来源 / Execution provenance.</param>
/// <param name="SolveDiagnostics">解析后的诊断 / Parsed diagnostics.</param>
public sealed record SliceResult(
    bool Completed,
    bool Feasible,
    Flt64? ObjectiveValue,
    Flt64? Gap,
    TimeSpan Elapsed,
    string? Message = null,
    TaskStatus Status = TaskStatus.Unknown,
    SliceOutcome? Outcome = null,
    string? RunId = null,
    string? AttemptId = null,
    SchedulingDecision? Scheduling = null,
    long? ObjectiveValueInt64 = null,
    RemoteProblemStatus? ProblemStatus = null,
    RemoteTerminationReason? TerminationReason = null,
    RemoteSolutionPresence? SolutionPresence = null,
    RemoteProofStatus? ProofStatus = null,
    ObjectRef? CheckpointRef = null,
    ObjectRef? IncumbentRef = null,
    ObjectRef? ResultRef = null,
    string? ArtifactDigest = null,
    Flt64? BestBound = null,
    IReadOnlyDictionary<string, string>? Statistics = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null,
    string SchemaVersion = "1.0",
    IReadOnlyDictionary<string, string>? Fingerprints = null,
    IReadOnlyDictionary<string, string>? FingerprintSchemas = null,
    IReadOnlyDictionary<string, string>? Provenance = null,
    Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>? SolveDiagnostics = null);

/// <summary>
/// 求解结果 / Solve result.
/// </summary>
public sealed record SolveResult(
    bool Feasible,
    bool Optimal,
    Flt64? ObjectiveValue,
    Flt64? Gap,
    TimeSpan Elapsed,
    ObjectRef? CheckpointRef = null,
    string? Message = null);
