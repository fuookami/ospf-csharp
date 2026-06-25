#nullable enable

using Fuookami.Ospf.Core.Solver.Config;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 二次求解器接口，扩展 IAbstractQuadraticSolver 并提供配置驱动的模型转储能力。
/// Quadratic solver extending IAbstractQuadraticSolver with configuration-driven model dumping.
/// </summary>
public interface IQuadraticSolver : IAbstractQuadraticSolver {
    /// <summary>求解器配置 / Solver configuration</summary>
    SolverConfig Config { get; }
}
