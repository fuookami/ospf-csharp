#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client
{
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
    public enum RemoteSolverHttpResumeMode
    {
        /// <summary>从头开始 / Start from scratch</summary>
        Fresh,
        /// <summary>从检查点恢复 / Resume from checkpoint</summary>
        FromCheckpoint
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
    public sealed class HttpClientRemoteSolverTransport
    {
        private readonly HttpClient _httpClient;

        public HttpClientRemoteSolverTransport(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>发送 HTTP 请求 / Send HTTP request.</summary>
        public async Task<RemoteSolverHttpResponse> SendAsync(RemoteSolverHttpRequest request)
        {
            using var httpRequest = new HttpRequestMessage(request.Method, request.Url);
            if (request.Body is not null)
            {
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            }
            if (request.Headers is not null)
            {
                foreach (var (key, value) in request.Headers)
                {
                    httpRequest.Headers.TryAddWithoutValidation(key, value);
                }
            }

            using var response = await _httpClient.SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            return new RemoteSolverHttpResponse((int)response.StatusCode, body);
        }

        /// <summary>转换为传输委托 / Convert to transport delegate.</summary>
        public RemoteSolverHttpTransport ToTransport() => SendAsync;
    }

    /// <summary>
    /// 远程求解器 HTTP 客户端 / Remote solver HTTP client.
    /// </summary>
    public sealed class RemoteSolverHttpClient : ISolverExecutionPort
    {
        private readonly RemoteSolverHttpTransport _transport;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly TenantId _tenantId;
        private readonly Func<TraceId>? _traceIdProvider;

        public RemoteSolverHttpClient(
            RemoteSolverHttpTransport transport,
            string baseUrl,
            TenantId tenantId,
            Func<TraceId>? traceIdProvider = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            _transport = transport;
            _baseUrl = baseUrl.TrimEnd('/');
            _tenantId = tenantId;
            _traceIdProvider = traceIdProvider;
            _jsonOptions = jsonOptions ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> StartAsync(
            SolvePayload payload, TaskId taskId, SliceId sliceId, NodeId nodeId, TenantId tenantId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = JsonSerializer.Serialize(new { taskId = taskId.Value, sliceId = sliceId.Value, nodeId = nodeId.Value, tenantId = tenantId.Value }, _jsonOptions);
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Post, $"{_baseUrl}/tasks/start", requestBody));
            if (response.StatusCode is >= 200 and < 300)
            {
                return Results.Ok(new ExecutionHandle(new HandleId(taskId.Value), taskId, sliceId, nodeId, tenantId));
            }
            return new Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"Start failed: {response.Body}"));
        }

        public async Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> ResumeAsync(
            SolvePayload payload, ObjectRef snapshot, TaskId taskId, SliceId sliceId, NodeId nodeId, TenantId tenantId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = JsonSerializer.Serialize(new { taskId = taskId.Value, sliceId = sliceId.Value, snapshotPath = snapshot.Path.Value }, _jsonOptions);
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Post, $"{_baseUrl}/tasks/resume", requestBody));
            if (response.StatusCode is >= 200 and < 300)
            {
                return Results.Ok(new ExecutionHandle(new HandleId(taskId.Value), taskId, sliceId, nodeId, tenantId));
            }
            return new Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"Resume failed: {response.Body}"));
        }

        public async Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> AwaitSliceEndAsync(
            ExecutionHandle handle, TimeSpan quantum,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/tasks/{handle.TaskId.Value}/slices/{handle.SliceId.Value}/await?quantum={quantum.TotalMilliseconds}";
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Get, url));
            if (response.StatusCode is >= 200 and < 300 && response.Body is not null)
            {
                var view = JsonSerializer.Deserialize<RemoteTaskView>(response.Body, _jsonOptions);
                if (view is not null)
                {
                    return Results.Ok(new SliceResult(
                        Completed: view.Status == "completed",
                        Feasible: view.Feasible ?? false,
                        ObjectiveValue: view.ObjectiveValue is { } obj ? new Flt64(obj) : null,
                        Gap: view.Gap is { } gap ? new Flt64(gap) : null,
                        Elapsed: TimeSpan.FromMilliseconds(view.ElapsedMs ?? 0),
                        Message: view.Message));
                }
            }
            return new Failed<SliceResult, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"AwaitSliceEnd failed: {response.Body}"));
        }

        public async Task<Result<ObjectRef?, ErrorCode, Error<ErrorCode>>> ExportCheckpointAsync(
            ExecutionHandle handle,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/tasks/{handle.TaskId.Value}/checkpoint";
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Post, url));
            if (response.StatusCode is >= 200 and < 300)
            {
                return Results.Ok<ObjectRef?>(null);
            }
            return new Failed<ObjectRef?, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"ExportCheckpoint failed: {response.Body}"));
        }

        public async Task<Result<SolveResult?, ErrorCode, Error<ErrorCode>>> FetchFinalResultAsync(
            ExecutionHandle handle,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/tasks/{handle.TaskId.Value}/result";
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Get, url));
            if (response.StatusCode is >= 200 and < 300)
            {
                return Results.Ok<SolveResult?>(null);
            }
            return new Failed<SolveResult?, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"FetchFinalResult failed: {response.Body}"));
        }

        public async Task<Result<bool, ErrorCode, Error<ErrorCode>>> StopAsync(
            ExecutionHandle handle,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/tasks/{handle.TaskId.Value}/stop";
            var response = await _transport(new RemoteSolverHttpRequest(HttpMethod.Post, url));
            if (response.StatusCode is >= 200 and < 300)
            {
                return Results.Ok(true);
            }
            return new Failed<bool, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, $"Stop failed: {response.Body}"));
        }
    }

    // Internal DTOs for JSON deserialization
    internal sealed record RemoteTaskView
    {
        public string? Status { get; init; }
        public bool? Feasible { get; init; }
        public double? ObjectiveValue { get; init; }
        public double? Gap { get; init; }
        public long? ElapsedMs { get; init; }
        public string? Message { get; init; }
    }
}
