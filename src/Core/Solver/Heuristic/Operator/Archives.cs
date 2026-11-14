// 搜索档案与拥挤距离 / Search archives and crowding distance.
// 与固定 Kotlin 基线 `operator/archive/Archive.kt`、`operator/archive/ParetoSearchArchive.kt`、
// `operator/multi_objective/ParetoArchive.kt`、`operator/multi_objective/NonDominatedSort.kt` 的 `CrowdingDistance` 对应。
// Mirrors the pinned baseline's `Archive.kt`, `ParetoSearchArchive.kt`, `ParetoArchive.kt` and `CrowdingDistance`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 搜索档案 / A search archive.
/// </summary>
/// <remarks>
/// **`Update` 的返回值是"档案最优是否被改善"，不是一个成功标记**。把它当成成功标记会让调用方无法判断收敛是否停滞，
/// 而"停滞"恰恰是切换阶段或终止搜索的唯一依据。
/// **`Update` returns "did the archive best improve", not a success flag.** Reading it as a success flag leaves the caller unable
/// to detect stagnation, and stagnation is the only basis for switching phases or stopping.
/// **档案必须自己持有独立存储**。直接引用外部传入的个体列表会让调用方后续的原地修改静默改写档案内容。
/// **An archive must own its storage.** Holding a reference to the caller's individual list lets later in-place edits silently rewrite
/// the archive contents.
/// </remarks>
/// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IArchive<TObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>当前档案中的个体，按优度排列 / The individuals currently in the archive, ordered by quality.</summary>
    IReadOnlyList<IIndividual<TObjValue, V>> Individuals { get; }

    /// <summary>
    /// 用新个体更新档案 / Update the archive with new individuals.
    /// </summary>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="candidates">新个体 / The new individuals.</param>
    /// <returns>档案最优是否被改善 / Whether the archive best improved.</returns>
    bool Update<TObj>(IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<IIndividual<TObjValue, V>> candidates);

    /// <summary>清空档案 / Clear the archive.</summary>
    void Clear();
}

/// <summary>
/// 保优档案，只保留最优的若干个个体 / A best-only archive keeping the top individuals.
/// </summary>
/// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Amount">保留数量 / The retained amount.</param>
public sealed class BestArchive<TObjValue, V>(int Amount) : IArchive<TObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly List<IIndividual<TObjValue, V>> _entries = new();

    /// <inheritdoc/>
    public IReadOnlyList<IIndividual<TObjValue, V>> Individuals => new List<IIndividual<TObjValue, V>>(_entries);

    /// <inheritdoc/>
    public bool Update<TObj>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<IIndividual<TObjValue, V>> candidates) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(candidates);

        IIndividual<TObjValue, V>? previousBest = _entries.Count > 0 ? _entries[0] : null;
        _entries.AddRange(candidates);
        _entries.Sort((lhs, rhs) => HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
        while (_entries.Count > Amount) {
            _entries.RemoveAt(_entries.Count - 1);
        }

        if (_entries.Count == 0) {
            return false;
        }
        IIndividual<TObjValue, V> currentBest = _entries[0];
        return previousBest is null
            || HeuristicOrdering.CompareStrictly(model, currentBest.Objective, previousBest.Objective) < 0;
    }

    /// <inheritdoc/>
    public void Clear() => _entries.Clear();
}

