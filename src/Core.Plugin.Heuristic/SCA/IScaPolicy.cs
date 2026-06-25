#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Sca;

public interface IAbstractScaPolicy<ObjValue, V> : IAbstractHeuristicPolicy
    where V : struct, IRealNumber<V>, INumberField<V> {
    Flt64 R1(Iteration iteration);
    IReadOnlyList<Flt64> R2(Iteration iteration, int dimension);
    IReadOnlyList<Flt64> R3(Iteration iteration, int dimension);
    IReadOnlyList<V> Move(
        Iteration iteration,
        IReadOnlyList<V> solution,
        IReadOnlyList<V> bestSolution,
        Flt64 r1,
        IReadOnlyList<Flt64> r2,
        IReadOnlyList<Flt64> r3);
}
