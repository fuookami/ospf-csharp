#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Ga
{
    using Chromosome = SolutionWithFitness<Flt64, Flt64>;

    /// <summary>
    /// 遗传算法策略接口 / Genetic Algorithm policy interface.
    /// </summary>
    public interface IGAPolicy : IAbstractHeuristicPolicy
    {
        /// <summary>精英数量 / Elite count</summary>
        int EliteCount { get; }
        /// <summary>判断是否结束 / Check if finished</summary>
        bool Finished(Iteration iteration);
        /// <summary>选择操作 / Selection</summary>
        List<Chromosome> Select(List<Chromosome> population, int count);
        /// <summary>交叉操作 / Crossover</summary>
        List<Chromosome> Cross(List<Chromosome> parents);
        /// <summary>变异操作 / Mutation</summary>
        Chromosome Mutate(Chromosome individual, Func<IReadOnlyList<Flt64>, Flt64?> objectiveFunc);
    }
}
