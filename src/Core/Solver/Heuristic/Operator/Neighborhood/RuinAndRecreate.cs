// 破坏—重建算子 / The ruin-and-recreate operators.
// 与固定 Kotlin 基线 `operator/neighborhood/RuinRecreate.kt`（10361 B）对应。
// Mirrors the pinned baseline's `operator/neighborhood/RuinRecreate.kt` (10361 B).

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator.Neighborhood;

/// <summary>
/// 破坏结果 / The ruin result.
/// </summary>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
/// <param name="Retained">保留的元素 / The retained elements.</param>
/// <param name="Removed">被移走的元素 / The removed elements.</param>
public sealed record RuinResult<T>(IReadOnlyList<T> Retained, IReadOnlyList<T> Removed);

/// <summary>
/// 破坏算子 / A ruin operator.
/// </summary>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public interface IRuin<T> {
    /// <summary>
    /// 从解中移走若干元素 / Remove some elements from the solution.
    /// </summary>
    /// <param name="solution">解 / The solution.</param>
    /// <param name="amount">移除数量 / The removal amount.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <returns>破坏结果 / The ruin result.</returns>
    Result<RuinResult<T>, ErrorCode, Error<ErrorCode>> Remove(
        IReadOnlyList<T> solution, int amount, SearchRandom random);
}

