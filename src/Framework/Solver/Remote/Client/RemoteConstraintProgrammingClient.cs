using System.Collections.Generic;
#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;

using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程约束规划客户端 / Remote constraint-programming client.
/// </summary>
/// <remarks>
/// 对齐固定 Kotlin 基线 <c>RemoteConstraintProgrammingClient</c> 的 <c>solve</c> 入口：把不含 native 句柄的
/// CP snapshot 编码为原始模型数据（格式 <c>ospf-cp-snapshot-json</c>），连同任务元数据一起提交，并等待
/// 远端完成。
///
/// 与基线一致的两点：
/// - 调度时间片由切片接口传递，**不属于** CP 语义配置，因此不进入 <see cref="SolvePayload.Config"/>
///   （否则会污染配置指纹）。
/// - <c>targetType</c> 固定为 <c>cp</c>，并附带变量数与约束数作为规模估计。
///
/// 本类当前只覆盖 <c>solve</c>；基线的 <c>solveOutput</c>／<c>materialize</c>（稳定整数解物化与全量成员
/// 校验）尚未移植，调用方应使用 <see cref="SolveAsync"/> 拿到原始协议结果。
///
/// Aligns with the fixed Kotlin baseline's <c>RemoteConstraintProgrammingClient.solve</c>: encode a CP
/// snapshot without native handles as raw model data (format <c>ospf-cp-snapshot-json</c>), submit it with
/// task metadata and wait for remote completion.
///
/// Two baseline behaviours are preserved: the scheduling quantum travels through the slice API and is
/// **not** part of the semantic CP configuration (putting it in <see cref="SolvePayload.Config"/> would
/// pollute the configuration fingerprint), and <c>targetType</c> is always <c>cp</c> with variable and
/// constraint counts attached as size estimates.
///
/// <see cref="SolveAsync"/> covers the baseline's <c>solve</c> and returns the reduced protocol result;
/// <see cref="SolveOutputAsync"/> covers <c>solveOutput</c>/<c>materialize</c> and produces a Core
/// <see cref="ConstraintProgrammingSolverOutput"/>. Only the latter needs a result-storage port, because
/// materializing a stable integer solution means reading the artifact the result references.
/// </remarks>
public sealed class RemoteConstraintProgrammingClient {
    /// <summary>
    /// CP snapshot 的 wire 格式名 / Wire format name for a CP snapshot.
    /// </summary>
    public const string SnapshotFormat = "ospf-cp-snapshot-json";

    private readonly RemoteSolverClient _remoteClient;
    private readonly IObjectStoragePort? _resultStorage;

    /// <summary>
    /// 由共享远程执行端口构造 / Construct from a shared remote execution port.
    /// </summary>
    /// <param name="executionPort">远程执行端口 / Remote execution port.</param>
    /// <param name="resultStorage">结果对象存储，可为空 / Result object storage, may be null.</param>
    public RemoteConstraintProgrammingClient(
        RemoteSolverClient executionPort,
        IObjectStoragePort? resultStorage = null) {
        ArgumentNullException.ThrowIfNull(executionPort);
        _remoteClient = executionPort;
        _resultStorage = resultStorage;
    }

    /// <summary>
    /// 由 HTTP 客户端构造 / Construct from an HTTP client.
    /// </summary>
    /// <param name="httpClient">远程 HTTP 客户端 / Remote HTTP client.</param>
    /// <param name="resultStorage">结果对象存储，可为空 / Result object storage, may be null.</param>
    public RemoteConstraintProgrammingClient(
        RemoteSolverHttpClient httpClient,
        IObjectStoragePort? resultStorage = null) {
        ArgumentNullException.ThrowIfNull(httpClient);
        _remoteClient = new RemoteSolverClient(httpClient);
        _resultStorage = resultStorage;
    }

