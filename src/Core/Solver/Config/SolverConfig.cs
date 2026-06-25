#nullable enable

namespace Fuookami.Ospf.Core.Solver.Config
{
    /// <summary>
    /// 求解器配置基类，包含模型转储标志。
    /// Solver configuration base, containing model dumping flags.
    /// </summary>
    public abstract record SolverConfig
    {
        /// <summary>是否转储中间模型边界 / Dump intermediate model bounds</summary>
        public bool DumpIntermediateModelBounds { get; init; }
        /// <summary>是否转储中间模型强制边界 / Dump intermediate model force bounds</summary>
        public bool DumpIntermediateModelForceBounds { get; init; }
        /// <summary>是否并发转储中间模型 / Dump intermediate model concurrently</summary>
        public bool DumpIntermediateModelConcurrent { get; init; }
        /// <summary>是否并发转储机制模型 / Dump mechanism model concurrently</summary>
        public bool DumpMechanismModelConcurrent { get; init; }
        /// <summary>是否阻塞转储机制模型 / Dump mechanism model blocking</summary>
        public bool DumpMechanismModelBlocking { get; init; }
    }

    /// <summary>
    /// COPT 求解器配置 / COPT solver configuration.
    /// </summary>
    public sealed record CoptSolverConfig : SolverConfig
    {
        /// <summary>COPT 线程数 / COPT thread count</summary>
        public int Threads { get; init; } = 1;
        /// <summary>COPT 时间限制（秒）/ COPT time limit (seconds)</summary>
        public double TimeLimit { get; init; } = double.PositiveInfinity;
        /// <summary>COPT MIP 间隙容忍度 / COPT MIP gap tolerance</summary>
        public double MipGap { get; init; } = 1e-4;
    }

    /// <summary>
    /// Gurobi 求解器配置 / Gurobi solver configuration.
    /// </summary>
    public sealed record GurobiSolverConfig : SolverConfig
    {
        /// <summary>Gurobi 线程数 / Gurobi thread count</summary>
        public int Threads { get; init; } = 1;
        /// <summary>Gurobi 时间限制（秒）/ Gurobi time limit (seconds)</summary>
        public double TimeLimit { get; init; } = double.PositiveInfinity;
        /// <summary>Gurobi MIP 间隙容忍度 / Gurobi MIP gap tolerance</summary>
        public double MipGap { get; init; } = 1e-4;
    }

    /// <summary>
    /// SCIP 求解器配置 / SCIP solver configuration.
    /// </summary>
    public sealed record ScipSolverConfig : SolverConfig;
}
