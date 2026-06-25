#nullable enable

using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chromosome = Fuookami.Ospf.Core.Solver.Heuristic.SolutionWithFitness<Fuookami.Ospf.Math.Algebra.Number.Flt64, Fuookami.Ospf.Math.Algebra.Number.Flt64>;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Ga;
/// <summary>
/// 遗传算法 / Genetic Algorithm.
/// </summary>
public sealed class GeneAlgorithm : HeuristicAlgorithm<Flt64, Flt64, Flt64> {
    public IGAPolicy Policy { get; }

    public GeneAlgorithm(IGAPolicy policy) {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc/>
    public override async Task<Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IAbstractCallBackModelInterface<Flt64, Flt64, Flt64> model,
        Func<Iteration, IIndividual<Flt64, Flt64>, IReadOnlyList<IIndividual<Flt64, Flt64>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
        CancellationToken cancellationToken = default) {
        // Initialize population
        IReadOnlyList<IReadOnlyList<Flt64>> initialSolutions = model.InitialSolutions((ulong)Policy.PopulationSize);
        var population = initialSolutions
            .Select(sol => new Chromosome(
                Solution: sol,
                Objective: model.Objective(sol)!,
                Fitness: 0.0
            ))
            .OrderBy(ind => ind.Objective, Comparer<Flt64>.Create((a, b) => {
                Order? cmp = model.CompareObjective(a, b);
                return cmp is Order.Less ? -1 : cmp is Order.Greater ? 1 : 0;
            }))
            .ToList();

        Chromosome best = population[0];
        var iteration = new Iteration();

        while (!Policy.Finished(iteration) && !cancellationToken.IsCancellationRequested) {
            bool globalBetter = false;

            // Selection
            List<Chromosome> selected = Policy.Select(population, Policy.PopulationSize);

            // Crossover
            List<Chromosome> offspring = Policy.Cross(selected);

            // Mutation
            var mutated = new List<Chromosome>();
            foreach (Chromosome ind in offspring) {
                Chromosome m = Policy.Mutate(ind, sol => model.Objective(sol));
                mutated.Add(m);
            }

            // Combine + elites
            var combined = population.Take(Policy.EliteCount)
                .Concat(offspring)
                .Concat(mutated)
                .OrderBy(ind => ind.Objective, Comparer<Flt64>.Create((a, b) => {
                    Order? cmp = model.CompareObjective(a, b);
                    return cmp is Order.Less ? -1 : cmp is Order.Greater ? 1 : 0;
                }))
                .Take(Policy.PopulationSize)
                .ToList();

            Chromosome newBest = combined[0];
            if (model.CompareObjective(newBest.Objective, best.Objective) is Order.Less) {
                best = newBest;
                globalBetter = true;
            }

            population = combined;
            model.Flush();
            iteration.Next(globalBetter);

            if (runningCallBack is not null) {
                Result<Success, ErrorCode, Error<ErrorCode>> cbResult = runningCallBack(iteration, best, population);
                if (cbResult.IsFailed) {
                    break;
                }
            }

            await Task.Yield();
        }

        return Results.Ok<List<IIndividual<Flt64, Flt64>>>(population.Cast<IIndividual<Flt64, Flt64>>().ToList());
    }
}