    /// <summary>
    /// 提交 CP snapshot 并等待远端完成 / Submit a CP snapshot and wait for remote completion.
    /// </summary>
    /// <remarks>
    /// 与基线一致：snapshot 先经 Core 的 <see cref="ConstraintProgrammingSnapshotCodec"/> 编码；编码失败
    /// 直接返回结构化错误，不做任何网络请求。时间片必须为正、轮数必须为正，由
    /// <see cref="RemoteSolverClient.SolveAsync"/> 统一校验。
    /// Mirrors the baseline: the snapshot is encoded through Core's
    /// <see cref="ConstraintProgrammingSnapshotCodec"/> first, and an encoding failure returns a
    /// structured error without issuing any network request. A positive quantum and round count are
    /// enforced by <see cref="RemoteSolverClient.SolveAsync"/>.
    /// </remarks>
    /// <param name="snapshot">不含 native 句柄的 CP snapshot / CP snapshot without native handles.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <param name="quantum">单次切片时长 / Slice quantum.</param>
    /// <param name="maxRounds">最大切片轮数 / Maximum slice rounds.</param>
    /// <param name="solverConfig">CP 求解配置 / CP solver configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>远程求解结果 / Remote solve result.</returns>
    public async Task<Result<SolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        TaskId taskId,
        SliceId sliceId,
        NodeId nodeId,
        TenantId tenantId,
        TimeSpan quantum,
        ulong maxRounds = 64,
        SolverConfig? solverConfig = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);

        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        if (!encoded.IsOk) {
            return encoded switch {
                Failed<string, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<SolveResult>(failed.Error),
                Fatal<string, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<SolveResult, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<SolveResult>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "CP snapshot 编码失败 / CP snapshot encoding failed")),
            };
        }

        var payload = new SolvePayload(
            ModelData: ModelData.Raw(
                bytes: System.Text.Encoding.UTF8.GetBytes(encoded.Value),
                format: SnapshotFormat),
            Config: solverConfig,
            TaskMeta: new TaskMeta(
                TargetType: new TargetTypeName("cp"),
                EstimatedVariableCount: snapshot.Variables.Count,
                EstimatedConstraintCount: snapshot.Constraints.Count));

        return await _remoteClient.SolveAsync(
            payload: payload,
            taskId: taskId,
            sliceId: sliceId,
            nodeId: nodeId,
            tenantId: tenantId,
            quantum: quantum,
            maxRounds: maxRounds,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 提交 CP 模型并物化完整求解输出 / Submit a CP model and materialize the full solver output.
    /// </summary>
    /// <remarks>
    /// 这是基线 <c>solveOutput</c> 的编排：提交 → 归一化历史结果 → 自一致校验 → 执行绑定校验 → 读工件 →
    /// 工件一致性校验 → 三态判定 → 构造 Core 输出。每一步失败都**原样上抛**，不降级成"成功但没有解"。
    ///
    /// 与 <see cref="SolveAsync"/> 的分工：那条路径只提交并返回精简结果（供只要任务标识的调用方）；本方法才
    /// 是"要把结果交给求解器用户"的入口，因此需要工件存储与配置指纹。
    ///
    /// This is the baseline's <c>solveOutput</c> orchestration: submit, normalize a legacy result, self-consistency
    /// check, execution-binding check, read the artifact, artifact-agreement check, three-state classification,
    /// then build the Core output. Any failing step propagates **unchanged** rather than degrading into
    /// "succeeded without a solution". The division of labour with <see cref="SolveAsync"/> is that the latter
    /// only submits and returns the reduced result for callers that just need a task identifier, while this is
    /// the entry for actually handing a result to a solver user, which is why it needs artifact storage and a
    /// configuration fingerprint.
    /// </remarks>
    /// <param name="snapshot">CP 模型快照 / CP model snapshot.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <param name="quantum">切片时间片 / Slice quantum.</param>
    /// <param name="maxRounds">最大轮数 / Maximum rounds.</param>
    /// <param name="solverConfig">求解配置，用于配置指纹 / Solver config, used for the configuration fingerprint.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>CP 求解输出 / CP solver output.</returns>
    public async Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>>
        SolveOutputAsync(
            ConstraintProgrammingModelSnapshot snapshot,
            TaskId taskId,
            SliceId sliceId,
            NodeId nodeId,
            TenantId tenantId,
            TimeSpan quantum,
            ulong maxRounds = 64,
            SolverConfig? solverConfig = null,
            CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);

        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        if (!encoded.IsOk) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "CP snapshot 编码失败 / CP snapshot encoding failed"));
        }
        var payload = new SolvePayload(
            ModelData: ModelData.Raw(
                bytes: System.Text.Encoding.UTF8.GetBytes(encoded.Value),
                format: SnapshotFormat),
            Config: solverConfig,
            TaskMeta: new TaskMeta(
                TargetType: new TargetTypeName("cp"),
                EstimatedVariableCount: snapshot.Variables.Count,
                EstimatedConstraintCount: snapshot.Constraints.Count));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> solved =
            await _remoteClient.SolveSliceAsync(
                payload: payload,
                taskId: taskId,
                sliceId: sliceId,
                nodeId: nodeId,
                tenantId: tenantId,
                quantum: quantum,
                maxRounds: maxRounds,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!solved.IsOk) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "远程 CP 求解未返回终态切片 / Remote CP solve returned no terminal slice"));
        }

        SliceResult slice = RemoteReportMapping.NormalizeLegacyCpResult(solved.Value);

        Try rawValidation = RemoteReportMapping.ValidateRawCpResult(slice);
        if (!rawValidation.IsOk) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程 CP 原始结果自相矛盾 / Remote CP raw result is not self-consistent"));
        }

        string expectedConfiguration = RemoteReportMapping.ConfigurationFingerprint(
            solverConfig ?? new SolverConfig());
        Try binding = RemoteReportMapping.ValidateExecutionBinding(
            slice: slice,
            snapshot: snapshot,
            expectedTaskId: taskId.Value,
            expectedSliceId: sliceId.Value,
            expectedConfigurationFingerprint: expectedConfiguration);
        if (!binding.IsOk) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程 CP 结果未绑定本次请求 / Remote CP result is not bound to this request: "
                + Reason(binding)));
        }

        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifact =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: slice,
                storage: _resultStorage,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!artifact.IsOk) {
            // 保留底层原因：压成通用文案会让"工件格式不对"和"工件缺失"无法区分，两者排查方向完全不同。
            // Keep the underlying reason: collapsing to a generic message makes "malformed artifact"
            // indistinguishable from "missing artifact", and the two point in entirely different directions.
            string reason = artifact switch {
                Failed<SerializedSolution?, ErrorCode, Error<ErrorCode>> failed =>
                    failed.Error.Message,
                Fatal<SerializedSolution?, ErrorCode, Error<ErrorCode>> fatal =>
                    JoinMessages(fatal.Errors),
                _ => string.Empty,
            };
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程 CP 结果工件不可读 / Remote CP result artifact cannot be read: " + reason));
        }
        if (artifact.Value is { } serialized) {
            Try agreement = RemoteReportMapping.ValidateSerializedCpResult(serialized, slice);
            if (!agreement.IsOk) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "远程 CP 原始结果与稳定解工件不一致 / "
                    + "Remote CP raw result disagrees with the serialized solution: "
                    + Reason(agreement)));
            }
        }

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> classification =
            RemoteCpResultMaterializer.Classify(snapshot, slice, artifact.Value);
        if (!classification.IsOk) {
            return Results.Failed<ConstraintProgrammingSolverOutput>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程 CP 解与模型不符 / Remote CP solution does not match the model: "
                + Reason(classification)));
        }

        return RemoteCpResultMaterializer.ToOutput(classification.Value, slice);
    }

    /// <summary>
    /// 取失败结果的底层原因 / Read the underlying reason of a failed result.
    /// </summary>
    /// <remarks>
    /// 每一段失败都保留原始消息：泛化成通用文案会让"哪个字段不一致"这类问题无法排查，调用方只能靠猜。
    /// Every failing stage keeps its original message: generalizing it makes questions like "which field
    /// disagrees" undiagnosable and leaves the caller guessing.
    /// </remarks>
    /// <typeparam name="T">结果值类型 / Result value type.</typeparam>
    /// <param name="result">失败的结果 / The failed result.</param>
    /// <returns>原因文本 / The reason text.</returns>
    private static string Reason<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<T, ErrorCode, Error<ErrorCode>> failed => failed.Error.Message,
            Fatal<T, ErrorCode, Error<ErrorCode>> fatal => JoinMessages(fatal.Errors),
            _ => string.Empty,
        };

    private static string JoinMessages(IReadOnlyList<Error<ErrorCode>> errors) {
        var messages = new System.Text.StringBuilder();
        foreach (Error<ErrorCode> error in errors) {
            if (messages.Length > 0) {
                messages.Append("; ");
            }
            messages.Append(error.Message);
        }
        return messages.ToString();
    }
}
