// 交叉算子 / Crossover operators.
// 与固定 Kotlin （8686 B）对应。

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
/// 交叉算子共用辅助 / Shared helpers for crossover operators.
/// </summary>
/// <remarks>
/// **交叉只产两个子代**，与"每个父代产一个子代"的更新算子不同。调用方若按父代数量推算子代数量，会在父代数为奇数时拿到比预期少一个的结果，
/// 而种群规模看起来仍然正常。
/// **A crossover yields exactly two children**, unlike update operators that yield one child per parent. A caller that derives the child
/// count from the parent count gets one fewer than expected for an odd parent count, while the population size still looks right.
/// **维数不一致时不交叉、原样返回**。强行按较短的一侧交叉会静默截断解向量，之后每次评估都在一个缺维的解上进行。
/// **Mismatched dimensions are returned unchanged.** Crossing over the shorter side silently truncates the solution vector, and every
/// subsequent evaluation runs on a dimension-deficient solution.
/// **所有切点/混合都使用注入的熵源**，不用全局随机数，否则同一份种子的重放会产出一致性完全不同的结果。
/// **Every cut point and mix uses the injected entropy**, never a global RNG; otherwise replay under the same seed produces
/// inconsistent results.
/// </remarks>
public static class CrossSupport {
    /// <summary>
    /// 取两个不同的父代 / Pick two distinct parents.
    /// </summary>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="parents">父代组 / The parent group.</param>
    /// <returns>两个父代，不足时返回 null / The two parents, or null when fewer than two exist.</returns>
    public static (T First, T Second)? PickTwo<T>(IReadOnlyList<T> parents) {
        ArgumentNullException.ThrowIfNull(parents);
        if (parents.Count < 2) {
            return null;
        }
        return (parents[0], parents[1]);
    }
}

/// <summary>
/// 单点交叉 / Single-point crossover.
/// </summary>
public sealed class SinglePointCross : ICross {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static SinglePointCross Instance { get; } = new();

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
        var a = new List<V>(first.Solution);
        var b = new List<V>(second.Solution);
        if (a.Count != b.Count || a.Count < 2) {
            return new[] { new Solution<V>(a), new Solution<V>(b) };
        }

        int cut = 1 + global::System.Math.Clamp(
            (int)(NumericSupport.Unit(entropy) * (a.Count - 1)), 0, a.Count - 2);
        for (int index = cut; index < a.Count; index++) {
            (b[index], a[index]) = (a[index], b[index]);
        }
        return new[] { new Solution<V>(a), new Solution<V>(b) };
    }
}

/// <summary>
/// 两点交叉 / Two-point crossover.
/// </summary>
/// <remarks>
/// **两个切点相同的情况必须扩成一段长度 1 的区间**。否则 `start == end` 时循环体一次都不执行，交叉退化成"原样返回"，
/// 而调用方从返回值上看不出任何异常——**子代就是父代本身，种群停止演化**。
/// **Coincident cut points must expand into an interval of length one.** Otherwise `start == end` makes the loop body run zero times,
/// the crossover degrades into "return the inputs unchanged", and the caller cannot tell from the return value — **the children are
/// literally the parents and the population stops evolving**.
/// </remarks>
public sealed class TwoPointCross : ICross {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static TwoPointCross Instance { get; } = new();

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
        var a = new List<V>(first.Solution);
        var b = new List<V>(second.Solution);
        if (a.Count != b.Count || a.Count < 2) {
            return new[] { new Solution<V>(a), new Solution<V>(b) };
        }

        int leftRaw = global::System.Math.Clamp(
            (int)(NumericSupport.Unit(entropy) * a.Count), 0, a.Count - 1);
        int rightRaw = global::System.Math.Clamp(
            (int)(NumericSupport.Unit(entropy) * a.Count), 0, a.Count - 1);
        int start = global::System.Math.Min(leftRaw, rightRaw);
        int end = start == global::System.Math.Max(leftRaw, rightRaw)
            ? global::System.Math.Min(start + 1, a.Count - 1)
            : global::System.Math.Max(leftRaw, rightRaw);
        for (int index = start; index < end; index++) {
            (b[index], a[index]) = (a[index], b[index]);
        }
        return new[] { new Solution<V>(a), new Solution<V>(b) };
    }
}

/// <summary>
/// 多点交叉 / Multi-point crossover.
/// </summary>
/// <remarks>
/// **切点抽样有尝试上限** `amount * 8`。切点必须互不相同，而 `cutAmount` 接近 `a.Count - 1` 时要抽满需要大量重抽；无上限会退化成近似无限循环，
/// **而迭代计数看起来毫无异常**。上限触发时最坏情况是少切几刀。
/// **Cut sampling has an attempt cap** of `amount * 8`. Cut points must be distinct, and with `cutAmount` close to `a.Count - 1`
/// drawing a full set takes many redraws; without a cap the loop degenerates into a near-infinite one **while the iteration counter
/// looks entirely normal**. The worst case is a few fewer cuts.
/// </remarks>
/// <param name="CutAmount">切点数量 / The cut-point amount.</param>
public sealed record MultiPointCross(int CutAmount) : ICross {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public MultiPointCross() : this(3) { }

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
        var a = new List<V>(first.Solution);
        var b = new List<V>(second.Solution);
        if (a.Count != b.Count || a.Count < 2) {
            return new[] { new Solution<V>(a), new Solution<V>(b) };
        }

