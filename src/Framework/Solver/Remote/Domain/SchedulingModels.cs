#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

// 调度模型逐字段对照固定 Kotlin 12fdb587 的 SchedulingModels.kt；时长一律按毫秒整型编码。
// Scheduling models mirror the pinned Kotlin SchedulingModels.kt field by field; every duration is encoded as integer milliseconds.

/// <summary>调度计费估算 / Estimates used for scheduler accounting.</summary>
/// <param name="EstimatedRuntimeMs">预估运行时长毫秒 / Estimated runtime in milliseconds.</param>
/// <param name="EstimatedCheckpointMs">预估检查点时长毫秒 / Estimated checkpoint duration in milliseconds.</param>
/// <param name="EstimatedQueueWaitMs">预估排队等待毫秒 / Estimated queue-wait duration in milliseconds.</param>
/// <param name="Cost">预估成本 / Estimated cost.</param>
public sealed record SchedulingEstimate(
    long? EstimatedRuntimeMs = null,
    long? EstimatedCheckpointMs = null,
    long? EstimatedQueueWaitMs = null,
    double? Cost = null);

/// <summary>与求解器无关的质量目标 / Solver-neutral quality target.</summary>
/// <param name="MaxGap">最大允许间隙 / Maximum allowed gap.</param>
/// <param name="ObjectiveLimit">目标值限制 / Objective limit.</param>
/// <param name="RequireFeasible">是否要求可行解 / Whether a feasible solution is required.</param>
/// <param name="RequireOptimal">是否要求最优解 / Whether an optimal solution is required.</param>
/// <param name="Metadata">扩展元数据 / Extension metadata.</param>
public sealed record QualityTarget(
    double? MaxGap = null,
    double? ObjectiveLimit = null,
    bool? RequireFeasible = null,
    bool? RequireOptimal = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>附加到求解载荷的调度要求 / Optional scheduling requirements attached to a solve payload.</summary>
/// <param name="Complexity">任务复杂度 / Task complexity.</param>
/// <param name="TimeSensitivity">时间敏感度 / Time sensitivity.</param>
/// <param name="Priority">优先级 / Priority.</param>
/// <param name="DeadlineEpochMs">截止时间 epoch 毫秒 / Deadline as epoch milliseconds.</param>
/// <param name="BudgetScope">预算范围 / Budget scope.</param>
/// <param name="BudgetLimit">预算上限 / Budget limit.</param>
/// <param name="Estimate">调度估算 / Scheduling estimate.</param>
/// <param name="ModelFingerprint">模型指纹 / Model fingerprint.</param>
/// <param name="ModelFingerprintSchema">模型指纹 schema / Model fingerprint schema.</param>
/// <param name="CheckpointRef">检查点引用 / Checkpoint reference.</param>
/// <param name="IncumbentRef">incumbent 引用 / Incumbent reference.</param>
/// <param name="QualityTarget">质量目标 / Quality target.</param>
/// <param name="PreemptionMode">抢占模式 / Preemption mode.</param>
/// <param name="ResumeMode">恢复模式 / Resume mode.</param>
/// <param name="Metadata">扩展元数据 / Extension metadata.</param>
public sealed record SchedulingRequest(
    TaskComplexity? Complexity = null,
    TimeSensitivity? TimeSensitivity = null,
    int? Priority = null,
    long? DeadlineEpochMs = null,
    BudgetScopeId? BudgetScope = null,
    double? BudgetLimit = null,
    SchedulingEstimate? Estimate = null,
    string? ModelFingerprint = null,
    string? ModelFingerprintSchema = null,
    ObjectRef? CheckpointRef = null,
    ObjectRef? IncumbentRef = null,
    QualityTarget? QualityTarget = null,
    PreemptionMode? PreemptionMode = null,
    ResumeMode? ResumeMode = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>随切片或最终结果返回的有效调度信息 / Effective scheduling information returned with a slice or final result.</summary>
/// <param name="DispatchId">分发操作标识 / Dispatch operation identifier.</param>
/// <param name="TaskId">任务标识 / Task identifier.</param>
/// <param name="SliceId">切片标识 / Slice identifier.</param>
/// <param name="NodeId">节点标识 / Node identifier.</param>
/// <param name="Priority">优先级 / Priority.</param>
/// <param name="DeadlineEpochMs">截止时间 epoch 毫秒 / Deadline as epoch milliseconds.</param>
/// <param name="BudgetScope">预算范围 / Budget scope.</param>
/// <param name="BudgetLimit">预算上限 / Budget limit.</param>
/// <param name="QuantumMs">调度时间片毫秒 / Scheduling quantum in milliseconds.</param>
/// <param name="QueueWaitMs">排队等待毫秒 / Queue-wait duration in milliseconds.</param>
/// <param name="Estimate">调度估算 / Scheduling estimate.</param>
/// <param name="ModelFingerprint">模型指纹 / Model fingerprint.</param>
/// <param name="ModelFingerprintSchema">模型指纹 schema / Model fingerprint schema.</param>
/// <param name="CheckpointRef">检查点引用 / Checkpoint reference.</param>
/// <param name="IncumbentRef">incumbent 引用 / Incumbent reference.</param>
/// <param name="QualityTarget">质量目标 / Quality target.</param>
/// <param name="PreemptionMode">抢占模式 / Preemption mode.</param>
/// <param name="ResumeMode">恢复模式 / Resume mode.</param>
/// <param name="Outcome">切片结果 / Slice outcome.</param>
/// <param name="Reason">调度原因 / Scheduling reason.</param>
/// <param name="Metadata">扩展元数据 / Extension metadata.</param>
public sealed record SchedulingDecision(
    DispatchId? DispatchId = null,
    TaskId? TaskId = null,
    SliceId? SliceId = null,
    NodeId? NodeId = null,
    int? Priority = null,
    long? DeadlineEpochMs = null,
    BudgetScopeId? BudgetScope = null,
    double? BudgetLimit = null,
    long? QuantumMs = null,
    long? QueueWaitMs = null,
    SchedulingEstimate? Estimate = null,
    string? ModelFingerprint = null,
    string? ModelFingerprintSchema = null,
    ObjectRef? CheckpointRef = null,
    ObjectRef? IncumbentRef = null,
    QualityTarget? QualityTarget = null,
    PreemptionMode? PreemptionMode = null,
    ResumeMode? ResumeMode = null,
    SliceOutcome? Outcome = null,
    string? Reason = null,
    IReadOnlyDictionary<string, string>? Metadata = null) {
    /// <summary>
    /// 补全缺失的调度字段 / Fill in missing scheduling fields.
    /// </summary>
    /// <remarks>
    /// ：只补空字段，绝不覆盖服务端或 artifact 已经给出的值；未知语义下结果强制为
    /// <c>UNKNOWN</c>，即使本对象原本宣称已完成。
    /// only empty fields are filled and values already supplied by the server or
    /// the artifact are never overwritten; under unknown semantics the outcome is forced to
    /// <c>UNKNOWN</c> even if this object claimed completion.
    /// </remarks>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="checkpointRef">检查点引用 / Checkpoint reference.</param>
    /// <param name="incumbentRef">incumbent 引用 / Incumbent reference.</param>
    /// <param name="modelFingerprint">模型指纹 / Model fingerprint.</param>
    /// <param name="outcome">有效切片结果 / Effective slice outcome.</param>
    /// <param name="unknownSemantics">是否未知语义 / Whether the semantics are unknown.</param>
    /// <returns>补全后的调度决策 / The completed decision.</returns>
    public SchedulingDecision Complete(
        TaskId? taskId = null,
        SliceId? sliceId = null,
        NodeId? nodeId = null,
        ObjectRef? checkpointRef = null,
        ObjectRef? incumbentRef = null,
        string? modelFingerprint = null,
        SliceOutcome? outcome = null,
        bool unknownSemantics = false) =>
        this with {
            TaskId = TaskId ?? taskId,
            SliceId = SliceId ?? sliceId,
            NodeId = NodeId ?? nodeId,
            CheckpointRef = CheckpointRef ?? checkpointRef,
            IncumbentRef = IncumbentRef ?? incumbentRef,
            ModelFingerprint = ModelFingerprint ?? modelFingerprint,
            Outcome = unknownSemantics ? SliceOutcome.Unknown : Outcome ?? outcome,
        };
}

/// <summary>服务端能力与协议版本摘要 / Server capability and protocol version summary.</summary>
/// <param name="SchemaVersion">能力摘要 schema 版本 / Capability summary schema version.</param>
/// <param name="ProtocolVersions">服务端支持的远程协议版本 / Remote protocol versions supported by the server.</param>
/// <param name="SupportedModelTypes">服务端可调度的模型类型 / Model types currently schedulable by the server.</param>
/// <param name="SupportsPortableCheckpoint">是否支持 portable checkpoint / Whether portable checkpoints are supported.</param>
/// <param name="SupportsNativeCheckpoint">是否支持原生搜索状态恢复 / Whether native search-state resume is supported.</param>
public sealed record RemoteSolverCapabilities(
    string SchemaVersion = "1.0",
    IReadOnlyList<string>? ProtocolVersions = null,
    IReadOnlyList<string>? SupportedModelTypes = null,
    bool SupportsPortableCheckpoint = false,
    bool SupportsNativeCheckpoint = false) {
    /// <summary>解码能力摘要 / Decode a capability summary.</summary>
    /// <param name="json">wire JSON / Wire JSON.</param>
    /// <returns>能力摘要或结构化错误 / Capability summary or a structured error.</returns>
    public static Result<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> Decode(string json) =>
        RemoteWireJson.DecodeCapabilities(json);

    /// <summary>编码为 wire JSON / Encode to wire JSON.</summary>
    /// <returns>wire JSON / Wire JSON.</returns>
    public string Encode() => RemoteWireJson.EncodeCapabilities(this);
}
