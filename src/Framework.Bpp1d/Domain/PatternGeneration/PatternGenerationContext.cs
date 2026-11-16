// 有界候选生成 / Bounded candidate generation.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.PatternGeneration;

/// <summary>
/// 有界候选搜索配置 / Bounded pattern search configuration.
/// </summary>
/// <param name="MaxPatterns">最多保留候选数 / Maximum retained patterns.</param>
/// <param name="MaxNodes">最多访问节点数 / Maximum visited nodes.</param>
public sealed record PatternGenerationConfig(int MaxPatterns = 10000, int MaxNodes = 100000);

/// <summary>
/// 候选及完整性证明 / Candidates and completeness certificate.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Patterns">可行候选 / Feasible patterns.</param>
/// <param name="Complete">是否穷尽所有可行组合 / Whether all feasible combinations were exhausted.</param>
/// <param name="VisitedNodes">搜索节点数 / Visited nodes.</param>
public sealed record PatternGenerationResult<V>(
    IReadOnlyList<PackingPattern<V>> Patterns,
    bool Complete,
    int VisitedNodes)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 可替换候选接收策略 / Replaceable candidate acceptance policy.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IPatternPolicy<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 接收候选 / Accept a candidate.
    /// </summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <returns>是否接收 / Whether accepted.</returns>
    bool Accept(PackingPattern<V> pattern);
}

