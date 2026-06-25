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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Sca
{
    public sealed class SineCosineAlgorithm<Obj, ObjValue, V> : HeuristicAlgorithm<Obj, ObjValue, V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly Func<ObjValue, double> _fitnessConverter;

        public int PopulationSize { get; }
        public int SolutionAmount { get; }
        public IAbstractScaPolicy<ObjValue, V> Policy { get; }

        public SineCosineAlgorithm(
            IAbstractScaPolicy<ObjValue, V> policy,
            int populationSize = 100,
            int solutionAmount = 1,
            Func<ObjValue, double>? fitnessConverter = null)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            PopulationSize = populationSize;
            SolutionAmount = solutionAmount;
            _fitnessConverter = fitnessConverter ?? DefaultFitnessConverter;
        }

        public override async Task<Result<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IAbstractCallBackModelInterface<Obj, ObjValue, V> model,
            Func<Iteration, IIndividual<ObjValue, V>, IReadOnlyList<IIndividual<ObjValue, V>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var iteration = new Iteration();
                var comparer = Comparer<SolutionWithFitness<ObjValue, V>>.Create((a, b) =>
                {
                    var order = model.CompareObjective(a.Objective, b.Objective);
                    return order switch { Order.Less => -1, Order.Equal => 0, _ => 1 };
                });

                var initialSolutions = model.InitialSolutions((ulong)PopulationSize);
                var population = initialSolutions.Select(s =>
                {
                    var obj = model.Objective(s) ?? model.DefaultObjective;
                    return new SolutionWithFitness<ObjValue, V>(s, obj, _fitnessConverter(obj));
                }).OrderBy(x => x, comparer).ToList();

                var bestIndividual = population[0];
                var goodIndividuals = population.Take(SolutionAmount).ToList();

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (iteration.Current >= Policy.MaxIterations) break;
                        if (iteration.NotBetterIteration >= Policy.MaxIterationsWithoutImprovement) break;

                        var globalBetter = false;
                        var dim = population[0].Solution.Count;
                        var r1 = Policy.R1(iteration);
                        var r2 = Policy.R2(iteration, dim);
                        var r3 = Policy.R3(iteration, dim);

                        var newPopulation = population.Select(ind =>
                        {
                            var newSolution = Policy.Move(iteration, ind.Solution, bestIndividual.Solution, r1, r2, r3);
                            var newObj = model.Objective(newSolution) ?? model.DefaultObjective;
                            return new SolutionWithFitness<ObjValue, V>(newSolution, newObj, _fitnessConverter(newObj));
                        }).OrderBy(x => x, comparer).ToList();

                        population = newPopulation;
                        var newBest = newPopulation[0];
                        if (comparer.Compare(newBest, bestIndividual) < 0)
                        {
                            bestIndividual = newBest;
                            globalBetter = true;
                        }

                        goodIndividuals = newPopulation.Take(SolutionAmount).ToList();
                        model.Flush();
                        iteration.Next(globalBetter);

                        if (runningCallBack is not null)
                        {
                            var cbResult = runningCallBack(iteration, bestIndividual, goodIndividuals.Cast<IIndividual<ObjValue, V>>().ToList());
                            if (cbResult.IsFailed) break;
                        }
                    }

                    return Results.Ok<List<IIndividual<ObjValue, V>>>(
                        goodIndividuals.Cast<IIndividual<ObjValue, V>>().ToList());
                }
                catch (Exception ex)
                {
                    return Results.Failed<List<IIndividual<ObjValue, V>>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationException, ex.Message));
                }
            }, cancellationToken);
        }

        private static double DefaultFitnessConverter(ObjValue obj)
        {
            if (obj is Flt64 flt) return flt.ToDouble();
            return Convert.ToDouble(obj);
        }
    }
}
