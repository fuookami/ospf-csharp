#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// CSP1D 排程入口（最小实现）/ CSP1D schedule entry point (minimal implementation).
///
/// 以列生成作为默认排程求解路径。
/// Uses column generation as the default scheduling path.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dSchedule<V> where V : struct {
    private readonly Csp1dColumnGeneration<V> _columnGeneration;

    /// <summary>
    /// 创建排程入口 / Create schedule entry.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="columnGeneration">列生成实例（可选）/ Column generation instance (optional).</param>
    public Csp1dSchedule(
        IColumnGenerationSolver solver,
        Csp1dColumnGeneration<V>? columnGeneration = null) {
        _columnGeneration = columnGeneration ?? new Csp1dColumnGeneration<V>(solver);
    }

    /// <summary>
    /// 求解 / Solve.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <returns>CSP1D 解 / CSP1D solution.</returns>
    public async Task<Csp1dSolution<V>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) => await _columnGeneration.SolveAsync(problem, solveConfig);
}
