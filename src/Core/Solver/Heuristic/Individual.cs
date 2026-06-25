#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Heuristic;
/// <summary>
/// 启发式个体接口，表示种群中的一个个体。
/// Heuristic individual interface, representing an individual in a population.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface IIndividual<ObjValue, V> {
    /// <summary>解向量 / Solution vector</summary>
    IReadOnlyList<V> Solution { get; }
    /// <summary>目标值 / Objective value</summary>
    ObjValue Objective { get; }
}

/// <summary>
/// 带适应度的解 / Solution with fitness.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed record SolutionWithFitness<ObjValue, V>(
    IReadOnlyList<V> Solution,
    ObjValue Objective,
    double Fitness) : IIndividual<ObjValue, V>;

/// <summary>
/// 种群容器 / Population container.
/// </summary>
/// <typeparam name="T">个体类型 / Individual type</typeparam>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed record Population<T, ObjValue, V>(IReadOnlyList<T> Individuals)
    where T : IIndividual<ObjValue, V> {
    /// <summary>种群大小 / Population size</summary>
    public int Size => Individuals.Count;

    /// <summary>按索引访问 / Access by index</summary>
    public T this[int index] => Individuals[index];

    /// <summary>获取最优个体 / Get best individual</summary>
    public T? Best(Func<ObjValue, ObjValue, int> comparer) {
        if (Individuals.Count == 0) {
            return default;
        }

        T best = Individuals[0];
        for (int i = 1; i < Individuals.Count; i++) {
            if (comparer(Individuals[i].Objective, best.Objective) < 0) {
                best = Individuals[i];
            }
        }
        return best;
    }
}

/// <summary>
/// 种群构建器 / Population builder.
/// </summary>
public sealed record PopulationBuilder {
    /// <summary>种群大小 / Population size</summary>
    public int PopulationSize { get; init; } = 100;
    /// <summary>精英个体数量 / Elite individual count</summary>
    public int EliteCount { get; init; } = 1;
}

/// <summary>
/// 种群操作工具 / Population operations utility.
/// </summary>
public static class PopulationOperations {
    /// <summary>
    /// 刷新优秀个体列表。
    /// Refresh good individuals list.
    /// </summary>
    public static List<T> RefreshGoodIndividuals<T, ObjValue, V>(
        Population<T, ObjValue, V> population,
        int count,
        Func<ObjValue, ObjValue, int> comparer)
        where T : IIndividual<ObjValue, V> {
        return population.Individuals
            .OrderBy(x => x, Comparer<T>.Create((a, b) => comparer(a.Objective, b.Objective)))
            .Take(count)
            .ToList();
    }
}