/// <summary>
/// 破坏—重建的公共校验 / Shared validation for ruin-and-recreate.
/// </summary>
/// <remarks>
/// **解里必须有互不相同的元素，且移除数量落在 `0..解长` 之内**。允许重复元素会让"按值删除"删掉多个，
/// **于是"移走 amount 个"这句话不再成立，而破坏本身看起来成功**；允许越界数量则让后续的"身份守恒"检查
/// **变成一个永远为假的条件**，破坏算子从此只能失败。
/// **The solution must hold distinct elements and the removal count must fall within `0..Count`**. Allowing duplicates makes
/// "remove by value" delete several at once, **so "removed `amount` elements" stops being true while the ruin looks successful**; allowing
/// an out-of-range count turns the later "identity conservation" check into **a condition that is never true**, so the ruin can only fail.
/// </remarks>
public static class RuinSupport {
    /// <summary>
    /// 校验解与移除数量 / Validate the solution and the removal count.
    /// </summary>
    /// <param name="solution">解 / The solution.</param>
    /// <param name="amount">移除数量 / The removal amount.</param>
    /// <returns>是否合法 / Whether the input is valid.</returns>
    public static bool Validate<T>(IReadOnlyList<T> solution, int amount) {
        ArgumentNullException.ThrowIfNull(solution);
        if (amount < 0 || amount > solution.Count) {
            return false;
        }
        var seen = new HashSet<T>();
        foreach (T value in solution) {
            if (!seen.Add(value)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 按下标集合划分 / Partition by an index set.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="solution">解 / The solution.</param>
    /// <param name="indices">下标集合 / The index set.</param>
    /// <returns>划分结果 / The partition.</returns>
    public static RuinResult<T> Partition<T>(IReadOnlyList<T> solution, IReadOnlySet<int> indices) {
        var retained = new List<T>(solution.Count);
        var removed = new List<T>(indices.Count);
        for (int index = 0; index < solution.Count; index++) {
            if (indices.Contains(index)) {
                removed.Add(solution[index]);
            } else {
                retained.Add(solution[index]);
            }
        }
        return new RuinResult<T>(retained, removed);
    }

    /// <summary>
    /// 构造失败结果 / Build a failure result.
    /// </summary>
    /// <typeparam name="T">成功值类型 / The success-value type.</typeparam>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"破坏—重建配置或状态无效 / Invalid ruin-and-recreate configuration or state: {message}"));
}

/// <summary>
/// 随机破坏 / Random ruin.
/// </summary>
/// <remarks>
/// **用部分 Fisher–Yates 洗牌选下标**，而不是"反复抽随机数直到凑够"。后者在接近全集时会退化成近似无限循环，
/// **而调用方只看到"这一步很慢"**。
/// **Uses a partial Fisher–Yates shuffle to pick indices**, rather than "draw until enough distinct ones". The latter degenerates into a
/// near-infinite loop as the amount approaches the whole set, **while the caller only sees "this step is slow"**.
/// </remarks>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public sealed class RandomRuin<T> : IRuin<T> {
    /// <inheritdoc/>
    public Result<RuinResult<T>, ErrorCode, Error<ErrorCode>> Remove(
        IReadOnlyList<T> solution, int amount, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(random);
        if (!RuinSupport.Validate(solution, amount)) {
            return RuinSupport.Invalid<RuinResult<T>>(
                "ruin requires unique elements and a valid removal count");
        }
        var indices = new List<int>(solution.Count);
        for (int index = 0; index < solution.Count; index++) {
            indices.Add(index);
        }
        for (int offset = 0; offset < amount; offset++) {
            int remaining = indices.Count - offset;
            int selected = offset + global::System.Math.Clamp(
                (int)(random.NextDouble() * remaining), 0, remaining - 1);
            int old = indices[offset];
            indices[offset] = indices[selected];
            indices[selected] = old;
        }
        var chosen = new HashSet<int>();
        for (int index = 0; index < amount; index++) {
            chosen.Add(indices[index]);
        }
        return Results.Ok<RuinResult<T>>(RuinSupport.Partition(solution, chosen));
    }
}

/// <summary>
/// 最差代价破坏 / Worst-cost ruin.
/// </summary>
/// <remarks>
/// **按代价降序取前 `amount` 个**。升序取会把最便宜的移走，**于是"重建"永远只能把解变得更差，而每步仍然成功返回**。
/// **Takes the top `amount` by descending cost.** Ascending order removes the cheapest, **so "recreate" can only make the solution worse
/// while every step still returns successfully**.
/// **代价必须有限**。出现 `NaN` 会让降序排序的结果未定义，**而挑出来的集合看起来仍然是一个合法的下标集合**。
/// **The cost must be finite.** A `NaN` makes the descending order undefined, **while the chosen set still looks like a valid index set**.
/// </remarks>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public sealed class WorstCostRuin<T> : IRuin<T> {
    private readonly Func<IReadOnlyList<T>, int, Result<Flt64, ErrorCode, Error<ErrorCode>>> _removalCost;

    /// <summary>
    /// 构造最差代价破坏 / Construct the worst-cost ruin.
    /// </summary>
    /// <param name="removalCost">移除代价 / The removal cost.</param>
    public WorstCostRuin(
        Func<IReadOnlyList<T>, int, Result<Flt64, ErrorCode, Error<ErrorCode>>> removalCost) {
        ArgumentNullException.ThrowIfNull(removalCost);
        _removalCost = removalCost;
    }

    /// <inheritdoc/>
    public Result<RuinResult<T>, ErrorCode, Error<ErrorCode>> Remove(
        IReadOnlyList<T> solution, int amount, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(random);
        if (!RuinSupport.Validate(solution, amount)) {
            return RuinSupport.Invalid<RuinResult<T>>(
                "ruin requires unique elements and a valid removal count");
        }
        var costs = new List<(int Index, double Cost)>(solution.Count);
        for (int index = 0; index < solution.Count; index++) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> value = _removalCost(solution, index);
            if (value is Failed<Flt64, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<RuinResult<T>>(failure.Error);
            }
            double cost = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)value).Value.ToDouble();
            if (!double.IsFinite(cost)) {
                return RuinSupport.Invalid<RuinResult<T>>("non-finite removal cost");
            }
            costs.Add((index, cost));
        }
        costs.Sort((lhs, rhs) => rhs.Cost.CompareTo(lhs.Cost));
        var chosen = new HashSet<int>();
        for (int index = 0; index < amount; index++) {
            chosen.Add(costs[index].Index);
        }
        return Results.Ok<RuinResult<T>>(RuinSupport.Partition(solution, chosen));
    }
}

