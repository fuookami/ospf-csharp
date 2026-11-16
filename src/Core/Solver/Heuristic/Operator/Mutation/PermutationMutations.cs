// 排列变异算子 / Permutation mutation operators.
// 与固定 Kotlin 、
// 对应。
// , and.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;

/// <summary>
/// 变异操作接口，定义对个体进行变异生成新解的行为 / The mutation-operation interface, defining how an individual is mutated into a
/// new solution.
/// </summary>
/// <remarks>
/// **熵源与值转换器由调用方注入，不由实现持有**。因此所有实现都是无状态单例，可安全共享于并行求解；实现自己持有随机源则会让
/// "同一份种子重放"取决于线程时序，检查点恢复也就失去意义。
/// **The entropy source and value converter are injected by the caller, not held by the implementation.** Every implementation is
/// therefore a stateless singleton that can be shared safely across parallel runs. An implementation owning its own random source
/// would make "replay under the same seed" depend on thread timing, and checkpoint restore would lose its meaning.
/// **`Iteration` 参与签名但不被所有算子使用**。自适应算子（例如温度驱动的变异步长）需要它决定强度；把它留在签名里，是为了让
/// 新增自适应算子不必改动接口，而不是鼓励实现随意读取。
/// **`Iteration` is part of the signature even though not every operator uses it.** Adaptive operators (say, temperature-driven step
/// sizes) need it to pick their strength; it stays in the signature so that adding one does not change the interface — not as an
/// invitation for implementations to read it casually.
/// **注意命名冲突**：`Core.Solver.Heuristic.IMutation&lt;V&gt;` 是既有的简化设计（固定概率、无注入），与本接口语义不同，两者
/// 互不兼容。算法从简化接口迁移到本接口需要单独批次，届时旧接口才会退役。
/// **Note the name collision**: `Core.Solver.Heuristic.IMutation&lt;V&gt;` is the existing simplified design (fixed probability, no
/// injection) and is not compatible with this interface. Migrating the algorithms from the simplified one to this one needs its own
/// batch; the old interface retires then.
/// </remarks>
public interface IMutationOperator {
    /// <summary>
    /// 对个体执行变异操作生成新解 / Mutate an individual into a new solution.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="TObj">目标类型 / The objective type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="individual">待变异的个体 / The individual to mutate.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <param name="mutationRate">逐基因变异率 / The per-gene mutation rate.</param>
    /// <returns>变异后的新解 / The new solution after mutation.</returns>
    Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V>;
}

/// <summary>
/// 排列变异共用辅助 / Shared helpers for permutation mutations.
/// </summary>
internal static class PermutationSupport {
    /// <summary>
    /// 在解上取两个不同的随机下标 / Draw two distinct random indices on a solution.
    /// </summary>
    /// <remarks>
    /// **第二个下标用确定的续位而不是重抽**。重抽在 `size == 2` 时可能连续命中同一个下标，形成潜在的长循环；`(first + 1) % size`
    /// 保证两者必然不同且只消耗一次抽样。
    /// **The second index is the deterministic successor rather than a redraw.** Redrawing can keep hitting the same index forever when
    /// `size == 2`; `(first + 1) % size` guarantees distinctness with a single draw.
    /// </remarks>
    /// <param name="size">解长度 / The solution length.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <returns>两个下标 / Two indices.</returns>
    public static (int First, int Second) TwoIndices(int size, Func<Flt64> entropy) {
        int first = (int)(NumericSupport.Unit(entropy) * size);
        first = global::System.Math.Clamp(first, 0, size - 1);
        int second = (int)(NumericSupport.Unit(entropy) * size);
        second = global::System.Math.Clamp(second, 0, size - 1);
        if (second == first) {
            second = (first + 1) % size;
        }
        return (first, second);
    }

    /// <summary>
    /// 用注入熵源做 Fisher-Yates 洗牌 / Fisher-Yates shuffle driven by the injected entropy source.
    /// </summary>
    /// <remarks>
    /// **不用 `Random.Shared` 或语言内置洗牌**。基线此处调用 Kotlin 的 `shuffled`，那走的是全局随机源，与注入熵源无关——
    /// 结果是"同一份种子重放"在散射变异上失效，而且**只有用到散射变异时才暴露**。C# 版改用注入熵源，属于对有意修正。
    /// **`Random.Shared` and language-level shuffles are not used.** The code calls Kotlin's `shuffled`, which runs on the
    /// global random source unrelated to the injected entropy — so "replay under the same seed" silently fails for scramble
    /// mutation, and **only surfaces when that operator is in use**. This implementation uses the injected source: a deliberate correction.
    /// </remarks>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="items">待洗牌的片段 / The segment to shuffle.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    public static void Shuffle<T>(IList<T> items, Func<Flt64> entropy) {
        for (int index = items.Count - 1; index > 0; index--) {
            int swap = (int)(NumericSupport.Unit(entropy) * (index + 1));
            swap = global::System.Math.Clamp(swap, 0, index);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }
}

