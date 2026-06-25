#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Saa
{
    /// <summary>
    /// 模拟退火算法 / Simulated Annealing Algorithm.
    /// 基于 Metropolis 接受准则的单解邻域搜索算法。
    /// Single-solution neighborhood search based on the Metropolis acceptance criterion.
    /// </summary>
    public sealed class SimulatedAnnealingAlgorithm : HeuristicAlgorithm<Flt64, Flt64, Flt64>
    {
        public ISaaPolicy Policy { get; }

        public SimulatedAnnealingAlgorithm(ISaaPolicy policy)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
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
                var random = new Random();

                // Step 1: Get initial solution and evaluate
                var initialSolutionList = model.InitialSolutions(1);
                var initialSolution = initialSolutionList.FirstOrDefault();
                if (initialSolution is null)
                {
                    return Results.Ok<List<IIndividual<Flt64, Flt64>>>(new List<IIndividual<Flt64, Flt64>>());
                }

                var initialObj = model.Objective(initialSolution)!;

                // Step 2: Set current = best = initial
                var bestSolution = new SolutionWithFitness<Flt64, Flt64>(initialSolution, initialObj, initialObj.ToDouble());
                var currentSolution = bestSolution;

                // Pre-compute token bounds for clamping
                var tokensInSolver = model.Tokens.TokensInSolver;

                try
                {
                    // Step 3: Main loop
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (Policy.Finished(iteration)) break;

                        var globalBetter = false;

                        for (int t = 0; t < Policy.MarkovLength; t++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Generate neighbor: pick random dimension, perturb
                            var dim = currentSolution.Solution.Count;
                            var point = random.Next(dim);
                            var perturbation = new Flt64(random.NextDouble() * 2.0 - 1.0);

                            var newSolutionList = new List<Flt64>(dim);
                            for (int i = 0; i < dim; i++)
                            {
                                newSolutionList.Add(currentSolution.Solution[i]);
                            }
                            newSolutionList[point] = currentSolution.Solution[point] + perturbation;

                            // Clamp to token bounds if accessible, otherwise [-1000, 1000]
                            if (point < tokensInSolver.Count)
                            {
                                var token = tokensInSolver[point];
                                var lowerBound = token.LowerBound;
                                var upperBound = token.UpperBound;
                                if (lowerBound is not null)
                                {
                                    var lo = lowerBound.Value.ToFlt64();
                                    if (newSolutionList[point] < lo) newSolutionList[point] = lo;
                                }
                                if (upperBound is not null)
                                {
                                    var hi = upperBound.Value.ToFlt64();
                                    if (newSolutionList[point] > hi) newSolutionList[point] = hi;
                                }
                            }
                            else
                            {
                                var lo = new Flt64(-1000.0);
                                var hi = new Flt64(1000.0);
                                if (newSolutionList[point] < lo) newSolutionList[point] = lo;
                                if (newSolutionList[point] > hi) newSolutionList[point] = hi;
                            }

                            // Evaluate new objective
                            var newObj = model.Objective(newSolutionList)!;

                            // Acceptance decision
                            bool accept;
                            var cmpResult = model.CompareObjective(newObj, currentSolution.Objective);
                            if (cmpResult is Order.Less)
                            {
                                // Better: always accept
                                accept = true;
                            }
                            else
                            {
                                // Worse or equal: accept with probability
                                accept = Policy.Accept(iteration, currentSolution.Objective, newObj);
                            }

                            if (accept)
                            {
                                currentSolution = new SolutionWithFitness<Flt64, Flt64>(
                                    newSolutionList, newObj, newObj.ToDouble());

                                // Check if this is a new global best
                                var bestCmp = model.CompareObjective(currentSolution.Objective, bestSolution.Objective);
                                if (bestCmp is Order.Less)
                                {
                                    bestSolution = currentSolution;
                                    globalBetter = true;
                                }
                            }
                        }

                        model.Flush();
                        iteration.Next(globalBetter);

                        // Running callback
                        if (runningCallBack is not null)
                        {
                            var cbResult = runningCallBack(iteration, bestSolution,
                                new List<IIndividual<Flt64, Flt64>> { bestSolution });
                            if (cbResult.IsFailed) break;
                        }
                    }

                    // Step 4: Return best as single-element list
                    return Results.Ok<List<IIndividual<Flt64, Flt64>>>(
                        new List<IIndividual<Flt64, Flt64>> { bestSolution });
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
