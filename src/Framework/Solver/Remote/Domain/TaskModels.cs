#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>
/// 任务复杂度 / Task complexity.
/// </summary>
public enum TaskComplexity {
    /// <summary>低 / Low</summary>
    Low,
    /// <summary>中 / Medium</summary>
    Medium,
    /// <summary>高 / High</summary>
    High
}

/// <summary>
/// 时间敏感度 / Time sensitivity.
/// </summary>
public enum TimeSensitivity {
    /// <summary>低 / Low</summary>
    Low,
    /// <summary>中 / Medium</summary>
    Medium,
    /// <summary>高 / High</summary>
    High,
    /// <summary>关键 / Critical</summary>
    Critical
}

/// <summary>
/// 任务状态 / Task status.
/// </summary>
public enum TaskStatus {
    /// <summary>待处理 / Pending</summary>
    Pending,
    /// <summary>运行中 / Running</summary>
    Running,
    /// <summary>已完成 / Completed</summary>
    Completed,
    /// <summary>已取消 / Cancelled</summary>
    Cancelled,
    /// <summary>失败 / Failed</summary>
    Failed
}

/// <summary>
/// 切片状态 / Slice status.
/// </summary>
public enum SliceStatus {
    /// <summary>待处理 / Pending</summary>
    Pending,
    /// <summary>运行中 / Running</summary>
    Running,
    /// <summary>已完成 / Completed</summary>
    Completed,
    /// <summary>超时 / Timed out</summary>
    TimedOut
}

/// <summary>
/// 任务元数据 / Task metadata.
/// </summary>
public sealed record TaskMeta(
    TaskId TaskId,
    TenantId TenantId,
    NodeId NodeId,
    SolverType SolverType,
    TaskComplexity Complexity,
    TimeSensitivity TimeSensitivity,
    TaskStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt = null,
    DateTime? CompletedAt = null);

/// <summary>
/// 模型数据 / Model data.
/// </summary>
public sealed record ModelData(
    string? Reference = null,
    string? Inline = null,
    byte[]? Raw = null);

/// <summary>
/// 求解器配置 / Solver configuration.
/// </summary>
public sealed record SolverConfig(
    SolverTypeName SolverType,
    Dictionary<string, string>? Parameters = null);

/// <summary>
/// 求解载荷 / Solve payload.
/// </summary>
public sealed record SolvePayload(
    ModelData Model,
    SolverConfig Config,
    ObjectRef? SnapshotRef = null);
