#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gwo
{
    using Wolf = SolutionWithFitness<Flt64, Flt64>;

    /// <summary>
    /// 灰狼优化算法 / Grey Wolf Optimizer algorithm.
    /// </summary>
    public sealed class GreyWolfOptimizer : HeuristicAlgorithm<Flt64, Flt64, Flt64>
    {
        /// <summary>种群大小 / Population size.</summary>
        public int PopulationSize { get; }

        /// <summary>返回解的数量 / Number of solutions to return.</summary>
        public int SolutionAmount { get; }

        /// <summary>灰狼策略 / GWO policy.</summary>
        public IGwoPolicy Policy { get; }

        public GreyWolfOptimizer(
            IGwoPolicy policy,
            int populationSize = 100,
            int solutionAmount = 1)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            PopulationSize = populationSize;
            SolutionAmount = solutionAmount;
        }

        /// <inheritdoc/>
        public override async Task<Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IAbstractCallBackModelInterface<Flt64, Flt64, Flt64> model,
            Func<Iteration, IIndividual<Flt64, Flt64>, IReadOnlyList<IIndividual<Flt64, Flt64>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var iteration = new Iteration();
                var comparer = Comparer<Wolf>.Create((a, b) =>
                {
                    var order = model.CompareObjective(a.Objective, b.Objective);
                    return order switch
                    {
                        Order.Less => -1,
                        Order.Equal => 0,
                        _ => 1
                    };
                });

                Flt64 ObjectiveFunc(IReadOnlyList<Flt64> solution)
                {
                    return model.Objective(solution);
                }

                var initialSolutions = model.InitialSolutions((ulong)PopulationSize);
                var population = initialSolutions
                    .Select(s =>
                    {
                        var obj = model.Objective(s)!;
                        return new Wolf(s, obj, obj.ToDouble());
                    })
                    .OrderBy(x => x, comparer)
                    .ToList();

                var bestWolf = population[0];
                var goodWolves = population.Take(SolutionAmount).ToList();

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (iteration.Current >= Policy.MaxIterations) break;
                        if (iteration.NotBetterIteration >= Policy.MaxIterationsWithoutImprovement) break;

                        var globalBetter = false;
                        var a = Policy.A(iteration);
                        var alpha = population[0];
                        var beta = population.Count > 1 ? population[1] : alpha;
                        var delta = population.Count > 2 ? population[2] : beta;

                        var newPopulation = new List<Wolf>(population.Count);
                        foreach (var wolf in population)
                        {
                            var moved = Policy.Move(iteration, wolf, alpha, beta, delta, a, ObjectiveFunc);
                            var newObj = ObjectiveFunc(moved.Solution);
                            newPopulation.Add(new Wolf(moved.Solution, newObj, newObj.ToDouble()));
                        }

                        population = newPopulation.OrderBy(x => x, comparer).ToList();
                        var newBest = population[0];

                        if (comparer.Compare(newBest, bestWolf) < 0)
                        {
                            bestWolf = newBest;
                            globalBetter = true;
                        }

                        goodWolves = population.Take(SolutionAmount).ToList();
                        model.Flush();
                        iteration.Next(globalBetter);

                        if (runningCallBack is not null)
                        {
                            var cbResult = runningCallBack(iteration, bestWolf, goodWolves);
                            if (cbResult.IsFailed) break;
                        }
                    }

                    return Results.Ok<List<IIndividual<Flt64, Flt64>>>(
                        goodWolves.Cast<IIndividual<Flt64, Flt64>>().ToList());
                }
                catch (Exception ex)
                {
                    return Results.Failed<List<IIndividual<Flt64, Flt64>>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationException, ex.Message));
                }
            }, cancellationToken);
        }
    }
}
