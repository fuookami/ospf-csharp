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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Mvo;

public sealed class MultiVerseOptimizer<Obj, ObjValue, V> : HeuristicAlgorithm<Obj, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<ObjValue, double> _fitnessConverter;

    public int UniverseAmount { get; }
    public int SolutionAmount { get; }
    public IAbstractMvoPolicy<ObjValue, V> Policy { get; }

    public MultiVerseOptimizer(
        IAbstractMvoPolicy<ObjValue, V> policy,
        int universeAmount = 100,
        int solutionAmount = 1,
        Func<ObjValue, double>? fitnessConverter = null) {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        UniverseAmount = universeAmount;
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

            IReadOnlyList<IReadOnlyList<V>> initialSolutions = model.InitialSolutions((ulong)UniverseAmount);
            var universes = initialSolutions.Select(s => {
                ObjValue? obj = model.Objective(s) ?? model.DefaultObjective;
                return new SolutionWithFitness<ObjValue, V>(s, obj, _fitnessConverter(obj));
            }).OrderBy(x => x, comparer).ToList();

            SolutionWithFitness<ObjValue, V> bestUniverse = universes[0];
            var goodUniverses = universes.Take(SolutionAmount).ToList();

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    if (iteration.Current >= Policy.MaxIterations) {
                        break;
                    }

                    if (iteration.NotBetterIteration >= Policy.MaxIterationsWithoutImprovement) {
                        break;
                    }

                    bool globalBetter = false;
                    Flt64 wep = Policy.Wep(iteration);
                    Flt64 tdr = Policy.Tdr(iteration);

                    IReadOnlyList<IReadOnlyList<V>> newSolutions = Policy.TransformUniverses(iteration, bestUniverse.Solution, universes, wep, tdr);
                    var newUniverses = newSolutions.Select(s => {
                        ObjValue? obj = model.Objective(s) ?? model.DefaultObjective;
                        return new SolutionWithFitness<ObjValue, V>(s, obj, _fitnessConverter(obj));
                    }).OrderBy(x => x, comparer).ToList();

                    universes = newUniverses;
                    SolutionWithFitness<ObjValue, V> newBest = newUniverses[0];
                    if (comparer.Compare(newBest, bestUniverse) < 0) {
                        bestUniverse = newBest;
                        globalBetter = true;
                    }

                    goodUniverses = newUniverses.Take(SolutionAmount).ToList();
                    model.Flush();
                    iteration.Next(globalBetter);

                    if (runningCallBack is not null) {
                        Result<Success, ErrorCode, Error<ErrorCode>> cbResult = runningCallBack(iteration, bestUniverse, goodUniverses.Cast<IIndividual<ObjValue, V>>().ToList());
                        if (cbResult.IsFailed) {
                            break;
                        }
                    }
                }

                return Results.Ok<List<IIndividual<ObjValue, V>>>(
                    goodUniverses.Cast<IIndividual<ObjValue, V>>().ToList());
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
