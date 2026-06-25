#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Gwo
{
    /// <summary>
    /// 灰狼个体类型 / Wolf type, alias for SolutionWithFitness.
    /// </summary>
    using Wolf = SolutionWithFitness<Flt64, Flt64>;

    /// <summary>
    /// 灰狼种群扩展方法 / Wolf population extension methods.
    /// </summary>
    public static class WolfPopulationExtensions
    {
        /// <summary>获取头狼（最优个体）/ Get alpha wolf (best individual).</summary>
        /// <exception cref="InvalidOperationException">种群为空时抛出 / Thrown when population is empty.</exception>
        public static Wolf Alpha(this IReadOnlyList<Wolf> wolves) =>
            wolves.Count > 0
                ? wolves[0]
                : throw new InvalidOperationException("Cannot get alpha from an empty population.");

        /// <summary>获取次优狼 / Get beta wolf (2nd best individual).</summary>
        /// <exception cref="InvalidOperationException">种群不足 2 只时抛出 / Thrown when population has fewer than 2 wolves.</exception>
        public static Wolf Beta(this IReadOnlyList<Wolf> wolves) =>
            wolves.Count > 1
                ? wolves[1]
                : throw new InvalidOperationException("Cannot get beta: population has fewer than 2 wolves.");

        /// <summary>获取第三优狼 / Get delta wolf (3rd best individual).</summary>
        /// <exception cref="InvalidOperationException">种群不足 3 只时抛出 / Thrown when population has fewer than 3 wolves.</exception>
        public static Wolf Delta(this IReadOnlyList<Wolf> wolves) =>
            wolves.Count > 2
                ? wolves[2]
                : throw new InvalidOperationException("Cannot get delta: population has fewer than 3 wolves.");
    }
}
