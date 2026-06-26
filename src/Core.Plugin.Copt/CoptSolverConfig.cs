#nullable enable

using Fuookami.Ospf.Core.Solver.Config;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 求解器配置 / COPT solver configuration.
/// </summary>
public sealed record CoptSolverConfig : SolverConfig {
    /// <summary>COPT 服务器地址 / COPT server address (for remote solving).</summary>
    public string? Server { get; init; }
    /// <summary>COPT 服务器端口 / COPT server port.</summary>
    public int Port { get; init; }
    /// <summary>COPT 服务器密码 / COPT server password.</summary>
    public string? Password { get; init; }
}
