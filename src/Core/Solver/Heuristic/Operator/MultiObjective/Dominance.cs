// 多目标支配、非支配排序与参考点 / Multi-objective dominance, non-dominated sorting and reference points.
// 对应。
// , and.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;

/// <summary>
/// 判断左目标向量是否支配右目标向量 / Decide whether the left objective vector dominates the right one.
/// </summary>
/// <remarks>
/// **支配要求"至少一个目标严格更好"**。只判"没有更差"会让两个目标完全相同（或互不支配）的向量互相"支配"，非支配排序里同层个体
/// 于是被拆散到不同层，Pareto 前沿的规模与形状全错，而**每一层的成员看起来都很正常**。
/// **Dominance requires at least one strictly better objective.** Testing only "not worse" makes identical (or mutually
/// non-dominating) vectors dominate each other; the non-dominated sort then splits peers across different fronts, wrecking the size
/// and shape of the Pareto front **while each front's membership looks perfectly reasonable**.
/// **目标维度必须与方向表一致**。少读一个目标等于在更高维空间里做比较，前几维的支配关系会被后面的维度的"未定义"悄悄覆盖。
/// **The objective arity must match the direction table.** Skipping an objective amounts to comparing in a lower-dimensional space,
/// where the "undefined" remaining dimensions silently override the dominance relations of the leading ones.
/// </remarks>
public interface IDominance {
    /// <summary>
    /// 判断左目标向量是否支配右目标向量 / Decide whether the left objective vector dominates the right one.
    /// </summary>
    /// <param name="lhs">左目标向量 / The left objective vector.</param>
    /// <param name="rhs">右目标向量 / The right objective vector.</param>
    /// <returns>是否支配 / Whether it dominates.</returns>
    bool Dominates(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs);
}

/// <summary>
/// Pareto 支配，逐目标方向由方向表给出 / Pareto dominance with per-objective directions from a direction table.
/// </summary>
/// <param name="Directions">每个目标是否最小化 / Whether each objective is minimized.</param>
public sealed record ParetoDominance(IReadOnlyList<bool> Directions) : IDominance {
    /// <inheritdoc/>
    public bool Dominates(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        if (lhs.Count != Directions.Count || rhs.Count != Directions.Count) {
            // 维度不符时**拒绝判定支配**，而不是把缺的目标当 0。当 0 处理会让一个完整向量凭空"支配"一个残缺向量。
            // On an arity mismatch, **refuse to assert dominance** rather than treating missing objectives as 0. Treating them as 0
            // would let a complete vector spuriously dominate a truncated one.
            return false;
        }

        bool strictlyBetter = false;
        for (int index = 0; index < Directions.Count; index++) {
            double left = lhs[index].ToDouble();
            double right = rhs[index].ToDouble();
            bool better = Directions[index] ? left < right : left > right;
            bool worse = Directions[index] ? left > right : left < right;
            if (worse) {
                return false;
            }
            if (better) {
                strictlyBetter = true;
            }
        }
        return strictlyBetter;
    }
}

/// <summary>
/// 全目标最小化的 Pareto 支配 / Pareto dominance with every objective minimized.
/// </summary>
/// <param name="ObjectiveAmount">目标数量 / The objective amount.</param>
public sealed record MinimizationDominance(int ObjectiveAmount) : IDominance {
    private readonly ParetoDominance _delegate = new(Enumerable.Repeat(true, ObjectiveAmount).ToList());

    /// <inheritdoc/>
    public bool Dominates(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) => _delegate.Dominates(lhs, rhs);
}