/// <summary>
/// 降序初始解与有界组合候选生成 / Decreasing initial packing and bounded combination generation.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="policy">候选策略 / Candidate policy.</param>
public sealed class PatternGenerationContext<V>(IPatternPolicy<V>? policy = null)
    where V : struct, IFloatingNumber<V> {
    private readonly IPatternPolicy<V>? _policy = policy;

    /// <summary>零值 / The additive zero.</summary>
    private static V Zero => NumericConstantsRegistry.For<V>().Zero;

    /// <summary>
    /// 枚举可行列，保留截断标记 / Enumerate feasible columns, preserving truncation information.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="config">搜索预算 / Search budget.</param>
    /// <returns>候选或失败 / Candidates or failure.</returns>
    /// <remarks>
    /// **`Complete` 标记是这一层的核心产出**：搜索被预算截断时必须如实报告 `false`，
    /// 否则上层会把"受限池的最优"误报成"全局最优"。
    ///
    /// **间距只加在件与件之间**：`nextUsed` 的增量是 `length + (nonempty || count &gt; 0 ? gap : 0)`。
    /// 若第一件也加间距，每个候选都会凭空多占一段，**可行域被无谓缩小**。
    ///
    /// **`Complete` is this layer's core output**: when the search is truncated by a budget it must honestly report `false`, otherwise the caller would pass off
    /// "optimal over a restricted pool" as "globally optimal". **Spacing is added between pieces only**: the `nextUsed` increment is
    /// `length + (nonempty || count &gt; 0 ? gap : 0)`; adding a gap for the first piece would shrink the feasible region for no reason.
    /// </remarks>
    public Result<PatternGenerationResult<V>, ErrorCode, Error<ErrorCode>> Generate(
        PackingProblem<V> problem,
        PatternGenerationConfig? config = null) {
        config ??= new PatternGenerationConfig();
        Result<Success, ErrorCode, Error<ErrorCode>> validation = problem.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PatternGenerationResult<V>>(validation);
        }
        if (config.MaxPatterns < 1 || config.MaxNodes < 1) {
            return Results.Failed<PatternGenerationResult<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "候选预算必须为正 / Pattern budgets must be positive"));
        }

        var patterns = new Dictionary<string, PackingPattern<V>>();
        var order = new List<string>();
        var state = new SearchState {
            Visited = 0,
            Complete = true,
        };

        foreach (BinType<V> bin in problem.BinTypes) {
            if (bin.Inventory is 0UL) {
                continue;
            }
            if (!CollectItems(problem, bin, out List<Item<V>> items, out V gap, out List<V> lengths, out List<V> weights)) {
                return Results.Failed<PatternGenerationResult<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "候选生成的量纲不一致 / Unit mismatch while generating patterns"));
            }

            var counts = new Dictionary<string, ulong>();
            Search(bin, items, lengths, weights, gap, counts, 0, Zero, Zero, false, config, patterns, order, state);
        }

        var result = new List<PackingPattern<V>>(order.Count);
        foreach (string key in order) {
            result.Add(patterns[key]);
        }
        return Results.Ok(new PatternGenerationResult<V>(result, state.Complete, state.Visited));
    }

    /// <summary>
    /// 生成降序首次/最佳适应初始列；不把启发式结果当作完备证明 / Generate FFD/BFD seeds without claiming completeness.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="bestFit">是否选择剩余长度最小的已有箱 / Whether to choose the tightest existing bin.</param>
    /// <returns>初始列或失败 / Initial patterns or failure.</returns>
    /// <remarks>
    /// **启发式，不是证明**：返回值**不带** `Complete` 标记，调用方不得据此宣称最优。
    /// 单件方案也在这里产出，否则只有一个物品能装进箱时初始池会为空。
    ///
    /// **Heuristic, not proof**: the return value carries **no** `Complete` flag, so callers must not claim optimality from it. Singleton patterns are emitted here too,
    /// otherwise the initial pool would be empty when only one item fits a bin.
    /// </remarks>
    public Result<IReadOnlyList<PackingPattern<V>>, ErrorCode, Error<ErrorCode>> InitialPatterns(
        PackingProblem<V> problem,
        bool bestFit = false) {
        Result<Success, ErrorCode, Error<ErrorCode>> validation = problem.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<IReadOnlyList<PackingPattern<V>>>(validation);
        }

        var patterns = new Dictionary<string, PackingPattern<V>>();
        var order = new List<string>();

        foreach (BinType<V> bin in problem.BinTypes) {
            if (!CollectItems(problem, bin, out List<Item<V>> items, out V gap, out List<V> lengths, out List<V> weights)) {
                return Results.Failed<IReadOnlyList<PackingPattern<V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "初始解的量纲不一致 / Unit mismatch while building initial patterns"));
            }

            var bins = new List<BinState>();
            for (int index = 0; index < items.Count; index++) {
                Item<V> item = items[index];
                V length = lengths[index];
                V weight = weights[index];
                if (item.Amount > 100000UL) {
                    return Results.Failed<IReadOnlyList<PackingPattern<V>>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "初始解展开预算超限 / Initial packing expansion budget exceeded"));
                }
                if (length.CompareTo(bin.Length.Value) > 0
                    || weight.CompareTo(bin.MaxWeight.Value) > 0
                    || item.Amount == 0) {
                    continue;
                }

                // **这一行与下面的循环有重叠**：循环至少跑一次（`Amount >= 1`，上面已排除 0），
                // 首次迭代在 `bins` 为空时新建一个箱并只放一件，产出的正是同一个单件方案。
                // 所以删掉这行**不改变结果**——变异存活是**真实的等价变异**，不是判据不够。
                //
                // 保留它是为了**逐行对应**。删掉会减少一次 `Add` 调用，
                // 但也会让后续阅读时多一处"为什么这里要这样"的疑问。
                //
                // **This line overlaps with the loop below**: the loop always runs at least once (`Amount >= 1`, zero already excluded) and its first iteration creates a
                // fresh bin holding exactly one piece — the same singleton. Deleting this line **does not change the result**, so the surviving mutant is a **genuine
                // equivalent mutant**, not a weak criterion. It is kept to **keep the two paths in step**; removing it would only save one `Add` call while adding
                // a "why is this different" question for the next reader.
                var singleton = new PackingPattern<V>(bin.Id, new Dictionary<string, ulong> { [item.Id] = 1 });
                Add(patterns, order, singleton);

                for (ulong copy = 0; copy < item.Amount; copy++) {
                    int chosen = -1;
                    for (int candidate = 0; candidate < bins.Count; candidate++) {
                        V usedWithGap = bins[candidate].Used.Plus(gap).Plus(length);
                        V weightWithItem = bins[candidate].Weight.Plus(weight);
                        if (usedWithGap.CompareTo(bin.Length.Value) > 0 || weightWithItem.CompareTo(bin.MaxWeight.Value) > 0) {
                            continue;
                        }
                        if (bestFit) {
                            if (chosen < 0 || bins[candidate].Used.CompareTo(bins[chosen].Used) > 0) {
                                chosen = candidate;
                            }
                        }
                        else {
                            chosen = candidate;
                            break;
                        }
                    }

                    if (chosen < 0) {
                        bins.Add(new BinState {
                            Counts = new Dictionary<string, ulong> { [item.Id] = 1 },
                            Used = length,
                            Weight = weight,
                        });
                    }
                    else {
                        BinState current = bins[chosen];
                        current.Counts[item.Id] = current.Counts.TryGetValue(item.Id, out ulong value) ? value + 1 : 1;
                        current.Used = current.Used.Plus(gap).Plus(length);
                        current.Weight = current.Weight.Plus(weight);
                    }
                }
            }

            foreach (BinState packed in bins) {
                Add(patterns, order, new PackingPattern<V>(bin.Id, packed.Counts));
            }
        }

        var result = new List<PackingPattern<V>>(order.Count);
        foreach (string key in order) {
            result.Add(patterns[key]);
        }
        return Results.Ok<IReadOnlyList<PackingPattern<V>>>(result);
    }

    /// <summary>搜索可变状态 / Mutable search state.</summary>
    private sealed class SearchState {
        /// <summary>已访问节点数 / Visited nodes.</summary>
        public int Visited { get; set; }
        /// <summary>是否仍然完整 / Whether the search is still complete.</summary>
        public bool Complete { get; set; }
    }

    /// <summary>一个已装箱体 / One packed bin.</summary>
    private sealed class BinState {
        /// <summary>逐物品件数 / Per-item counts.</summary>
        public Dictionary<string, ulong> Counts { get; init; } = new();
        /// <summary>已用长度 / Used length.</summary>
        public V Used { get; set; }
        /// <summary>已用重量 / Used weight.</summary>
        public V Weight { get; set; }
    }

    /// <summary>
    /// 把物品换算到箱型单位并按长度降序排列 / Convert items into the bin's units and sort by descending length.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="bin">箱型 / Bin type.</param>
    /// <param name="items">排序后的物品 / Sorted items.</param>
    /// <param name="gap">间距 / Spacing.</param>
    /// <param name="lengths">换算后的长度 / Converted lengths.</param>
    /// <param name="weights">换算后的重量 / Converted weights.</param>
    /// <returns>是否全部换算成功 / Whether every conversion succeeded.</returns>
    private static bool CollectItems(
        PackingProblem<V> problem,
        BinType<V> bin,
        out List<Item<V>> items,
        out V gap,
        out List<V> lengths,
        out List<V> weights) {
        items = new List<Item<V>>();
        lengths = new List<V>();
        weights = new List<V>();

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> gapResult = problem.Spacing.To(bin.Length.Unit);
        if (gapResult is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> gapOk) {
            gap = Zero;
            return false;
        }
        gap = gapOk.Value.Value;

        var candidates = new List<(Item<V> Item, V Length, V Weight)>();
        foreach (Item<V> item in problem.Items) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> length = item.Length.To(bin.Length.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> weight = item.Weight.To(bin.MaxWeight.Unit);
            if (length is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthOk
                || weight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> weightOk) {
                return false;
            }
            candidates.Add((item, lengthOk.Value.Value, weightOk.Value.Value));
        }

        // **降序**：长件先试，剪枝更快，也保证枚举顺序确定。
        // **Descending**: try long pieces first so pruning bites earlier, and the enumeration order stays deterministic.
        candidates.Sort((left, right) => right.Length.CompareTo(left.Length));
        foreach ((Item<V> item, V length, V weight) in candidates) {
            items.Add(item);
            lengths.Add(length);
            weights.Add(weight);
        }
        return true;
    }

    /// <summary>
    /// 有界深度优先枚举 / Bounded depth-first enumeration.
    /// </summary>
    /// <param name="bin">箱型 / Bin type.</param>
    /// <param name="items">物品 / Items.</param>
    /// <param name="lengths">换算后的长度 / Converted lengths.</param>
    /// <param name="weights">换算后的重量 / Converted weights.</param>
    /// <param name="gap">间距 / Spacing.</param>
    /// <param name="counts">当前计数 / Current counts.</param>
    /// <param name="index">当前物品下标 / Current item index.</param>
    /// <param name="used">已用长度 / Used length.</param>
    /// <param name="weight">已用重量 / Used weight.</param>
    /// <param name="nonEmpty">是否已放入物品 / Whether anything was placed.</param>
    /// <param name="config">预算 / Budget.</param>
    /// <param name="patterns">候选表 / Pattern map.</param>
    /// <param name="order">插入顺序 / Insertion order.</param>
    /// <param name="state">可变状态 / Mutable state.</param>
    private void Search(
        BinType<V> bin,
        IReadOnlyList<Item<V>> items,
        IReadOnlyList<V> lengths,
        IReadOnlyList<V> weights,
        V gap,
        Dictionary<string, ulong> counts,
        int index,
        V used,
        V weight,
        bool nonEmpty,
        PatternGenerationConfig config,
        Dictionary<string, PackingPattern<V>> patterns,
        List<string> order,
        SearchState state) {
        if (state.Visited >= config.MaxNodes || patterns.Count >= config.MaxPatterns) {
            state.Complete = false;
            return;
        }
        state.Visited++;

        if (index == items.Count) {
            if (nonEmpty) {
                var pattern = new PackingPattern<V>(bin.Id, new Dictionary<string, ulong>(counts));
                Add(patterns, order, pattern);
            }
            return;
        }

        Item<V> item = items[index];
        V length = lengths[index];
        V itemWeight = weights[index];
        ulong count = 0;
        V nextUsed = used;
        V nextWeight = weight;

        while (true) {
            if (count > 0) {
                counts[item.Id] = count;
            }
            else {
                counts.Remove(item.Id);
            }
            Search(
                bin, items, lengths, weights, gap, counts, index + 1,
                nextUsed, nextWeight, nonEmpty || count > 0, config, patterns, order, state);

            if (!state.Complete || count == item.Amount) {
                break;
            }
            // **第一件不加间距**：只有已经放过东西时才加。
            // **No gap for the first piece**: add one only when something is already placed.
            nextUsed = nextUsed.Plus(length);
            if (nonEmpty || count > 0) {
                nextUsed = nextUsed.Plus(gap);
            }
            nextWeight = nextWeight.Plus(itemWeight);
            if (nextUsed.CompareTo(bin.Length.Value) > 0 || nextWeight.CompareTo(bin.MaxWeight.Value) > 0) {
                break;
            }
            count++;
        }
        counts.Remove(item.Id);
    }

    /// <summary>按策略接收并按稳定键去重 / Accept by policy and deduplicate by the stable key.</summary>
    /// <param name="patterns">候选表 / Pattern map.</param>
    /// <param name="order">插入顺序 / Insertion order.</param>
    /// <param name="pattern">候选 / Candidate.</param>
    private void Add(
        Dictionary<string, PackingPattern<V>> patterns,
        List<string> order,
        PackingPattern<V> pattern) {
        if (_policy is not null && !_policy.Accept(pattern)) {
            return;
        }
        string key = pattern.Key;
        if (patterns.TryAdd(key, pattern)) {
            order.Add(key);
        }
    }

    /// <summary>把同码型的失败/致命结果转成本函数的返回类型 / Re-shape a failure or fatal result.</summary>
    /// <typeparam name="T">目标载荷类型 / Target payload type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Ok(default(T)!),
        };
}
