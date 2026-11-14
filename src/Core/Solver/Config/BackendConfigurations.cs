#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Solver.Config;

/// <summary>
/// Gurobi 求解器的连接配置 / Connection configuration for the Gurobi solver.
/// </summary>
/// <remarks>
/// 挂在 <see cref="SolverConfig.BackendConfiguration"/> 上，密码只以脱敏形式进入快照。
/// Attached to <see cref="SolverConfig.BackendConfiguration"/>; the password only reaches snapshots redacted.
/// </remarks>
/// <param name="Server">服务器地址 / Compute server address.</param>
/// <param name="Password">服务器密码 / Compute server password.</param>
/// <param name="ConnectionTime">连接超时 / Connection timeout.</param>
public sealed record GurobiSolverConfig(
    string? Server = null,
    string? Password = null,
    TimeSpan? ConnectionTime = null) : IBackendConfiguration {
    /// <inheritdoc/>
    public string Type => "gurobi";

    /// <inheritdoc/>
    public IReadOnlyList<BackendParameter> Parameters() {
        var parameters = new List<BackendParameter>();
        if (Server is { } server) {
            parameters.Add(new BackendParameter("server", new BackendParameterValue.Text(server)));
        }
        if (Password is not null) {
            parameters.Add(new BackendParameter(
                Name: "password",
                Value: new BackendParameterValue.Text("<redacted>"),
                Sensitive: true));
        }
        if (ConnectionTime is { } connectionTime) {
            parameters.Add(new BackendParameter(
                "connectionTime",
                new BackendParameterValue.Text(connectionTime.ToString())));
        }
        parameters.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return parameters;
    }
}

/// <summary>
/// COPT 求解器的连接配置 / Connection configuration for the COPT solver.
/// </summary>
/// <param name="Server">服务器地址 / Compute server address.</param>
/// <param name="Port">服务器端口 / Compute server port.</param>
/// <param name="Password">服务器密码 / Compute server password.</param>
/// <param name="ConnectionTime">连接超时 / Connection timeout.</param>
public sealed record CoptSolverConfig(
    string? Server = null,
    long? Port = null,
    string? Password = null,
    TimeSpan? ConnectionTime = null) : IBackendConfiguration {
    /// <inheritdoc/>
    public string Type => "copt";

    /// <inheritdoc/>
    public IReadOnlyList<BackendParameter> Parameters() {
        var parameters = new List<BackendParameter>();
        if (Server is { } server) {
            parameters.Add(new BackendParameter("server", new BackendParameterValue.Text(server)));
        }
        if (Port is { } port) {
            parameters.Add(new BackendParameter("port", new BackendParameterValue.Integer(port)));
        }
        if (Password is not null) {
            parameters.Add(new BackendParameter(
                Name: "password",
                Value: new BackendParameterValue.Text("<redacted>"),
                Sensitive: true));
        }
        if (ConnectionTime is { } connectionTime) {
            parameters.Add(new BackendParameter(
                "connectionTime",
                new BackendParameterValue.Text(connectionTime.ToString())));
        }
        parameters.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return parameters;
    }
}

/// <summary>
/// SCIP 求解器的后端配置 / Backend configuration for the SCIP solver.
/// </summary>
/// <param name="Presolve">显式启用或关闭预求解 / Explicitly enable or disable presolving.</param>
/// <param name="RandomSeed">原生随机种子 / Native random seed.</param>
/// <param name="Deterministic">是否要求确定性模式 / Whether deterministic mode is requested.</param>
/// <param name="NativeParameters">额外的已脱敏原生参数 / Additional already-redacted native parameters.</param>
public sealed record ScipSolverConfig(
    bool? Presolve = null,
    long? RandomSeed = null,
    bool? Deterministic = null,
    IReadOnlyDictionary<string, BackendParameterValue>? NativeParameters = null) : IBackendConfiguration {
    /// <inheritdoc/>
    public string Type => "scip";

    /// <summary>已脱敏原生参数 / Already-redacted native parameters.</summary>
    public IReadOnlyDictionary<string, BackendParameterValue> NativeParameters { get; } =
        NativeParameters ?? new Dictionary<string, BackendParameterValue>();

    /// <inheritdoc/>
    public IReadOnlyList<BackendParameter> Parameters() {
        var parameters = new List<BackendParameter>();
        if (Presolve is { } presolve) {
            parameters.Add(new BackendParameter("presolve", new BackendParameterValue.BooleanValue(presolve)));
        }
        if (RandomSeed is { } seed) {
            parameters.Add(new BackendParameter("randomSeed", new BackendParameterValue.Integer(seed)));
        }
        if (Deterministic is { } deterministic) {
            parameters.Add(new BackendParameter("deterministic", new BackendParameterValue.BooleanValue(deterministic)));
        }
        parameters.AddRange(this.NativeParameters
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new BackendParameter(entry.Key, entry.Value)));
        parameters.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return parameters;
    }
}