/// <summary>
/// 拥挤距离分配 / Crowding-distance assignment.
/// </summary>
/// <remarks>
/// **边界的两个个体距离为无穷大**，这是 NSGA-II 保持前沿两端不被淘汰的机制。把边界距离算成有限值，前沿会随时间向中间收缩，
/// 最终丢掉整个极端权衡区域，**而每一代的成员看起来都合法**。
/// **The two boundary individuals get infinite distance** — NSGA-II's mechanism for keeping both ends of a front alive. Computing a
/// finite boundary distance makes the front contract toward its middle over the generations and eventually lose the entire extreme
/// trade-off region, **while every generation's membership looks legitimate**.
/// **某一维跨度为 0 时跳过该维**。直接用 0 做分母会得到 `∞` 或 `NaN`，进而污染所有个体的距离与后续裁剪顺序。
/// **A dimension with zero span is skipped.** Dividing by 0 yields `∞` or `NaN`, poisoning every individual's distance and the
/// subsequent trimming order.
/// </remarks>
public static class CrowdingDistance {
    /// <summary>
    /// 计算拥挤距离 / Compute the crowding distance.
    /// </summary>
    /// <param name="objectives">目标向量 / The objective vectors.</param>
    /// <returns>每个个体的拥挤距离 / The crowding distance of each individual.</returns>
    public static IReadOnlyList<double> Assign(IReadOnlyList<IReadOnlyList<Flt64>> objectives) {
        ArgumentNullException.ThrowIfNull(objectives);
        int size = objectives.Count;
        if (size == 0) {
            return Array.Empty<double>();
        }
        if (size <= 2) {
            return Enumerable.Repeat(double.PositiveInfinity, size).ToList();
        }

        double[] distance = new double[size];
        int dimension = objectives[0].Count;
        for (int index = 0; index < dimension; index++) {
            var order = Enumerable.Range(0, size)
                .OrderBy(position => objectives[position][index].ToDouble())
                .ToList();
            double minimum = objectives[order[0]][index].ToDouble();
            double maximum = objectives[order[size - 1]][index].ToDouble();
            distance[order[0]] = double.PositiveInfinity;
            distance[order[size - 1]] = double.PositiveInfinity;

            double span = maximum - minimum;
            if (span <= 0.0) {
                continue;
            }
            for (int position = 1; position < size - 1; position++) {
                double current = objectives[order[position]][index].ToDouble();
                double next = objectives[order[position + 1]][index].ToDouble();
                double previous = objectives[order[position - 1]][index].ToDouble();
                distance[order[position]] += (next - previous) / span;
                if (!double.IsFinite(current)) {
                    distance[order[position]] = double.PositiveInfinity;
                }
            }
        }
        return distance;
    }
}

/// <summary>
/// Pareto 档案，按支配关系与拥挤距离保容量 / A Pareto archive bounded by dominance and crowding distance.
/// </summary>
/// <remarks>
/// **重复目标向量只保留一份**。目标完全相同的两个解在决策空间里可能差别很大，但对"前沿形状"没有贡献；全部保留会让档案被同一片区域的
/// 重复点塞满，真正的权衡区域挤不进去。
/// **Only one copy of a repeated objective vector is kept.** Two solutions with identical objectives may differ a lot in decision space
/// but contribute nothing to the front's shape; keeping all of them fills the archive with duplicates from one region and crowds out
/// the genuine trade-off areas.
/// **容量裁剪按拥挤距离降序保留**，距离相同按先来后到——必须用稳定顺序，否则同一份输入两次运行得到不同档案，检查点对比失去意义。
/// **Trimming keeps the largest crowding distances**, breaking ties by arrival order. The order must be stable; otherwise the same
/// input yields different archives on two runs, and checkpoint comparison loses its meaning.
/// </remarks>
/// <typeparam name="T">候选类型 / The candidate type.</typeparam>
/// <param name="Capacity">容量 / The capacity.</param>
/// <param name="ObjectivesOf">目标向量提取器 / The objective-vector extractor.</param>
/// <param name="Dominance">支配判据 / The dominance predicate.</param>
public sealed class ParetoArchive<T>(
    int Capacity,
    Func<T, IReadOnlyList<Flt64>> ObjectivesOf,
    IDominance Dominance) {
    private readonly List<T> _members = new();

    /// <summary>档案内容 / The archive content.</summary>
    public IReadOnlyList<T> Items => new List<T>(_members);

    /// <summary>档案大小 / The archive size.</summary>
    public int Size => _members.Count;

    /// <summary>
    /// 提交一个候选 / Offer one candidate.
    /// </summary>
    /// <param name="candidate">候选 / The candidate.</param>
    /// <returns>更新后的档案内容 / The updated archive content.</returns>
    public IReadOnlyList<T> Offer(T candidate) => Offer(new[] { candidate });

    /// <summary>
    /// 批量提交候选 / Offer several candidates.
    /// </summary>
    /// <param name="candidates">候选集 / The candidates.</param>
    /// <returns>更新后的档案内容 / The updated archive content.</returns>
    public IReadOnlyList<T> Offer(IReadOnlyList<T> candidates) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ObjectivesOf);
        ArgumentNullException.ThrowIfNull(Dominance);

        foreach (T candidate in candidates) {
            IReadOnlyList<Flt64> candidateObjectives = ObjectivesOf(candidate);
            if (_members.Any(member => Dominance.Dominates(ObjectivesOf(member), candidateObjectives))) {
                continue;
            }
            if (_members.Any(member => ObjectivesOf(member).SequenceEqual(candidateObjectives))) {
                continue;
            }
            _members.RemoveAll(member => Dominance.Dominates(candidateObjectives, ObjectivesOf(member)));
            _members.Add(candidate);
        }
        Trim();
        return Items;
    }

    /// <summary>
    /// 按容量裁剪 / Trim to the capacity.
    /// </summary>
    private void Trim() {
        if (_members.Count <= Capacity) {
            return;
        }

        IReadOnlyList<double> distances = CrowdingDistance.Assign(
            _members.Select(member => ObjectivesOf(member)).ToList());
        // 稳定顺序：距离降序，距离相同则先到先留。`ThenBy` 用原始下标，避免依赖排序算法自身的稳定性。
        // Stable order: descending distance, ties broken by arrival index. `ThenBy` uses the original index so the result does not
        // rely on the sort algorithm's own stability.
        var retained = Enumerable.Range(0, _members.Count)
            .OrderByDescending(index => distances[index])
            .ThenBy(index => index)
            .Take(Capacity)
            .OrderBy(index => index)
            .ToList();

        var survivors = _members.Where((_, index) => retained.Contains(index)).ToList();
        _members.Clear();
        _members.AddRange(survivors);
    }
}