/// <summary>
/// 任意维度的全目标最小化 Pareto 支配 / Pareto dominance with every objective minimized, at any arity.
/// </summary>
/// <remarks>
/// **与 <see cref="MinimizationDominance"/> 的区别只有一点：目标数量在构造时未知**。接受策略与搜索记忆拿到的是
/// "问题给的坐标投影"，维度由问题决定而不是由这里决定，因此不能固定数量。
///
/// **长度不一致时判为"不支配"而不是抛异常**：接受策略在每一步都会调用它，抛异常会把一次配置错误放大成运行期崩溃。
///
/// **The only difference from <see cref="MinimizationDominance"/> is that the objective count is unknown at construction.** An acceptance policy
/// and the search memory receive "the coordinate projection the problem supplies"; the arity belongs to the problem, not to this type, so it cannot be
/// fixed here.
///
/// **Mismatched lengths yield "does not dominate" rather than throwing**: an acceptance policy calls this on every step, and throwing would blow a
/// configuration mistake up into a runtime crash.
/// </remarks>
public sealed record AnyArityMinimizationDominance : IDominance {
    /// <inheritdoc/>
    public bool Dominates(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        if (lhs.Count == 0 || lhs.Count != rhs.Count) {
            return false;
        }
        bool strictlyBetter = false;
        for (int index = 0; index < lhs.Count; index++) {
            double left = lhs[index].ToDouble();
            double right = rhs[index].ToDouble();
            if (left > right) {
                return false;
            }
            if (left < right) {
                strictlyBetter = true;
            }
        }
        return strictlyBetter;
    }
}

/// <summary>
/// 快速非支配排序 / Fast non-dominated sorting.
/// </summary>
/// <remarks>
/// **同一层内的个体互不支配**，这是 Pareto 前沿的定义；排序的第 0 层就是当前种群的 Pareto 前沿。分层错乱的直接后果是选择压力
/// 偏向"恰好被先遍历到"的个体，而不是真正的非支配解。
/// **Members of one front never dominate each other** — that is the definition of a Pareto front, and front 0 is the current
/// population's front. Mis-layering directly biases selection pressure toward whichever individuals happen to be visited first rather
/// than toward the genuinely non-dominated ones.
/// </remarks>
public static class FastNonDominatedSort {
    /// <summary>
    /// 对候选集分层 / Layer the candidate set.
    /// </summary>
    /// <typeparam name="T">候选类型 / The candidate type.</typeparam>
    /// <param name="items">候选集 / The candidate set.</param>
    /// <param name="objectivesOf">目标向量提取器 / The objective-vector extractor.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <returns>分层结果，按层号升序 / The fronts ordered by ascending index.</returns>
    public static IReadOnlyList<IReadOnlyList<T>> Sort<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<Flt64>> objectivesOf,
        IDominance dominance) {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(objectivesOf);
        ArgumentNullException.ThrowIfNull(dominance);

        int size = items.Count;
        if (size == 0) {
            return Array.Empty<IReadOnlyList<T>>();
        }

        var objectives = new List<IReadOnlyList<Flt64>>(size);
        foreach (T item in items) {
            objectives.Add(objectivesOf(item));
        }

        var dominated = new List<int>[size];
        int[] dominatedAmount = new int[size];
        var fronts = new List<List<int>>();
        var current = new List<int>();
        for (int index = 0; index < size; index++) {
            dominated[index] = new List<int>();
            for (int other = 0; other < size; other++) {
                if (index == other) {
                    continue;
                }
                if (dominance.Dominates(objectives[index], objectives[other])) {
                    dominated[index].Add(other);
                } else if (dominance.Dominates(objectives[other], objectives[index])) {
                    dominatedAmount[index]++;
                }
            }
            if (dominatedAmount[index] == 0) {
                current.Add(index);
            }
        }

        bool[] visited = new bool[size];
        while (current.Count > 0) {
            fronts.Add(current);
            foreach (int index in current) {
                visited[index] = true;
            }

            var next = new List<int>();
            foreach (int index in current) {
                foreach (int dominatedIndex in dominated[index]) {
                    if (visited[dominatedIndex]) {
                        continue;
                    }
                    dominatedAmount[dominatedIndex]--;
                    if (dominatedAmount[dominatedIndex] == 0) {
                        next.Add(dominatedIndex);
                    }
                }
            }
            current = next;
        }

        // **必须兜住"仍有未访问个体"的情况**。若支配判据不满足传递性（或维度不符时一律返回 false 造成环），上面的循环会提前结束，
        // 剩下未被分层的个体就此消失——种群规模无声缩小，而迭代继续正常进行。兜底把它们并入最后一层。
        // **Individuals left unvisited must be caught.** If the dominance predicate is non-transitive (or the arity-mismatch guard
        // produces cycles), the loop above ends early and the unlayered individuals simply vanish — the population silently shrinks
        // while iterations keep running normally. The fallback folds them into a final front.
        var leftovers = Enumerable.Range(0, size).Where(index => !visited[index]).ToList();
        if (leftovers.Count > 0) {
            fronts.Add(leftovers);
        }

        return fronts
            .Select(front => (IReadOnlyList<T>)front.Select(index => items[index]).ToList())
            .ToList();
    }
}

