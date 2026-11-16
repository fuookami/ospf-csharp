// 搜索记忆与接受策略 / Search memory and acceptance policies.
// 与固定 Kotlin （6374 B）、（4164 B）、
//（4765 B）对应。
// , (4164 B) and
// (4765 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 接受策略 / An acceptance policy.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IMemoryAcceptance<ObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 判断是否接受候选解 / Decide whether to accept the candidate.
    /// </summary>
    /// <param name="working">当前工作集 / The current working set.</param>
    /// <param name="candidate">候选个体 / The candidate individual.</param>
    /// <returns>是否接受 / Whether to accept.</returns>
    bool Accept(IReadOnlyList<IIndividual<ObjValue, V>> working, IIndividual<ObjValue, V> candidate);
}

/// <summary>
/// 不劣于即接受 / Accept whenever nothing in the working set is strictly better.
/// </summary>
/// <remarks>
/// **判据是"工作集里没有任何个体支配候选"，而不是"候选支配工作集的第一个"**。用后者会让工作集里存在多个个体时只看第一个，
/// 剩下的被完全忽略——**接受率随工作集规模变化，而策略配置看起来完全一致**。
/// **The predicate is "nothing in the working set dominates the candidate", not "the candidate dominates the first entry".** The latter
/// only looks at the first entry when the working set has several, ignoring the rest — **the acceptance rate then varies with the
/// working-set size while the configuration looks identical**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class NotWorseAcceptancePolicy<ObjValue, V> : IMemoryAcceptance<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IDominance _dominance;
    private readonly Func<ObjValue, IReadOnlyList<Flt64>> _coordinates;

    /// <summary>
    /// 构造策略 / Construct the policy.
    /// </summary>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="coordinates">目标坐标投影 / The objective-coordinate projection.</param>
    public NotWorseAcceptancePolicy(
        IDominance dominance, Func<ObjValue, IReadOnlyList<Flt64>> coordinates) {
        ArgumentNullException.ThrowIfNull(dominance);
        ArgumentNullException.ThrowIfNull(coordinates);
        _dominance = dominance;
        _coordinates = coordinates;
    }

    /// <inheritdoc/>
    public bool Accept(IReadOnlyList<IIndividual<ObjValue, V>> working, IIndividual<ObjValue, V> candidate) {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(candidate);
        foreach (IIndividual<ObjValue, V> member in working) {
            if (_dominance.Dominates(_coordinates(member.Objective), _coordinates(candidate.Objective))) {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 交换策略 / An exchange policy.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IExchangePolicy<ObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 选择要交换的个体 / Select the individuals to exchange.
    /// </summary>
    /// <param name="decision">决策序号 / The decision index.</param>
    /// <param name="target">目标槽位 / The target slot.</param>
    /// <param name="working">当前工作集 / The current working set.</param>
    /// <returns>被选中的个体 / The selected individuals.</returns>
    IReadOnlyList<IIndividual<ObjValue, V>> Select(
        long decision, string target, IReadOnlyList<IIndividual<ObjValue, V>> working);
}

/// <summary>
/// 搜索记忆 / Search memory.
/// </summary>
/// <remarks>
/// **档案在单目标下按支配关系排序并截断到容量，在多目标下只保留第一前沿**。单目标也保留第一前沿会让档案退化成一个"最优解"，
/// 而多目标按支配排序会让互不支配的解被随意截断——**两种情况下容量都看起来被正确遵守**。
/// **Under single-objective search the archive sorts by dominance and truncates to capacity; under multi-objective search only the first
/// front is kept.** Keeping only the first front in single-objective collapses the archive to one solution, and dominance-sorting in
/// multi-objective truncates mutually non-dominating solutions arbitrarily — **in both cases the capacity appears correctly honoured**.
/// **`merged` 先过滤"有效解 + 有效目标 + 可行"再按解向量去重**。跳过任一过滤会让不可行解进入档案，而档案规模与最优值都正常；
/// 跳过去重会让同一个解被计入多次，**贡献度因此虚高**。
/// **`merged` filters "valid solution + valid objective + feasible" before deduplicating by solution vector.** Skipping any filter lets
/// infeasible solutions into the archive while its size and best value look normal; skipping deduplication counts one solution several
/// times and **inflates the contribution**.
/// **`contribution` 的第二个分量是"新颖个体数 / 候选数"**，不是"新颖个体数 / 档案规模"。用档案规模会让候选很少时贡献度虚高。
/// **`contribution`'s second component is "novel count / candidate count"**, not "novel count / archive size". Using the archive size
/// inflates the contribution when there are few candidates.
/// **`restore` 通过 `merged` 重建档案**，而不是直接赋值。直接赋值会让一份含重复或不可行个体的快照原样进入档案。
/// **`restore` rebuilds the archive through `merged`** instead of assigning directly. Direct assignment would let a snapshot containing
/// duplicates or infeasible individuals enter the archive verbatim.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class SearchMemory<ObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IDominance _dominance;
    private readonly Func<ObjValue, IReadOnlyList<Flt64>> _coordinates;
    private readonly Func<IIndividual<ObjValue, V>, bool> _admissible;
    private readonly Func<IReadOnlyList<IIndividual<ObjValue, V>>, int,
        IReadOnlyList<IIndividual<ObjValue, V>>> _trim;
    private List<IIndividual<ObjValue, V>> _entries = new();
    private List<IIndividual<ObjValue, V>> _accepted = new();

    /// <summary>
    /// 构造记忆 / Construct the memory.
    /// </summary>
    /// <param name="capacity">档案容量 / The archive capacity.</param>
    /// <param name="workingCapacity">工作集容量 / The working-set capacity.</param>
    /// <param name="acceptance">接受策略 / The acceptance policy.</param>
    /// <param name="multiObjective">是否多目标 / Whether it is multi-objective.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="coordinates">目标坐标投影 / The objective-coordinate projection.</param>
    /// <param name="admissible">个体是否合法可行 / Whether an individual is valid and feasible.</param>
    /// <param name="trim">前沿截断 / The front trimming.</param>
    public SearchMemory(
        int capacity,
        int workingCapacity,
        IMemoryAcceptance<ObjValue, V> acceptance,
        bool multiObjective,
        IDominance dominance,
        Func<ObjValue, IReadOnlyList<Flt64>> coordinates,
        Func<IIndividual<ObjValue, V>, bool> admissible,
        Func<IReadOnlyList<IIndividual<ObjValue, V>>, int,
            IReadOnlyList<IIndividual<ObjValue, V>>>? trim = null) {
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(dominance);
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(admissible);
        Capacity = capacity;
        WorkingCapacity = workingCapacity;
        Acceptance = acceptance;
        MultiObjective = multiObjective;
        _dominance = dominance;
        _coordinates = coordinates;
        _admissible = admissible;
        _trim = trim ?? ((entries, amount) => {
            var taken = new List<IIndividual<ObjValue, V>>(global::System.Math.Min(amount, entries.Count));
            for (int index = 0; index < entries.Count && index < amount; index++) {
                taken.Add(entries[index]);
            }
            return taken;
        });
    }

    /// <summary>档案容量 / The archive capacity.</summary>
    public int Capacity { get; }

    /// <summary>工作集容量 / The working-set capacity.</summary>
    public int WorkingCapacity { get; }

    /// <summary>接受策略 / The acceptance policy.</summary>
    public IMemoryAcceptance<ObjValue, V> Acceptance { get; }

    /// <summary>是否多目标 / Whether it is multi-objective.</summary>
    public bool MultiObjective { get; }

    /// <summary>档案 / The archive.</summary>
    public IReadOnlyList<IIndividual<ObjValue, V>> Archive => new List<IIndividual<ObjValue, V>>(_entries);

    /// <summary>工作集 / The working set.</summary>
    public IReadOnlyList<IIndividual<ObjValue, V>> Working => new List<IIndividual<ObjValue, V>>(_accepted);

    /// <summary>
    /// 合并参考集与候选集 / Merge the reference set with the candidates.
    /// </summary>
    /// <param name="reference">参考集 / The reference set.</param>
    /// <param name="candidates">候选集 / The candidates.</param>
    /// <returns>合并后的集合 / The merged set.</returns>
    public IReadOnlyList<IIndividual<ObjValue, V>> Merged(
        IReadOnlyList<IIndividual<ObjValue, V>> reference,
        IReadOnlyList<IIndividual<ObjValue, V>> candidates) {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidates);
        var all = new List<IIndividual<ObjValue, V>>(reference.Count + candidates.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IIndividual<ObjValue, V> candidate in reference) {
            AddIfAdmissible(all, seen, candidate);
        }
        foreach (IIndividual<ObjValue, V> candidate in candidates) {
            AddIfAdmissible(all, seen, candidate);
        }

        if (!MultiObjective) {
            var sorted = new List<IIndividual<ObjValue, V>>(all);
            sorted.Sort((left, right) => {
                if (_dominance.Dominates(_coordinates(left.Objective), _coordinates(right.Objective))) {
                    return -1;
                }
                if (_dominance.Dominates(_coordinates(right.Objective), _coordinates(left.Objective))) {
                    return 1;
                }
                return 0;
            });
            var truncated = new List<IIndividual<ObjValue, V>>(
                global::System.Math.Min(Capacity, sorted.Count));
            for (int index = 0; index < sorted.Count && index < Capacity; index++) {
                truncated.Add(sorted[index]);
            }
            return truncated;
        }

        var front = new List<IIndividual<ObjValue, V>>();
        var frontKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (IIndividual<ObjValue, V> candidate in all) {
            bool dominated = false;
            foreach (IIndividual<ObjValue, V> other in all) {
                if (_dominance.Dominates(_coordinates(other.Objective), _coordinates(candidate.Objective))) {
                    dominated = true;
                    break;
                }
            }
            if (!dominated && frontKeys.Add(Key(_coordinates(candidate.Objective)))) {
                front.Add(candidate);
            }
        }
        return _trim(front, Capacity);
    }

    /// <summary>
    /// 计算贡献度 / Compute the contribution.
    /// </summary>
    /// <param name="reference">参考集 / The reference set.</param>
    /// <param name="candidates">候选集 / The candidates.</param>
    /// <returns>是否有所贡献与贡献比例 / Whether it contributed and the contribution ratio.</returns>
    public (bool Improved, double Ratio) Contribution(
        IReadOnlyList<IIndividual<ObjValue, V>> reference,
        IReadOnlyList<IIndividual<ObjValue, V>> candidates) {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidates);
        IReadOnlyList<IIndividual<ObjValue, V>> merged = Merged(reference, candidates);
        int novel;
        if (MultiObjective) {
            novel = 0;
            foreach (IIndividual<ObjValue, V> candidate in merged) {
                bool fresh = true;
                foreach (IIndividual<ObjValue, V> other in reference) {
                    IReadOnlyList<Flt64> left = _coordinates(other.Objective);
                    IReadOnlyList<Flt64> right = _coordinates(candidate.Objective);
                    if (SameVector(left, right) || _dominance.Dominates(left, right)) {
                        fresh = false;
                        break;
                    }
                }
                if (fresh) {
                    novel++;
                }
            }
        } else if (merged.Count > 0 && (reference.Count == 0
            || _dominance.Dominates(_coordinates(merged[0].Objective), _coordinates(reference[0].Objective)))) {
            novel = 1;
        } else {
            novel = 0;
        }
        int divisor = global::System.Math.Max(candidates.Count, 1);
        return (novel > 0, novel / (double)divisor);
    }

    /// <summary>
    /// 提交一批候选 / Offer a batch of candidates.
    /// </summary>
    /// <param name="candidates">候选集 / The candidates.</param>
    /// <returns>档案是否被改善 / Whether the archive improved.</returns>
    public bool Offer(IReadOnlyList<IIndividual<ObjValue, V>> candidates) {
        ArgumentNullException.ThrowIfNull(candidates);
        bool improved = Contribution(_entries, candidates).Improved;
        _entries = new List<IIndividual<ObjValue, V>>(Merged(_entries, candidates));
        foreach (IIndividual<ObjValue, V> candidate in candidates) {
            if (!_admissible(candidate)) {
                continue;
            }
            if (Acceptance.Accept(_accepted, candidate)) {
                var next = new List<IIndividual<ObjValue, V>> { candidate };
                var seen = new HashSet<string>(StringComparer.Ordinal) { Key(candidate.Solution) };
                foreach (IIndividual<ObjValue, V> member in _accepted) {
                    if (seen.Add(Key(member.Solution)) && next.Count < WorkingCapacity) {
                        next.Add(member);
                    }
                }
                _accepted = next;
            }
        }
        return improved;
    }

    /// <summary>
    /// 从快照恢复 / Restore from a snapshot.
    /// </summary>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="working">工作集 / The working set.</param>
    public void Restore(
        IReadOnlyList<IIndividual<ObjValue, V>> archive,
        IReadOnlyList<IIndividual<ObjValue, V>> working) {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(working);
        _entries = new List<IIndividual<ObjValue, V>>(Merged(Array.Empty<IIndividual<ObjValue, V>>(), archive));
        var accepted = new List<IIndividual<ObjValue, V>>(
            global::System.Math.Min(WorkingCapacity, working.Count));
        for (int index = 0; index < working.Count && index < WorkingCapacity; index++) {
            accepted.Add(working[index]);
        }
        _accepted = accepted;
    }

    /// <summary>
    /// 若合法可行则加入 / Add when valid and feasible.
    /// </summary>
    /// <param name="target">目标列表 / The target list.</param>
    /// <param name="seen">已见键 / The seen keys.</param>
    /// <param name="candidate">候选 / The candidate.</param>
    private void AddIfAdmissible(
        List<IIndividual<ObjValue, V>> target,
        HashSet<string> seen,
        IIndividual<ObjValue, V> candidate) {
        if (!_admissible(candidate) || !seen.Add(Key(candidate.Solution))) {
            return;
        }
        target.Add(candidate);
    }

    /// <summary>
    /// 目标向量是否相同 / Whether two objective vectors are identical.
    /// </summary>
    /// <param name="left">左向量 / The left vector.</param>
    /// <param name="right">右向量 / The right vector.</param>
    /// <returns>是否相同 / Whether they are identical.</returns>
    private static bool SameVector(IReadOnlyList<Flt64> left, IReadOnlyList<Flt64> right) =>
        Key(left) == Key(right);

    /// <summary>
    /// 把向量转为可比较的键 / Turn a vector into a comparable key.
    /// </summary>
    /// <param name="values">向量 / The vector.</param>
    /// <returns>键 / The key.</returns>
    private static string Key<T>(IReadOnlyList<T> values) {
        var parts = new List<string>(values.Count);
        foreach (T value in values) {
            parts.Add(value is Flt64 flt ? flt.ToDouble().ToString("R") : value?.ToString() ?? "");
        }
        return string.Join(",", parts);
    }
}
