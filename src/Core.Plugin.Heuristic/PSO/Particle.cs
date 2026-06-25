#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Pso
{
    /// <summary>
    /// 粒子 / Particle in PSO.
    /// Carries current solution, velocity, and personal best information.
    /// </summary>
    public sealed record Particle(
        IReadOnlyList<Flt64> Solution,
        Flt64 Objective,
        IReadOnlyList<Flt64> Velocity,
        IReadOnlyList<Flt64> PersonalBest,
        Flt64 PersonalBestObjective
    ) : IIndividual<Flt64, Flt64>;
}
