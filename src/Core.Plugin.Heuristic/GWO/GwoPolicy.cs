#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gwo
{
    using Wolf = SolutionWithFitness<Flt64, Flt64>;

    /// <summary>
    /// 灰狼优化器默认策略 / Default Grey Wolf Optimizer policy.
    /// </summary>
    public class GwoPolicy : HeuristicPolicy, IGwoPolicy
    {
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
            int? seed = null)
        {
            _minA = new Flt64(minA);
            _maxA = new Flt64(maxA);
            _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
            MaxIterations = maxIterations;
            PopulationSize = populationSize;
        }

        /// <inheritdoc/>
        public Flt64 A(Iteration iteration)
        {
            var maxIter = MaxIterations > 0 ? MaxIterations : 1;
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
            Func<IReadOnlyList<Flt64>, Flt64> objectiveFunc)
        {
            var dim = wolf.Solution.Count;
            var newSolution = new List<Flt64>(dim);

            for (int i = 0; i < dim; i++)
            {
                var pos = wolf.Solution[i];

                var r1 = new Flt64(_random.NextDouble());
                var r2 = new Flt64(_random.NextDouble());
                var a1 = new Flt64(2.0) * a * r1 - a;
                var c1 = new Flt64(2.0) * r2;
                var dAlpha = (c1 * alpha.Solution[i] - pos).Abs();
                var x1 = alpha.Solution[i] - a1 * dAlpha;

                r1 = new Flt64(_random.NextDouble());
                r2 = new Flt64(_random.NextDouble());
                var a2 = new Flt64(2.0) * a * r1 - a;
                var c2 = new Flt64(2.0) * r2;
                var dBeta = (c2 * beta.Solution[i] - pos).Abs();
                var x2 = beta.Solution[i] - a2 * dBeta;

                r1 = new Flt64(_random.NextDouble());
                r2 = new Flt64(_random.NextDouble());
                var a3 = new Flt64(2.0) * a * r1 - a;
                var c3 = new Flt64(2.0) * r2;
                var dDelta = (c3 * delta.Solution[i] - pos).Abs();
                var x3 = delta.Solution[i] - a3 * dDelta;

                var newPos = (x1 + x2 + x3) / new Flt64(3.0);
                newSolution.Add(newPos);
            }

            var newObj = objectiveFunc(newSolution);
            return new Wolf(newSolution, newObj, newObj.ToDouble());
        }
    }
}
