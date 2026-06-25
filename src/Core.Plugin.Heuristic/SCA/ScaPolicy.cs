#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Sca;

public class ScaPolicy<ObjValue, V> : HeuristicPolicy, IAbstractScaPolicy<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<V, Flt64> _fromValue;
    private readonly Func<Flt64, V> _intoValue;

    public override string Name => "SCA";
    public Random Random { get; init; } = Random.Shared;

    public ScaPolicy(Func<V, Flt64> fromValue, Func<Flt64, V> intoValue) {
        _fromValue = fromValue ?? throw new ArgumentNullException(nameof(fromValue));
        _intoValue = intoValue ?? throw new ArgumentNullException(nameof(intoValue));
    }

    public Flt64 R1(Iteration iteration) {
        double ratio = MaxIterations > 0 ? (double)iteration.Current / MaxIterations : 0;
        return new Flt64(2.0 * (1.0 - ratio));
    }

    public IReadOnlyList<Flt64> R2(Iteration iteration, int dimension) {
        var result = new List<Flt64>(dimension);
        for (int i = 0; i < dimension; i++) {
            result.Add(new Flt64(Random.NextDouble() * 2.0 * System.Math.PI));
        }

        return result;
    }

    public IReadOnlyList<Flt64> R3(Iteration iteration, int dimension) {
        var result = new List<Flt64>(dimension);
        for (int i = 0; i < dimension; i++) {
            result.Add(new Flt64(Random.NextDouble() * 2.0));
        }

        return result;
    }

    public IReadOnlyList<V> Move(
        Iteration iteration,
        IReadOnlyList<V> solution,
        IReadOnlyList<V> bestSolution,
        Flt64 r1,
        IReadOnlyList<Flt64> r2,
        IReadOnlyList<Flt64> r3) {
        int dim = solution.Count;
        var newSolution = new List<V>(dim);
        for (int i = 0; i < dim; i++) {
            Flt64 pos = _fromValue(solution[i]);
            Flt64 bestPos = _fromValue(bestSolution[i]);
            Flt64 delta = r3[i] * bestPos - pos;
            Flt64 newPos;
            if (new Flt64(Random.NextDouble()) < new Flt64(0.5)) {
                newPos = pos + r1 * new Flt64(System.Math.Sin(r2[i].ToDouble())) * delta;
            }
            else {
                newPos = pos + r1 * new Flt64(System.Math.Cos(r2[i].ToDouble())) * delta;
            }

            newSolution.Add(_intoValue(newPos));
        }
        return newSolution;
    }
}