/// <summary>
/// 多目标搜索档案 / A multi-objective search archive.
/// </summary>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ParetoSearchArchive<V> : IArchive<IReadOnlyList<Flt64>, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly int _capacity;
    private readonly IDominance _dominance;
    private ParetoArchive<IIndividual<IReadOnlyList<Flt64>, V>> _entries;

    /// <summary>
    /// 构造多目标搜索档案 / Construct the multi-objective search archive.
    /// </summary>
    /// <param name="capacity">容量 / The capacity.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    public ParetoSearchArchive(int capacity, IDominance dominance) {
        ArgumentNullException.ThrowIfNull(dominance);
        _capacity = capacity;
        _dominance = dominance;
        // 字段初始值设定项不能引用实例方法，必须在构造函数里建；否则只能把 `Fresh` 改成静态并多传两个参数。
        // A field initializer cannot call an instance method, so the archive is built in the constructor; the alternative is making
        // `Fresh` static and threading two extra parameters through it.
        _entries = Fresh();
    }

    /// <inheritdoc/>
    public IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> Individuals => _entries.Items;

    /// <inheritdoc/>
    public bool Update<TObj>(
        IAbstractCallBackModelInterface<TObj, IReadOnlyList<Flt64>, V> model,
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> candidates) {
        ArgumentNullException.ThrowIfNull(candidates);

        var previous = _entries.Items.Select(member => member.Objective).ToList();
        _entries.Offer(candidates);
        // 只有当"档案里存在某个成员，既不等价于旧成员也不被旧成员支配"时，才算真正改善。
        // It only counts as an improvement when some archive member is neither equal to nor dominated by any previous member.
        return _entries.Items.Any(candidate => !previous.Any(prior =>
            prior.SequenceEqual(candidate.Objective) || _dominance.Dominates(prior, candidate.Objective)));
    }

    /// <inheritdoc/>
    public void Clear() => _entries = Fresh();

    /// <summary>
    /// 构造一个空档案 / Build an empty archive.
    /// </summary>
    /// <returns>空档案 / An empty archive.</returns>
    private ParetoArchive<IIndividual<IReadOnlyList<Flt64>, V>> Fresh() =>
        new(_capacity, individual => individual.Objective, _dominance);
}
