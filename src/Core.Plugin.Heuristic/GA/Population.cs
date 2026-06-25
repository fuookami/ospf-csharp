#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

using Chromosome = Fuookami.Ospf.Core.Solver.Heuristic.SolutionWithFitness<Fuookami.Ospf.Math.Algebra.Number.Flt64, Fuookami.Ospf.Math.Algebra.Number.Flt64>;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Ga
{
    /// <summary>
    /// 染色体类型，等同于 SolutionWithFitness / Chromosome type, alias for SolutionWithFitness.
    /// </summary>
    internal static class ChromosomeAlias
    {
        // Chromosome is defined as a top-level using alias above.
        // This class exists solely to anchor the namespace.
    }
}
