#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Pso
{
    /// <summary>
    /// 粒子群优化策略接口 / Particle Swarm Optimization policy interface.
    /// </summary>
    public interface IPsoPolicy
    {
        string Name { get; }
        int MaxIterations { get; }
        int ParticleCount { get; }
        bool Finished(Iteration iteration);
        /// <summary>加速粒子 / Accelerate particle: update velocity and position</summary>
        (IReadOnlyList<Flt64> NewVelocity, IReadOnlyList<Flt64> NewPosition) Accelerate(
            Iteration iteration,
            IReadOnlyList<Flt64> currentVelocity,
            IReadOnlyList<Flt64> currentPosition,
            IReadOnlyList<Flt64> personalBest,
            IReadOnlyList<Flt64> globalBest);
    }
}
