#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Wolf = Fuookami.Ospf.Core.Solver.Heuristic.SolutionWithFitness<Fuookami.Ospf.Math.Algebra.Number.Flt64, Fuookami.Ospf.Math.Algebra.Number.Flt64>;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gwo;
/// <summary>
/// 灰狼优化器默认策略 / Default Grey Wolf Optimizer policy.
/// </summary>
public class GwoPolicy : HeuristicPolicy, IGwoPolicy {
    private readonly Flt64 _minA;
    private readonly Flt64 _maxA;
    private readonly Random _random;

    /// <inheritdoc/>
    public override string Name => "GWO";

    public GwoPolicy(
        double minA = 0.02,
        double maxA = 2.2,
        int maxIterations = 1000,
        int populationSize = 100,
        int? seed = null) {
        _minA = new Flt64(minA);
        _maxA = new Flt64(maxA);
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        MaxIterations = maxIterations;
        PopulationSize = populationSize;
    }

    /// <inheritdoc/>
    public Flt64 A(Iteration iteration) {
        int maxIter = MaxIterations > 0 ? MaxIterations : 1;
        var ratio = new Flt64((double)iteration.Current / maxIter);
        return _maxA - (_maxA - _minA) * ratio;
    }

    /// <inheritdoc/>
    public Wolf Move(
        Iteration iteration,
        Wolf wolf,
        Wolf alpha,
        Wolf beta,
        Wolf delta,
        Flt64 a,
        Func<IReadOnlyList<Flt64>, Flt64> objectiveFunc) {
        int dim = wolf.Solution.Count;
        var newSolution = new List<Flt64>(dim);

        for (int i = 0; i < dim; i++) {
            Flt64 pos = wolf.Solution[i];

            var r1 = new Flt64(_random.NextDouble());
            var r2 = new Flt64(_random.NextDouble());
            Flt64 a1 = new Flt64(2.0) * a * r1 - a;
            Flt64 c1 = new Flt64(2.0) * r2;
            Flt64 dAlpha = (c1 * alpha.Solution[i] - pos).Abs();
            Flt64 x1 = alpha.Solution[i] - a1 * dAlpha;

            r1 = new Flt64(_random.NextDouble());
            r2 = new Flt64(_random.NextDouble());
            Flt64 a2 = new Flt64(2.0) * a * r1 - a;
            Flt64 c2 = new Flt64(2.0) * r2;
            Flt64 dBeta = (c2 * beta.Solution[i] - pos).Abs();
            Flt64 x2 = beta.Solution[i] - a2 * dBeta;

            r1 = new Flt64(_random.NextDouble());
            r2 = new Flt64(_random.NextDouble());
            Flt64 a3 = new Flt64(2.0) * a * r1 - a;
            Flt64 c3 = new Flt64(2.0) * r2;
            Flt64 dDelta = (c3 * delta.Solution[i] - pos).Abs();
            Flt64 x3 = delta.Solution[i] - a3 * dDelta;

            Flt64 newPos = (x1 + x2 + x3) / new Flt64(3.0);
            newSolution.Add(newPos);
        }

        Flt64 newObj = objectiveFunc(newSolution);
        return new Wolf(newSolution, newObj, newObj.ToDouble());
    }
}
