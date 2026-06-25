#nullable enable

using Fuookami.Ospf.Core.Solver.Config;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 线性求解器接口，扩展 IAbstractLinearSolver 并提供配置驱动的模型转储能力。
/// Linear solver extending IAbstractLinearSolver with configuration-driven model dumping.
/// </summary>
public interface ILinearSolver : IAbstractLinearSolver {
    /// <summary>求解器配置 / Solver configuration</summary>
    SolverConfig Config { get; }
}
