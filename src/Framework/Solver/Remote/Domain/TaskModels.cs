#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

// 任务与载荷模型逐字段对照固定 Kotlin 12fdb587 的 TaskModels.kt；时长一律按毫秒整型编码。
// Task and payload models mirror the pinned Kotlin TaskModels.kt field by field; every duration is encoded as integer milliseconds.

/// <summary>任务复杂度 / Task complexity.</summary>
public enum TaskComplexity {
    /// <summary>简单任务 / Simple task</summary>
    Simple,
    /// <summary>复杂任务 / Complex task</summary>
    Complex
}

/// <summary>时间敏感度 / Time sensitivity.</summary>
public enum TimeSensitivity {
    /// <summary>实时任务 / Realtime task</summary>
    Realtime,
    /// <summary>非实时任务 / Non-realtime task</summary>
    NonRealtime
}

/// <summary>任务状态 / Task status.</summary>
public enum TaskStatus {
    /// <summary>已创建 / Created</summary>
    Created,
    /// <summary>已接受 / Accepted</summary>
    Accepted,
    /// <summary>已入队 / Queued</summary>
    Queued,
    /// <summary>正在分发 / Dispatching</summary>
    Dispatching,
    /// <summary>正在运行 / Running</summary>
    Running,
    /// <summary>已暂停 / Suspended</summary>
    Suspended,
    /// <summary>已完成 / Completed</summary>
    Completed,
    /// <summary>正在停止 / Stopping</summary>
    Stopping,
    /// <summary>已停止 / Stopped</summary>
    Stopped,
    /// <summary>已失败 / Failed</summary>
    Failed,
    /// <summary>等待预算释放 / Waiting for budget</summary>
    WaitingForBudget,
    /// <summary>未知；用于兼容服务端新增值 / Unknown; for forward-compatible decoding</summary>
    Unknown
}

/// <summary>切片状态 / Slice status.</summary>
public enum SliceStatus {
    /// <summary>已规划 / Planned</summary>
    Planned,
    /// <summary>正在运行 / Running</summary>
    Running,
    /// <summary>正在保存检查点 / Checkpointing</summary>
    Checkpointing,
    /// <summary>已暂停 / Suspended</summary>
    Suspended,
    /// <summary>已完成 / Completed</summary>
    Completed,
    /// <summary>已失败 / Failed</summary>
    Failed
}

