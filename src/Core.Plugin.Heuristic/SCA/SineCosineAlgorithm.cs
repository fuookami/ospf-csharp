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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Sca;

public sealed class SineCosineAlgorithm<Obj, ObjValue, V> : HeuristicAlgorithm<Obj, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<ObjValue, double> _fitnessConverter;

    public int PopulationSize { get; }
    public int SolutionAmount { get; }
    public IAbstractScaPolicy<ObjValue, V> Policy { get; }

    public SineCosineAlgorithm(
        IAbstractScaPolicy<ObjValue, V> policy,
        int populationSize = 100,
        int solutionAmount = 1,
        Func<ObjValue, double>? fitnessConverter = null) {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        PopulationSize = populationSize;
        SolutionAmount = solutionAmount;
        _fitnessConverter = fitnessConverter ?? DefaultFitnessConverter;
    }

    public override async Task<Result<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IAbstractCallBackModelInterface<Obj, ObjValue, V> model,
        Func<Iteration, IIndividual<ObjValue, V>, IReadOnlyList<IIndividual<ObjValue, V>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
        CancellationToken cancellationToken = default) {
        return await Task.Run(() => {
            var iteration = new Iteration();
            var comparer = Comparer<SolutionWithFitness<ObjValue, V>>.Create((a, b) => {
                Order? order = model.CompareObjective(a.Objective, b.Objective);
                return order switch { Order.Less => -1, Order.Equal => 0, _ => 1 };
            });

            IReadOnlyList<IReadOnlyList<V>> initialSolutions = model.InitialSolutions((ulong)PopulationSize);
            var population = initialSolutions.Select(s => {
                ObjValue? obj = model.Objective(s) ?? model.DefaultObjective;
                return new SolutionWithFitness<ObjValue, V>(s, obj, _fitnessConverter(obj));
            }).OrderBy(x => x, comparer).ToList();

            SolutionWithFitness<ObjValue, V> bestIndividual = population[0];
            var goodIndividuals = population.Take(SolutionAmount).ToList();

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    if (iteration.Current >= Policy.MaxIterations) {
                        break;
                    }

                    if (iteration.NotBetterIteration >= Policy.MaxIterationsWithoutImprovement) {
                        break;
                    }

                    bool globalBetter = false;
                    int dim = population[0].Solution.Count;
                    Flt64 r1 = Policy.R1(iteration);
                    IReadOnlyList<Flt64> r2 = Policy.R2(iteration, dim);
                    IReadOnlyList<Flt64> r3 = Policy.R3(iteration, dim);

                    var newPopulation = population.Select(ind => {
                        IReadOnlyList<V> newSolution = Policy.Move(iteration, ind.Solution, bestIndividual.Solution, r1, r2, r3);
                        ObjValue? newObj = model.Objective(newSolution) ?? model.DefaultObjective;
                        return new SolutionWithFitness<ObjValue, V>(newSolution, newObj, _fitnessConverter(newObj));
                    }).OrderBy(x => x, comparer).ToList();

                    population = newPopulation;
                    SolutionWithFitness<ObjValue, V> newBest = newPopulation[0];
                    if (comparer.Compare(newBest, bestIndividual) < 0) {
                        bestIndividual = newBest;
                        globalBetter = true;
                    }

                    goodIndividuals = newPopulation.Take(SolutionAmount).ToList();
                    model.Flush();
                    iteration.Next(globalBetter);

                    if (runningCallBack is not null) {
                        Result<Success, ErrorCode, Error<ErrorCode>> cbResult = runningCallBack(iteration, bestIndividual, goodIndividuals.Cast<IIndividual<ObjValue, V>>().ToList());
                        if (cbResult.IsFailed) {
                            break;
                        }
                    }
                }

                return Results.Ok<List<IIndividual<ObjValue, V>>>(
                    goodIndividuals.Cast<IIndividual<ObjValue, V>>().ToList());
            }
            catch (Exception ex) {
                return Results.Failed<List<IIndividual<ObjValue, V>>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationException, ex.Message));
            }
        }, cancellationToken);
    }

    private static double DefaultFitnessConverter(ObjValue obj) {
        if (obj is Flt64 flt) {
            return flt.ToDouble();
        }

        return Convert.ToDouble(obj);
    }
}