/// <summary>
/// 相关度破坏 / Relatedness ruin.
/// </summary>
/// <remarks>
/// **先随机选一个种子，然后每次从"已选集合"里随机取一个基点，选与它最不相似的未选元素**。用固定的第一个基点会让整个集合退化成一簇，
/// **于是"移走一片相关元素"变成"移走一小团"，而重建仍然正常返回**。
/// **It seeds with one random element and then, each round, picks a random pivot from the already-selected set and takes the least similar
/// unselected element.** Using a fixed first pivot collapses the whole set into a single cluster, **so "remove a related region" becomes
/// "remove a tiny neighbourhood" while recreate still returns normally**.
/// **相关度必须是有限非负距离**。负数会让"最小"失去意义，**而挑出来的集合仍然是合法下标集合**。
/// **The relatedness must be a finite non-negative distance.** A negative value makes "smallest" meaningless, **while the chosen set is
/// still a valid index set**.
/// </remarks>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public sealed class RelatednessRuin<T> : IRuin<T> {
    private readonly Func<T, T, Result<Flt64, ErrorCode, Error<ErrorCode>>> _distance;

    /// <summary>
    /// 构造相关度破坏 / Construct the relatedness ruin.
    /// </summary>
    /// <param name="distance">相关度距离 / The relatedness distance.</param>
    public RelatednessRuin(Func<T, T, Result<Flt64, ErrorCode, Error<ErrorCode>>> distance) {
        ArgumentNullException.ThrowIfNull(distance);
        _distance = distance;
    }

    /// <inheritdoc/>
    public Result<RuinResult<T>, ErrorCode, Error<ErrorCode>> Remove(
        IReadOnlyList<T> solution, int amount, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(random);
        if (!RuinSupport.Validate(solution, amount)) {
            return RuinSupport.Invalid<RuinResult<T>>(
                "ruin requires unique elements and a valid removal count");
        }
        if (amount == 0) {
            return Results.Ok<RuinResult<T>>(new RuinResult<T>(new List<T>(solution), Array.Empty<T>()));
        }
        var selected = new List<int> {
            global::System.Math.Clamp((int)(random.NextDouble() * solution.Count), 0, solution.Count - 1),
        };
        var chosen = new HashSet<int>(selected);
        while (selected.Count < amount) {
            int pivot = selected[global::System.Math.Clamp(
                (int)(random.NextDouble() * selected.Count), 0, selected.Count - 1)];
            int nearest = -1;
            double best = double.PositiveInfinity;
            for (int index = 0; index < solution.Count; index++) {
                if (chosen.Contains(index)) {
                    continue;
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> value =
                    _distance(solution[pivot], solution[index]);
                if (value is Failed<Flt64, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<RuinResult<T>>(failure.Error);
                }
                double score = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)value).Value.ToDouble();
                if (!double.IsFinite(score) || score < 0.0) {
                    return RuinSupport.Invalid<RuinResult<T>>(
                        "relatedness must be a finite nonnegative distance");
                }
                if (score < best) {
                    nearest = index;
                    best = score;
                }
            }
            selected.Add(nearest);
            chosen.Add(nearest);
        }
        return Results.Ok<RuinResult<T>>(RuinSupport.Partition(solution, chosen));
    }
}

/// <summary>
/// 插入代价 / An insertion cost.
/// </summary>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public interface IInsertionCost<T> {
    /// <summary>
    /// 估计把一个元素插到某个位置的代价 / Estimate the cost of inserting an item at a position.
    /// </summary>
    /// <param name="partial">部分解 / The partial solution.</param>
    /// <param name="item">待插入元素 / The item to insert.</param>
    /// <param name="position">插入位置 / The insertion position.</param>
    /// <returns>代价，`null` 表示该位置不可行 / The cost, or `null` when the position is infeasible.</returns>
    Result<Flt64?, ErrorCode, Error<ErrorCode>> Cost(
        IReadOnlyList<T> partial, T item, int position);
}