/// <summary>任务元数据 / Task metadata.</summary>
/// <param name="SolverType">求解器类型偏好 / Solver type preference.</param>
/// <param name="TargetType">目标类型 / Target type.</param>
/// <param name="TimeLimitMs">时间限制毫秒 / Time limit in milliseconds.</param>
/// <param name="SolutionLimit">解数量限制 / Solution count limit.</param>
/// <param name="EstimatedVariableCount">预估变量数 / Estimated variable count.</param>
/// <param name="EstimatedConstraintCount">预估约束数 / Estimated constraint count.</param>
/// <param name="HistoricalRuntimeMs">历史运行时间毫秒 / Historical runtime in milliseconds.</param>
/// <param name="Metadata">扩展元数据 / Extension metadata.</param>
public sealed record TaskMeta(
    SolverTypeName? SolverType = null,
    TargetTypeName? TargetType = null,
    long? TimeLimitMs = null,
    int? SolutionLimit = null,
    int? EstimatedVariableCount = null,
    int? EstimatedConstraintCount = null,
    long? HistoricalRuntimeMs = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>规范化模型类型 / Normalized model type.</summary>
public enum NormalizedModelType {
    /// <summary>线性模型 / Linear model</summary>
    Linear,
    /// <summary>二次模型 / Quadratic model</summary>
    Quadratic,
    /// <summary>约束规划 snapshot / Constraint-programming snapshot</summary>
    Cp,
    /// <summary>未知模型 / Unknown model</summary>
    Unknown
}

/// <summary>
/// 模型数据 / Model data.
/// </summary>
/// <remarks>
/// 引用、内联线性、内联二次与原始字节四种模式互斥；<see cref="ModelType"/> 顺序推断。
/// Reference, inline linear, inline quadratic and raw-bytes modes are exclusive; <see cref="ModelType"/> infers in baseline order.
/// </remarks>
/// <param name="Ref">模型对象引用 / Model object reference.</param>
/// <param name="LinearModel">内联线性模型 / Inline linear model.</param>
/// <param name="QuadraticModel">内联二次模型 / Inline quadratic model.</param>
/// <param name="RawBytes">原始模型字节 / Raw model bytes.</param>
/// <param name="Format">原始字节格式 / Raw bytes format.</param>
public sealed record ModelData(
    ObjectRef? Ref = null,
    SerializedLinearModel? LinearModel = null,
    SerializedQuadraticModel? QuadraticModel = null,
    byte[]? RawBytes = null,
    string? Format = null) {
    /// <summary>是否引用模式 / Whether reference mode.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReference => Ref is not null;

    /// <summary>是否内联模式 / Whether inline mode.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInline => LinearModel is not null || QuadraticModel is not null || RawBytes is not null;

    /// <summary>模型类型 / Model type.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public NormalizedModelType ModelType => QuadraticModel is not null
        ? NormalizedModelType.Quadratic
        : LinearModel is not null
            ? NormalizedModelType.Linear
            : string.Equals(Format, "ospf-cp-snapshot-json", System.StringComparison.Ordinal)
                ? NormalizedModelType.Cp
                : NormalizedModelType.Unknown;

    /// <summary>创建引用模型数据 / Create reference model data.</summary>
    /// <param name="reference">对象引用 / Object reference.</param>
    /// <returns>模型数据 / Model data.</returns>
    public static ModelData Reference(ObjectRef reference) => new(Ref: reference);

    /// <summary>创建线性模型数据 / Create linear model data.</summary>
    /// <param name="model">线性模型 / Linear model.</param>
    /// <returns>模型数据 / Model data.</returns>
    public static ModelData Linear(SerializedLinearModel model) => new(LinearModel: model);

    /// <summary>创建二次模型数据 / Create quadratic model data.</summary>
    /// <param name="model">二次模型 / Quadratic model.</param>
    /// <returns>模型数据 / Model data.</returns>
    public static ModelData Quadratic(SerializedQuadraticModel model) => new(QuadraticModel: model);

    /// <summary>创建原始字节模型数据 / Create raw-bytes model data.</summary>
    /// <param name="bytes">字节内容 / Bytes.</param>
    /// <param name="format">字节格式 / Bytes format.</param>
    /// <returns>模型数据 / Model data.</returns>
    public static ModelData Raw(byte[] bytes, string format) => new(RawBytes: bytes, Format: format);
}

/// <summary>求解配置 / Solver config.</summary>
/// <param name="TimeLimitMs">时间限制毫秒 / Time limit in milliseconds.</param>
/// <param name="SolutionLimit">解数量限制 / Solution count limit.</param>
/// <param name="MipGapTolerance">MIP gap 容忍度 / MIP gap tolerance.</param>
/// <param name="Threads">线程数 / Thread count.</param>
/// <param name="SolverParams">求解器参数 / Solver params.</param>
public sealed record SolverConfig(
    long? TimeLimitMs = null,
    int? SolutionLimit = null,
    double? MipGapTolerance = null,
    int? Threads = null,
    IReadOnlyDictionary<string, string>? SolverParams = null);

/// <summary>
/// 远程任务提交请求 / Remote task submit request.
/// </summary>
/// <remarks>
/// 逐字段对照固定 Kotlin `RemoteTaskSubmitRequest`；`deadline` 以 <c>deadlineEpochMs</c> 整型 epoch
/// 毫秒编码，其余调度字段与 <see cref="SchedulingRequest"/> 同源。
/// RemoteTaskSubmitRequest: `deadline` is encoded as an integer
/// <c>deadlineEpochMs</c> epoch-millisecond value, and the remaining scheduling fields share
/// <see cref="SchedulingRequest"/> as their source.
/// </remarks>
/// <param name="PayloadRef">载荷对象路径 / Payload object path.</param>
/// <param name="RequestId">请求标识 / Request identifier.</param>
/// <param name="TenantId">租户标识 / Tenant identifier.</param>
/// <param name="Complexity">任务复杂度 / Task complexity.</param>
/// <param name="TimeSensitivity">时间敏感度 / Time sensitivity.</param>
/// <param name="Priority">优先级 / Priority.</param>
/// <param name="BudgetScope">预算范围 / Budget scope.</param>
/// <param name="BudgetLimit">预算上限 / Budget limit.</param>
/// <param name="DeadlineEpochMs">截止时间 epoch 毫秒 / Deadline as epoch milliseconds.</param>
/// <param name="Scheduling">完整调度请求 / Full scheduling request.</param>
public sealed record RemoteTaskSubmitRequest(
    ObjectPath PayloadRef,
    RequestId? RequestId = null,
    TenantId? TenantId = null,
    TaskComplexity? Complexity = null,
    TimeSensitivity? TimeSensitivity = null,
    int? Priority = null,
    BudgetScopeId? BudgetScope = null,
    double? BudgetLimit = null,
    long? DeadlineEpochMs = null,
    SchedulingRequest? Scheduling = null) {
    /// <summary>
    /// 由求解载荷派生提交请求 / Derive a submit request from a solve payload.
    /// </summary>
    /// <param name="payloadRef">载荷对象路径 / Payload object path.</param>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="requestId">请求标识 / Request identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <returns>提交请求 / Submit request.</returns>
    public static RemoteTaskSubmitRequest FromPayload(
        ObjectPath payloadRef,
        SolvePayload payload,
        RequestId? requestId = null,
        TenantId? tenantId = null) {
        ArgumentNullException.ThrowIfNull(payload);
        SchedulingRequest? scheduling = payload.Scheduling;
        return new RemoteTaskSubmitRequest(
            PayloadRef: payloadRef,
            RequestId: requestId,
            TenantId: tenantId,
            Complexity: scheduling?.Complexity,
            TimeSensitivity: scheduling?.TimeSensitivity,
            Priority: scheduling?.Priority,
            BudgetScope: scheduling?.BudgetScope,
            BudgetLimit: scheduling?.BudgetLimit,
            DeadlineEpochMs: scheduling?.DeadlineEpochMs,
            Scheduling: scheduling);
    }
}

/// <summary>远程任务提交响应 / Remote task submit response.</summary>
/// <param name="TaskId">任务标识 / Task identifier.</param>
/// <param name="Accepted">是否受理 / Whether accepted.</param>
/// <param name="Status">任务状态 / Task status.</param>
/// <param name="Message">响应消息 / Response message.</param>
public sealed record RemoteTaskSubmitResponse(
    string TaskId,
    bool Accepted,
    string Status,
    string Message) {
    /// <summary>状态是否为未知 / Whether the status is unknown.</summary>
    public bool IsUnknownStatus => !RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus _);
}

