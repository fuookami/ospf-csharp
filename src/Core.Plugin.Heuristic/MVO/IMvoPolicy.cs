#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Mvo
{
    public interface IAbstractMvoPolicy<ObjValue, V> : IAbstractHeuristicPolicy
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        Flt64 Wep(Iteration iteration);
        Flt64 Tdr(Iteration iteration);
        IReadOnlyList<IReadOnlyList<V>> TransformUniverses(
            Iteration iteration,
            IReadOnlyList<V> bestSolution,
            IReadOnlyList<SolutionWithFitness<ObjValue, V>> solutions,
            Flt64 wep,
            Flt64 tdr);
    }
}
