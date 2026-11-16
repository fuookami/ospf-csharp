// 进阶交叉算子 / Advanced crossover operators.
// 与固定 Kotlin （9530 B）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 模拟二进制交叉 / Simulated binary crossover.
/// </summary>
/// <remarks>
/// **两个父代在该维上相等时必须跳过交叉**。`|a - b| &lt; 1e-12` 时若仍走公式，`beta` 里的 `1 / (2 * (1 - r))` 与
/// `0.5 * ((1 + beta) * a + (1 - beta) * b)` 会给出一个与父代相同的值，看起来无害；但**它消耗了一个随机数**，
/// 于是同一份种子的后续轨迹与"真的跳过"完全不同——重放对比会莫名失败。
/// **Two parents equal in a dimension must skip the crossover.** Running the formula anyway yields the same value, which looks
/// harmless; but **it consumes a random draw**, so the rest of the trajectory under the same seed differs from "really skipping", and a
/// replay comparison fails for no visible reason.
/// **分布指数必须夹到非负**。负指数会让 `beta` 的幂运算进入复数域，进而产出 `NaN`，而 `ClampTo` 会把它折算成边界——
/// **整代解被钉在边界上，看起来只是"收敛得快"**。
/// **The distribution index must be clamped to non-negative.** A negative exponent pushes the `beta` power into the complex domain and
/// yields `NaN`, which `ClampTo` folds onto a bound — **the whole generation is pinned to the boundary and it merely looks like "fast
/// convergence"**.
/// </remarks>
/// <param name="DistributionIndex">分布指数 / The distribution index.</param>
public sealed record SimulatedBinaryCross(Flt64 DistributionIndex) : ICross {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SimulatedBinaryCross() : this(new Flt64(20.0)) { }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        if (CrossSupport.PickTwo(parents) is not (T first, T second)) {
            return Array.Empty<Solution<V>>();
        }
        IReadOnlyList<V> left = first.Solution;
        IReadOnlyList<V> right = second.Solution;
        if (left.Count != right.Count) {
            return new[] { new Solution<V>(left), new Solution<V>(right) };
        }

        double eta = global::System.Math.Max(DistributionIndex.ToDouble(), 0.0);
        double exponent = 1.0 / (eta + 1.0);
        var firstChild = new List<V>(left.Count);
        var secondChild = new List<V>(left.Count);
        for (int index = 0; index < left.Count; index++) {
            double a = converter.FromValue(left[index]).ToDouble();
            double b = converter.FromValue(right[index]).ToDouble();
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            if (global::System.Math.Abs(a - b) < 1.0e-12) {
                firstChild.Add(NumericSupport.AsValue(NumericSupport.ClampTo(a, lower, upper), converter));
                secondChild.Add(NumericSupport.AsValue(NumericSupport.ClampTo(b, lower, upper), converter));
                continue;
            }

            double random = NumericSupport.Unit(entropy);
            double beta = random <= 0.5
                ? global::System.Math.Pow(2.0 * random, exponent)
                : global::System.Math.Pow(1.0 / (2.0 * (1.0 - random)), exponent);
            double childA = 0.5 * (((1.0 + beta) * a) + ((1.0 - beta) * b));
            double childB = 0.5 * (((1.0 - beta) * a) + ((1.0 + beta) * b));
            firstChild.Add(NumericSupport.AsValue(NumericSupport.ClampTo(childA, lower, upper), converter));
            secondChild.Add(NumericSupport.AsValue(NumericSupport.ClampTo(childB, lower, upper), converter));
        }
        return new[] { new Solution<V>(firstChild), new Solution<V>(secondChild) };
    }
}

/// <summary>
/// 差分交叉 / Differential crossover.
/// </summary>
/// <remarks>
/// **只产一个子代**，且 `parents[0]` 是目标向量、`parents[1]` 是基向量、`parents[2 % n]` 与 `parents[3 % n]` 是差分对。
/// 父代不足两个时**原样返回全部父代**（不是空列表）；父代只有两个或三个时取模复用同一个个体，差分项退化为 0，
/// 变异量随之消失——这是基线行为，不是缺陷，但意味着**少样本时差分交叉实际等于无操作**。
/// **Exactly one child**, with `parents[0]` the target vector, `parents[1]` the base, and `parents[2 % n]` / `parents[3 % n]` the
/// difference pair. With fewer than two parents **all parents are returned unchanged** (not an empty list); with two or three parents
/// the modulo reuses the same individual and the difference term collapses to 0, so the mutation vanishes — baseline behaviour, not a
/// defect, but it means **differential crossover is effectively a no-op on small samples**.
/// </remarks>
/// <param name="DifferentialWeight">差分权重 / The differential weight.</param>
/// <param name="CrossRate">交叉概率 / The crossover rate.</param>
public sealed record DifferentialCross(Flt64 DifferentialWeight, Flt64 CrossRate) : ICross {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public DifferentialCross() : this(new Flt64(0.5), new Flt64(0.9)) { }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        if (parents.Count < 2) {
            return parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
        }

