#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
/// <summary>
/// 粒子群优化默认策略 / Default Particle Swarm Optimization policy.
/// Uses inertia weight model with cognitive and social acceleration coefficients.
/// </summary>
public class PsoPolicy : HeuristicPolicy, IPsoPolicy {
    private readonly Random _random;

    /// <inheritdoc/>
    public override string Name => "PSO";

    /// <summary>惯性权重 / Inertia weight</summary>
    public Flt64 W { get; }

    /// <summary>局部学习因子 / Local (cognitive) learning factor</summary>
    public Flt64 C1 { get; }

    /// <summary>全局学习因子 / Global (social) learning factor</summary>
    public Flt64 C2 { get; }

    /// <summary>最大速度 / Maximum velocity clamp</summary>
    public Flt64 MaxVelocity { get; }

    /// <summary>粒子数量 / Number of particles</summary>
    public int ParticleCount { get; }

    public PsoPolicy(
        double w = 0.4,
        double c1 = 2.0,
        double c2 = 2.0,
        double maxVelocity = 10000.0,
        int maxIterations = 1000,
        int particleCount = 100,
        int? seed = null) {
        W = new Flt64(w);
        C1 = new Flt64(c1);
        C2 = new Flt64(c2);
        MaxVelocity = new Flt64(maxVelocity);
        MaxIterations = maxIterations;
        PopulationSize = particleCount;
        ParticleCount = particleCount;
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    /// <inheritdoc/>
    public override bool Finished(Iteration iteration) =>
        iteration.Current >= MaxIterations
        || iteration.NotBetterIteration >= MaxIterationsWithoutImprovement;

    /// <inheritdoc/>
    public (IReadOnlyList<Flt64> NewVelocity, IReadOnlyList<Flt64> NewPosition) Accelerate(
        Iteration iteration,
        IReadOnlyList<Flt64> currentVelocity,
        IReadOnlyList<Flt64> currentPosition,
        IReadOnlyList<Flt64> personalBest,
        IReadOnlyList<Flt64> globalBest) {
        int dim = currentPosition.Count;
        var newVelocity = new List<Flt64>(dim);
        var newPosition = new List<Flt64>(dim);

        for (int i = 0; i < dim; i++) {
            var r1 = new Flt64(_random.NextDouble());
            var r2 = new Flt64(_random.NextDouble());

            Flt64 vel = W * currentVelocity[i]
                + C1 * r1 * (personalBest[i] - currentPosition[i])
                + C2 * r2 * (globalBest[i] - currentPosition[i]);

            if (vel > MaxVelocity) {
                vel = MaxVelocity;
            }
            else if (vel < -MaxVelocity) {
                vel = -MaxVelocity;
            }

            newVelocity.Add(vel);
            newPosition.Add(currentPosition[i] + vel);
        }

        return (newVelocity, newPosition);
    }
}
