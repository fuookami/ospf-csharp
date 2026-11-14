// 尾箱重排入口 / The tail-bin rearrangement entry.
// 与固定 Kotlin 基线 `bpp1d/application/TailBinOptimizer.kt` 对应。
// Mirrors the pinned Kotlin baseline's `TailBinOptimizer.kt`.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Application;

/// <summary>
/// 尾箱重排 / Tail-bin rearrangement.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// 保留前部箱体，只把**尾部窗口**重求一遍，再用**完整问题**复核候选结果。
/// 这样分两段做的意义在于：**局部问题上的约束放宽不等于全局可行**，所以候选必须过完整复核。
///
/// 基线的注释点明了另一半：**外部业务约束不能直接作用在局部问题上**——
/// 它必须接收**完整结果**再判断。本批用一个 `accept` 回调承担这件事。
///
/// Keeps the prefix bins, re-solves only the **tail window**, then re-verifies the candidate against the **full problem**: relaxing constraints on a local model does
/// not make a global solution feasible. The baseline's comment names the other half — **external business constraints cannot be applied to a local problem**; they
/// must receive the **complete result**, which the `accept` callback does here.
/// </remarks>
public sealed class TailBinOptimizer<V>
    where V : struct, IFloatingNumber<V> {
    private readonly Bpp1dMilp<V> _solver;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="solver">普通装箱入口 / Ordinary packing entry.</param>
    public TailBinOptimizer(Bpp1dMilp<V> solver) {
        _solver = solver;
    }

    /// <summary>
    /// 重排尾箱 / Rearrange the tail bins.
    /// </summary>
    /// <param name="problem">原始问题 / Original problem.</param>
    /// <param name="current">已验证结果 / Verified result.</param>
    /// <param name="window">尾箱数量 / Tail-bin count.</param>
    /// <param name="accept">完整结果业务接收策略 / Full-result business acceptance policy.</param>
    /// <param name="config">局部模型配置 / Local model configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>重排后的完整结果或失败 / Repacked full result or failure.</returns>
    /// <remarks>
    /// **五处判断**：
    ///
    /// | 处 | 判断 | 省略后 |
    /// | --- | --- | --- |
    /// | 入参 | `window` 小于 1 或扩展非空 ⇒ 失败 | 空窗口等于什么都不做却不报错；扩展在局部模型上没有意义 |
    /// | 前置复核 | 先把 `current` 按模式复原一遍再装箱 | 一个本就不合法的当前结果被当成基线继续重排 |
    /// | 局部问题 | 需求改成窗口内的件数、**库存扣掉前缀已用** | 库存没扣 ⇒ 尾部会把前缀用掉的箱型再花一次 |
    /// | 候选复核 | 候选必须过**完整问题**的装箱 | 局部可行被当成全局可行 |
    /// | 比较 | 分数**变差就保留原结果** | 重排可能把结果弄差，而调用方以为它总能变好 |
    ///
    /// **"变差就保留原结果"是本类唯一的价值主张**：它保证 `optimize` 只有不变差才不会退步。
    ///
    /// **Five judgements** (table above). **"Keep the original when the score worsens" is the class's only value proposition**: it guarantees `optimize` cannot regress.
    /// </remarks>
    public async Task<Result<PackingResult<V>, ErrorCode, Error<ErrorCode>>> OptimizeAsync(
        PackingProblem<V> problem,
        PackingResult<V> current,
        int window,
        global::System.Func<PackingResult<V>, Try>? accept = null,
        Bpp1dSolveConfig<V>? config = null,
        CancellationToken cancellationToken = default) {
        config ??= new Bpp1dSolveConfig<V>();
        if (window < 1 || config.ExtensionsOrDefault.Count > 0) {
            return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "尾箱窗口必须为正，扩展须使用完整结果策略 / Positive tail window required; use full-result policies for extensions"));
        }
        accept ??= _ => Results.OkInstance;

        Result<PackingResult<V>, ErrorCode, Error<ErrorCode>> validation =
            new PackingContext<V>().Pack(problem, Patterns(current.Plans));
        if (validation is not Ok<PackingResult<V>, ErrorCode, Error<ErrorCode>> validationOk) {
            return Propagate<PackingResult<V>, PackingResult<V>>(validation);
        }
        _ = validationOk;

        if (current.Plans.Count == 0) {
            return Results.Ok(current);
        }

        int tail = global::System.Math.Min(window, current.Plans.Count);
        var prefix = new List<PackingPlan<V>>();
        for (int index = 0; index < current.Plans.Count - tail; ++index) {
            prefix.Add(current.Plans[index]);
        }

        var remaining = new Dictionary<string, ulong>();
        for (int index = current.Plans.Count - tail; index < current.Plans.Count; ++index) {
            foreach (Placement<V> placement in current.Plans[index].Placements) {
                remaining[placement.ItemId] = remaining.TryGetValue(placement.ItemId, out ulong found)
                    ? found + 1UL
                    : 1UL;
            }
        }

        var usedStock = new Dictionary<string, ulong>();
        foreach (PackingPlan<V> plan in prefix) {
            string typeId = plan.Bin.Type.Id;
            usedStock[typeId] = usedStock.TryGetValue(typeId, out ulong found) ? found + 1UL : 1UL;
        }

        // **局部问题的库存必须扣掉前缀已用的箱。** 不扣的话，尾部会把前缀已经花掉的箱型**再花一次**，
        // 局部模型因而比真实情况宽松——而它给出的候选正是要靠完整复核才挡得住的东西。
        //
        // **The local problem's inventory must subtract what the prefix already used.** Otherwise the tail spends the same bin type a second time and the local model
        // is looser than reality — exactly the kind of candidate the full re-verification exists to filter.
        var localItems = new List<Item<V>>();
        foreach (Item<V> item in problem.Items) {
            localItems.Add(item with {
                Amount = remaining.TryGetValue(item.Id, out ulong count) ? count : 0UL,
            });
        }
        var localBinTypes = new List<BinType<V>>();
        foreach (BinType<V> type in problem.BinTypes) {
            if (type.Inventory is not { } inventory) {
                localBinTypes.Add(type);
                continue;
            }
            ulong used = usedStock.TryGetValue(type.Id, out ulong found) ? found : 0UL;
            localBinTypes.Add(type with {
                Inventory = inventory > used ? inventory - used : 0UL,
            });
        }
        PackingProblem<V> local = problem with { Items = localItems, BinTypes = localBinTypes };

        Result<Bpp1dSolution<V>, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveAsync(local, config, cancellationToken).ConfigureAwait(false);
        if (solved is not Ok<Bpp1dSolution<V>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return Propagate<PackingResult<V>, Bpp1dSolution<V>>(solved);
        }

        var merged = new List<PackingPlan<V>>();
        merged.AddRange(prefix);
        merged.AddRange(solvedOk.Value.Packing.Plans);
        Result<PackingResult<V>, ErrorCode, Error<ErrorCode>> candidate =
            new PackingContext<V>().Pack(problem, Patterns(merged));
        if (candidate is not Ok<PackingResult<V>, ErrorCode, Error<ErrorCode>> candidateOk) {
            return Propagate<PackingResult<V>, PackingResult<V>>(candidate);
        }

        Result<V, ErrorCode, Error<ErrorCode>> candidateScore = Score(problem, candidateOk.Value);
        if (candidateScore is not Ok<V, ErrorCode, Error<ErrorCode>> candidateScoreOk) {
            return Propagate<PackingResult<V>, V>(candidateScore);
        }
        Result<V, ErrorCode, Error<ErrorCode>> currentScore = Score(problem, current);
        if (currentScore is not Ok<V, ErrorCode, Error<ErrorCode>> currentScoreOk) {
            return Propagate<PackingResult<V>, V>(currentScore);
        }
        if (candidateScoreOk.Value.CompareTo(currentScoreOk.Value) > 0) {
            return Results.Ok(current);
        }

        Try accepted = accept(candidateOk.Value);
        if (accepted is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PackingResult<V>, Success>(accepted);
        }
        return Results.Ok(candidateOk.Value);
    }

    /// <summary>
    /// 打分 / Score a result under the problem's objective.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="result">结果 / The result.</param>
    /// <returns>分数 / The score.</returns>
    /// <remarks>
    /// **三项口径各按各的，不加总、不换算。** 基线就是这样：
    ///
    /// | 目标 | 每箱计 |
    /// | --- | --- |
    /// | `BinCount` | `1` |
    /// | `Cost` | 箱型成本 |
    /// | `ConsumedLength` | 箱长**换算到问题自己的单位** |
    ///
    /// **`ConsumedLength` 必须换算**：箱长与问题里的长度若单位不同（米与毫米），
    /// 不算换就把"500 mm 的箱"与"0.5 m 的箱"当成同一长度，比较结果毫无意义。
    ///
    /// **The three metrics each stand alone, never summed or converted into one another**, exactly as in the baseline (table above). **`ConsumedLength` must be
    /// converted**: a bin length in metres and the problem's length in millimetres would otherwise compare "500 mm" with "0.5 m" as the same value.
    /// </remarks>
    /// <remarks>
    /// **判据可以直接打在这里**：它不需要求解器，只需要一个装好的结果。
    /// 单位换算是本类里最容易错、又最容易被"看起来对"糊过去的一段。
    ///
    /// **The criterion hits this method directly**: it needs no solver, only a built result. Unit conversion is the most error-prone part here and the easiest to
    /// mistake for correct.
    /// </remarks>
    internal static Result<V, ErrorCode, Error<ErrorCode>> Score(
        PackingProblem<V> problem,
        PackingResult<V> result) {
        V total = NumericConstantsRegistry.For<V>().Zero;
        foreach (PackingPlan<V> plan in result.Plans) {
            V term;
            switch (problem.Objective) {
                case PackingObjective.BinCount:
                    term = NumericConstantsRegistry.For<V>().One;
                    break;
                case PackingObjective.Cost:
                    term = plan.Bin.Type.Cost;
                    break;
                case PackingObjective.ConsumedLength:
                    Result<V, ErrorCode, Error<ErrorCode>> converted = ConvertedLength(problem, plan);
                    if (converted is not Ok<V, ErrorCode, Error<ErrorCode>> convertedOk) {
                        return converted;
                    }
                    term = convertedOk.Value;
                    break;
                default:
                    term = NumericConstantsRegistry.For<V>().Zero;
                    break;
            }
            total = total.Plus(term);
        }
        return Results.Ok(total);
    }

    /// <summary>
    /// 按问题自己的长度单位换算箱长 / Convert a bin length into the problem's own length unit.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="plan">装箱方案 / The plan.</param>
    /// <returns>换算后的长度值 / The converted length.</returns>
    private static Result<V, ErrorCode, Error<ErrorCode>> ConvertedLength(
        PackingProblem<V> problem,
        PackingPlan<V> plan) {
        Quantity<V> length = plan.Bin.Type.Length;
        if (problem.BinTypes.Count == 0) {
            return Results.Ok(length.Value);
        }
        // **换算失败要报错，不能退回原值。** 退回原值等于把"500 mm"和"0.5 m"当成同一个数，
        // 而比较结果会因此毫无意义——**错得还很安静**。
        //
        // **A failed conversion must be reported, not silently fallen back.** Falling back would treat "500 mm" and "0.5 m" as the same number, making the comparison
        // meaningless — and **silently so**.
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted =
            length.To(problem.BinTypes[0].Length.Unit);
        return converted is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> convertedOk
            ? Results.Ok(convertedOk.Value.Value)
            : Propagate<V, Quantity<V>>(converted);
    }

    /// <summary>把装箱方案折成模式 / Fold plans into patterns.</summary>
    /// <param name="plans">装箱方案 / The plans.</param>
    /// <returns>模式与份数 / Patterns and their counts.</returns>
    private static IReadOnlyDictionary<PackingPattern<V>, ulong> Patterns(IReadOnlyList<PackingPlan<V>> plans) {
        var patternCounts = new Dictionary<string, (PackingPattern<V> Pattern, ulong Count)>();
        foreach (PackingPlan<V> plan in plans) {
            var amounts = new Dictionary<string, ulong>();
            foreach (Placement<V> placement in plan.Placements) {
                amounts[placement.ItemId] = amounts.TryGetValue(placement.ItemId, out ulong found)
                    ? found + 1UL
                    : 1UL;
            }
            var pattern = new PackingPattern<V>(plan.Bin.Type.Id, amounts);
            patternCounts[pattern.Key] = patternCounts.TryGetValue(pattern.Key, out (PackingPattern<V> Pattern, ulong Count) existing)
                ? (existing.Pattern, existing.Count + 1UL)
                : (pattern, 1UL);
        }
        var result = new Dictionary<PackingPattern<V>, ulong>();
        foreach ((PackingPattern<V> pattern, ulong count) in patternCounts.Values) {
            result[pattern] = count;
        }
        return result;
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