        IReadOnlyList<V> target = parents[0].Solution;
        IReadOnlyList<V> baseVector = parents[1].Solution;
        IReadOnlyList<V> firstDifference = parents[2 % parents.Count].Solution;
        IReadOnlyList<V> secondDifference = parents[3 % parents.Count].Solution;
        if (baseVector.Count != firstDifference.Count
            || baseVector.Count != secondDifference.Count
            || baseVector.Count != target.Count) {
            return new[] { new Solution<V>(target) };
        }

        double weight = DifferentialWeight.ToDouble();
        double rate = CrossRate.ToDouble();
        var child = new List<V>(target.Count);
        for (int index = 0; index < target.Count; index++) {
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            double mutant = converter.FromValue(baseVector[index]).ToDouble()
                + (weight * (converter.FromValue(firstDifference[index]).ToDouble()
                    - converter.FromValue(secondDifference[index]).ToDouble()));
            double value = NumericSupport.Unit(entropy) < rate
                ? mutant
                : converter.FromValue(target[index]).ToDouble();
            child.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
        }
        return new[] { new Solution<V>(child) };
    }
}

/// <summary>
/// 顺序交叉 / Order crossover.
/// </summary>
/// <remarks>
/// **子代是排列，必须去重**。片段直接从 `segmentSource` 拷贝，剩余位置再从 `orderSource` 的轮转顺序里**跳过已用值**填充；
/// 不去重就会出现同一个值在两个位置，排列性被破坏，而"子代看起来是个合法解"这件事在解码阶段才炸。
/// **The child must remain a permutation, so duplicates must be removed.** The segment is copied from `segmentSource` and the remaining
/// positions are filled from the rotated `orderSource` **skipping already-used values**; without deduplication one value appears twice,
/// the permutation property breaks, and the fault only surfaces at decode time.
/// **两个切点相同时必须扩成长度 1 的区间**。`start == end` 时片段为空，两个子代都退化成 `orderSource` 的原样轮转，
/// 交叉变成无操作。
/// **Coincident cut points must expand into an interval of length one.** With `start == end` the segment is empty, both children degrade
/// into a plain rotation of `orderSource`, and the crossover becomes a no-op.
/// </remarks>
public sealed class OrderCross : ICross {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static OrderCross Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        if (CrossSupport.PickTwo(parents) is not (T first, T second)) {
            return Array.Empty<Solution<V>>();
        }
        IReadOnlyList<V> left = first.Solution;
        IReadOnlyList<V> right = second.Solution;
        if (left.Count != right.Count || left.Count == 0) {
            return new[] { new Solution<V>(left), new Solution<V>(right) };
        }

