#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 选择模式接口，定义如何从种群中选择个体数量。
/// Selection mode interface, defining how to select the number of individuals from the population.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface ISelectionMode<ObjValue, V> {
    /// <summary>
    /// 计算种群中应选择的个体数量。
    /// Calculate the number of individuals to select from the population.
    /// </summary>
    /// <typeparam name="T">个体类型 / Individual type</typeparam>
    /// <param name="iteration">当前迭代 / Current iteration</param>
    /// <param name="population">种群 / Population</param>
    /// <param name="compareObjective">目标值比较函数 / Objective value comparison function</param>
    /// <returns>选择数量 / Selection count</returns>
    int GetSelectionCount<T>(
        Iteration iteration,
        Population<T, ObjValue, V> population,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V>;
}

/// <summary>
/// 静态选择模式，返回固定的选择数量。
/// Static selection mode, returning a fixed selection count.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class StaticSelectionMode<ObjValue, V> : ISelectionMode<ObjValue, V> {
    /// <summary>
    /// 选择数量 / Selection count
    /// </summary>
    public int SelectionCount { get; init; } = 10;

    /// <inheritdoc/>
    public int GetSelectionCount<T>(
        Iteration iteration,
        Population<T, ObjValue, V> population,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> =>
        System.Math.Min(SelectionCount, population.Size);
}

/// <summary>
/// 自适应动态选择模式，根据适应度方差动态调整选择数量。
/// Adaptive dynamic selection mode, dynamically adjusting selection count based on fitness variance.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class AdaptiveDynamicSelectionMode<ObjValue, V> : ISelectionMode<ObjValue, V> {
    /// <summary>
    /// 最小选择数量 / Minimum selection count
    /// </summary>
    public int MinSelectionCount { get; init; } = 2;

    /// <summary>
    /// 最大选择比例 / Maximum selection ratio
    /// </summary>
    public double MaxSelectionRatio { get; init; } = 0.5;

    /// <inheritdoc/>
    public int GetSelectionCount<T>(
        Iteration iteration,
        Population<T, ObjValue, V> population,
        Func<ObjValue, ObjValue, int> compareObjective)
        where T : IIndividual<ObjValue, V> {
        if (population.Size <= MinSelectionCount) {
            return population.Size;
        }

        int maxCount = System.Math.Max(MinSelectionCount, (int)System.Math.Round(population.Size * MaxSelectionRatio));

        // Rank individuals by objective and compute a normalized spread metric.
        // 对个体按目标值排序，计算归一化的分散度量。
        var ranked = population.Individuals
            .Select((ind, idx) => (ind, idx))
            .OrderBy(pair => pair.idx, Comparer<int>.Create((a, b) =>
                compareObjective(population[a].Objective, population[b].Objective)))
            .ToList();

        // Compute the fraction of pairwise inversions as a measure of diversity.
        // 使用排名差异的方差作为多样性度量。
        double rankSum = 0;
        double rankSqSum = 0;
        int n = ranked.Count;
        for (int i = 0; i < n; i++) {
            rankSum += i;
            rankSqSum += (double)i * i;
        }

        double mean = rankSum / n;
        double variance = rankSqSum / n - mean * mean;
        double maxVariance = (double)(n - 1) * (n - 1) / 4.0;
        double normalizedSpread = maxVariance > 0 ? System.Math.Sqrt(variance / maxVariance) : 0.0;

        // When spread is high (diverse), select more; when concentrated, select fewer.
        // 分散度高（多样性好）时多选，集中时少选。
        double ratio = 0.2 + 0.6 * normalizedSpread;
        int count = System.Math.Max(MinSelectionCount, (int)System.Math.Round(population.Size * ratio));
        return System.Math.Min(count, maxCount);
    }
}
