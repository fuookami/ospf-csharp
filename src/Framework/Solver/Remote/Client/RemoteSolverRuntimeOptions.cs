// 远程求解运行时配置 / Remote solve runtime options.
// 与`RemoteSolverRuntimeConfig` 对应：由调用方注入任务／切片标识、节点、租户与调度参数，
// 而不是由适配器自己编造。
// `RemoteSolverRuntimeConfig`: the caller injects the task/slice
// identity, node, tenant and scheduling parameters instead of the adapter inventing them.

using System;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程求解运行时配置 / Remote solve runtime options.
/// </summary>
/// <remarks>
/// 默认值是**可用的兜底**而不是建议：任务与切片标识默认各生成一个新 guid（每次调用都是新任务），节点默认
/// 取机器名，租户默认 <c>default</c>。真正部署时调用方应注入稳定的标识来源——尤其是 <c>taskId</c>，因为
/// 它同时决定幂等性与远程侧的任务归属。
///
/// The defaults are a **usable fallback**, not a recommendation: task and slice identifiers each default to a
/// fresh guid (so every call is a new task), the node defaults to the machine name and the tenant to
/// <c>default</c>. A real deployment should inject stable sources — especially <c>taskId</c>, which governs
/// both idempotency and remote task ownership.
/// </remarks>
public sealed record RemoteSolverRuntimeOptions {
    /// <summary>默认切片时间片 / Default slice quantum.</summary>
    public static readonly TimeSpan DefaultQuantum = TimeSpan.FromSeconds(4);

    /// <summary>默认最大轮数 / Default maximum rounds.</summary>
    public const ulong DefaultMaxRounds = 64UL;

    /// <summary>默认租户 / Default tenant.</summary>
    public const string DefaultTenantId = "default";

    /// <summary>任务标识提供者 / Task identifier provider.</summary>
    public Func<TaskId> TaskIdProvider { get; init; } =
        static () => new TaskId(Guid.NewGuid().ToString("N"));

    /// <summary>切片标识提供者 / Slice identifier provider.</summary>
    public Func<SliceId> SliceIdProvider { get; init; } =
        static () => new SliceId(Guid.NewGuid().ToString("N"));

    /// <summary>节点标识 / Node identifier.</summary>
    public string NodeId { get; init; } = Environment.MachineName;

    /// <summary>租户标识 / Tenant identifier.</summary>
    public string TenantId { get; init; } = DefaultTenantId;

    /// <summary>切片时间片 / Slice quantum.</summary>
    public TimeSpan Quantum { get; init; } = DefaultQuantum;

    /// <summary>最大轮数 / Maximum rounds.</summary>
    public ulong MaxRounds { get; init; } = DefaultMaxRounds;
}
