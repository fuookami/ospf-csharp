#nullable enable

namespace Fuookami.Ospf.Core.Solver.Iis;
/// <summary>
/// IIS（不可行不可约子集）计算配置。
/// IIS (Irreducible Infeasible Subsystem) computation configuration.
/// </summary>
public sealed record IisConfig {
    /// <summary>是否启用 IIS 计算 / Enable IIS computation</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>最大 IIS 迭代次数 / Maximum IIS iterations</summary>
    public int MaxIterations { get; init; } = 1000;
    /// <summary>IIS 计算超时（秒）/ IIS computation timeout (seconds)</summary>
    public double TimeoutSeconds { get; init; } = 300.0;
}
