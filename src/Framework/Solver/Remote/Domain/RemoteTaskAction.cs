#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

/// <summary>
/// 任务操作的后端来源信息 / Backend provenance reported by a task action.
/// </summary>
/// <param name="SolverId">求解器标识 / Solver identifier.</param>
/// <param name="BackendName">后端名称 / Backend name.</param>
/// <param name="BackendVersion">后端版本 / Backend version.</param>
/// <param name="PluginVersion">插件版本 / Plugin version.</param>
/// <param name="RequestedConfiguration">请求配置 / Requested configuration.</param>
/// <param name="EffectiveConfiguration">生效配置 / Effective configuration.</param>
/// <param name="ThreadCount">线程数 / Thread count.</param>
/// <param name="RandomSeed">随机种子 / Random seed.</param>
/// <param name="Deterministic">是否确定性 / Whether deterministic.</param>
/// <param name="EnvironmentSummary">环境摘要 / Environment summary.</param>
/// <remarks>
/// <c>RemoteTaskView.provenance</c> 是 <c>Map&lt;String, String&gt;</c>，而
/// <c>RemoteTaskAction.provenance</c> 是**本类型**——两者同名但形状不同。因此本类型刻意**不**挂在
/// <see cref="RemoteTaskActionView"/> 上：视图按 Map 形状解析（它对应 <c>GET /api/v1/tasks/{taskId}</c>），
/// 本类型留给动作响应，避免同一个 JSON 名被两个不同形状抢用。
/// <c>RemoteTaskView.provenance</c> is a <c>Map&lt;String, String&gt;</c> while
/// <c>RemoteTaskAction.provenance</c> is **this type** — same name, different shape. This type is
/// therefore deliberately not a member of <see cref="RemoteTaskActionView"/>: the view decodes the map
/// shape (it corresponds to <c>GET /api/v1/tasks/{taskId}</c>) and this type is reserved for action
/// responses, so one JSON name is never claimed by two shapes.
/// </remarks>
public sealed record RemoteTaskActionProvenance(
    string SolverId = "unknown",
    string BackendName = "unknown",
    string? BackendVersion = null,
    string? PluginVersion = null,
    IReadOnlyDictionary<string, string>? RequestedConfiguration = null,
    IReadOnlyDictionary<string, string>? EffectiveConfiguration = null,
    int? ThreadCount = null,
    long? RandomSeed = null,
    bool? Deterministic = null,
    IReadOnlyDictionary<string, string>? EnvironmentSummary = null);

/// <summary>
/// 任务操作的取消事实 / Cancellation record reported by a task action.
/// </summary>
/// <remarks>
/// 未知的 <paramref name="Origin"/> 落到 <c>CancellationSource.Other</c>，且此时**必须**
/// 保留原始文本作为 <c>WireOrigin</c>——否则 <c>external</c> 与 <c>backend</c> 会在写回时都变成
/// <c>Other</c> 的规范代码而失真。
/// an unknown <paramref name="Origin"/> lands in <c>CancellationSource.Other</c>,
/// and the raw text **must** then be preserved as <c>WireOrigin</c> — otherwise <c>external</c> and
/// <c>backend</c> would both be written back as <c>Other</c>'s canonical code and lose fidelity.
/// </remarks>
/// <param name="Origin">来源 wire 代码 / Origin wire code.</param>
/// <param name="RequestedAtEpochMs">请求时刻的 epoch 毫秒 / Request time in epoch milliseconds.</param>
/// <param name="Reason">取消原因 / Cancellation reason.</param>
public sealed record RemoteTaskActionCancellation(
    string Origin,
    long RequestedAtEpochMs,
    string? Reason = null) {
    /// <summary>
    /// 转为核心取消事实 / Convert to a core cancellation record.
    /// </summary>
    /// <returns>核心取消事实 / Core cancellation record.</returns>
    public Fuookami.Ospf.Core.Solver.Report.CancellationRecord ToCancellationRecord() {
        Fuookami.Ospf.Core.Solver.Report.CancellationSource source =
            Fuookami.Ospf.Core.Solver.Report.CancellationSourceExtensions.FromWireCode(Origin);
        return new Fuookami.Ospf.Core.Solver.Report.CancellationRecord(
            Source: source,
            RequestedAt: DateTimeOffset.FromUnixTimeMilliseconds(RequestedAtEpochMs),
            Reason: Reason,
            // 只有兜底变体需要保留原始文本；否则 backend／future 会退化成 Other 的代码。
            // Only the catch-all variant keeps the raw text; otherwise backend/future degrade to Other's code.
            WireOrigin: source == Fuookami.Ospf.Core.Solver.Report.CancellationSource.Other
                ? Origin
                : null);
    }

    /// <summary>
    /// 由核心取消事实构造 wire 形状 / Build the wire shape from a core cancellation record.
    /// </summary>
    /// <param name="record">核心取消事实 / Core cancellation record.</param>
    /// <returns>wire 形状 / Wire shape.</returns>
    public static RemoteTaskActionCancellation FromCancellationRecord(
        Fuookami.Ospf.Core.Solver.Report.CancellationRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        return new RemoteTaskActionCancellation(
            Origin: record.WireOrigin ??
                Fuookami.Ospf.Core.Solver.Report.CancellationSourceExtensions.ToWireCode(record.Source),
            RequestedAtEpochMs: record.RequestedAt.ToUnixTimeMilliseconds(),
            Reason: record.Reason);
    }
}