/// <summary>
/// Das–Dennis 参考点生成 / Das-Dennis reference-point generation.
/// </summary>
/// <remarks>
/// **参数非法时返回空列表而不是抛异常**。参考点用于 NSGA-III 的关联与选择；抛异常会让一次配置失误终止整个搜索，而空列表只会让
/// "没有参考点"这件事显式暴露在后续的选择结果里。
/// **Invalid arguments return an empty list instead of throwing.** Reference points drive NSGA-III association and selection; a
/// throw would abort the whole search over one configuration slip, while an empty list surfaces "no reference points" explicitly in
/// the subsequent selection results.
/// **点数等于 C(divisions + objectiveAmount − 1, objectiveAmount − 1)**——`divisions` 变大时点数组合式增长，调用方需要自己控制规模。
/// **The count equals C(divisions + objectiveAmount - 1, objectiveAmount - 1)** — it grows combinatorially with `divisions`, so the
/// caller controls the scale.
/// </remarks>
public static class ReferencePoints {
    /// <summary>
    /// 生成 Das–Dennis 参考点 / Generate Das-Dennis reference points.
    /// </summary>
    /// <param name="objectiveAmount">目标数量 / The objective amount.</param>
    /// <param name="divisions">每个目标上的分割数 / The divisions per objective.</param>
    /// <returns>参考点列表；参数非法时为空 / The reference points, empty for invalid arguments.</returns>
    public static IReadOnlyList<IReadOnlyList<double>> DasDennis(int objectiveAmount, int divisions) {
        if (objectiveAmount < 1 || divisions < 1) {
            return Array.Empty<IReadOnlyList<double>>();
        }

        var points = new List<IReadOnlyList<double>>();
        var current = new List<double>(objectiveAmount);
        Generate(divisions, objectiveAmount);

        void Generate(int remaining, int objectivesLeft) {
            if (objectivesLeft == 1) {
                current.Add((double)remaining / divisions);
                points.Add(new List<double>(current));
                current.RemoveAt(current.Count - 1);
                return;
            }
            for (int share = 0; share <= remaining; share++) {
                current.Add((double)share / divisions);
                Generate(remaining - share, objectivesLeft - 1);
                current.RemoveAt(current.Count - 1);
            }
        }

        return points;
    }

    /// <summary>
    /// 目标向量到参考方向的垂直距离 / The perpendicular distance from an objective vector to a reference direction.
    /// </summary>
    /// <remarks>
    /// **参考方向的模长不大于 `1e-12` 时返回正无穷**，而不是 0。返回 0 会让"零向量"看起来是所有个体里最贴近方向的那一个，
    /// **于是每轮补位都挑中同一批个体，参考方向的多样性维护彻底失效而编号仍然正常递增**。
    /// **A reference direction whose norm is not greater than `1e-12` yields positive infinity**, not zero. Returning zero would make the
    /// "zero vector" look like the closest direction for every individual, **so every fill round picks the same batch and the reference
    /// direction's diversity maintenance silently collapses while the indices still advance normally**.
    /// </remarks>
    /// <param name="objectives">归一化后的目标向量 / The normalised objective vector.</param>
    /// <param name="reference">参考方向 / The reference direction.</param>
    /// <returns>垂直距离 / The perpendicular distance.</returns>
    public static double PerpendicularDistance(
        IReadOnlyList<double> objectives, IReadOnlyList<double> reference) {
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentNullException.ThrowIfNull(reference);
        if (objectives.Count != reference.Count) {
            throw new ArgumentException(
                "the objective vector and the reference direction must have the same length",
                nameof(reference));
        }
        double projection = 0.0;
        double referenceNorm = 0.0;
        for (int index = 0; index < reference.Count; index++) {
            projection += objectives[index] * reference[index];
            referenceNorm += reference[index] * reference[index];
        }
        if (referenceNorm <= 1.0e-12) {
            return double.PositiveInfinity;
        }
        double squared = 0.0;
        for (int index = 0; index < reference.Count; index++) {
            double component = objectives[index] - (projection * reference[index] / referenceNorm);
            squared += component * component;
        }
        return global::System.Math.Sqrt(squared);
    }
}
