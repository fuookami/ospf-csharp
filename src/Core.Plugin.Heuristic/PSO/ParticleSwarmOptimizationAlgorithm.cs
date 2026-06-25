#nullable enable

using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
/// <summary>
/// 粒子群优化算法 / Particle Swarm Optimization algorithm.
/// Operates on Flt64 decision variables with Flt64 objective values.
/// </summary>
public sealed class ParticleSwarmOptimizationAlgorithm
    : HeuristicAlgorithm<Flt64, Flt64, Flt64> {
    public IPsoPolicy Policy { get; }

    public ParticleSwarmOptimizationAlgorithm(IPsoPolicy policy) {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc/>
    public override async Task<Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IAbstractCallBackModelInterface<Flt64, Flt64, Flt64> model,
        Func<Iteration, IIndividual<Flt64, Flt64>, IReadOnlyList<IIndividual<Flt64, Flt64>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
        CancellationToken cancellationToken = default) {
        return await Task.Run(() => {
            var iteration = new Iteration();
            Random random = Random.Shared;

            // 1. Initialize particles from model with random velocities in [-1, 1]
            IReadOnlyList<IReadOnlyList<Flt64>> initialSolutions = model.InitialSolutions((ulong)Policy.ParticleCount);
            var particles = initialSolutions.Select(s => {
                Flt64 obj = model.Objective(s)!;
                var velocity = Enumerable.Range(0, s.Count)
                    .Select(_ => new Flt64(random.NextDouble() * 2.0 - 1.0))
                    .ToList();
                return new Particle(s, obj, velocity, s, obj);
            }).ToList();

            // 2. Track global best particle
            Particle globalBest = particles[0];
            foreach (Particle? p in particles.Skip(1)) {
                if (model.CompareObjective(p.Objective, globalBest.Objective) is Order.Less) {
                    globalBest = p;
                }
            }

            try {
                // 3. Main loop
                while (!cancellationToken.IsCancellationRequested) {
                    if (Policy.Finished(iteration)) {
                        break;
                    }

                    bool globalBetter = false;
                    var newParticles = new List<Particle>();

                    foreach (Particle? p in particles) {
                        (IReadOnlyList<Flt64>? newVel, IReadOnlyList<Flt64>? newPos) = Policy.Accelerate(
                            iteration,
                            p.Velocity,
                            p.Solution,
                            p.PersonalBest,
                            globalBest.Solution);

                        Flt64 newObj = model.Objective(newPos)!;

                        // Update personal best if improved
                        IReadOnlyList<Flt64> personalBest;
                        Flt64 personalBestObj;
                        if (model.CompareObjective(newObj, p.PersonalBestObjective) is Order.Less) {
                            personalBest = newPos;
                            personalBestObj = newObj;
                        }
                        else {
                            personalBest = p.PersonalBest;
                            personalBestObj = p.PersonalBestObjective;
                        }

                        var newParticle = new Particle(
                            newPos, newObj, newVel, personalBest, personalBestObj);
                        newParticles.Add(newParticle);

                        // Update global best if improved
                        if (model.CompareObjective(newObj, globalBest.Objective) is Order.Less) {
                            globalBest = newParticle;
                            globalBetter = true;
                        }
                    }

                    particles = newParticles;
                    model.Flush();
                    iteration.Next(globalBetter);

                    if (runningCallBack is not null) {
                        Result<Success, ErrorCode, Error<ErrorCode>> cbResult = runningCallBack(
                            iteration,
                            globalBest,
                            particles.Cast<IIndividual<Flt64, Flt64>>().ToList());
                        if (cbResult.IsFailed) {
                            break;
                        }
                    }
                }

                // 4. Return global best as single-element list
                return Results.Ok<List<IIndividual<Flt64, Flt64>>>(
                    new List<IIndividual<Flt64, Flt64>> { globalBest });
            }
            catch (Exception ex) {
                return Results.Failed<List<IIndividual<Flt64, Flt64>>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationException, ex.Message));
            }
        }, cancellationToken);
    }
}
