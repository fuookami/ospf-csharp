#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;
/// <summary>
/// 远程求解器 HTTP 传输委托 / Remote solver HTTP transport delegate.
/// </summary>
public delegate Task<RemoteSolverHttpResponse> RemoteSolverHttpTransport(RemoteSolverHttpRequest request);

/// <summary>
/// 远程求解器 HTTP 请求 / Remote solver HTTP request.
/// </summary>
public sealed record RemoteSolverHttpRequest(
    HttpMethod Method,
    string Url,
    string? Body = null,
    Dictionary<string, string>? Headers = null);

/// <summary>
/// 远程求解器 HTTP 响应 / Remote solver HTTP response.
/// </summary>
public sealed record RemoteSolverHttpResponse(
    int StatusCode,
    string? Body = null);

/// <summary>
/// 远程求解器 HTTP 恢复模式 / Remote solver HTTP resume mode.
/// </summary>
/// <remarks>
/// 与基线一致：规范端点只支持服务端最新检查点；指定检查点恢复明确不支持。
/// Matches the baseline: the canonical endpoint only supports the server's latest checkpoint; checkpoint-specific resume is explicitly unsupported.
/// </remarks>
public enum RemoteSolverHttpResumeMode {
    /// <summary>恢复服务端任务的最新检查点 / Resume the server task from its latest checkpoint</summary>
    ServerTaskLatestCheckpoint,
    /// <summary>严格要求按指定检查点恢复，当前 HTTP API 不支持 / Require checkpoint-specific resume, unsupported by the current HTTP API</summary>
    StrictCheckpoint
}

/// <summary>
/// 远程求解器运行时配置 / Remote solver runtime config.
/// </summary>
public sealed record RemoteSolverRuntimeConfig(
    TenantId TenantId,
    NodeId NodeId,
    TimeSpan Quantum,
    ulong MaxRounds = 64,
    Func<TaskId>? TaskIdProvider = null,
    Func<SliceId>? SliceIdProvider = null);

/// <summary>
/// HTTP 远程求解器传输实现 / HTTP-based remote solver transport.
/// </summary>
public sealed class HttpClientRemoteSolverTransport {
    private readonly HttpClient _httpClient;

    public HttpClientRemoteSolverTransport(HttpClient httpClient) {
        _httpClient = httpClient;
    }

