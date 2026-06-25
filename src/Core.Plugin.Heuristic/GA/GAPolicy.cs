#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Math = System.Math;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Ga
{
    using Chromosome = SolutionWithFitness<Flt64, Flt64>;

    /// <summary>
    /// 遗传算法默认策略 / Default Genetic Algorithm policy.
    /// 使用锦标赛选择、单点交叉和高斯变异。
    /// Uses tournament selection, single-point crossover, and Gaussian mutation.
    /// </summary>
    public sealed class GAPolicy : HeuristicPolicy, IGAPolicy
    {
        private readonly Random _random;
        private readonly double _mutationRate;
        private readonly double _mutationStrength;
        private readonly int _tournamentSize;

        /// <inheritdoc/>
        public override string Name => "GA";

        /// <inheritdoc/>
        public int EliteCount { get; }

        public GAPolicy(
            int maxIterations = 1000,
            int populationSize = 100,
            int eliteCount = 2,
            int tournamentSize = 3,
            double mutationRate = 0.1,
            double mutationStrength = 0.1,
            int? seed = null)
        {
            MaxIterations = maxIterations;
            PopulationSize = populationSize;
            EliteCount = eliteCount;
            _tournamentSize = tournamentSize;
            _mutationRate = mutationRate;
            _mutationStrength = mutationStrength;
            _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        }

        /// <inheritdoc/>
        public override bool Finished(Iteration iteration) =>
            iteration.Current >= MaxIterations
            || iteration.NotBetterIteration >= MaxIterationsWithoutImprovement;

        /// <inheritdoc/>
        public List<Chromosome> Select(List<Chromosome> population, int count)
        {
            var result = new List<Chromosome>(count);
            for (int i = 0; i < count; i++)
            {
                Chromosome? best = null;
                for (int j = 0; j < _tournamentSize; j++)
                {
                    var candidate = population[_random.Next(population.Count)];
                    if (best is null || candidate.Objective.Ls(best.Objective))
                    {
                        best = candidate;
                    }
                }
                result.Add(best!);
            }
            return result;
        }

        /// <inheritdoc/>
        public List<Chromosome> Cross(List<Chromosome> parents)
        {
            var offspring = new List<Chromosome>(parents.Count);
            for (int i = 0; i < parents.Count - 1; i += 2)
            {
                var parent1 = parents[i];
                var parent2 = parents[i + 1];
                var point = _random.Next(1, parent1.Solution.Count);
                var sol1 = new List<Flt64>(parent1.Solution.Count);
                var sol2 = new List<Flt64>(parent2.Solution.Count);
                for (int j = 0; j < parent1.Solution.Count; j++)
                {
                    if (j < point)
                    {
                        sol1.Add(parent1.Solution[j]);
                        sol2.Add(parent2.Solution[j]);
                    }
                    else
                    {
                        sol1.Add(parent2.Solution[j]);
                        sol2.Add(parent1.Solution[j]);
                    }
                }
                offspring.Add(new Chromosome(
                    Solution: sol1,
                    Objective: parent1.Objective,
                    Fitness: parent1.Fitness
                ));
                offspring.Add(new Chromosome(
                    Solution: sol2,
                    Objective: parent2.Objective,
                    Fitness: parent2.Fitness
                ));
            }
            if (parents.Count % 2 != 0)
            {
                offspring.Add(parents[^1]);
            }
            return offspring;
        }

        /// <inheritdoc/>
        public Chromosome Mutate(Chromosome individual, Func<IReadOnlyList<Flt64>, Flt64?> objectiveFunc)
        {
            if (_random.NextDouble() >= _mutationRate)
            {
                return individual;
            }

            var solution = new List<Flt64>(individual.Solution.Count);
            for (int i = 0; i < individual.Solution.Count; i++)
            {
                if (_random.NextDouble() < _mutationRate)
                {
                    var noise = SampleGaussian() * _mutationStrength;
                    var newValue = individual.Solution[i].Plus(new Flt64(noise));
                    var clamped = System.Math.Clamp(newValue.ToDouble(), -100.0, 100.0);
                    solution.Add(new Flt64(clamped));
                }
                else
                {
                    solution.Add(individual.Solution[i]);
                }
            }

            var newObj = objectiveFunc(solution) ?? individual.Objective;
            return new Chromosome(
                Solution: solution,
                Objective: newObj,
                Fitness: newObj.ToDouble()
            );
        }

        private double SampleGaussian()
        {
            // Box-Muller transform
            double u1, u2;
            do
            {
                u1 = _random.NextDouble();
            } while (u1 <= double.Epsilon);
            u2 = _random.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
        }
    }
}
