// 排序、适应度评估与邻域算子 / Ranking, fitness evaluation and neighbourhood operators.
// 对应。
// , and.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Distribution;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 对个体排序 / Rank individuals.
/// </summary>
/// <remarks>
/// **排序必须走 `CompareStrictly`**。模型可以注册非严格比较器，此时 `CompareObjective(a, a)` 返回 `Less`；直接拿去 `Sort` 会破坏比较器
/// 契约，某些输入上直接抛"比较方法违反通用契约"。
/// **Ranking must go through `CompareStrictly`.** A model may register a non-strict comparator, in which case `CompareObjective(a, a)`
/// returns `Less`; feeding that to `Sort` breaks the comparator contract and throws "comparison method violates its general contract"
/// on some inputs.
/// </remarks>
public interface IRanking {
    /// <summary>
    /// 对个体排序 / Rank individuals.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="individuals">待排序个体 / The individuals to rank.</param>
    /// <returns>排序后的个体 / The ranked individuals.</returns>
    IReadOnlyList<T> Rank<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> individuals)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V>;
}

/// <summary>
/// 按原目标排序，优者在前 / Rank by the original objective, best first.
/// </summary>
public sealed class ObjectiveRanking : IRanking {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ObjectiveRanking Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<T> Rank<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> individuals)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(individuals);
        var ranked = new List<T>(individuals);
        ranked.Sort((lhs, rhs) => HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
        return ranked;
    }
}

/// <summary>
/// 按原目标排序后反转，劣者在前 / Rank by the original objective then reverse, worst first.
/// </summary>
public sealed class ReversedObjectiveRanking : IRanking {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ReversedObjectiveRanking Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<T> Rank<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> individuals)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        IReadOnlyList<T> ranked = ObjectiveRanking.Instance.Rank(model, individuals);
        var reversed = new List<T>(ranked);
        reversed.Reverse();
        return reversed;
    }
}

/// <summary>
/// 保持原顺序 / Keep the original order.
/// </summary>
public sealed class IdentityRanking : IRanking {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static IdentityRanking Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<T> Rank<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> individuals)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individuals);
        return new List<T>(individuals);
    }
}

/// <summary>
/// 评估一个解的适应度 / Evaluate the fitness of one solution.
/// </summary>
public interface IFitnessEvaluator {
    /// <summary>
    /// 评估一个解的适应度 / Evaluate the fitness of one solution.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="solution">待评估解 / The solution to evaluate.</param>
    /// <returns>适应度，无法评估时返回 null / The fitness, or null when it cannot be evaluated.</returns>
    TObjValue? Evaluate<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Solution<V> solution)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 直接取模型目标 / Take the model objective directly.
/// </summary>
public sealed class ObjectiveFitnessEvaluator : IFitnessEvaluator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ObjectiveFitnessEvaluator Instance { get; } = new();

    /// <inheritdoc/>
    public TObjValue? Evaluate<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Solution<V> solution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(solution);
        return model.Objective(solution.Values);
    }
}

/// <summary>
/// 目标缺失时回落到模型默认目标 / Fall back to the model's default objective when the objective is missing.
/// </summary>
/// <remarks>
/// **回落值必须显式是"默认目标"而不是 0**。用 0 冒充缺失目标会让不可评估的解在最小化问题里看起来最优，随后被当作精英保留，
/// 整个种群被一个从未真正评估过的解带偏。
/// **The fallback must be the explicit "default objective", never 0.** Substituting 0 for a missing objective makes an unevaluable
/// solution look optimal in a minimization problem; it then gets retained as an elite and drags the whole population along.
/// </remarks>
public sealed class DefaultOnMissFitnessEvaluator : IFitnessEvaluator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static DefaultOnMissFitnessEvaluator Instance { get; } = new();

    /// <inheritdoc/>
    public TObjValue? Evaluate<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Solution<V> solution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(solution);
        TObjValue? objective = model.Objective(solution.Values);
        return objective is null ? model.DefaultObjective : objective;
    }
}