    /// <summary>发送 HTTP 请求 / Send HTTP request.</summary>
    public async Task<RemoteSolverHttpResponse> SendAsync(RemoteSolverHttpRequest request) {
        using var httpRequest = new HttpRequestMessage(request.Method, request.Url);
        if (request.Body is not null) {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }
        if (request.Headers is not null) {
            foreach ((string? key, string? value) in request.Headers) {
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
        string body = await response.Content.ReadAsStringAsync();
        return new RemoteSolverHttpResponse((int)response.StatusCode, body);
    }

    /// <summary>转换为传输委托 / Convert to transport delegate.</summary>
    public RemoteSolverHttpTransport ToTransport() => SendAsync;
}

/// <summary>
/// 远程求解器 HTTP 客户端 / Remote solver HTTP client.
/// </summary>
public sealed class RemoteSolverHttpClient : ISolverExecutionPort {
    private readonly RemoteSolverHttpTransport _transport;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TenantId _tenantId;
    private readonly Func<TraceId>? _traceIdProvider;
    private readonly RemoteSolverHttpResumeMode _resumeMode;
    private readonly Func<TaskId, SliceId, TenantId, RequestId> _requestIdProvider;
    private readonly IObjectStoragePort? _objectStoragePort;
    private readonly Func<TaskId, SliceId, TenantId, ObjectPath> _payloadPathProvider;

    public RemoteSolverHttpClient(
        RemoteSolverHttpTransport transport,
        string baseUrl,
        TenantId tenantId,
        Func<TraceId>? traceIdProvider = null,
        JsonSerializerOptions? jsonOptions = null,
        RemoteSolverHttpResumeMode resumeMode = RemoteSolverHttpResumeMode.ServerTaskLatestCheckpoint,
        Func<TaskId, SliceId, TenantId, RequestId>? requestIdProvider = null,
        IObjectStoragePort? objectStoragePort = null,
        Func<TaskId, SliceId, TenantId, ObjectPath>? payloadPathProvider = null) {
        _transport = transport;
        _baseUrl = baseUrl.TrimEnd('/');
        _tenantId = tenantId;
        _traceIdProvider = traceIdProvider;
        _resumeMode = resumeMode;
        _requestIdProvider = requestIdProvider ?? DefaultRequestId;
        _objectStoragePort = objectStoragePort;
        _payloadPathProvider = payloadPathProvider ?? DefaultPayloadPath;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// 查询服务端能力和协议版本 / Probe server capabilities and protocol versions.
    /// </summary>
    /// <remarks>
    /// 与固定基线一致：<c>GET /api/v1/capabilities</c>，响应为 <c>{code, message, data}</c> 信封。
    /// Matches the pinned baseline: <c>GET /api/v1/capabilities</c> with a <c>{code, message, data}</c> envelope.
    /// </remarks>
    /// <returns>能力摘要或结构化错误 / Capability summary or a structured error.</returns>
    public async Task<Result<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>>> ProbeCapabilitiesAsync() {
        RemoteSolverHttpResponse response = await _transport(
            new RemoteSolverHttpRequest(HttpMethod.Get, $"{_baseUrl}/api/v1/capabilities"));
        if (response.StatusCode is not (>= 200 and < 300)) {
            return new Failed<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"能力探测失败：{response.Body} / Capability probe failed: {response.Body}"));
        }
        string? data = ReadEnvelopeData(response.Body, out string? code, out string? message);
        if (data is null) {
            return new Failed<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"能力响应信封无效：code={code}, message={message} / Invalid capability envelope"));
        }
        return RemoteSolverCapabilities.Decode(data);
    }

    /// <summary>
    /// 读取 API 信封的 data 字段 / Read the data field of an API envelope.
    /// </summary>
    /// <param name="body">响应体 / Response body.</param>
    /// <param name="code">信封 code / Envelope code.</param>
    /// <param name="message">信封 message / Envelope message.</param>
    /// <returns>data 的原始 JSON；缺失为 null / Raw JSON of data, or null when absent.</returns>
    private static string? ReadEnvelopeData(string? body, out string? code, out string? message) {
        code = null;
        message = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return null;
        }
        try {
            using var document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return null;
            }
            code = ReadString(root, "code");
            message = ReadString(root, "message");
            foreach (JsonProperty property in root.EnumerateObject()) {
                if (string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase)) {
                    return property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : property.Value.GetRawText();
                }
            }
            return null;
        }
        catch (JsonException) {
            return null;
        }
    }

    public async Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> StartAsync(
        SolvePayload payload, TaskId taskId, SliceId sliceId, NodeId nodeId, TenantId tenantId,
        CancellationToken cancellationToken = default) {
        // 基线语义：CP 载荷提交前必须先探测能力，能力不符直接拒绝，不上传载荷。
        // Baseline semantics: a CP payload must probe capabilities before submission; a mismatch rejects without uploading.
        if (payload.ModelData.ModelType == NormalizedModelType.Cp) {
            Result<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> probed =
                await ProbeCapabilitiesAsync();
            if (!probed.IsOk) {
                return probed switch {
                    Failed<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> failed =>
                        Results.Failed<ExecutionHandle>(failed.Error),
                    Fatal<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> fatal =>
                        new Fatal<ExecutionHandle, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                    _ => Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "能力探测失败 / Capability probe failed")),
                };
            }
            RemoteSolverCapabilities capabilities = probed.Value;
            bool schemaOk = int.TryParse(
                capabilities.SchemaVersion.Split('.')[0],
                out int major) && major == 1;
            bool protocolOk = capabilities.ProtocolVersions is { } protocols && protocols.Contains("2.0");
            bool cpOk = capabilities.SupportedModelTypes is { } models && models.Any(
                type => string.Equals(type, "CP", StringComparison.OrdinalIgnoreCase));
            if (!schemaOk || !protocolOk || !cpOk) {
                return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "远程求解器未声明 CP 协议/模型能力 / Remote solver does not advertise CP protocol/model capability: "
                    + $"schema={capabilities.SchemaVersion}, "
                    + $"protocols={string.Join(',', capabilities.ProtocolVersions ?? Array.Empty<string>())}, "
                    + $"models={string.Join(',', capabilities.SupportedModelTypes ?? Array.Empty<string>())}"));
            }
        }
        // 与基线一致：先把载荷写入对象存储，再用返回的引用提交；没有存储端口时不得静默合成路径。
        // Matches the baseline: upload the payload first and submit the returned reference; without a
        // storage port the path must not be fabricated silently.
        Result<ObjectRef, ErrorCode, Error<ErrorCode>> uploaded =
            await PutPayloadAsync(payload, taskId, sliceId, tenantId);
        if (!uploaded.IsOk) {
            return uploaded switch {
                Failed<ObjectRef, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<ExecutionHandle>(failed.Error),
                Fatal<ObjectRef, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<ExecutionHandle, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "载荷上传失败 / Payload upload failed")),
            };
        }
        var submitRequest = new RemoteTaskSubmitRequest(
            PayloadRef: uploaded.Value.Path,
            RequestId: _requestIdProvider(taskId, sliceId, tenantId),
            TenantId: tenantId,
            Complexity: payload.Scheduling?.Complexity,
            TimeSensitivity: payload.Scheduling?.TimeSensitivity,
            Priority: payload.Scheduling?.Priority,
            BudgetScope: payload.Scheduling?.BudgetScope,
            BudgetLimit: payload.Scheduling?.BudgetLimit,
            DeadlineEpochMs: payload.Scheduling?.DeadlineEpochMs,
            Scheduling: payload.Scheduling);
        string requestBody = JsonSerializer.Serialize(submitRequest, RemoteWireJson.Options);
        RemoteSolverHttpResponse response = await _transport(
            new RemoteSolverHttpRequest(HttpMethod.Post, $"{_baseUrl}/api/v1/tasks", requestBody));
        if (response.StatusCode is not (>= 200 and < 300)) {
            return new Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"提交失败 / Start failed: {response.Body}"));
        }
        string? data = ReadEnvelopeData(response.Body, out string? code, out string? message);
        if (data is null) {
            return new Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"提交响应信封无效：code={code}, message={message} / Invalid submit envelope"));
        }
        System.Text.Json.JsonElement? view = ParseBody(data);
        string? responseTaskId = ReadString(view, "taskId");
        string? responseStatus = ReadString(view, "status");
        bool accepted = view is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty("accepted", out JsonElement acceptedElement) &&
            acceptedElement.ValueKind == JsonValueKind.True;
        if (!accepted || responseStatus is null || !RemoteWireEnums.TryParseTaskStatus(responseStatus, out _)) {
            // 基线语义：未受理或状态未知都是非法状态转换，且不产出句柄。
            // Baseline semantics: a rejected or unknown status is an invalid state transition and yields no handle.
            return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                string.IsNullOrWhiteSpace(ReadString(view, "message"))
                    ? "远程任务提交返回未知或拒绝状态 / Remote task submission returned an unknown or rejected status"
                    : ReadString(view, "message")!));
        }
        return Results.Ok(new ExecutionHandle(new HandleId(taskId.Value), taskId, sliceId, nodeId, tenantId));
    }

    /// <summary>
    /// 恢复远程任务 / Resume a remote task.
    /// </summary>
    /// <remarks>
    /// 规范 HTTP 端点只恢复服务端选择的最新检查点，没有检查点选择器。严格模式下拒绝，
    /// 绝不把"服务端最新检查点"静默替换成调用方指定的引用。
    /// The canonical HTTP endpoint only resumes a server-selected latest checkpoint and has no selector.
    /// Strict mode is rejected rather than silently substituting latest-resume for the caller's reference.
    /// </remarks>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="snapshot">调用方指定的检查点引用 / Caller-provided checkpoint reference.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>执行句柄或结构化错误 / Execution handle or a structured error.</returns>
    public async Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> ResumeAsync(
        SolvePayload payload, ObjectRef snapshot, TaskId taskId, SliceId sliceId, NodeId nodeId, TenantId tenantId,
        CancellationToken cancellationToken = default) {
        if (_resumeMode == RemoteSolverHttpResumeMode.StrictCheckpoint) {
            return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "HTTP 任务恢复不支持指定检查点 / HTTP task resume does not support checkpoint-specific resume: "
                + $"taskId={taskId.Value}, checkpointPath={snapshot.Path.Value}, resumeMode={_resumeMode}"));
        }
        // 恢复前必须校验租户范围 v2 checkpoint 的身份，避免把不匹配的检查点静默喂给任务。
        // A tenant-scoped v2 checkpoint must be verified before resume, so a mismatched checkpoint is
        // never silently handed to the task.
        Result<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope, ErrorCode, Error<ErrorCode>> verified =
            await ValidateResumeCheckpointAsync(payload, snapshot, taskId, sliceId, tenantId);
        if (!verified.IsOk) {
            return verified switch {
                Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<ExecutionHandle>(failed.Error),
                Fatal<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<ExecutionHandle, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "检查点校验失败 / Checkpoint validation failed")),
            };
        }
        string requestBody = JsonSerializer.Serialize(new { taskId = taskId.Value, sliceId = sliceId.Value, snapshotPath = snapshot.Path.Value }, _jsonOptions);
        RemoteSolverHttpResponse response = await _transport(new RemoteSolverHttpRequest(
            HttpMethod.Post, $"{_baseUrl}/api/v1/tasks/{taskId.Value}/resume", requestBody));
        if (response.StatusCode is not (>= 200 and < 300)) {
            return new Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"Resume failed: {response.Body}"));
        }
        // 基线语义：恢复响应必须保留所选检查点的身份，任一字段不符即拒绝，避免静默恢复成别的运行。
        // Baseline semantics: the resume action must preserve the selected checkpoint identity; any
        // mismatch rejects, so a silent resume into a different run is impossible.
        RemoteTaskActionView? action = TryDecodeAction(response.Body);
        if (action is null) {
            return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "恢复响应无法解析 / Remote resume response cannot be parsed"));
        }
        if (!action.Accepted || !action.HasKnownStatus) {
            return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                string.IsNullOrWhiteSpace(action.Message)
                    ? "远程任务恢复返回未知或拒绝状态 / Remote task resume returned an unknown or rejected status"
                    : action.Message));
        }
        System.Collections.Generic.IReadOnlyList<string> mismatched = action.IdentityMismatches(
            taskId: taskId.Value,
            tenantId: tenantId.Value,
            checkpoint: verified.Value);
        if (mismatched.Count > 0) {
            return Results.Failed<ExecutionHandle>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "恢复动作未保留所选检查点身份 / Remote resume action does not preserve the selected checkpoint identity: "
                + string.Join(",", mismatched)));
        }
        return Results.Ok(new ExecutionHandle(new HandleId(taskId.Value), taskId, sliceId, nodeId, tenantId));
    }

    /// <summary>
    /// 解析任务动作响应 / Decode a task action response.
    /// </summary>
    /// <param name="body">响应体 / Response body.</param>
    /// <returns>动作视图；无法解析为 null / Action view, or null when it cannot be parsed.</returns>
    private static RemoteTaskActionView? TryDecodeAction(string? body) {
        string? data = ReadEnvelopeData(body, out _, out _) ?? body;
        if (string.IsNullOrWhiteSpace(data)) {
            return null;
        }
        try {
            return JsonSerializer.Deserialize<RemoteTaskActionView>(data, RemoteWireJson.Options);
        }
        catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// 把原始诊断字段解析成强类型诊断 / Parse raw diagnostics fields into typed diagnostics.
    /// </summary>
    /// <remarks>
    /// 与基线一致：诊断字段解析失败时**不产出半个结果**——把解析原因写进结果消息并保留原始字段，
    /// 而不是静默丢弃或抛异常。原始 <c>Diagnostics</c> 字典始终保留，下游仍可看到未经解释的内容。
    /// Mirrors the baseline: a diagnostics parse failure never yields a half-built result — the reason is
    /// recorded in the message while the raw fields are kept, rather than being silently dropped or
    /// thrown. The raw <c>Diagnostics</c> map always survives so downstream code still sees the
    /// unexplained content.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <returns>附带解析诊断的结果 / The result with parsed diagnostics attached.</returns>
    private static SliceResult AttachDiagnostics(SliceResult slice) {
        if (slice.Diagnostics is null || slice.Diagnostics.Count == 0) {
            return slice;
        }
        Result<
            Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>,
            ErrorCode,
            Error<ErrorCode>> parsed = RemoteReportMapping.ToRemoteDiagnostics(slice.Diagnostics);
        if (parsed.IsOk) {
            return slice with { SolveDiagnostics = parsed.Value };
        }
        string reason = parsed switch {
            Failed<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                failed.Error.Message,
            Fatal<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                string.Join("; ", fatal.Errors.Select(error => error.Message)),
            _ => "unknown diagnostics failure",
        };
        return slice with {
            Message = slice.Message is null
                ? $"诊断解析失败 / Diagnostics cannot be parsed: {reason}"
                : $"{slice.Message} | 诊断解析失败 / Diagnostics cannot be parsed: {reason}",
        };
    }

    /// <summary>
    /// 读取远程任务视图 / Read a remote task view.
    /// </summary>
    /// <remarks>
    /// 与基线一致：任务观测一律通过 <c>GET /api/v1/tasks/{taskId}</c>，不另设 await／checkpoint／result
    /// 端点。返回 null 表示服务端已无该任务。
    /// Mirrors the baseline: task observations always go through
    /// <c>GET /api/v1/tasks/{taskId}</c>; there is no separate await/checkpoint/result endpoint. A null
    /// result means the server no longer has the task.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>任务视图或 null / Task view or null.</returns>
    private async Task<Result<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>>> GetTaskViewAsync(
        ExecutionHandle handle, CancellationToken cancellationToken = default) {
        string url = $"{_baseUrl}/api/v1/tasks/{handle.TaskId.Value}";
        RemoteSolverHttpResponse response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Get, url));
        if (response.StatusCode is 404) {
            return Results.Ok<RemoteTaskActionView?>(null);
        }
        if (response.StatusCode is not (>= 200 and < 300)) {
            return Results.Failed<RemoteTaskActionView?>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"读取任务视图失败 / Cannot read the task view: {response.Body}"));
        }
        string? data = ReadEnvelopeData(response.Body, out _, out _) ?? response.Body;
        if (string.IsNullOrWhiteSpace(data)) {
            return Results.Ok<RemoteTaskActionView?>(null);
        }
        try {
            return Results.Ok<RemoteTaskActionView?>(
                JsonSerializer.Deserialize<RemoteTaskActionView>(data, RemoteWireJson.Options));
        }
        catch (JsonException exception) {
            return Results.Failed<RemoteTaskActionView?>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"任务视图无法解析 / Task view cannot be parsed: {exception.Message}"));
        }
    }

    public async Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> AwaitSliceEndAsync(
        ExecutionHandle handle, TimeSpan quantum,
        CancellationToken cancellationToken = default) {
        // 与基线一致：时间片只用于校验必须为正；轮询靠固定间隔，请求本身不带 quantum。
        // Matches the baseline: the quantum only has to be positive; polling uses a fixed interval and
        // the request itself carries no quantum.
        if (quantum <= TimeSpan.Zero) {
            return Results.Failed<SliceResult>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "时间片必须为正 / quantum must be positive"));
        }
        long startedAt = System.Environment.TickCount64;
        while (true) {
            Result<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewResult =
                await GetTaskViewAsync(handle, cancellationToken);
            if (viewResult is Failed<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFailure) {
                return Results.Failed<SliceResult>(viewFailure.Error);
            }
            if (viewResult is Fatal<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFatal) {
                return new Fatal<SliceResult, ErrorCode, Error<ErrorCode>>(viewFatal.Errors);
            }
            RemoteTaskActionView? view = viewResult.Value;
            if (view is null) {
                // 与基线一致：任务已不存在属于失败，不是"仍在运行"。
                // Matches the baseline: a missing task is a failure, not "still running".
                return Results.Failed<SliceResult>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    $"远程任务不存在 / Remote task is not found: {handle.TaskId.Value}"));
            }
            RemoteWireEnums.TryParseTaskStatus(view.Status, out Domain.TaskStatus status);
            if (status == Domain.TaskStatus.Unknown) {
                // 未知状态拒绝推断，不把它当成"仍在运行"。
                // An unknown status refuses inference instead of being treated as "still running".
                return Results.Failed<SliceResult>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"远程任务返回未知状态，拒绝推断切片结果 / Remote task returned an unknown status; refusing to infer a slice result: {view.Status}"));
            }
            var elapsed = TimeSpan.FromMilliseconds(System.Environment.TickCount64 - startedAt);
            if (status is Domain.TaskStatus.Completed or Domain.TaskStatus.Failed or Domain.TaskStatus.Stopped) {
                // 与基线一致：终态时合并结果 artifact（若已产出），artifact 优先于任务视图。
                // Matches the baseline: a terminal state merges the result artifact when produced, and the
                // artifact wins over the task view.
                Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifact =
                    await FetchResultArtifactAsync(handle, view, cancellationToken);
                if (artifact is Failed<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifactFailure) {
                    return Results.Failed<SliceResult>(artifactFailure.Error);
                }
                if (artifact is Fatal<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifactFatal) {
                    return new Fatal<SliceResult, ErrorCode, Error<ErrorCode>>(artifactFatal.Errors);
                }
                return Results.Ok(AttachDiagnostics(
                    view.ToSliceResult(handle, artifact.Value, elapsed)));
            }
            if (status == Domain.TaskStatus.Suspended) {
                return Results.Ok(AttachDiagnostics(
                    view.ToSliceResult(handle, null, elapsed)));
            }
            // 调度器负责时间片并执行检查点、停止与重新入队；客户端不设截止时间，
            // 以免把服务端的正常暂停误转成任务停止。
            // The dispatcher owns the quantum and performs checkpoint, stop and requeue; the client sets
            // no deadline, so a normal server suspension is never turned into a task stop.
            await Task.Delay(PollIntervalMs, cancellationToken);
        }
    }

    /// <summary>
    /// 切片轮询间隔毫秒 / Slice polling interval in milliseconds.
    /// </summary>
    /// <remarks>
    /// 与固定 Kotlin 基线的 <c>pollInterval</c> 默认值 200 ms 保持一致。此前 C# 用 50 ms，对同一台服务端
    /// 会造成四倍请求量，而基线的默认值是可以被调用方覆盖的。
    /// Matches the pinned Kotlin baseline's <c>pollInterval</c> default of 200 ms. C# previously used
    /// 50 ms, quadrupling the request volume against the same server, while the baseline default is
    /// caller-overridable.
    /// </remarks>
    private const int PollIntervalMs = 200;

    /// <summary>
    /// 把任务观测映射为规范切片结果 / Map a task observation to the canonical slice result.
    /// </summary>
    /// <param name="view">任务动作视图 / Task action view.</param>
    /// <param name="status">解析后的状态 / Parsed status.</param>
    /// <param name="elapsed">已耗时 / Elapsed time.</param>
    /// <param name="terminal">是否终态 / Whether terminal.</param>
    /// <returns>切片结果 / Slice result.</returns>
    private static SliceResult BuildSliceResult(
        RemoteTaskActionView view,
        Domain.TaskStatus status,
        TimeSpan elapsed,
        bool terminal) =>
        new(
            Completed: terminal,
            // 未知语义下绝不声称可行；可行性只由解存在性推断。
            // Never claim feasibility under unknown semantics; feasibility is inferred from presence only.
            Feasible: view.InferredFeasible,
            ObjectiveValue: null,
            Gap: null,
            Elapsed: elapsed,
            Message: view.Message,
            Status: status,
            Outcome: view.InferredOutcome,
            RunId: view.RunId,
            AttemptId: view.AttemptId,
            Scheduling: view.Scheduling);

    /// <summary>
    /// 导出检查点引用 / Export the checkpoint reference.
    /// </summary>
    /// <remarks>
    /// 与基线一致：没有独立的导出端点——读取任务视图并返回其 <c>latestCheckpointRef</c>。任务不存在
    /// 或尚未产出检查点时返回 null，与失败区分。
    /// Mirrors the baseline: there is no dedicated export endpoint — read the task view and return its
    /// <c>latestCheckpointRef</c>. A missing task or an unproduced checkpoint returns null, which stays
    /// distinct from a failure.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>检查点引用或 null / Checkpoint reference or null.</returns>
    public async Task<Result<ObjectRef?, ErrorCode, Error<ErrorCode>>> ExportCheckpointAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default) {
        Result<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewResult =
            await GetTaskViewAsync(handle, cancellationToken);
        if (viewResult is Failed<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFailure) {
            return Results.Failed<ObjectRef?>(viewFailure.Error);
        }
        if (viewResult is Fatal<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFatal) {
            return new Fatal<ObjectRef?, ErrorCode, Error<ErrorCode>>(viewFatal.Errors);
        }
        return Results.Ok<ObjectRef?>(
            viewResult.Value?.LatestCheckpointRef ?? viewResult.Value?.Scheduling?.CheckpointRef);
    }

    /// <summary>
    /// 取回最终求解结果 / Fetch the final solve result.
    /// </summary>
    /// <remarks>
    /// 与基线一致：先读任务视图；视图无任何观测时返回 null。有 <c>latestResultRef</c> 且配置了对象存储
    /// 时取回 artifact 并合并（artifact 优先），否则仅凭视图构造结果。合并逻辑复用
    /// <see cref="RemoteTaskActionView.ToSliceResult"/>，不在此处重写回退链。
    /// Mirrors the baseline: read the task view first and return null when it carries no observation at
    /// all. With a <c>latestResultRef</c> and a configured object store the artifact is fetched and merged
    /// (artifact wins); otherwise the result is built from the view alone. The merge reuses
    /// <see cref="RemoteTaskActionView.ToSliceResult"/> instead of duplicating the fallback chain here.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>最终结果，或 null 表示无可观测结果 / Final result, or null when nothing is observed.</returns>
    public async Task<Result<SolveResult?, ErrorCode, Error<ErrorCode>>> FetchFinalResultAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default) {
        Result<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewResult =
            await GetTaskViewAsync(handle, cancellationToken);
        if (viewResult is Failed<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFailure) {
            return Results.Failed<SolveResult?>(viewFailure.Error);
        }
        if (viewResult is Fatal<RemoteTaskActionView?, ErrorCode, Error<ErrorCode>> viewFatal) {
            return new Fatal<SolveResult?, ErrorCode, Error<ErrorCode>>(viewFatal.Errors);
        }
        RemoteTaskActionView? view = viewResult.Value;
        if (view is null || !view.HasObservation) {
            return Results.Ok<SolveResult?>(null);
        }
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifact =
            await FetchResultArtifactAsync(handle, view, cancellationToken);
        if (artifact is Failed<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifactFailure) {
            return Results.Failed<SolveResult?>(artifactFailure.Error);
        }
        if (artifact is Fatal<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifactFatal) {
            return new Fatal<SolveResult?, ErrorCode, Error<ErrorCode>>(artifactFatal.Errors);
        }
        SliceResult slice = AttachDiagnostics(
            view.ToSliceResult(handle, artifact.Value, elapsed: TimeSpan.Zero));
        return Results.Ok<SolveResult?>(new SolveResult(
            Feasible: slice.Feasible,
            Optimal: slice.SolutionPresence == RemoteSolutionPresence.Optimal,
            ObjectiveValue: slice.ObjectiveValue ??
                (slice.ObjectiveValueInt64 is { } exact ? new Fuookami.Ospf.Math.Algebra.Number.Flt64(exact) : null),
            Gap: slice.Gap,
            Elapsed: slice.Elapsed,
            CheckpointRef: slice.CheckpointRef,
            Message: slice.Message));
    }

    /// <summary>
    /// 取回已验证的结果 artifact / Fetch the verified result artifact.
    /// </summary>
    /// <remarks>
    /// 与基线一致：空响应体或未产出都返回 null（表示"尚未产出"），不当作失败；解码失败或协议校验失败
    /// 则返回结构化错误，不产出半个结果。
    /// Mirrors the baseline: an empty body or an unproduced artifact returns null ("not produced yet")
    /// rather than a failure, while a decode or protocol-validation failure returns a structured error
    /// instead of a half-built result.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="view">任务视图，提供 <c>latestResultRef</c> / Task view supplying <c>latestResultRef</c>.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>结果 artifact 或 null / Result artifact or null.</returns>
    private async Task<Result<SerializedSolution?, ErrorCode, Error<ErrorCode>>> FetchResultArtifactAsync(
        ExecutionHandle handle,
        RemoteTaskActionView view,
        CancellationToken cancellationToken = default) {
        ObjectRef? resultRef = view.LatestResultRef;
        if (resultRef is null || _objectStoragePort is null) {
            return Results.Ok<SerializedSolution?>(null);
        }
        Result<StoredObject, ErrorCode, Error<ErrorCode>> stored;
        try {
            stored = await _objectStoragePort.GetAsync(resultRef, cancellationToken);
        }
        catch (Exception exception) {
            return Results.Failed<SerializedSolution?>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"读取结果 artifact 失败 / Cannot read the result artifact: {exception.Message}"));
        }
        if (!stored.IsOk) {
            return Results.Ok<SerializedSolution?>(null);
        }
        Result<SerializedSolution, ErrorCode, Error<ErrorCode>> decoded = SerializedSolution.Decode(
            System.Text.Encoding.UTF8.GetString(stored.Value.Data));
        if (!decoded.IsOk) {
            return decoded switch {
                Failed<SerializedSolution, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<SerializedSolution?>(failed.Error),
                Fatal<SerializedSolution, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<SerializedSolution?, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<SerializedSolution?>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "远程结果解码失败 / Remote result decoding failed")),
            };
        }
        return Results.Ok<SerializedSolution?>(decoded.Value);
    }

    private static string? ReadString(JsonElement root, string name) {
        if (root.ValueKind != JsonValueKind.Object) {
            return null;
        }
        foreach (JsonProperty property in root.EnumerateObject()) {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String) {
                string? value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        return null;
    }

    /// <summary>
    /// 停止远程任务并确认终态 / Stop a remote task and confirm a terminal state.
    /// </summary>
    /// <remarks>
    /// 与基线一致：受理后轮询任务视图直到终态，最多 <c>StopConfirmationAttempts</c> 次；
    /// 只有 <c>STOPPED</c> 或 <c>COMPLETED</c> 才返回 true。幂等：已处于终态时直接成功。
    /// Matches the baseline: after acceptance, poll the task view up to <c>StopConfirmationAttempts</c> times until
    /// terminal; only <c>STOPPED</c> or <c>COMPLETED</c> returns true. Idempotent: an already-terminal task succeeds.
    /// </remarks>
    /// <param name="handle">执行句柄 / Execution handle.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>是否已确认停止 / Whether the stop is confirmed.</returns>
    public async Task<Result<bool, ErrorCode, Error<ErrorCode>>> StopAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default) {
        string url = $"{_baseUrl}/api/v1/tasks/{handle.TaskId.Value}/stop";
        RemoteSolverHttpResponse response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Post, url));
        if (response.StatusCode is not (>= 200 and < 300)) {
            return new Failed<bool, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"Stop failed: {response.Body}"));
        }
        // 受理但未确认终态时，按基线语义返回 false 而不是谎报成功。
        // Accepted without a confirmed terminal state returns false rather than claiming success.
        string? status = ReadEnvelopeString(response.Body, "status");
        if (IsTerminal(status)) {
            return Results.Ok(IsConfirmedStop(status));
        }
        for (int attempt = 0; attempt < StopConfirmationAttempts; ++attempt) {
            RemoteSolverHttpResponse viewResponse = await _transport(
                new RemoteSolverHttpRequest(HttpMethod.Get, $"{_baseUrl}/api/v1/tasks/{handle.TaskId.Value}"));
            if (viewResponse.StatusCode is not (>= 200 and < 300)) {
                return new Failed<bool, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    $"停止确认读取任务失败 / Stop confirmation cannot read the task: {viewResponse.Body}"));
            }
            string? viewStatus = ReadEnvelopeString(viewResponse.Body, "status");
            if (IsTerminal(viewStatus)) {
                return Results.Ok(IsConfirmedStop(viewStatus));
            }
        }
        return Results.Ok(false);
    }

    /// <summary>停止确认的最大轮询次数 / Maximum polling attempts for stop confirmation.</summary>
    private const int StopConfirmationAttempts = 32;

    /// <summary>
    /// 上传求解载荷 / Upload a solve payload.
    /// </summary>
    /// <remarks>
    /// 与基线一致：没有对象存储端口时明确失败，不合成路径假装已上传。载荷按 `*Ms` 整型时长序列化。
    /// Matches the baseline: without an object-storage port this fails explicitly instead of fabricating
    /// a path and pretending the payload was uploaded. The payload serializes with integer `*Ms` durations.
    /// </remarks>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <returns>载荷对象引用或结构化错误 / Payload object reference or a structured error.</returns>
    private async Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutPayloadAsync(
        SolvePayload payload,
        TaskId taskId,
        SliceId sliceId,
        TenantId tenantId) {
        if (_objectStoragePort is null) {
            return Results.Failed<ObjectRef>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "作为 SolverExecutionPort 使用 RemoteSolverHttpClient 时必须提供 objectStoragePort / "
                + "objectStoragePort is required when RemoteSolverHttpClient is used as SolverExecutionPort"));
        }
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(payload, RemoteWireJson.Options));
        ObjectPath path = _payloadPathProvider(taskId, sliceId, tenantId);
        // 与基线一致：把 taskId／sliceId／tenantId 作为对象元数据一并写入。
        // Matches the baseline: write taskId/sliceId/tenantId as object metadata alongside the payload.
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["taskId"] = taskId.Value,
            ["sliceId"] = sliceId.Value,
            ["tenantId"] = tenantId.Value,
        };
        try {
            return await _objectStoragePort.PutAsync(path, bytes, metadata);
        }
        catch (Exception exception) {
            return Results.Failed<ObjectRef>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                $"写入远程求解载荷失败 / Failed to put remote solver payload: {exception.Message}"));
        }
    }

    /// <summary>
    /// 恢复前加载并校验租户范围内的 v2 检查点 / Load and verify a tenant-scoped v2 checkpoint before resume.
    /// </summary>
    /// <remarks>
    /// 逐项对照固定基线：路径必须是 <c>&lt;tenant&gt;/checkpoint/&lt;task&gt;/&lt;checkpointId&gt;</c>
    /// 且不含 <c>.</c>/<c>..</c>；envelope 必须是 schema <c>3.0</c>、<c>sourceFormat = "v2"</c>、身份字段
    /// 齐全、<c>runId</c> 等于任务标识、<c>attemptId</c> 是路径检查点标识的前缀；随后把载荷声明的
    /// model/configuration/solver 指纹与 envelope 逐项比对，任一不符即拒绝。
    /// Mirrors the pinned baseline: the path must be
    /// <c>&lt;tenant&gt;/checkpoint/&lt;task&gt;/&lt;checkpointId&gt;</c> with no <c>.</c>/<c>..</c>
    /// segment; the envelope must be schema <c>3.0</c> with <c>sourceFormat = "v2"</c>, complete identity
    /// fields, <c>runId</c> equal to the task id, and an <c>attemptId</c> that prefixes the path checkpoint
    /// id. The payload-declared model/configuration/solver fingerprints are then compared field by field,
    /// and any mismatch rejects the resume.
    /// </remarks>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="checkpoint">调用方指定的检查点引用 / Caller-provided checkpoint reference.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <returns>校验通过的 envelope 或结构化错误 / Verified envelope or a structured error.</returns>
    private async Task<Result<
        Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>>> ValidateResumeCheckpointAsync(
        SolvePayload payload,
        ObjectRef checkpoint,
        TaskId taskId,
        SliceId sliceId,
        TenantId tenantId) {
        string[] segments = checkpoint.Path.Value.Split('/');
        bool pathShapeOk = segments.Length == 4 &&
            string.Equals(segments[0], tenantId.Value, StringComparison.Ordinal) &&
            string.Equals(segments[1], "checkpoint", StringComparison.Ordinal) &&
            string.Equals(segments[2], taskId.Value, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(segments[3]) &&
            !segments.Any(segment => segment is "." or "..");
        if (!pathShapeOk) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"检查点引用不是本任务的租户范围引用 / Checkpoint reference is not a tenant-scoped reference for this task: {checkpoint.Path.Value}"));
        }
        if (_objectStoragePort is null) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "校验检查点必须提供 objectStoragePort / objectStoragePort is required to verify a checkpoint before resume"));
        }
        Result<StoredObject, ErrorCode, Error<ErrorCode>> stored;
        try {
            stored = await _objectStoragePort.GetAsync(checkpoint);
        }
        catch (Exception exception) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    $"恢复前读取检查点失败 / Failed to read checkpoint before resume: {exception.Message}"));
        }
        if (!stored.IsOk) {
            return stored switch {
                Failed<StoredObject, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(failed.Error),
                Fatal<StoredObject, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                    new Err<ErrorCode>(ErrorCode.ApplicationFailed, "检查点对象缺失 / Checkpoint object is missing")),
            };
        }
        Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope? envelope =
            PortableCheckpointCodec.DecodeOrNull(
                System.Text.Encoding.UTF8.GetString(stored.Value.Data)) ?? null;
        if (envelope is null) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "检查点不是经验证的可移植 v2 envelope / Checkpoint is not a verified portable v2 envelope"));
        }
        string pathCheckpointId = segments[3];
        var requiredFields = new Dictionary<string, string?> {
            ["schemaVersion"] = envelope.SchemaVersion,
            ["sourceFormat"] = envelope.SourceFormat,
            ["checkpointId"] = envelope.CheckpointId,
            ["identitySchemaVersion"] = envelope.IdentitySchemaVersion,
            ["identityNamespace"] = envelope.IdentityNamespace,
            ["modelFingerprint"] = envelope.ModelFingerprint,
            ["configurationFingerprint"] = envelope.ConfigurationFingerprint,
            ["solverFingerprint"] = envelope.SolverFingerprint,
            ["runId"] = envelope.RunId,
            ["attemptId"] = envelope.AttemptId,
            ["integritySha256"] = envelope.IntegritySha256,
        };
        bool identityIncomplete =
            requiredFields.Values.Any(string.IsNullOrWhiteSpace) ||
            !string.Equals(envelope.SchemaVersion, "3.0", StringComparison.Ordinal) ||
            !string.Equals(envelope.SourceFormat, "v2", StringComparison.Ordinal) ||
            !string.Equals(envelope.CheckpointId, pathCheckpointId, StringComparison.Ordinal) ||
            !string.Equals(envelope.RunId, taskId.Value, StringComparison.Ordinal) ||
            envelope.AttemptId is null ||
            !pathCheckpointId.StartsWith(envelope.AttemptId + "-", StringComparison.Ordinal);
        if (identityIncomplete) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "检查点身份不完整或不属于该任务/引用 / Checkpoint identity is incomplete or does not belong to this task/reference: "
                    + string.Join(", ", requiredFields.Select(pair => $"{pair.Key}={(string.IsNullOrWhiteSpace(pair.Value) ? "<missing>" : "set")}"))));
        }
        string? expectedModel = payload.Scheduling?.ModelFingerprint
            ?? (payload.Extension is { } extension && extension.TryGetValue("modelFingerprint", out string? fromExtension)
                ? fromExtension
                : null);
        if (expectedModel is null &&
            payload.ModelData.RawBytes is { } rawBytes &&
            string.Equals(payload.ModelData.Format, "ospf-cp-snapshot-json", StringComparison.Ordinal)) {
            expectedModel = PortableCheckpointCodec.Sha256(System.Text.Encoding.UTF8.GetString(rawBytes));
        }
        string? expectedConfiguration = ReadSchedulingMetadata(payload, "configurationFingerprint");
        string? expectedSolver = ReadSchedulingMetadata(payload, "solverFingerprint");
        if (string.IsNullOrWhiteSpace(expectedModel) ||
            string.IsNullOrWhiteSpace(expectedConfiguration) ||
            string.IsNullOrWhiteSpace(expectedSolver)) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "恢复载荷必须声明 model、configuration 与 solver 指纹 / "
                    + "Resume payload must declare model, configuration, and solver fingerprints"));
        }
        var mismatched = new List<string>();
        if (!string.Equals(expectedModel, envelope.ModelFingerprint, StringComparison.Ordinal)) {
            mismatched.Add("modelFingerprint");
        }
        if (!string.Equals(expectedConfiguration, envelope.ConfigurationFingerprint, StringComparison.Ordinal)) {
            mismatched.Add("configurationFingerprint");
        }
        if (!string.Equals(expectedSolver, envelope.SolverFingerprint, StringComparison.Ordinal)) {
            mismatched.Add("solverFingerprint");
        }
        if (mismatched.Count > 0) {
            return Results.Failed<Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "恢复载荷身份与所选检查点不匹配 / Resume payload identity does not match the selected checkpoint: "
                    + string.Join(",", mismatched)));
        }
        _ = sliceId;
        return Results.Ok(envelope);
    }

    /// <summary>
    /// 读取调度元数据中的指纹 / Read a fingerprint from scheduling metadata.
    /// </summary>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="key">字段名 / Field name.</param>
    /// <returns>指纹值或 null / Fingerprint value, or null.</returns>
    private static string? ReadSchedulingMetadata(SolvePayload payload, string key) {
        if (payload.Scheduling?.Metadata is { } metadata &&
            metadata.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value)) {
            return value;
        }
        if (payload.Extension is { } extension &&
            extension.TryGetValue(key, out string? fromExtension) &&
            !string.IsNullOrWhiteSpace(fromExtension)) {
            return fromExtension;
        }
        return null;
    }

    /// <summary>
    /// 默认载荷路径 / Default payload path.
    /// </summary>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <returns>载荷对象路径 / Payload object path.</returns>
    private static ObjectPath DefaultPayloadPath(TaskId taskId, SliceId sliceId, TenantId tenantId) =>
        new($"{tenantId.Value}/payload/{taskId.Value}/{sliceId.Value}");

    /// <summary>
    /// 默认请求标识派生 / Default request-id derivation.
    /// </summary>
    /// <remarks>
    /// 基线由调用方注入 `requestIdProvider`；未注入时按 task/slice/tenant 派生一个确定性标识，
    /// 使同一提交可重试且不会因随机值产生重复任务。
    /// The baseline injects `requestIdProvider`; without one, a deterministic identifier is derived from
    /// task/slice/tenant so a retried submission stays idempotent instead of spawning a duplicate task.
    /// </remarks>
    private static RequestId DefaultRequestId(TaskId taskId, SliceId sliceId, TenantId tenantId) =>
        new($"{tenantId.Value}:{taskId.Value}:{sliceId.Value}");

    private static System.Text.Json.JsonElement? ParseBody(string body) {
        try {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException) {
            return null;
        }
    }

    private static bool IsTerminal(string? status) => status switch {
        "COMPLETED" or "STOPPED" or "FAILED" => true,
        _ => false,
    };

    private static bool IsConfirmedStop(string? status) =>
        status is "STOPPED" or "COMPLETED";

    /// <summary>
    /// 读取信封 <c>data</c> 内的字符串字段 / Read a string field inside the envelope's <c>data</c>.
    /// </summary>
    /// <remarks>
    /// 基线的任务视图与停止响应都把字段放在 <c>{code, message, data}</c> 信封内。
    /// Baseline task views and stop responses carry their fields inside a <c>{code, message, data}</c> envelope.
    /// </remarks>
    /// <param name="body">响应体 / Response body.</param>
    /// <param name="name">字段名 / Field name.</param>
    /// <returns>字段值；缺失为 null / Field value, or null when absent.</returns>
    private static string? ReadEnvelopeString(string? body, string name) {
        string? data = ReadEnvelopeData(body, out _, out _);
        if (data is null) {
            return null;
        }
        return ReadString(ParseBody(data), name);
    }

    private static string? ReadString(System.Text.Json.JsonElement? root, string name) {
        if (root is not { ValueKind: JsonValueKind.Object } element) {
            return null;
        }
        foreach (JsonProperty property in element.EnumerateObject()) {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String) {
                string? value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        return null;
    }
}

// Internal DTOs for JSON deserialization
internal sealed record RemoteTaskView {
    public string? Status { get; init; }
    public bool? Feasible { get; init; }
    public double? ObjectiveValue { get; init; }
    public double? Gap { get; init; }
    public long? ElapsedMs { get; init; }
    public string? Message { get; init; }
}
