#nullable enable

using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Saa;
/// <summary>
/// 模拟退火算法 / Simulated Annealing Algorithm.
/// 基于 Metropolis 接受准则的单解邻域搜索算法。
/// Single-solution neighborhood search based on the Metropolis acceptance criterion.
/// </summary>
public sealed class SimulatedAnnealingAlgorithm : HeuristicAlgorithm<Flt64, Flt64, Flt64> {
    public ISaaPolicy Policy { get; }

    public SimulatedAnnealingAlgorithm(ISaaPolicy policy) {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc/>
    public override async Task<Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IAbstractCallBackModelInterface<Flt64, Flt64, Flt64> model,
        Func<Iteration, IIndividual<Flt64, Flt64>, IReadOnlyList<IIndividual<Flt64, Flt64>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
        CancellationToken cancellationToken = default) {
        return await Task.Run(() => {
            var iteration = new Iteration();
            var random = new Random();

            // Step 1: Get initial solution and evaluate
            IReadOnlyList<IReadOnlyList<Flt64>> initialSolutionList = model.InitialSolutions(1);
            IReadOnlyList<Flt64>? initialSolution = initialSolutionList.FirstOrDefault();
            if (initialSolution is null) {
                return Results.Ok<List<IIndividual<Flt64, Flt64>>>(new List<IIndividual<Flt64, Flt64>>());
            }

            Flt64 initialObj = model.Objective(initialSolution)!;

            // Step 2: Set current = best = initial
            var bestSolution = new SolutionWithFitness<Flt64, Flt64>(initialSolution, initialObj, initialObj.ToDouble());
            SolutionWithFitness<Flt64, Flt64> currentSolution = bestSolution;

            // Pre-compute token bounds for clamping
            IReadOnlyList<Token<Flt64>> tokensInSolver = model.Tokens.TokensInSolver;

            try {
                // Step 3: Main loop
                while (!cancellationToken.IsCancellationRequested) {
                    if (Policy.Finished(iteration)) {
                        break;
                    }

                    bool globalBetter = false;

                    for (int t = 0; t < Policy.MarkovLength; t++) {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Generate neighbor: pick random dimension, perturb
                        int dim = currentSolution.Solution.Count;
                        int point = random.Next(dim);
                        var perturbation = new Flt64(random.NextDouble() * 2.0 - 1.0);

                        var newSolutionList = new List<Flt64>(dim);
                        for (int i = 0; i < dim; i++) {
                            newSolutionList.Add(currentSolution.Solution[i]);
                        }
                        newSolutionList[point] = currentSolution.Solution[point] + perturbation;

                        // Clamp to token bounds if accessible, otherwise [-1000, 1000]
                        if (point < tokensInSolver.Count) {
                            Token<Flt64> token = tokensInSolver[point];
                            Bound<Flt64>? lowerBound = token.LowerBound;
                            Bound<Flt64>? upperBound = token.UpperBound;
                            if (lowerBound is not null) {
                                var lo = lowerBound.Value.ToFlt64();
                                if (newSolutionList[point] < lo) {
                                    newSolutionList[point] = lo;
                                }
                            }
                            if (upperBound is not null) {
                                var hi = upperBound.Value.ToFlt64();
                                if (newSolutionList[point] > hi) {
                                    newSolutionList[point] = hi;
                                }
                            }
                        }
                        else {
                            var lo = new Flt64(-1000.0);
                            var hi = new Flt64(1000.0);
                            if (newSolutionList[point] < lo) {
                                newSolutionList[point] = lo;
                            }

                            if (newSolutionList[point] > hi) {
                                newSolutionList[point] = hi;
                            }
                        }

                        // Evaluate new objective
                        Flt64 newObj = model.Objective(newSolutionList)!;

                        // Acceptance decision
                        bool accept;
                        Order? cmpResult = model.CompareObjective(newObj, currentSolution.Objective);
                        if (cmpResult is Order.Less) {
                            // Better: always accept
                            accept = true;
                        }
                        else {
                            // Worse or equal: accept with probability
                            accept = Policy.Accept(iteration, currentSolution.Objective, newObj);
                        }

                        if (accept) {
                            currentSolution = new SolutionWithFitness<Flt64, Flt64>(
                                newSolutionList, newObj, newObj.ToDouble());

                            // Check if this is a new global best
                            Order? bestCmp = model.CompareObjective(currentSolution.Objective, bestSolution.Objective);
                            if (bestCmp is Order.Less) {
                                bestSolution = currentSolution;
                                globalBetter = true;
                            }
                        }
                    }

                    model.Flush();
                    iteration.Next(globalBetter);

                    // Running callback
                    if (runningCallBack is not null) {
                        Result<Success, ErrorCode, Error<ErrorCode>> cbResult = runningCallBack(iteration, bestSolution,
                            new List<IIndividual<Flt64, Flt64>> { bestSolution });
                        if (cbResult.IsFailed) {
                            break;
                        }
                    }
                }

                // Step 4: Return best as single-element list
                return Results.Ok<List<IIndividual<Flt64, Flt64>>>(
                    new List<IIndividual<Flt64, Flt64>> { bestSolution });
            }
            catch (Exception ex) {
                return Results.Failed<List<IIndividual<Flt64, Flt64>>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationException, ex.Message));
            }
        }, cancellationToken);
    }
}