/// <summary>
/// 遗憾 k 插入修复 / Regret-k insertion repair.
/// </summary>
/// <remarks>
/// **每一轮挑的是"遗憾值最大"的元素，而不是"代价最小"的元素**。挑最小代价等于贪心，**于是最"没有选择余地"的元素被留到最后，
/// 那时它往往已经没有可行的插入位置，整步以失败告终**——而失败前的那些轮次全都"成功"，看起来像算法本身在收敛。
/// **Each round picks the item with the largest regret, not the smallest cost.** Picking the smallest cost is plain greed, **so the item
/// with the least choice is left until the end, by which point it often has no feasible position and the whole step fails** — while every
/// earlier round "succeeded", making it look like the algorithm itself is converging.
/// **优先级是三段比较：可选位置数少者优先 → 遗憾大者优先 → 代价小者优先**。少了第一段会让"只有一个可行位置的元素"和
/// "有很多可行位置的元素"按遗憾比较，**而后者几乎总是赢，前者于是被无限期推迟**。
/// **The priority is a three-part comparison: fewer feasible positions first, then larger regret, then smaller cost.** Dropping the first
/// part makes "the item with exactly one feasible position" compete on regret against items with many positions, **and the latter almost
/// always wins, so the former is postponed indefinitely**.
/// **`k` 大于 1 时"可选位置数"取 `min(位置数, k)`**，否则取 `k` 本身。
/// **For `k` greater than 1 the "feasible position count" is `min(count, k)`**, otherwise `k` itself.
/// **修复结果必须是原解的一个排列**。只检查长度会让"重建"漏掉一个元素、重复另一个，**而解看起来仍然是一个合法排列**，
/// 于是搜索在另一个问题的解空间里继续。
/// **The repair result must be a permutation of the original solution.** Checking only the length lets "recreate" drop one element and
/// duplicate another, **while the solution still looks like a valid permutation**, so the search continues inside a different space.
/// </remarks>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public class RegretKInsertRepair<T> {
    private readonly IInsertionCost<T> _insertionCost;

    /// <summary>
    /// 构造遗憾 k 插入修复 / Construct the regret-k insertion repair.
    /// </summary>
    /// <param name="insertionCost">插入代价 / The insertion cost.</param>
    /// <param name="k">遗憾阶数 / The regret order.</param>
    public RegretKInsertRepair(IInsertionCost<T> insertionCost, int k = 2) {
        ArgumentNullException.ThrowIfNull(insertionCost);
        _insertionCost = insertionCost;
        K = k;
    }

    /// <summary>遗憾阶数 / The regret order.</summary>
    public int K { get; }

    /// <summary>
    /// 修复被破坏的解 / Repair a ruined solution.
    /// </summary>
    /// <param name="ruined">破坏结果 / The ruin result.</param>
    /// <returns>修复后的解 / The repaired solution.</returns>
    public Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> Repair(RuinResult<T> ruined) {
        ArgumentNullException.ThrowIfNull(ruined);
        if (K <= 0) {
            return RuinSupport.Invalid<IReadOnlyList<T>>(
                "invalid repair definition/elements");
        }
        var identity = new HashSet<T>();
        foreach (T value in ruined.Retained) {
            if (!identity.Add(value)) {
                return RuinSupport.Invalid<IReadOnlyList<T>>(
                    "invalid repair definition/elements");
            }
        }
        foreach (T value in ruined.Removed) {
            if (!identity.Add(value)) {
                return RuinSupport.Invalid<IReadOnlyList<T>>(
                    "invalid repair definition/elements");
            }
        }

        var partial = new List<T>(ruined.Retained);
        var remaining = new List<T>(ruined.Removed);
        while (remaining.Count > 0) {
            int chosenItem = -1;
            int chosenPosition = -1;
            double chosenRegret = double.NegativeInfinity;
            double chosenCost = double.PositiveInfinity;
            int chosenOptions = int.MaxValue;

            for (int itemIndex = 0; itemIndex < remaining.Count; itemIndex++) {
                T item = remaining[itemIndex];
                var options = new List<(int Position, double Cost)>();
                for (int position = 0; position <= partial.Count; position++) {
                    Result<Flt64?, ErrorCode, Error<ErrorCode>> outcome =
                        _insertionCost.Cost(new List<T>(partial), item, position);
                    if (outcome is Failed<Flt64?, ErrorCode, Error<ErrorCode>> failure) {
                        return Results.Failed<IReadOnlyList<T>>(failure.Error);
                    }
                    Flt64? value = ((Ok<Flt64?, ErrorCode, Error<ErrorCode>>)outcome).Value;
                    if (value is null) {
                        continue;
                    }
                    double cost = value.Value.ToDouble();
                    if (!double.IsFinite(cost)) {
                        return RuinSupport.Invalid<IReadOnlyList<T>>(
                            "non-finite insertion estimate");
                    }
                    options.Add((position, cost));
                }
                if (options.Count == 0) {
                    continue;
                }
                options.Sort((lhs, rhs) => lhs.Cost.CompareTo(rhs.Cost));
                double regret = 0.0;
                int top = global::System.Math.Min(K, options.Count);
                for (int index = 0; index < top; index++) {
                    regret += options[index].Cost - options[0].Cost;
                }
                if (!double.IsFinite(regret)) {
                    return RuinSupport.Invalid<IReadOnlyList<T>>("non-finite insertion regret");
                }
                int urgent = K > 1 ? global::System.Math.Min(options.Count, K) : K;
                bool better = urgent < chosenOptions
                    || (urgent == chosenOptions
                        && (regret > chosenRegret
                            || (regret == chosenRegret && options[0].Cost < chosenCost)));
                if (better) {
                    chosenItem = itemIndex;
                    chosenPosition = options[0].Position;
                    chosenRegret = regret;
                    chosenCost = options[0].Cost;
                    chosenOptions = urgent;
                }
            }

            if (chosenItem < 0) {
                return RuinSupport.Invalid<IReadOnlyList<T>>(
                    "unrepairable partial solution: no feasible insertion for remaining items");
            }
            partial.Insert(chosenPosition, remaining[chosenItem]);
            remaining.RemoveAt(chosenItem);
        }
        return Results.Ok<IReadOnlyList<T>>(partial);
    }
}

