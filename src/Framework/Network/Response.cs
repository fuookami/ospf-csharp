#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Network
{
    /// <summary>
    /// 请求授权接口 / Request authorization interface.
    /// </summary>
    public interface IAuthorization
    {
        /// <summary>应用授权到请求 / Apply authorization to request.</summary>
        Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// HTTP Basic 认证 / HTTP Basic authorization.
    /// </summary>
    public sealed record BasicAuthorization(string Username, string Password) : IAuthorization
    {
        /// <inheritdoc/>
        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 响应重试策略 / Response retry policy.
    /// </summary>
    public sealed record ResponseRetry(
        int Times = 3,
        TimeSpan? Delay = null,
        Func<HttpResponseMessage, bool>? Condition = null);

    /// <summary>
    /// HTTP 响应发送辅助方法 / HTTP response sending helper methods.
    /// </summary>
    public static class Response
    {
        /// <summary>
        /// 发送 HTTP POST 请求（JSON）/ Send HTTP POST request (JSON).
        /// </summary>
        public static async Task<Result<string, ErrorCode, Error<ErrorCode>>> SendAsync(
            HttpClient httpClient,
            string url,
            object payload,
            IAuthorization? authorization = null,
            ResponseRetry? retry = null,
            JsonSerializerOptions? jsonOptions = null,
            CancellationToken cancellationToken = default)
        {
            var maxAttempts = (retry?.Times ?? 1);
            var delay = retry?.Delay ?? TimeSpan.FromSeconds(1);

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    var json = JsonSerializer.Serialize(payload, jsonOptions);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    if (authorization is not null)
                    {
                        await authorization.ApplyAsync(request, cancellationToken);
                    }

                    using var response = await httpClient.SendAsync(request, cancellationToken);

                    if (retry?.Condition is not null && retry.Condition(response) && attempt < maxAttempts - 1)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    var body = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        return Results.Ok(body);
                    }

                    return new Failed<string, ErrorCode, Error<ErrorCode>>(
                        new Utils.Error.Err<ErrorCode>(ErrorCode.ApplicationFailed,
                            $"HTTP {(int)response.StatusCode}: {body}"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt < maxAttempts - 1)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }
                    return new Failed<string, ErrorCode, Error<ErrorCode>>(
                        new Utils.Error.Err<ErrorCode>(ErrorCode.ApplicationFailed, ex.Message));
                }
            }

            return new Failed<string, ErrorCode, Error<ErrorCode>>(
                new Utils.Error.Err<ErrorCode>(ErrorCode.ApplicationFailed, "Max retry attempts exceeded"));
        }
    }
}
