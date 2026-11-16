// NSGA-III 运行器 / The NSGA-III runner.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// NSGA-III 运行器 / The NSGA-III runner.
/// </summary>
/// <remarks>
/// **只重写了"最后一层怎么补位"**，其余（初始化、交叉、变异、分层）全部继承自 <see cref="Nsga2Runner{V}"/>。
/// 这正是 NSGA-III 相对 NSGA-II 的全部算法差异：把"拥挤距离"换成"参考方向的小生境计数"。
/// **Only "how the last front is filled" is overridden**; everything else (initialisation, crossover, mutation, layering) is inherited from
/// <see cref="Nsga2Runner{V}"/>. That is the entire algorithmic difference between NSGA-III and NSGA-II: crowding distance is replaced by
/// niche counting against reference directions.
/// **归一化的 `min`/`max` 取自"已选个体 + 末层候选"的并集**，不是只看末层。只看末层会让归一化尺度随末层抖动，
/// **于是同一个个体在不同代里的距离不可比，小生境计数变成噪声**。
/// **The normalisation's `min`/`max` come from "already selected + last front"**, not from the last front alone. Using the last front alone
/// makes the scale jitter with that front, **so the same individual's distance is incomparable across generations and the niche count
/// becomes noise**.
/// **某个目标的极差不大于 `1e-12` 时归一化值取 0**，不做除法。做除法会得到 `NaN` 或正负无穷，
/// **而所有个体的距离于是都变成 `NaN`，比较全部返回假，补位退化成"按原顺序取前若干个"而没有任何提示**。
/// **An objective whose range is not greater than `1e-12` normalises to 0** instead of dividing. Dividing yields `NaN` or infinities,
/// **so every individual's distance becomes `NaN`, every comparison returns false, and the fill silently degenerates into "take the first
/// few in the original order"**.
/// **参考点生成失败时退化为"按目标顺序截断"**，保证仍然返回合法结果而不抛异常。
/// **When the reference points cannot be generated it falls back to "truncate in objective order"**, so a valid result is still returned
/// instead of throwing.
/// **补位时选的是"成员最少且仍有末层候选落在这个方向上"的参考方向**。只挑"成员最少"会在某个方向已经没有候选时死循环在一处，
/// **于是补位把名额全给同一批个体，而前沿的覆盖度看起来只是"不够均匀"**。
/// **The fill picks "the direction with the fewest members that still has last-front candidates mapping to it"**. Picking merely "the
/// fewest members" would spin on a direction that has no candidates left, **so the fill hands every slot to the same batch and the front's
/// spread merely looks "a bit uneven"**.
/// </remarks>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class Nsga3Runner<V> : Nsga2Runner<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 构造 NSGA-III 运行器 / Construct the NSGA-III runner.
    /// </summary>
    /// <param name="objectivesOf">目标向量提取器 / The objective-vector extractor.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="update">解更新算子 / The solution-update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="divisions">每个目标的分割数 / The divisions per objective.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public Nsga3Runner(
        Func<Solution<V>, IReadOnlyList<Flt64>> objectivesOf,
        IDominance dominance,
        ISolutionUpdate update,
        IMutationOperator mutation,
        Func<Flt64> entropy,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        Flt64? mutationRate = null,
        int archiveAmount = 100,
        int divisions = 8,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(objectivesOf, dominance, update, mutation, entropy, stopping, converter,
            size, mutationRate, archiveAmount, observers) {
        if (divisions < 1) {
            throw new ArgumentOutOfRangeException(nameof(divisions), "divisions must be positive");
        }
        Divisions = divisions;
    }

    /// <inheritdoc/>
    public override string Name => "nsga-iii";

    /// <summary>每个目标的分割数 / The divisions per objective.</summary>
    public int Divisions { get; }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> Select(
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> candidates,
        int amount) {
        if (candidates.Count <= amount) {
            return candidates;
        }
        ArgumentNullException.ThrowIfNull(candidates);
        IReadOnlyList<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>> fronts =
            FastNonDominatedSort.Sort(
                candidates, individual => individual.Objective, Dominance);
        var selected = new List<IIndividual<IReadOnlyList<Flt64>, V>>(amount);
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> lastFront =
            Array.Empty<IIndividual<IReadOnlyList<Flt64>, V>>();
        foreach (IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> front in fronts) {
            if (selected.Count + front.Count <= amount) {
                selected.AddRange(front);
            } else {
                lastFront = front;
                break;
            }
        }
        if (lastFront.Count == 0) {
            return selected;
        }

        int remaining = amount - selected.Count;
        int dimension = candidates[0].Objective.Count;
        IReadOnlyList<IReadOnlyList<double>> referencePoints =
            ReferencePoints.DasDennis(dimension, Divisions);
        if (referencePoints.Count == 0) {
            // 参考点生成失败时退化为按目标顺序截断。
            // When the reference points cannot be generated, fall back to truncation in objective order.
            for (int index = 0; index < lastFront.Count && index < remaining; index++) {
                selected.Add(lastFront[index]);
            }
            return selected;
        }

        // 归一化：用「已选个体 + 末层候选」的逐目标极值做 min-max。
        // Normalisation: min-max per objective over "already selected + last front".
        var pool = new List<IIndividual<IReadOnlyList<Flt64>, V>>(selected);
        pool.AddRange(lastFront);
        double[] minimum = new double[dimension];
        double[] maximum = new double[dimension];
        for (int index = 0; index < dimension; index++) {
            minimum[index] = double.PositiveInfinity;
            maximum[index] = double.NegativeInfinity;
        }
        foreach (IIndividual<IReadOnlyList<Flt64>, V> individual in pool) {
            for (int index = 0; index < dimension; index++) {
                double value = individual.Objective[index].ToDouble();
                if (value < minimum[index]) {
                    minimum[index] = value;
                }
                if (value > maximum[index]) {
                    maximum[index] = value;
                }
            }
        }
        double[] Normalise(IIndividual<IReadOnlyList<Flt64>, V> individual) {
            double[] normalised = new double[dimension];
            for (int index = 0; index < dimension; index++) {
                double span = maximum[index] - minimum[index];
                normalised[index] = span <= 1.0e-12
                    ? 0.0
                    : (individual.Objective[index].ToDouble() - minimum[index]) / span;
            }
            return normalised;
        }

        int[] nicheCount = new int[referencePoints.Count];
        foreach (IIndividual<IReadOnlyList<Flt64>, V> individual in selected) {
            nicheCount[NearestReference(Normalise(individual), referencePoints)] += 1;
        }

        var available = new List<IIndividual<IReadOnlyList<Flt64>, V>>(lastFront);
        for (int round = 0; round < remaining; round++) {
            if (available.Count == 0) {
                break;
            }
            int targetReference = 0;
            int fewest = int.MaxValue;
            for (int index = 0; index < nicheCount.Length; index++) {
                if (nicheCount[index] < fewest) {
                    bool occupied = false;
                    foreach (IIndividual<IReadOnlyList<Flt64>, V> candidate in available) {
                        if (NearestReference(Normalise(candidate), referencePoints) == index) {
                            occupied = true;
                            break;
                        }
                    }
                    if (occupied) {
                        fewest = nicheCount[index];
                        targetReference = index;
                    }
                }
            }

            int chosenIndex = -1;
            double bestDistance = double.PositiveInfinity;
            for (int index = 0; index < available.Count; index++) {
                double[] normalised = Normalise(available[index]);
                if (NearestReference(normalised, referencePoints) != targetReference) {
                    continue;
                }
                double distance = ReferencePoints.PerpendicularDistance(
                    normalised, referencePoints[targetReference]);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    chosenIndex = index;
                }
            }
            if (chosenIndex < 0) {
                chosenIndex = 0;
            }
            selected.Add(available[chosenIndex]);
            available.RemoveAt(chosenIndex);
            nicheCount[targetReference] += 1;
        }
        return selected;
    }

    /// <summary>
    /// 找最近的参考方向 / Find the nearest reference direction.
    /// </summary>
    /// <param name="objectives">归一化后的目标向量 / The normalised objective vector.</param>
    /// <param name="referencePoints">参考方向 / The reference directions.</param>
    /// <returns>最近方向的下标 / The index of the nearest direction.</returns>
    private static int NearestReference(
        IReadOnlyList<double> objectives,
        IReadOnlyList<IReadOnlyList<double>> referencePoints) {
        int nearest = 0;
        double bestDistance = double.PositiveInfinity;
        for (int index = 0; index < referencePoints.Count; index++) {
            double distance = ReferencePoints.PerpendicularDistance(
                objectives, referencePoints[index]);
            if (distance < bestDistance) {
                bestDistance = distance;
                nearest = index;
            }
        }
        return nearest;
    }
}
