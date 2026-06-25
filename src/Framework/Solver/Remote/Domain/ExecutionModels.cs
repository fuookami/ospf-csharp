#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;

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
public sealed record SliceResult(
    bool Completed,
    bool Feasible,
    Flt64? ObjectiveValue,
    Flt64? Gap,
    TimeSpan Elapsed,
    string? Message = null);

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
