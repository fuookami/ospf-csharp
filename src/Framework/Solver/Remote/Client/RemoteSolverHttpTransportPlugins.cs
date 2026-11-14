#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

// 传输插件注册表逐字段对照固定 Kotlin 12fdb587 的 RemoteSolverHttpTransportPlugin.kt。
// The transport plugin registry mirrors the pinned Kotlin RemoteSolverHttpTransportPlugin.kt.

/// <summary>
/// HTTP 传输配置 / HTTP transport config.
/// </summary>
/// <param name="ConnectTimeoutMs">连接超时毫秒 / Connect timeout in milliseconds.</param>
/// <param name="RequestTimeoutMs">请求超时毫秒 / Request timeout in milliseconds.</param>
/// <param name="Headers">默认请求头 / Default request headers.</param>
/// <param name="Properties">插件扩展配置 / Plugin extension properties.</param>
public sealed record RemoteSolverHttpTransportConfig(
    long? ConnectTimeoutMs = null,
    long? RequestTimeoutMs = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyDictionary<string, string>? Properties = null);

/// <summary>
/// HTTP 传输插件 / HTTP transport plugin.
/// </summary>
public interface IRemoteSolverHttpTransportPlugin {
    /// <summary>插件名称 / Plugin name.</summary>
    string Name { get; }

    /// <summary>
    /// 创建 HTTP 传输 / Create an HTTP transport.
    /// </summary>
    /// <param name="config">HTTP 传输配置 / HTTP transport config.</param>
    /// <returns>传输委托 / Transport delegate.</returns>
    RemoteSolverHttpTransport Create(RemoteSolverHttpTransportConfig config);
}

/// <summary>
/// 基于 <see cref="HttpClient"/> 的默认传输插件 / Default transport plugin backed by <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// 与基线一致：默认插件名为 <c>jdk</c>；连接超时与请求超时都按毫秒整型配置。
/// Matches the baseline: the default plugin is named <c>jdk</c>, and both connect and request timeouts are configured in whole milliseconds.
/// </remarks>
public sealed class HttpClientRemoteSolverHttpTransportPlugin : IRemoteSolverHttpTransportPlugin {
    /// <inheritdoc/>
    public string Name => "jdk";

    /// <inheritdoc/>
    public RemoteSolverHttpTransport Create(RemoteSolverHttpTransportConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        HttpClient client;
        if (config.ConnectTimeoutMs is { } connectMs) {
            if (connectMs < 0) {
                throw new ArgumentOutOfRangeException(nameof(config), "连接超时不能为负 / Connect timeout must not be negative");
            }
            client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(connectMs) };
        }
        else {
            client = new HttpClient();
        }
        if (config.RequestTimeoutMs is { } requestMs) {
            if (requestMs < 0) {
                throw new ArgumentOutOfRangeException(nameof(config), "请求超时不能为负 / Request timeout must not be negative");
            }
            client.Timeout = TimeSpan.FromMilliseconds(requestMs);
        }
        if (config.Headers is { } headers) {
            foreach ((string key, string value) in headers) {
                if (string.IsNullOrWhiteSpace(key)) {
                    continue;
                }
                client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
        }
        var transport = new HttpClientRemoteSolverTransport(client);
        return transport.ToTransport();
    }
}

/// <summary>
/// HTTP 传输插件注册表 / HTTP transport plugin registry.
/// </summary>
/// <remarks>
/// 名称按去空白后小写归一；未注册名称返回 <see cref="ErrorCode.SolverNotFound"/>，不静默回退到默认插件。
/// Names normalize to trimmed lowercase; an unregistered name returns <see cref="ErrorCode.SolverNotFound"/> rather than silently falling back.
/// </remarks>
public static class RemoteSolverHttpTransportPlugins {
    private static readonly ConcurrentDictionary<string, IRemoteSolverHttpTransportPlugin> Plugins = new();
    private static readonly IRemoteSolverHttpTransportPlugin DefaultPlugin =
        new HttpClientRemoteSolverHttpTransportPlugin();

    static RemoteSolverHttpTransportPlugins() {
        Register(DefaultPlugin);
    }

    /// <summary>注册插件 / Register a plugin.</summary>
    /// <param name="plugin">HTTP 传输插件 / HTTP transport plugin.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public static Try Register(IRemoteSolverHttpTransportPlugin plugin) {
        if (plugin is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "传输插件为空 / Transport plugin is null"));
        }
        string key = Normalize(plugin.Name);
        if (string.IsNullOrWhiteSpace(key)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "传输插件名称不能为空 / Transport plugin name must not be blank"));
        }
        Plugins[key] = plugin;
        return Results.OkInstance;
    }

    /// <summary>查找插件 / Resolve a plugin.</summary>
    /// <param name="name">插件名称 / Plugin name.</param>
    /// <returns>插件或结构化错误 / Plugin or a structured error.</returns>
    public static Result<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> Resolve(string name) {
        string key = Normalize(name);
        if (Plugins.TryGetValue(key, out IRemoteSolverHttpTransportPlugin? plugin)) {
            return Results.Ok(plugin);
        }
        return Results.Failed<IRemoteSolverHttpTransportPlugin>(new Err<ErrorCode>(
            ErrorCode.SolverNotFound,
            $"传输插件未注册 / HTTP transport plugin is not registered: {name}"));
    }

    /// <summary>创建传输 / Create a transport.</summary>
    /// <param name="name">插件名称 / Plugin name.</param>
    /// <param name="config">传输配置 / Transport config.</param>
    /// <returns>传输委托或结构化错误 / Transport delegate or a structured error.</returns>
    public static Result<RemoteSolverHttpTransport, ErrorCode, Error<ErrorCode>> Create(
        string name,
        RemoteSolverHttpTransportConfig? config = null) {
        Result<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> resolved = Resolve(name);
        if (!resolved.IsOk) {
            return resolved switch {
                Failed<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> failed =>
                    Results.Failed<RemoteSolverHttpTransport>(failed.Error),
                Fatal<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<RemoteSolverHttpTransport, ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<RemoteSolverHttpTransport>(new Err<ErrorCode>(
                    ErrorCode.SolverNotFound,
                    $"传输插件未注册 / HTTP transport plugin is not registered: {name}")),
            };
        }
        try {
            return Results.Ok(resolved.Value.Create(config ?? new RemoteSolverHttpTransportConfig()));
        }
        catch (ArgumentException exception) {
            return Results.Failed<RemoteSolverHttpTransport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"传输配置无效：{exception.Message} / Invalid transport config: {exception.Message}"));
        }
    }

    /// <summary>默认插件 / Default plugin.</summary>
    /// <returns>默认传输插件 / Default transport plugin.</returns>
    public static IRemoteSolverHttpTransportPlugin Default() => DefaultPlugin;

    /// <summary>已注册插件名称 / Registered plugin names.</summary>
    /// <returns>名称集合 / Name set.</returns>
    public static IReadOnlyCollection<string> Names() =>
        Plugins.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

    private static string Normalize(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();
}
