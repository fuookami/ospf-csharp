#nullable enable

using System;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 求解器的远程连接配置 / Connection configuration for the Gurobi solver (remote compute server).
/// </summary>
/// <param name="Server">服务器地址 / Compute server address.</param>
/// <param name="Password">服务器密码 / Compute server password.</param>
/// <param name="ConnectionTime">连接超时 / Connection timeout.</param>
public sealed record GurobiSolverConfig(
    string? Server = null,
    string? Password = null,
    TimeSpan? ConnectionTime = null);