/// <summary>
/// 贪心插入修复 / Greedy insertion repair.
/// </summary>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public sealed class GreedyInsertRepair<T> : RegretKInsertRepair<T> {
    /// <summary>
    /// 构造贪心插入修复 / Construct the greedy insertion repair.
    /// </summary>
    /// <param name="insertionCost">插入代价 / The insertion cost.</param>
    public GreedyInsertRepair(IInsertionCost<T> insertionCost) : base(insertionCost, 1) { }
}

/// <summary>
/// 破坏—重建算子 / The ruin-and-recreate operator.
/// </summary>
/// <remarks>
/// **破坏与修复之后各做一次"元素身份守恒"检查**。只检查长度会让"重建"漏掉一个元素、重复另一个，**而解看起来仍然是一个合法排列**，
/// 于是搜索在另一个问题的解空间里继续，**而目标值曲线看起来只是"抖动"**。
/// **There is one "identity conservation" check after the ruin and another after the repair.** Checking only the length lets "recreate" drop
/// one element and duplicate another, **while the solution still looks like a valid permutation**, so the search continues inside a
/// different space **and the objective curve merely looks "jittery"**.
/// </remarks>
/// <typeparam name="T">元素类型 / The element type.</typeparam>
public sealed class RuinRecreateOperator<T> {
    private readonly IRuin<T> _ruin;
    private readonly RegretKInsertRepair<T> _repair;

    /// <summary>
    /// 构造破坏—重建算子 / Construct the ruin-and-recreate operator.
    /// </summary>
    /// <param name="ruin">破坏算子 / The ruin operator.</param>
    /// <param name="repair">修复算子 / The repair operator.</param>
    public RuinRecreateOperator(IRuin<T> ruin, RegretKInsertRepair<T> repair) {
        ArgumentNullException.ThrowIfNull(ruin);
        ArgumentNullException.ThrowIfNull(repair);
        _ruin = ruin;
        _repair = repair;
    }

    /// <summary>
    /// 执行一次破坏—重建 / Perform one ruin-and-recreate.
    /// </summary>
    /// <param name="solution">当前解 / The current solution.</param>
    /// <param name="amount">移除数量 / The removal amount.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <returns>新解 / The new solution.</returns>
    public Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<T> solution, int amount, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(random);
        if (!RuinSupport.Validate(solution, amount)) {
            return RuinSupport.Invalid<IReadOnlyList<T>>(
                "ruin requires unique elements and a valid removal count");
        }
        Result<RuinResult<T>, ErrorCode, Error<ErrorCode>> ruined =
            _ruin.Remove(solution, amount, random);
        if (ruined is Failed<RuinResult<T>, ErrorCode, Error<ErrorCode>> ruinFailure) {
            return Results.Failed<IReadOnlyList<T>>(ruinFailure.Error);
        }
        RuinResult<T> victim = ((Ok<RuinResult<T>, ErrorCode, Error<ErrorCode>>)ruined).Value;

        var all = new List<T>(victim.Retained);
        all.AddRange(victim.Removed);
        var identity = new HashSet<T>(all);
        var original = new HashSet<T>(solution);
        if (victim.Removed.Count != amount
            || all.Count != solution.Count
            || identity.Count != all.Count) {
            return RuinSupport.Invalid<IReadOnlyList<T>>(
                "Ruin did not conserve identities and removal count");
        }
        foreach (T value in all) {
            if (!original.Contains(value)) {
                return RuinSupport.Invalid<IReadOnlyList<T>>(
                    "Ruin did not conserve identities and removal count");
            }
        }

        Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> repaired = _repair.Repair(victim);
        if (repaired is Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> repairFailure) {
            return Results.Failed<IReadOnlyList<T>>(repairFailure.Error);
        }
        IReadOnlyList<T> result = ((Ok<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>)repaired).Value;
        var resultIdentity = new HashSet<T>(result);
        if (result.Count != solution.Count || resultIdentity.Count != result.Count) {
            return RuinSupport.Invalid<IReadOnlyList<T>>(
                "Repair did not conserve element identities");
        }
        foreach (T value in result) {
            if (!original.Contains(value)) {
                return RuinSupport.Invalid<IReadOnlyList<T>>(
                    "Repair did not conserve element identities");
            }
        }
        return Results.Ok<IReadOnlyList<T>>(result);
    }
}
