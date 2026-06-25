#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Mvo;

public class MvoPolicy<ObjValue, V> : HeuristicPolicy, IAbstractMvoPolicy<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<V, Flt64> _fromValue;
    private readonly Func<Flt64, V> _intoValue;

    public override string Name => "MVO";
    public Flt64 MinWep { get; init; } = new(0.2);
    public Flt64 MaxWep { get; init; } = Flt64.One;
    public Random Random { get; init; } = Random.Shared;

    public MvoPolicy(Func<V, Flt64> fromValue, Func<Flt64, V> intoValue) {
        _fromValue = fromValue ?? throw new ArgumentNullException(nameof(fromValue));
        _intoValue = intoValue ?? throw new ArgumentNullException(nameof(intoValue));
    }

    public Flt64 Wep(Iteration iteration) {
        double ratio = MaxIterations > 0 ? (double)iteration.Current / MaxIterations : 0;
        return MinWep + (MaxWep - MinWep) * new Flt64(ratio);
    }

    public Flt64 Tdr(Iteration iteration) {
        double ratio = MaxIterations > 0 ? (double)iteration.Current / MaxIterations : 0;
        return Flt64.One - new Flt64(System.Math.Pow(ratio, 1.0 / 6.0));
    }

    public IReadOnlyList<IReadOnlyList<V>> TransformUniverses(
        Iteration iteration,
        IReadOnlyList<V> bestSolution,
        IReadOnlyList<SolutionWithFitness<ObjValue, V>> solutions,
        Flt64 wep,
        Flt64 tdr) {
        var result = new List<IReadOnlyList<V>>(solutions.Count);
        var bestFlt64 = new List<Flt64>(bestSolution.Count);
        for (int i = 0; i < bestSolution.Count; i++) {
            bestFlt64.Add(_fromValue(bestSolution[i]));
        }

        for (int u = 0; u < solutions.Count; u++) {
            SolutionWithFitness<ObjValue, V> universe = solutions[u];
            var newSolution = new List<V>(universe.Solution.Count);
            for (int d = 0; d < universe.Solution.Count; d++) {
                var r = new Flt64(Random.NextDouble());
                Flt64 newVal;
                if (r < wep) {
                    var r2 = new Flt64(Random.NextDouble());
                    newVal = r2 < new Flt64(0.5)
                        ? bestFlt64[d] + tdr * new Flt64(Random.NextDouble())
                        : bestFlt64[d] - tdr * new Flt64(Random.NextDouble());
                }
                else {
                    int selectedIdx = Random.Next(solutions.Count);
                    newVal = _fromValue(solutions[selectedIdx].Solution[d]);
                }
                newSolution.Add(_intoValue(newVal));
            }
            result.Add(newSolution);
        }
        return result;
    }
}