        int amount = global::System.Math.Clamp(CutAmount, 1, a.Count - 1);
        var cuts = new SortedSet<int>();
        int attempts = 0;
        while (cuts.Count < amount && attempts < amount * 8) {
            cuts.Add(global::System.Math.Clamp((int)(NumericSupport.Unit(entropy) * a.Count), 1, a.Count - 1));
            attempts++;
        }

        bool swapping = false;
        int cursor = 0;
        foreach (int cut in cuts) {
            if (swapping) {
                for (int index = cursor; index < cut; index++) {
                    (b[index], a[index]) = (a[index], b[index]);
                }
            }
            swapping = !swapping;
            cursor = cut;
        }
        if (swapping) {
            for (int index = cursor; index < a.Count; index++) {
                (b[index], a[index]) = (a[index], b[index]);
            }
        }
        return new[] { new Solution<V>(a), new Solution<V>(b) };
    }
}

/// <summary>
/// 均匀交叉 / Uniform crossover.
/// </summary>
/// <param name="MixingRatio">每个位置的交换概率 / The per-position exchange probability.</param>
public sealed record UniformCross(Flt64 MixingRatio) : ICross {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public UniformCross() : this(new Flt64(0.5)) { }

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
        var a = new List<V>(first.Solution);
        var b = new List<V>(second.Solution);
        if (a.Count != b.Count) {
            return new[] { new Solution<V>(a), new Solution<V>(b) };
        }

        double ratio = MixingRatio.ToDouble();
        for (int index = 0; index < a.Count; index++) {
            if (NumericSupport.Unit(entropy) < ratio) {
                (b[index], a[index]) = (a[index], b[index]);
            }
        }
        return new[] { new Solution<V>(a), new Solution<V>(b) };
    }
}

/// <summary>
/// 算术交叉，按一个随机权重线性组合两个父代 / Arithmetic crossover blending both parents with a random weight.
/// </summary>
public sealed class ArithmeticCross : ICross {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ArithmeticCross Instance { get; } = new();

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

        // **一个权重用于整条子代**，不是逐分量各取一次。逐分量重取会让两个子代在每一维上来自不同的插值系数，"遗传物质的继承比例"
        // 失去意义，跨代的可解释性也没有了。
        // **One weight for the whole child**, not one per component. Re-drawing per component gives each dimension a different
        // interpolation coefficient, so the notion of "inherited ratio" loses meaning and cross-generation interpretability is gone.
        double alpha = NumericSupport.Unit(entropy);
        var firstChild = new List<V>(left.Count);
        var secondChild = new List<V>(left.Count);
        for (int index = 0; index < left.Count; index++) {
            double a = converter.FromValue(left[index]).ToDouble();
            double b = converter.FromValue(right[index]).ToDouble();
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            firstChild.Add(NumericSupport.AsValue(
                NumericSupport.ClampTo((alpha * a) + ((1.0 - alpha) * b), lower, upper), converter));
            secondChild.Add(NumericSupport.AsValue(
                NumericSupport.ClampTo((alpha * b) + ((1.0 - alpha) * a), lower, upper), converter));
        }
        return new[] { new Solution<V>(firstChild), new Solution<V>(secondChild) };
    }
}

/// <summary>
/// 混合交叉，允许子代越过父代区间 / Blend crossover, allowing children to move beyond the parents' interval.
/// </summary>
/// <param name="Expansion">区间扩展比例 / The interval expansion ratio.</param>
public sealed record BlendCross(Flt64 Expansion) : ICross {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public BlendCross() : this(new Flt64(0.5)) { }

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

        double alpha = Expansion.ToDouble();
        var firstChild = new List<V>(left.Count);
        var secondChild = new List<V>(left.Count);
        for (int index = 0; index < left.Count; index++) {
            double a = converter.FromValue(left[index]).ToDouble();
            double b = converter.FromValue(right[index]).ToDouble();
            double spread = global::System.Math.Abs(a - b) * alpha;
            double low = global::System.Math.Min(a, b) - spread;
            double high = global::System.Math.Max(a, b) + spread;
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            firstChild.Add(NumericSupport.AsValue(
                NumericSupport.ClampTo(low + (NumericSupport.Unit(entropy) * (high - low)), lower, upper), converter));
            secondChild.Add(NumericSupport.AsValue(
                NumericSupport.ClampTo(low + (NumericSupport.Unit(entropy) * (high - low)), lower, upper), converter));
        }
        return new[] { new Solution<V>(firstChild), new Solution<V>(secondChild) };
    }
}
