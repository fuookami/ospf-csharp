#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Wolf = Fuookami.Ospf.Core.Solver.Heuristic.SolutionWithFitness<Fuookami.Ospf.Math.Algebra.Number.Flt64, Fuookami.Ospf.Math.Algebra.Number.Flt64>;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gwo;
/// <summary>
/// 灰狼优化器策略接口 / Grey Wolf Optimizer policy interface.
/// </summary>
public interface IGwoPolicy : IAbstractHeuristicPolicy {
    /// <summary>判断搜索是否结束 / Check if the search should terminate.</summary>
    bool Finished(Iteration iteration);

    /// <summary>计算收敛系数 a / Calculate convergence coefficient a (shrinks over iterations).</summary>
    Flt64 A(Iteration iteration);

    /// <summary>移动狼 / Move a wolf towards alpha, beta, delta.</summary>
    Wolf Move(
        Iteration iteration,
        Wolf wolf,
        Wolf alpha,
        Wolf beta,
        Wolf delta,
        Flt64 a,
        Func<IReadOnlyList<Flt64>, Flt64> objectiveFunc);
}