/// <summary>
/// 远程任务动作响应 / Remote task action response.
/// </summary>
/// <remarks>
/// 只解析恢复/停止身份校验与调度透传所需的字段；其余字段（统计、诊断、取消链、
/// 祖先 provenance 等）尚未映射，不得据此认为已完整对齐。
/// Only the fields needed for resume/stop identity validation and scheduling propagation are mapped; the
/// remaining action fields (statistics, diagnostics, cancellation chain, ancestor provenance, …)
/// are not mapped yet, so this is not full parity.
/// </remarks>
/// <param name="TaskId">任务标识 / Task identifier.</param>
/// <param name="SliceId">切片标识 / Slice identifier.</param>
/// <param name="TenantId">租户标识 / Tenant identifier.</param>
/// <param name="Accepted">是否受理 / Whether accepted.</param>
/// <param name="Status">任务状态 / Task status.</param>
/// <param name="Message">响应消息 / Response message.</param>
/// <param name="RunId">求解运行标识 / Solve run identifier.</param>
/// <param name="AttemptId">求解尝试标识 / Solve attempt identifier.</param>
/// <param name="ModelFingerprint">模型指纹 / Model fingerprint.</param>
/// <param name="ConfigurationFingerprint">配置指纹 / Configuration fingerprint.</param>
/// <param name="SolverFingerprint">求解器指纹 / Solver fingerprint.</param>
/// <param name="Scheduling">有效调度信息 / Effective scheduling decision.</param>
/// <param name="Outcome">切片结果 / Slice outcome.</param>
/// <param name="TerminationReason">终止原因 / Termination reason.</param>
/// <param name="SolutionPresence">解存在性 / Solution presence.</param>
/// <param name="ProofStatus">证明状态 / Proof status.</param>
/// <param name="ProblemStatus">问题结论 / Problem status.</param>
/// <param name="ObjectiveValue">目标值 / Objective value.</param>
/// <param name="ObjectiveValueInt64">精确 Int64 目标值 / Exact Int64 objective.</param>
/// <param name="Gap">间隙 / Gap.</param>
/// <param name="ElapsedMs">已耗时毫秒 / Elapsed time in milliseconds.</param>
/// <param name="BestBound">最优界 / Best bound.</param>
/// <param name="Bound">界 / Bound.</param>
/// <param name="LatestCheckpointRef">最新检查点引用 / Latest checkpoint reference.</param>
/// <param name="LatestResultRef">最新结果引用 / Latest result reference.</param>
/// <param name="IncumbentRef">incumbent 引用 / Incumbent reference.</param>
/// <param name="ArtifactDigest">结果 artifact 摘要 / Result artifact digest.</param>
/// <param name="Statistics">统计 / Statistics.</param>
/// <param name="Diagnostics">诊断 / Diagnostics.</param>
/// <param name="SchemaVersion">结果 schema 版本 / Result schema version.</param>
/// <param name="Fingerprints">显式指纹 / Explicit fingerprints.</param>
/// <param name="FingerprintSchemas">指纹 schema / Fingerprint schemas.</param>
/// <param name="ModelFingerprintSchema">模型指纹 schema / Model fingerprint schema.</param>
/// <param name="ConfigurationFingerprintSchema">配置指纹 schema / Configuration fingerprint schema.</param>
/// <param name="SolverFingerprintSchema">求解器指纹 schema / Solver fingerprint schema.</param>
/// <param name="Provenance">执行来源 / Execution provenance.</param>
/// <param name="CancellationChain">取消链 / Cancellation chain.</param>
public sealed record RemoteTaskActionView(
    string? TaskId = null,
    string? SliceId = null,
    string? TenantId = null,
    bool Accepted = false,
    string? Status = null,
    string? Message = null,
    string? RunId = null,
    string? AttemptId = null,
    string? ModelFingerprint = null,
    string? ConfigurationFingerprint = null,
    string? SolverFingerprint = null,
    SchedulingDecision? Scheduling = null,
    SliceOutcome? Outcome = null,
    RemoteTerminationReason? TerminationReason = null,
    RemoteSolutionPresence? SolutionPresence = null,
    RemoteProofStatus? ProofStatus = null,
    RemoteProblemStatus? ProblemStatus = null,
    double? ObjectiveValue = null,
    long? ObjectiveValueInt64 = null,
    double? Gap = null,
    long? ElapsedMs = null,
    double? BestBound = null,
    double? Bound = null,
    ObjectRef? LatestCheckpointRef = null,
    ObjectRef? LatestResultRef = null,
    ObjectRef? IncumbentRef = null,
    string? ArtifactDigest = null,
    IReadOnlyDictionary<string, string>? Statistics = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null,
    string SchemaVersion = "1.0",
    IReadOnlyDictionary<string, string>? Fingerprints = null,
    IReadOnlyDictionary<string, string>? FingerprintSchemas = null,
    string? ModelFingerprintSchema = null,
    string? ConfigurationFingerprintSchema = null,
    string? SolverFingerprintSchema = null,
    IReadOnlyDictionary<string, string>? Provenance = null,
    IReadOnlyList<RemoteTaskActionCancellation>? CancellationChain = null) {
    /// <summary>状态是否为已知值 / Whether the status is a known value.</summary>
    public bool HasKnownStatus =>
        RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus _);

    /// <summary>
    /// 是否携带未知结果语义 / Whether any outcome semantic is unknown.
    /// </summary>
    /// <remarks>
    /// 状态、切片结果、终止原因、解存在性、证明状态或调度结果中任一为 <c>UNKNOWN</c>，
    /// 就视为语义未知；此时**不得**再推断可行性或最优性，只允许把结果标记为未知。
    /// if the status, slice outcome, termination reason, solution presence, proof
    /// status or scheduling outcome is <c>UNKNOWN</c>, the semantics are unknown and feasibility or
    /// optimality must **not** be inferred — the result may only be reported as unknown.
    /// </remarks>
    public bool HasUnknownOutcomeSemantics =>
        !HasKnownStatus ||
        Outcome == SliceOutcome.Unknown ||
        TerminationReason == RemoteTerminationReason.Unknown ||
        SolutionPresence == RemoteSolutionPresence.Unknown ||
        ProofStatus == RemoteProofStatus.Unknown ||
        Scheduling?.Outcome == SliceOutcome.Unknown;

    /// <summary>
    /// 推断可行性 / Infer feasibility.
    /// </summary>
    /// <remarks>
    /// 未知语义下恒为 false；否则 <c>INCUMBENT</c>／<c>OPTIMAL</c> 解存在性即视为可行。
    /// Always false under unknown semantics; otherwise an <c>INCUMBENT</c>/<c>OPTIMAL</c> presence
    /// counts as feasible.
    /// </remarks>
    public bool InferredFeasible =>
        !HasUnknownOutcomeSemantics &&
        SolutionPresence is RemoteSolutionPresence.Incumbent or RemoteSolutionPresence.Optimal;

    /// <summary>
    /// 推断切片结果 / Infer the slice outcome.
    /// </summary>
    /// <remarks>
    /// 未知语义恒为 <see cref="SliceOutcome.Unknown"/>；否则顺序取显式结果，再按状态与可行性回退。
    /// Under unknown semantics this is always <see cref="SliceOutcome.Unknown"/>; otherwise the explicit
    /// outcome wins, then the status and feasibility decide in that order.
    /// </remarks>
    public SliceOutcome InferredOutcome {
        get {
            if (HasUnknownOutcomeSemantics) {
                return SliceOutcome.Unknown;
            }
            if (Outcome is { } explicitOutcome) {
                return explicitOutcome;
            }
            RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus status);
            return status switch {
                TaskStatus.Stopped => SliceOutcome.Cancelled,
                TaskStatus.Failed => SliceOutcome.Failed,
                TaskStatus.Completed => SliceOutcome.Completed,
                _ => InferredFeasible ? SliceOutcome.Resumable : SliceOutcome.Preempted,
            };
        }
    }

    /// <summary>
    /// 推断终止原因 / Infer the termination reason.
    /// </summary>
    /// <remarks>
    /// 回退链：未知语义 → <c>UNKNOWN</c>；否则显式原因优先；<c>STOPPED</c> →
    /// <c>CANCELLED</c>；<c>FAILED</c> → 取非 <c>COMPLETED</c> 的显式原因，缺省 <c>BACKEND_FAILURE</c>；
    /// 其余情况缺失时，若结果为 <c>COMPLETED</c> 则 <c>COMPLETED</c>，否则 <c>TIME_LIMIT</c>。
    /// Fallback chain: unknown semantics yield <c>UNKNOWN</c>; otherwise the explicit reason
    /// wins; <c>STOPPED</c> maps to <c>CANCELLED</c>; <c>FAILED</c> takes a non-<c>COMPLETED</c> explicit
    /// reason and defaults to <c>BACKEND_FAILURE</c>; anything else defaults to <c>COMPLETED</c> when the
    /// outcome is completed and to <c>TIME_LIMIT</c> otherwise.
    /// </remarks>
    public RemoteTerminationReason InferredTermination {
        get {
            if (HasUnknownOutcomeSemantics) {
                return RemoteTerminationReason.Unknown;
            }
            RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus status);
            if (status == TaskStatus.Stopped) {
                return RemoteTerminationReason.Cancelled;
            }
            if (status == TaskStatus.Failed) {
                if (TerminationReason is { } explicitFailure &&
                    explicitFailure != RemoteTerminationReason.Completed) {
                    return explicitFailure;
                }
                return RemoteTerminationReason.BackendFailure;
            }
            if (TerminationReason is { } explicitReason) {
                return explicitReason;
            }
            return InferredOutcome == SliceOutcome.Completed
                ? RemoteTerminationReason.Completed
                : RemoteTerminationReason.TimeLimit;
        }
    }

    /// <summary>
    /// 推断问题结论 / Infer the problem status.
    /// </summary>
    /// <remarks>
    /// 未知语义恒为 <c>UNKNOWN</c>；否则显式结论优先，缺失时可行 → <c>FEASIBLE</c>，否则 <c>UNKNOWN</c>。
    /// Always <c>UNKNOWN</c> under unknown semantics; otherwise the explicit status wins, and when
    /// absent a feasible result is <c>FEASIBLE</c> and an infeasible one stays <c>UNKNOWN</c>.
    /// </remarks>
    public RemoteProblemStatus InferredProblemStatus =>
        HasUnknownOutcomeSemantics
            ? RemoteProblemStatus.Unknown
            : ProblemStatus ?? (InferredFeasible
                ? RemoteProblemStatus.Feasible
                : RemoteProblemStatus.Unknown);

    /// <summary>
    /// 推断解存在性 / Infer the solution presence.
    /// </summary>
    /// <remarks>
    /// 未知语义恒为 <c>UNKNOWN</c>；否则显式存在性优先，缺失时可行 → <c>INCUMBENT</c>，否则 <c>NONE</c>。
    /// Always <c>UNKNOWN</c> under unknown semantics; otherwise the explicit presence wins, and when
    /// absent a feasible result is <c>INCUMBENT</c> and an infeasible one is <c>NONE</c>.
    /// </remarks>
    public RemoteSolutionPresence InferredSolutionPresence =>
        HasUnknownOutcomeSemantics
            ? RemoteSolutionPresence.Unknown
            : SolutionPresence ?? (InferredFeasible
                ? RemoteSolutionPresence.Incumbent
                : RemoteSolutionPresence.None);

    /// <summary>
    /// 推断证明状态 / Infer the proof status.
    /// </summary>
    /// <remarks>
    /// 未知语义恒为 <c>UNKNOWN</c>；否则显式证明状态优先，缺失时 <c>NONE</c>——绝不由可行性或零间隙
    /// 反推证明。
    /// Always <c>UNKNOWN</c> under unknown semantics; otherwise the explicit proof status wins and
    /// absence stays <c>NONE</c> — proof is never back-inferred from feasibility or a zero gap.
    /// </remarks>
    public RemoteProofStatus InferredProofStatus =>
        HasUnknownOutcomeSemantics
            ? RemoteProofStatus.Unknown
            : ProofStatus ?? RemoteProofStatus.None;

    /// <summary>
    /// 合并为规范切片结果 / Merge into the canonical slice result.
    /// </summary>
    /// <remarks>
    /// 结果 artifact（<paramref name="result"/>）优先，其次本任务视图，最后按状态推断。
    /// <c>Completed</c> 只在状态已知且为 <c>COMPLETED</c>／<c>FAILED</c>／<c>STOPPED</c> 时为真。
    /// the result artifact (<paramref name="result"/>) wins, then this task view,
    /// then status inference. <c>Completed</c> is true only when the status is known and is
    /// <c>COMPLETED</c>, <c>FAILED</c> or <c>STOPPED</c>.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="result">结果 artifact；未产出时为 null / Result artifact, or null when not produced.</param>
    /// <param name="elapsed">本地已耗时 / Locally measured elapsed time.</param>
    /// <returns>规范切片结果 / Canonical slice result.</returns>
    public SliceResult ToSliceResult(
        ExecutionHandle handle,
        SerializedSolution? result,
        TimeSpan elapsed) {
        ArgumentNullException.ThrowIfNull(handle);
        bool unknownSemantics = HasUnknownOutcomeSemantics;
        RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus status);
        bool terminal = HasKnownStatus &&
            status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Stopped;
        // 可行性：未知语义恒 false；否则 artifact 优先，再按解存在性推断。
        // Feasibility: false under unknown semantics; otherwise the artifact wins, then presence infers it.
        bool feasible = !unknownSemantics &&
            (result?.Feasible ?? InferredFeasible);
        ObjectRef? checkpoint = LatestCheckpointRef ?? Scheduling?.CheckpointRef;
        ObjectRef? incumbent = IncumbentRef ?? Scheduling?.IncumbentRef;
        double? objective = result?.ObjectiveValue ?? ObjectiveValue;
        double? gap = result?.Gap ?? Gap;
        SliceOutcome effectiveOutcome = unknownSemantics
            ? SliceOutcome.Unknown
            : InferredOutcome;
        // 调度决策补全缺失字段；未知语义下结果强制 UNKNOWN。
 // The scheduling decision fills its missing fields per the implementation; the outcome is forced to
        // UNKNOWN under unknown semantics.
        SchedulingDecision? effectiveScheduling = Scheduling?.Complete(
            taskId: new TaskId(handle.TaskId.Value),
            sliceId: new SliceId(handle.SliceId.Value),
            nodeId: new NodeId(handle.NodeId.Value),
            checkpointRef: checkpoint,
            incumbentRef: incumbent,
            modelFingerprint: ModelFingerprint,
            outcome: effectiveOutcome,
            unknownSemantics: unknownSemantics);
        return new SliceResult(
            Completed: terminal,
            Feasible: feasible,
            ObjectiveValue: objective is { } objectiveValue && double.IsFinite(objectiveValue)
                ? new Fuookami.Ospf.Math.Algebra.Number.Flt64(objectiveValue)
                : null,
            Gap: gap is { } gapValue && double.IsFinite(gapValue)
                ? new Fuookami.Ospf.Math.Algebra.Number.Flt64(gapValue)
                : null,
            Elapsed: result?.ElapsedMs is { } resultElapsed
                ? TimeSpan.FromMilliseconds(resultElapsed)
                : elapsed,
            Message: result?.Message ?? Message,
            Status: status,
            Outcome: effectiveOutcome,
            RunId: result?.RunId ?? RunId,
            AttemptId: result?.AttemptId ?? AttemptId,
            Scheduling: effectiveScheduling,
            ObjectiveValueInt64: result?.ObjectiveValueInt64 ?? ObjectiveValueInt64,
            ProblemStatus: unknownSemantics
                ? RemoteProblemStatus.Unknown
                : result?.ProblemStatus ?? InferredProblemStatus,
            TerminationReason: InferredTermination,
            SolutionPresence: unknownSemantics
                ? RemoteSolutionPresence.Unknown
                : result?.SolutionPresence ?? InferredSolutionPresence,
            ProofStatus: unknownSemantics
                ? RemoteProofStatus.Unknown
                : result?.ProofStatus ?? InferredProofStatus,
            CheckpointRef: checkpoint,
            IncumbentRef: incumbent,
            ResultRef: LatestResultRef,
            ArtifactDigest: result?.ArtifactDigest ?? ArtifactDigest,
            BestBound: BestBound is { } bestBound && double.IsFinite(bestBound)
                ? new Fuookami.Ospf.Math.Algebra.Number.Flt64(bestBound)
                : Bound is { } bound && double.IsFinite(bound)
                    ? new Fuookami.Ospf.Math.Algebra.Number.Flt64(bound)
                    : null,
            Statistics: result is null ? Statistics : Merge(Statistics, result.Statistics),
            Diagnostics: result is null ? Diagnostics : Merge(result.Diagnostics, Diagnostics),
            SchemaVersion: result?.SchemaVersion ?? SchemaVersion,
            // 身份指纹：视图侧合并后，再由 artifact 的指纹覆盖同名键。
            // Identity fingerprints: merge on the view side, then let the artifact's entries overwrite by key.
            Fingerprints: result is null
                ? IdentityFingerprints()
                : Merge(IdentityFingerprints(), result.Fingerprints),
            FingerprintSchemas: result is null
                ? IdentityFingerprintSchemas()
                : Merge(IdentityFingerprintSchemas(), result.FingerprintSchemas),
            // provenance：视图侧在前，artifact 覆盖同名键。
            // Provenance: the view side first, then the artifact overwrites by key.
            Provenance: result is null ? Provenance : Merge(Provenance, result.Provenance));
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, string>? Merge(
        System.Collections.Generic.IReadOnlyDictionary<string, string>? first,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? second) {
        if (first is null || first.Count == 0) {
            return second;
        }
        if (second is null || second.Count == 0) {
            return first;
        }
        var merged = new System.Collections.Generic.Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach ((string key, string value) in second) {
            merged[key] = value;
        }
        return merged;
    }

    /// <summary>
    /// 身份指纹合并视图 / Merged identity fingerprints view.
    /// </summary>
    /// <remarks>
    /// 先取显式 <c>fingerprints</c>，再用三个平铺指纹覆盖同名键，最后让调度决策里的
    /// <c>model</c> 指纹胜出——顺序决定优先级，不允许两处静默给出不同值。
    /// start from the explicit <c>fingerprints</c>, then overwrite by the three flat
    /// fingerprint fields, and finally let the scheduling decision's <c>model</c> fingerprint win. The
    /// order defines precedence, so two sources never silently disagree.
    /// </remarks>
    /// <returns>合并后的指纹表 / Merged fingerprint map.</returns>
    public IReadOnlyDictionary<string, string> IdentityFingerprints() {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Fingerprints is { } fingerprints) {
            foreach ((string key, string value) in fingerprints) {
                merged[key] = value;
            }
        }
        PutIfPresent(merged, "model", ModelFingerprint);
        PutIfPresent(merged, "configuration", ConfigurationFingerprint);
        PutIfPresent(merged, "solver", SolverFingerprint);
        PutIfPresent(merged, "model", Scheduling?.ModelFingerprint);
        return merged;
    }

    /// <summary>
    /// 身份指纹 schema 合并视图 / Merged identity fingerprint schema view.
    /// </summary>
    /// <returns>合并后的 schema 表 / Merged schema map.</returns>
    public IReadOnlyDictionary<string, string> IdentityFingerprintSchemas() {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (FingerprintSchemas is { } schemas) {
            foreach ((string key, string value) in schemas) {
                merged[key] = value;
            }
        }
        PutIfPresent(merged, "model", ModelFingerprintSchema);
        PutIfPresent(merged, "configuration", ConfigurationFingerprintSchema);
        PutIfPresent(merged, "solver", SolverFingerprintSchema);
        PutIfPresent(merged, "model", Scheduling?.ModelFingerprintSchema);
        return merged;
    }

    private static void PutIfPresent(
        IDictionary<string, string> target,
        string key,
        string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            target[key] = value;
        }
    }

    /// <summary>
    /// 是否已观测到任何结果信息 / Whether any result information has been observed.
    /// </summary>
    /// <remarks>
    /// 终态、状态未知、任一结果字段存在、或存在未知语义，都算"已观测"；否则调用方应得到
    /// null 而不是一个空结果。
    /// a terminal status, an unknown status, any populated result field or unknown
    /// semantics all count as observed; otherwise the caller gets null rather than an empty result.
    /// </remarks>
    public bool HasObservation =>
        !HasKnownStatus ||
        RemoteWireEnums.TryParseTaskStatus(Status, out TaskStatus status) &&
        status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Stopped ||
        ObjectiveValue is not null ||
        ObjectiveValueInt64 is not null ||
        Gap is not null ||
        IncumbentRef is not null ||
        LatestResultRef is not null ||
        HasUnknownOutcomeSemantics;

    /// <summary>
    /// 与所选检查点的身份差异字段 / Identity fields that disagree with the selected checkpoint.
    /// </summary>
    /// <remarks>
    /// `taskId` 必须相等；`tenantId`、`runId`、`attemptId` 与三个指纹必须存在且相等；
    /// 缺失也算不一致，不允许"缺失即通过"。
    /// `taskId` must match; `tenantId`, `runId`, `attemptId` and all three
    /// fingerprints must be present and equal. A missing value counts as a mismatch — absence never passes.
    /// </remarks>
    /// <param name="taskId">期望任务标识 / Expected task identifier.</param>
    /// <param name="tenantId">期望租户标识 / Expected tenant identifier.</param>
    /// <param name="checkpoint">校验通过的检查点 / Verified checkpoint.</param>
    /// <returns>不匹配字段列表；空表示身份一致 / Mismatched fields; empty means identity matches.</returns>
    public System.Collections.Generic.IReadOnlyList<string> IdentityMismatches(
        string taskId,
        string tenantId,
        Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope checkpoint) {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var mismatched = new System.Collections.Generic.List<string>();
        if (!string.Equals(TaskId, taskId, System.StringComparison.Ordinal)) {
            mismatched.Add("taskId");
        }
        if (!string.Equals(TenantId, tenantId, System.StringComparison.Ordinal)) {
            mismatched.Add("tenantId");
        }
        if (RunId is null || !string.Equals(RunId, checkpoint.RunId, System.StringComparison.Ordinal)) {
            mismatched.Add("runId");
        }
        if (AttemptId is null || !string.Equals(AttemptId, checkpoint.AttemptId, System.StringComparison.Ordinal)) {
            mismatched.Add("attemptId");
        }
        if (ModelFingerprint is null ||
            !string.Equals(ModelFingerprint, checkpoint.ModelFingerprint, System.StringComparison.Ordinal)) {
            mismatched.Add("modelFingerprint");
        }
        if (ConfigurationFingerprint is null ||
            !string.Equals(ConfigurationFingerprint, checkpoint.ConfigurationFingerprint, System.StringComparison.Ordinal)) {
            mismatched.Add("configurationFingerprint");
        }
        if (SolverFingerprint is null ||
            !string.Equals(SolverFingerprint, checkpoint.SolverFingerprint, System.StringComparison.Ordinal)) {
            mismatched.Add("solverFingerprint");
        }
        return mismatched;
    }
}