/// <summary>
/// 空变异，原样返回解 / A no-op mutation returning the solution unchanged.
/// </summary>
/// <remarks>
/// 用于本身不含变异步骤的算法（GWO、SCA、MVO 等），使它们可以复用种群骨架而不被强加一次扰动。
/// Used by algorithms with no mutation step of their own (GWO, SCA, MVO, …) so they can reuse the population skeleton without being
/// forced through a perturbation.
/// </remarks>
public sealed class NoMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static NoMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        return new Solution<V>(new List<V>(individual.Solution));
    }
}

/// <summary>
/// 交换变异，交换两个基因 / Swap mutation exchanging two genes.
/// </summary>
public sealed class SwapMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static SwapMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        IReadOnlyList<V> solution = individual.Solution;
        if (solution.Count < 2 || !NumericSupport.ShouldFire(entropy, mutationRate.ToDouble())) {
            return new Solution<V>(new List<V>(solution));
        }

        var result = new List<V>(solution);
        (int first, int second) = PermutationSupport.TwoIndices(solution.Count, entropy);
        (result[first], result[second]) = (result[second], result[first]);
        return new Solution<V>(result);
    }
}

/// <summary>
/// 逆序变异，反转两个切点之间的片段 / Inversion mutation reversing the segment between two cuts.
/// </summary>
public sealed class InversionMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static InversionMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        IReadOnlyList<V> solution = individual.Solution;
        if (solution.Count < 2 || !NumericSupport.ShouldFire(entropy, mutationRate.ToDouble())) {
            return new Solution<V>(new List<V>(solution));
        }

        (int first, int second) = PermutationSupport.TwoIndices(solution.Count, entropy);
        int start = global::System.Math.Min(first, second);
        int end = global::System.Math.Max(first, second);
        var result = new List<V>(solution);
        // 只反转 [start, end) 区间：`end` 是开区间端，与`subList(start, end).reverse` 一致。
        // Reverse only [start, end): `end` is exclusive, matching its `subList(start, end).reverse`.
        result.Reverse(start, end - start);
        return new Solution<V>(result);
    }
}

/// <summary>
/// 插入变异，取出一个基因并插入到另一个位置 / Insertion mutation removing one gene and reinserting it elsewhere.
/// </summary>
public sealed class InsertionMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static InsertionMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        IReadOnlyList<V> solution = individual.Solution;
        if (solution.Count < 2 || !NumericSupport.ShouldFire(entropy, mutationRate.ToDouble())) {
            return new Solution<V>(new List<V>(solution));
        }

        (int from, int to) = PermutationSupport.TwoIndices(solution.Count, entropy);
        var result = new List<V>(solution);
        V value = result[from];
        result.RemoveAt(from);
        // 移除后长度减一，目标下标必须重新夹紧：直接用移除前的 `to` 在 `to == Count-1` 时会越界。
        // The length drops by one, so the target index must be re-clamped: using the pre-removal `to` overflows when `to == Count-1`.
        result.Insert(global::System.Math.Clamp(to, 0, result.Count), value);
        return new Solution<V>(result);
    }
}

/// <summary>
/// 散射变异，打乱一段随机片段内的基因顺序 / Scramble mutation shuffling the genes inside a random segment.
/// </summary>
/// <param name="SegmentRatio">片段长度占比 / The segment length as a ratio of the solution length.</param>
public sealed record ScrambleMutation(Flt64 SegmentRatio) : IMutationOperator {
    /// <summary>
    /// 以默认片段占比构造 / Construct with the default segment ratio.
    /// </summary>
    public ScrambleMutation() : this(new Flt64(0.2)) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        IReadOnlyList<V> solution = individual.Solution;
        if (solution.Count < 2 || !NumericSupport.ShouldFire(entropy, mutationRate.ToDouble())) {
            return new Solution<V>(new List<V>(solution));
        }

        (int first, int second) = PermutationSupport.TwoIndices(solution.Count, entropy);
        int start = global::System.Math.Min(first, second);
        int end = global::System.Math.Max(first, second);
        int minimumEnd = global::System.Math.Min(
            start + global::System.Math.Max((int)(solution.Count * SegmentRatio.ToDouble()), 2),
            solution.Count);
        end = global::System.Math.Max(end, minimumEnd);

        var result = new List<V>(solution);
        var segment = new List<V>();
        for (int index = start; index < end; index++) {
            segment.Add(result[index]);
        }
        PermutationSupport.Shuffle(segment, entropy);
        for (int index = 0; index < segment.Count; index++) {
            result[start + index] = segment[index];
        }
        return new Solution<V>(result);
    }
}