/// <summary>求解载荷 / Solve payload.</summary>
/// <param name="ModelData">模型数据 / Model data.</param>
/// <param name="ConfigRef">配置引用 / Config reference.</param>
/// <param name="Config">内联配置 / Inline config.</param>
/// <param name="SnapshotRef">快照引用 / Snapshot reference.</param>
/// <param name="TaskMeta">任务元数据 / Task metadata.</param>
/// <param name="Extension">扩展字段 / Extension fields.</param>
/// <param name="Scheduling">调度请求 / Scheduling request.</param>
public sealed record SolvePayload(
    ModelData ModelData,
    ObjectRef? ConfigRef = null,
    SolverConfig? Config = null,
    ObjectRef? SnapshotRef = null,
    TaskMeta? TaskMeta = null,
    IReadOnlyDictionary<string, string>? Extension = null,
    SchedulingRequest? Scheduling = null) {
    /// <summary>载荷携带的模型引用 / Model reference carried by the payload.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ObjectRef? ModelRef => ModelData.Ref;

    /// <summary>引用模式便捷构造 / Reference-mode convenience constructor.</summary>
    /// <param name="modelRef">模型对象引用 / Model object reference.</param>
    /// <param name="configRef">可选求解配置引用 / Optional solver configuration reference.</param>
    /// <param name="snapshotRef">可选快照引用 / Optional snapshot reference.</param>
    /// <param name="taskMeta">任务元数据 / Task metadata.</param>
    /// <param name="extension">扩展字段 / Extension fields.</param>
    /// <param name="scheduling">调度要求 / Scheduling requirements.</param>
    /// <returns>求解载荷 / Solve payload.</returns>
    public static SolvePayload FromReference(
        ObjectRef modelRef,
        ObjectRef? configRef = null,
        ObjectRef? snapshotRef = null,
        TaskMeta? taskMeta = null,
        IReadOnlyDictionary<string, string>? extension = null,
        SchedulingRequest? scheduling = null) =>
        new(
            ModelData: ModelData.Reference(modelRef),
            ConfigRef: configRef,
            SnapshotRef: snapshotRef,
            TaskMeta: taskMeta,
            Extension: extension,
            Scheduling: scheduling);

    /// <summary>内联线性模型便捷构造 / Inline-linear convenience constructor.</summary>
    /// <param name="linearModel">内联序列化线性模型 / Inline serialized linear model.</param>
    /// <param name="config">可选内联求解配置 / Optional inline solver configuration.</param>
    /// <param name="taskMeta">任务元数据 / Task metadata.</param>
    /// <param name="extension">扩展字段 / Extension fields.</param>
    /// <param name="scheduling">调度要求 / Scheduling requirements.</param>
    /// <returns>求解载荷 / Solve payload.</returns>
    public static SolvePayload FromLinear(
        SerializedLinearModel linearModel,
        SolverConfig? config = null,
        TaskMeta? taskMeta = null,
        IReadOnlyDictionary<string, string>? extension = null,
        SchedulingRequest? scheduling = null) =>
        new(
            ModelData: ModelData.Linear(linearModel),
            Config: config,
            TaskMeta: taskMeta,
            Extension: extension,
            Scheduling: scheduling);
}
