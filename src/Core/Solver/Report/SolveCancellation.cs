#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>取消来源 / Cancellation source.</summary>
public enum CancellationSource {
    /// <summary>调用方 / Caller.</summary>
    Caller,
    /// <summary>回调 / Callback.</summary>
    Callback,
    /// <summary>任务中止 / Task abort.</summary>
    Coroutine,
    /// <summary>Future.</summary>
    Future,
    /// <summary>组合求解淘汰 / Combinatorial loser.</summary>
    Combinatorial,
    /// <summary>远程停止 / Remote stop.</summary>
    Remote,
    /// <summary>超时 / Timeout.</summary>
    Timeout,
    /// <summary>兜底来源 / Catch-all source.</summary>
    Other,
}

/// <summary>取消来源线格式扩展 / Cancellation-source wire format extensions.</summary>
public static class CancellationSourceExtensions {
    /// <summary>转换为线格式规范码 / Convert to the canonical wire code.</summary>
    /// <param name="source">取消来源 / Cancellation source.</param>
    /// <returns>规范线格式码 / Canonical wire code.</returns>
    public static string ToWireCode(this CancellationSource source) => source switch {
        CancellationSource.Caller => "user",
        CancellationSource.Callback => "callback",
        CancellationSource.Coroutine => "taskAbort",
        CancellationSource.Future => "future",
        CancellationSource.Combinatorial => "frameworkLoser",
        CancellationSource.Remote => "remoteStop",
        CancellationSource.Timeout => "timeout",
        _ => "external",
    };

    /// <summary>解析线格式规范码 / Parse a canonical wire code.</summary>
    /// <param name="code">规范线格式码 / Canonical wire code.</param>
    /// <returns>取消来源；未知码返回 Other / Cancellation source, defaulting to Other.</returns>
    public static CancellationSource FromWireCode(string code) => code switch {
        "user" => CancellationSource.Caller,
        "callback" => CancellationSource.Callback,
        "taskAbort" => CancellationSource.Coroutine,
        "future" => CancellationSource.Future,
        "frameworkLoser" => CancellationSource.Combinatorial,
        "remoteStop" => CancellationSource.Remote,
        "timeout" => CancellationSource.Timeout,
        _ => CancellationSource.Other,
    };
}

/// <summary>取消事实 / Cancellation fact.</summary>
public sealed record CancellationRecord(
    CancellationSource Source,
    DateTimeOffset RequestedAt,
    string? Reason = null,
    string? WireOrigin = null);

/// <summary>求解取消令牌 / Solve cancellation token.</summary>
public sealed class SolveCancellationToken {
    private readonly object _gate = new();
    private readonly List<Func<CancellationRecord, Try>> _listeners = new();
    private CancellationRecord? _record;

    /// <summary>是否已经请求取消 / Whether cancellation has been requested.</summary>
    public bool IsCancellationRequested => Volatile.Read(ref _record) is not null;

    /// <summary>首次取消事实 / First cancellation record.</summary>
    public CancellationRecord? Record => Volatile.Read(ref _record);

    /// <summary>注册取消监听 / Register a cancellation listener.</summary>
    /// <param name="listener">取消监听 / Cancellation listener.</param>
    /// <returns>注册或立即执行结果 / Registration or immediate execution result.</returns>
    public Try Register(Func<CancellationRecord, Try> listener) {
        ArgumentNullException.ThrowIfNull(listener);
        CancellationRecord? existing;
        lock (_gate) {
            existing = _record;
            if (existing is null) {
                _listeners.Add(listener);
                return Results.OkInstance;
            }
        }
        return Invoke(listener, existing);
    }

    /// <summary>移除取消监听 / Remove a cancellation listener.</summary>
    /// <param name="listener">取消监听 / Cancellation listener.</param>
    /// <returns>移除结果 / Removal result.</returns>
    public Try Unregister(Func<CancellationRecord, Try> listener) {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_gate) {
            _listeners.Remove(listener);
        }
        return Results.OkInstance;
    }

    /// <summary>请求取消；重复请求保留首次事实 / Request cancellation, retaining the first fact.</summary>
    /// <param name="source">取消来源 / Cancellation source.</param>
    /// <param name="reason">取消原因 / Cancellation reason.</param>
    /// <returns>监听执行结果 / Listener execution result.</returns>
    public Try Request(
        CancellationSource source = CancellationSource.Caller,
        string? reason = null) {
        CancellationRecord record = new(
            Source: source,
            RequestedAt: DateTimeOffset.UtcNow,
            Reason: reason);
        Func<CancellationRecord, Try>[] listeners;
        lock (_gate) {
            if (_record is not null) {
                return Results.OkInstance;
            }
            _record = record;
            listeners = _listeners.ToArray();
            _listeners.Clear();
        }

        Try? failure = null;
        foreach (Func<CancellationRecord, Try> listener in listeners) {
            Try result = Invoke(listener, record);
            if (failure is null && result.IsFailed) {
                failure = result;
            }
        }
        return failure ?? Results.OkInstance;
    }

    private static Try Invoke(
        Func<CancellationRecord, Try> listener,
        CancellationRecord record) {
        try {
            return listener(record);
        }
        catch (Exception error) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"原生取消监听失败：{error.Message} / Native cancellation listener failed: {error.Message}"));
        }
    }
}

/// <summary>单次求解句柄 / Per-solve handle.</summary>
public sealed class SolveHandle {
    private SolveHandle(SolveCancellationToken token) {
        Token = token;
    }

    /// <summary>取消令牌 / Cancellation token.</summary>
    public SolveCancellationToken Token { get; }

    /// <summary>幂等请求取消 / Request cancellation idempotently.</summary>
    /// <param name="source">取消来源 / Cancellation source.</param>
    /// <param name="reason">取消原因 / Cancellation reason.</param>
    /// <returns>中断执行结果 / Interruption result.</returns>
    public Try Cancel(
        CancellationSource source = CancellationSource.Caller,
        string? reason = null) =>
        Token.Request(source, reason);

    /// <summary>创建独立求解句柄 / Create an independent solve handle.</summary>
    /// <param name="interrupt">原生中断监听 / Native interruption listener.</param>
    /// <returns>求解句柄 / Solve handle.</returns>
    public static SolveHandle Create(Func<CancellationRecord, Try>? interrupt = null) {
        SolveCancellationToken token = new();
        if (interrupt is not null) {
            token.Register(interrupt);
        }
        return new SolveHandle(token);
    }
}
