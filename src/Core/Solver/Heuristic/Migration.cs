#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 迁移策略接口，定义种群间个体迁移的行为。
/// Migration strategy interface, defining behavior for individual migration between populations.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface IMigration<ObjValue, V> {
    /// <summary>策略名称 / Strategy name</summary>
    string Name { get; }

    /// <summary>
    /// 在种群间执行个体迁移。
    /// Perform individual migration between populations.
    /// </summary>
    /// <typeparam name="T">个体类型 / Individual type</typeparam>
    /// <param name="iteration">当前迭代 / Current iteration</param>
    /// <param name="populations">种群列表 / Populations list</param>
    /// <param name="compareObjective">目标值比较函数 / Objective value comparison function</param>
    /// <returns>每个种群及其迁入个体的列表 / List of populations with their incoming individuals</returns>
    IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V>;
}

/// <summary>
/// 随机迁移策略，从每个种群中随机选择约10%个体，发送到下一个种群。
/// Random migration strategy, randomly selecting ~10% individuals from each population, sending to the next.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class RandomMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "Random";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        var migrants = populations.Select(population => {
            int migrateCount = System.Math.Max(1, (int)System.Math.Round(population.Size * 0.1));
            return population.Individuals.OrderBy(_ => Random.Shared.Next()).Take(migrateCount).ToList();
        }).ToList();

        return populations.Select((population, index) => {
            int sourceIndex = (index - 1 + populations.Count) % populations.Count;
            return (population, (IReadOnlyList<T>)migrants[sourceIndex]);
        }).ToList();
    }
}

/// <summary>
/// 优到劣迁移策略，从每个种群中找到最佳个体，发送到下一个种群。
/// Better-to-worse migration strategy, finding best individual from each population, sending to the next.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class BetterToWorseMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "BetterToWorse";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        var bestFromEach = populations.Select(pop => pop.Best(compareObjective)).ToList();

        return populations.Select((population, index) => {
            int sourceIndex = (index - 1 + populations.Count) % populations.Count;
            T? best = bestFromEach[sourceIndex];
            IReadOnlyList<T> incoming = best is not null ? new[] { best } : Array.Empty<T>();
            return (population, incoming);
        }).ToList();
    }
}

/// <summary>
/// 多到少迁移策略，从最大的种群向所有其他种群捐赠个体。
/// More-to-less migration strategy, donating from the largest population to all others.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class MoreToLessMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "MoreToLess";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        var sorted = populations.OrderByDescending(p => p.Size).ToList();
        int donateCount = System.Math.Max(1, sorted[0].Size / populations.Count);
        IReadOnlyList<T> donors = sorted[0].Individuals.Take(donateCount).ToList();

        return populations.Select(population => (population, donors)).ToList();
    }
}

/// <summary>
/// 标准迁移策略，按10%比例从相邻种群迁移个体。
/// Standard migration strategy, migrating 10% of individuals from adjacent populations.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class StandardMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "Standard";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        double migrationRate = 0.1;
        var migrants = populations.Select(population => {
            int migrateCount = System.Math.Max(1, (int)System.Math.Round(population.Size * migrationRate));
            return population.Individuals.OrderBy(_ => Random.Shared.Next()).Take(migrateCount).ToList();
        }).ToList();

        return populations.Select((population, index) => {
            int sourceIndex = (index - 1 + populations.Count) % populations.Count;
            return (population, (IReadOnlyList<T>)migrants[sourceIndex]);
        }).ToList();
    }
}

/// <summary>
/// 环形交换迁移策略，以0.5概率在相邻种群间交换个体。
/// Ring exchange migration strategy, exchanging individuals between neighbors with probability 0.5.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class RingExchangeMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "RingExchange";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        double exchangeProb = 0.5;
        return populations.Select((population, index) => {
            int neighborIndex = (index + 1) % populations.Count;
            int toExchangeCount = population.Individuals.Count(_ => Random.Shared.NextDouble() < exchangeProb);
            IReadOnlyList<T> incoming = populations[neighborIndex].Individuals.Take(toExchangeCount).ToList();
            return (population, incoming);
        }).ToList();
    }
}

/// <summary>
/// 随机扩散迁移策略，全局随机打乱10%个体分配到所有种群。
/// Random diffusion migration strategy, globally shuffling 10% of individuals across all populations.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class RandomDiffusionMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "RandomDiffusion";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        double diffusionRate = 0.1;
        IReadOnlyList<T> allIndividuals = populations
            .SelectMany(p => p.Individuals)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        int offset = 0;
        return populations.Select(population => {
            int replaceCount = System.Math.Max(1, (int)System.Math.Round(population.Size * diffusionRate));
            IReadOnlyList<T> incoming = allIndividuals.Skip(offset).Take(replaceCount).ToList();
            offset += replaceCount;
            return (population, incoming);
        }).ToList();
    }
}

/// <summary>
/// 精英迁移策略，从每个种群中选择前10%精英个体，发送到下一个种群。
/// Elitist migration strategy, selecting top 10% elite individuals from each population, sending to the next.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class ElitistMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "Elitist";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        int eliteCount = System.Math.Max(1, populations[0].Size / 10);
        var elites = populations.Select(pop =>
            pop.Individuals
                .OrderBy(ind => ind, Comparer<T>.Create((a, b) => compareObjective(a.Objective, b.Objective)))
                .Take(eliteCount)
                .ToList()
        ).ToList();

        return populations.Select((population, index) => {
            int sourceIndex = (index - 1 + populations.Count) % populations.Count;
            return (population, (IReadOnlyList<T>)elites[sourceIndex]);
        }).ToList();
    }
}

/// <summary>
/// 种群合并迁移策略，合并所有种群后均匀重新分配。
/// Population merge migration strategy, merging all populations and redistributing evenly.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class PopulationMergeMigration<ObjValue, V> : IMigration<ObjValue, V> {
    /// <inheritdoc/>
    public string Name => "PopulationMerge";

    /// <inheritdoc/>
    public IReadOnlyList<(Population<T, ObjValue, V> Population, IReadOnlyList<T> Incoming)> Migrate<T>(
        Iteration iteration,
        IReadOnlyList<Population<T, ObjValue, V>> populations,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (populations.Count < 2) {
            return populations.Select(p => (p, (IReadOnlyList<T>)Array.Empty<T>())).ToList();
        }

        IReadOnlyList<T> merged = populations
            .SelectMany(p => p.Individuals)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        int chunkSize = System.Math.Max(1, merged.Count / populations.Count);
        return populations.Select((population, index) => {
            int start = index * chunkSize;
            int count = System.Math.Min(chunkSize, merged.Count - start);
            IReadOnlyList<T> incoming = start < merged.Count
                ? merged.Skip(start).Take(count).ToList()
                : Array.Empty<T>();
            return (population, incoming);
        }).ToList();
    }
}