/// <summary>
/// 生成一个邻域解 / Generate one neighbour solution.
/// </summary>
public interface INeighborhood {
    /// <summary>
    /// 生成一个邻域解 / Generate one neighbour solution.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="individual">当前个体 / The current individual.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>邻域解 / The neighbour solution.</returns>
    Solution<V> Generate<TObj, TObjValue, V>(
        Iteration iteration,
        IIndividual<TObjValue, V> individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Func<Flt64> entropy,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 邻域扰动共用辅助 / Shared helpers for neighbourhood perturbation.
/// </summary>
public static class NeighborhoodSupport {
    /// <summary>
    /// 对若干个随机位置施加步长 / Apply a step to a number of randomly chosen positions.
    /// </summary>
    /// <remarks>
    /// **位置抽样有尝试次数上限**。解长度大而 `amount` 接近长度时，"抽到足够多的不同位置"需要大量重抽；不设上限会让一次邻域生成
    /// 退化成近似无限循环，**而迭代计数看起来毫无异常**。上限设为 `dimension * 4`，最坏情况少扰动几个位置。
    /// **Position sampling has an attempt cap.** With a long solution and `amount` close to its length, drawing enough distinct
    /// positions needs many redraws; without a cap one neighbourhood generation degenerates into a near-infinite loop **while the
    /// iteration counter looks entirely normal**. The cap is `dimension * 4`, and the worst case simply perturbs a few fewer positions.
    /// **越界夹紧用 `NumericSupport` 的求解器序下标**。`model.tokens[index]` 是令牌表的自然顺序，与解向量的求解器顺序可能错位，
    /// 之后每次夹紧都用错区间，而表现只是"解的质量不如预期"。
    /// **Bounds come from `NumericSupport`'s solver-order indexing.** The code's `model.tokens[index]` follows the token table's
    /// natural order, which can be misaligned with the solution vector's solver order; every clamp then uses the wrong interval and the
    /// only symptom is "solution quality is worse than expected".
    /// </remarks>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="individual">当前个体 / The current individual.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="amount">扰动位置数量 / The number of positions to perturb.</param>
    /// <param name="step">单位置步长函数 / The per-position step function.</param>
    /// <returns>邻域解 / The neighbour solution.</returns>
    public static Solution<V> Perturb<TObj, TObjValue, V>(
        IIndividual<TObjValue, V> individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Func<Flt64> entropy,
        IIntoValue<V> converter,
        int amount,
        Func<double, double> step) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(individual);
        ArgumentNullException.ThrowIfNull(step);
        var solution = new List<V>(individual.Solution);
        if (solution.Count == 0) {
            return new Solution<V>(solution);
        }

        int dimension = solution.Count;
        int target = global::System.Math.Min(amount, dimension);
        var touched = new HashSet<int>();
        int attempts = 0;
        while (touched.Count < target && attempts < dimension * 4) {
            touched.Add(global::System.Math.Clamp((int)(NumericSupport.Unit(entropy) * dimension), 0, dimension - 1));
            attempts++;
        }

        foreach (int index in touched) {
            double current = converter.FromValue(solution[index]).ToDouble();
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            double width = NumericSupportWidth(lower, upper);
            double sampledStep = step(width);
            if (double.IsNaN(sampledStep)) {
                sampledStep = 0.0;
            }
            solution[index] = NumericSupport.AsValue(
                NumericSupport.ClampTo(current + sampledStep, lower, upper), converter);
        }
        return new Solution<V>(solution);
    }

    /// <summary>
    /// 参考宽度：无有限边界时退化为 1.0 / The reference width, degrading to 1.0 without finite bounds.
    /// </summary>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <returns>宽度 / The width.</returns>
    private static double NumericSupportWidth(double? lower, double? upper) =>
        lower is double low && upper is double high && double.IsFinite(low) && double.IsFinite(high)
            ? high - low
            : 1.0;
}

/// <summary>
/// 均匀随机扰动 / Uniform random perturbation.
/// </summary>
/// <param name="Amount">扰动位置数量 / The number of positions to perturb.</param>
public sealed record RandomPerturbation(int Amount = 1) : INeighborhood {
    /// <inheritdoc/>
    public Solution<V> Generate<TObj, TObjValue, V>(
        Iteration iteration,
        IIndividual<TObjValue, V> individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Func<Flt64> entropy,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        NeighborhoodSupport.Perturb(
            individual, model, entropy, converter, Amount,
            width => (NumericSupport.Unit(entropy) - 0.5) * width);
}

/// <summary>
/// 高斯扰动 / Gaussian perturbation.
/// </summary>
/// <param name="Amount">扰动位置数量 / The number of positions to perturb.</param>
/// <param name="StandardDeviation">标准差 / The standard deviation.</param>
public sealed record GaussianPerturbation(int Amount, Flt64 StandardDeviation) : INeighborhood {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public GaussianPerturbation() : this(1, new Flt64(0.1)) { }

    /// <inheritdoc/>
    public Solution<V> Generate<TObj, TObjValue, V>(
        Iteration iteration,
        IIndividual<TObjValue, V> individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Func<Flt64> entropy,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var gaussian = new Gaussian(Flt64.Zero, StandardDeviation);
        return NeighborhoodSupport.Perturb(
            individual, model, entropy, converter, Amount,
            width => gaussian.Sample(() => entropy()).ToDouble() * width);
    }
}

/// <summary>
/// 莱维扰动，重尾步长有利于跨出局部最优 / Levy perturbation whose heavy-tailed step helps cross out of local optima.
/// </summary>
/// <param name="Amount">扰动位置数量 / The number of positions to perturb.</param>
/// <param name="Alpha">稳定性参数 / The stability parameter.</param>
public sealed record LevyPerturbation(int Amount, Flt64 Alpha) : INeighborhood {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public LevyPerturbation() : this(1, new Flt64(1.5)) { }

    /// <inheritdoc/>
    public Solution<V> Generate<TObj, TObjValue, V>(
        Iteration iteration,
        IIndividual<TObjValue, V> individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Func<Flt64> entropy,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var levy = new Levy(Alpha);
        return NeighborhoodSupport.Perturb(
            individual, model, entropy, converter, Amount,
            width => levy.Sample(() => entropy()).ToDouble() * width);
    }
}