        return new[] {
            new Solution<V>(OrderCrossOnce(left, right, entropy)),
            new Solution<V>(OrderCrossOnce(right, left, entropy)),
        };
    }

    /// <summary>
    /// 从一段来源拷贝片段，其余按另一来源的顺序填充 / Copy a segment from one source and fill the rest in the other source's order.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="segmentSource">片段来源 / The segment source.</param>
    /// <param name="orderSource">顺序来源 / The order source.</param>
    /// <param name="entropy">均匀熵源 / The uniform entropy source.</param>
    /// <returns>子代 / The child.</returns>
    private static List<V> OrderCrossOnce<V>(IReadOnlyList<V> segmentSource, IReadOnlyList<V> orderSource,
        Func<Flt64> entropy) {
        int size = segmentSource.Count;
        int leftCut = global::System.Math.Clamp((int)(NumericSupport.Unit(entropy) * size), 0, size - 1);
        int rightCut = global::System.Math.Clamp((int)(NumericSupport.Unit(entropy) * size), 0, size - 1);
        int start = global::System.Math.Min(leftCut, rightCut);
        int end = start == global::System.Math.Max(leftCut, rightCut)
            ? global::System.Math.Min(start + 1, size)
            : global::System.Math.Max(leftCut, rightCut);

        var child = new List<V>(orderSource);
        var used = new HashSet<V>();
        for (int index = start; index < end; index++) {
            child[index] = segmentSource[index];
            used.Add(segmentSource[index]);
        }

        var freePositions = new List<int>(size);
        for (int index = 0; index < size; index++) {
            if (index < start || index >= end) {
                freePositions.Add(index);
            }
        }
        var orderedValues = new List<V>(size);
        for (int offset = 0; offset < size; offset++) {
            V value = orderSource[(end + offset) % size];
            if (!used.Contains(value)) {
                orderedValues.Add(value);
            }
        }

        int amount = global::System.Math.Min(freePositions.Count, orderedValues.Count);
        for (int index = 0; index < amount; index++) {
            child[freePositions[index]] = orderedValues[index];
        }
        return child;
    }


}
/// <summary>
/// 循环交叉 / Cycle crossover.
/// </summary>
/// <remarks>
/// **真正让循环终止的是 `assigned` 数组，不是 `guard`**。`index = firstIndices[cycleSource[index]]` 沿着值→位置映射跳转，
/// 一旦映射成环，跳转本身会永远继续下去；但每个位置在被访问时立即置 `assigned[index] = true`，于是下一次回到该位置时
/// `!assigned[index]` 为假、循环退出。循环次数因此天然被解的长度限制住。
/// **`guard &lt;= size` 是第二道保险，在本实现里是冗余的**：变异测试把上界放宽到 `size * 4` 后测试**仍然通过**，
/// 因为 `assigned` 已经先一步终止了循环。保留它只为；把它说成"防止死循环的关键"会是不准确的。
/// **What actually terminates the loop is the `assigned` array, not `guard`.** `index = firstIndices[cycleSource[index]]` hops along
/// the value-to-position map and would continue forever once that map forms a cycle; but each position sets `assigned[index] = true`
/// when visited, so the next time the walk returns there `!assigned[index]` is false and the loop exits. The iteration count is thus
/// naturally bounded by the solution length.
/// **`guard &lt;= size` is its second line of defence and is redundant here**: loosening the bound to `size * 4` still passes
/// the tests because `assigned` terminates the loop first. It is kept only for baseline fidelity; calling it "the thing that prevents
/// the infinite loop" would be inaccurate.
/// **`firstIndices` 记录的是每个值在另一来源中的首次出现位置**（`putIfAbsent`）。用最后一次出现会让环的走向完全不同，
/// 而子代仍是一个合法排列——错误只在收敛速度上体现。
/// **`firstIndices` records each value's first occurrence in the other source** (`putIfAbsent`). Using the last occurrence changes the
/// cycle's direction entirely while the child remains a valid permutation — the error shows up only in convergence speed.
/// </remarks>
public sealed class CycleCross : ICross {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static CycleCross Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        if (CrossSupport.PickTwo(parents) is not (T first, T second)) {
            return Array.Empty<Solution<V>>();
        }
        IReadOnlyList<V> left = first.Solution;
        IReadOnlyList<V> right = second.Solution;
        if (left.Count != right.Count || left.Count == 0) {
            return new[] { new Solution<V>(left), new Solution<V>(right) };
        }

        return new[] {
            new Solution<V>(CycleCrossOnce(left, right)),
            new Solution<V>(CycleCrossOnce(right, left)),
        };
    }

    /// <summary>
    /// 按值→位置的循环拷贝 / Copy along the value-to-position cycle.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="cycleSource">循环来源 / The cycle source.</param>
    /// <param name="otherSource">另一来源 / The other source.</param>
    /// <returns>子代 / The child.</returns>
    private static List<V> CycleCrossOnce<V>(IReadOnlyList<V> cycleSource, IReadOnlyList<V> otherSource) {
        int size = cycleSource.Count;
        var child = new List<V>(otherSource);
        bool[] assigned = new bool[size];
        // **用线性扫描而不是 `Dictionary<V, int>`**：`V` 只有 `struct` 约束、没有 `notnull`，无法直接做 `Dictionary` 的键
        // （`CS8714`）。排列长度就是解维数，这里的 O(n²) 最坏情况远小于一次目标函数评估，不值得为它加约束。
        // **A linear scan instead of `Dictionary<V, int>`**: `V` carries only a `struct` constraint (no `notnull`), so it cannot be used
        // as a `Dictionary` key directly (`CS8714`). The permutation length equals the solution dimension, so this O(n²) worst case costs
        // far less than one objective evaluation and does not justify extra constraints.

        int index = 0;
        int guard = 0;
        while (index >= 0 && index < size && !assigned[index] && guard <= size) {
            assigned[index] = true;
            child[index] = cycleSource[index];
            if (!TryFindFirstIndex(otherSource, cycleSource[index], out int next)) {
                break;
            }
            index = next;
            guard++;
        }
        return child;
    }

    /// <summary>
    /// 查找值在来源中的首次出现位置 / Find the first index of a value in the source.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="source">来源序列 / The source sequence.</param>
    /// <param name="value">待查找的值 / The value to find.</param>
    /// <param name="index">首次出现的位置 / The first index.</param>
    /// <returns>是否找到 / Whether it was found.</returns>
    private static bool TryFindFirstIndex<V>(IReadOnlyList<V> source, V value, out int index) {
        for (int position = 0; position < source.Count; position++) {
            if (EqualityComparer<V>.Default.Equals(source[position], value)) {
                index = position;
                return true;
            }
        }
        index = -1;
        return false;
    }
}
