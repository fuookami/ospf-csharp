// 算子共用样板 / Shared boilerplate for operators.
// And.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 目标比较规范化 / Objective comparison normalisation.
/// </summary>
/// <remarks>
/// **模型可以注册任意比较器**，包括非严格的 `&lt;=` / `&gt;=`。这种情况下 `CompareObjective(a, a)` 会返回 `Order.Less`——
/// 直接拿去排序会破坏比较器契约，拿去判断"是否改进"则会把**持平误判成改进**，让搜索在平台上无限前进。
/// **A model may register an arbitrary comparator**, including the non-strict `&lt;=` / `&gt;=`. In that case
/// `CompareObjective(a, a)` returns `Order.Less` — using it directly for sorting breaks the comparator contract, and using it for
/// "has improved" detection **misreads a tie as an improvement**, letting the search march forever on a plateau.
/// **双向比较是唯一的通用解法**。只有"前向判左更优且反向不判右更优"才算真正更优；单向比较无法区分"严格更优"与"非严格比较器
/// 在持平处也返回 `Less`"。
/// **Bidirectional comparison is the only general fix.** Something is genuinely better only when the forward call prefers the left
/// *and* the backward call does not prefer the right; a one-way comparison cannot tell "strictly better" from "a non-strict comparator
/// returning `Less` on a tie".
/// </remarks>
public static class HeuristicOrdering {
    /// <summary>
    /// 把模型的目标比较规范化为严格弱序 / Normalize the model objective comparison into a strict weak ordering.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="lhs">左侧目标值 / The left-hand side objective value.</param>
    /// <param name="rhs">右侧目标值 / The right-hand side objective value.</param>
    /// <returns>负数表示左侧更优，正数表示右侧更优，0 表示持平 / Negative when the left side is better, positive when the right
    /// side is better, 0 when tied.</returns>
    public static int CompareStrictly<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        TObjValue lhs,
        TObjValue rhs) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        Order? forward = model.CompareObjective(lhs, rhs);
        Order? backward = model.CompareObjective(rhs, lhs);
        return forward switch {
            Order.Less when backward is Order.Less => 0,
            Order.Less => -1,
            _ => backward is Order.Less ? 1 : 0,
        };
    }
}

/// <summary>
/// 运算级数值辅助 / Numeric support for operators.
/// </summary>
/// <remarks>
/// 这些是各算子与骨架共用的样板：夹紧并拦截非有限值、按概率触发、从父代列表取两个父代。它们不属于任何单一算子族，
/// `update/`、`mutation/`、`runner/` 各用一部分。
/// These are the shared boilerplate of every operator and skeleton: clamping while rejecting non-finite values, firing by
/// probability, and taking two parents. They belong to no single family; `update/`, `mutation/` and `runner/` each use a part.
/// </remarks>
public static class NumericSupport {
    /// <summary>
    /// 把值规约到变量上下界之间 / Clamp a value between the variable bounds.
    /// </summary>
    /// <remarks>
    /// **非有限值（`NaN`、`±∞`）一律替换为可用边界或 0**。这类值一旦进入种群，目标值会溢出为 `∞`，而 `∞` 之间的比较不满足全序，
    /// 会让环境选择的排序直接抛"比较方法违反通用契约"。这个失败点的**表现与成因相距极远**——症状出现在排序，原因在几代之前的变异。
    /// **Non-finite values (`NaN`, `±∞`) are replaced by an available bound or 0.** Once such a value enters the population the
    /// objective overflows to `∞`, and comparisons between `∞` values fail to form a total order, making the survivor sort throw
    /// "comparison method violates its general contract". That failure point is **very far from its cause** — the symptom appears in
    /// sorting, the cause is a mutation several generations earlier.
    /// </remarks>
    /// <param name="value">待规约值 / The value to clamp.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <returns>规约后的值 / The clamped value.</returns>
    public static double ClampTo(double value, double? lower, double? upper) {
        if (!double.IsFinite(value)) {
            return lower ?? upper ?? 0.0;
        }
        double result = value;
        if (lower is double low && result < low) {
            result = low;
        }
        if (upper is double high && result > high) {
            result = high;
        }
        return result;
    }

    /// <summary>
    /// 按概率判断是否触发 / Decide whether an event fires at the given probability.
    /// </summary>
    /// <remarks>
    /// **概率 &gt;= 1 时无条件触发，且不消耗随机数**。若仍然抽样，那么"某个算子被配置为必触发"这一事实会悄悄改变整条随机流，
    /// 让同一份种子在配置微调后产生完全不同、且无法对比的轨迹。
    /// **A probability &gt;= 1 fires unconditionally and consumes no random draw.** Drawing anyway would let "this operator is configured
    /// to always fire" silently shift the whole random stream, so the same seed yields a completely different — and incomparable —
    /// trajectory after a minor configuration change.
    /// </remarks>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <param name="probability">触发概率 / The trigger probability.</param>
    /// <returns>是否触发 / Whether it fires.</returns>
    public static bool ShouldFire(Func<Flt64> entropy, double probability) {
        ArgumentNullException.ThrowIfNull(entropy);
        return probability >= 1.0 || Unit(entropy) < probability;
    }

    /// <summary>
    /// 采样 [0, 1) 上的均匀随机数 / Sample a uniform random number in [0, 1).
    /// </summary>
    /// <remarks>
    /// **熵源返回 null 时回落到 0.5**。回落值选 0.5 而不是 0 或 1，是为了让"熵源失效"表现为两个方向都可能发生，而不是把偏好
    /// 悄悄偏向某一侧。
    /// **A null entropy reading falls back to 0.5.** The fallback is 0.5 rather than 0 or 1 so that a dead entropy source shows up as
    /// "both directions still possible" instead of silently biasing one way.
    /// </remarks>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <returns>均匀随机数 / The uniform random number.</returns>
    public static double Unit(Func<Flt64?> entropy) {
        ArgumentNullException.ThrowIfNull(entropy);
        Flt64? drawn = entropy();
        double value = drawn is Flt64 flt ? flt.ToDouble() : 0.5;
        return global::System.Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// 采样 [0, 1) 上的均匀随机数 / Sample a uniform random number in [0, 1).
    /// </summary>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <returns>均匀随机数 / The uniform random number.</returns>
    public static double Unit(Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(entropy);
        return global::System.Math.Clamp(entropy().ToDouble(), 0.0, 1.0);
    }

    /// <summary>
    /// 从父代列表中取两个父代，只有一个时复用该父代 / Take two parents, reusing the only one when there is just one.
    /// </summary>
    /// <typeparam name="T">父代类型 / The parent type.</typeparam>
    /// <param name="parents">父代列表 / The parent list.</param>
    /// <returns>父代对，列表为空时返回 null / The parent pair, or null when the list is empty.</returns>
    public static (T First, T Second)? PickTwo<T>(IReadOnlyList<T> parents) {
        ArgumentNullException.ThrowIfNull(parents);
        return parents.Count switch {
            >= 2 => (parents[0], parents[1]),
            1 => (parents[0], parents[0]),
            _ => null,
        };
    }

    /// <summary>
    /// 读取变量的下界 / Read the lower bound of a variable.
    /// </summary>
    /// <remarks>
    /// **按下标读 `TokensInSolver` 而不是全部令牌**。模型返回的解向量按求解器顺序排列；用令牌表的自然顺序会读到与解位置错位的边界，
    /// 之后每次夹紧都用错区间，而表现只是"解的质量不如预期"。
    /// **Indexed into `TokensInSolver`, not all tokens.** The model returns solution vectors in solver order; using the token table's
    /// natural order reads bounds misaligned with the solution positions, so every clamp uses the wrong interval and the only symptom
    /// is "solution quality is worse than expected".
    /// </remarks>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="index">变量下标 / The variable index.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>下界，不可用时返回 null / The lower bound, or null when unavailable.</returns>
    public static double? LowerBoundOf<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        int index,
        IIntoValue<V> converter) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);
        IReadOnlyList<Fuookami.Ospf.Core.Token.Token<V>> tokens = model.Tokens.TokensInSolver;
        if (index < 0 || index >= tokens.Count) {
            return null;
        }
        V? bound = tokens[index].LowerBoundV(converter);
        return bound is V value ? value.ToFlt64().ToDouble() : null;
    }

    /// <summary>
    /// 读取变量的上界 / Read the upper bound of a variable.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="index">变量下标 / The variable index.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>上界，不可用时返回 null / The upper bound, or null when unavailable.</returns>
    public static double? UpperBoundOf<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        int index,
        IIntoValue<V> converter) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);
        IReadOnlyList<Fuookami.Ospf.Core.Token.Token<V>> tokens = model.Tokens.TokensInSolver;
        if (index < 0 || index >= tokens.Count) {
            return null;
        }
        V? bound = tokens[index].UpperBoundV(converter);
        return bound is V value ? value.ToFlt64().ToDouble() : null;
    }

    /// <summary>
    /// 把双精度转回解值类型 / Convert a double back into the solution value type.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="value">待转换值 / The value to convert.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>解值 / The solution value.</returns>
    public static V AsValue<V>(double value, IIntoValue<V> converter) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(converter);
        return converter.IntoValue(new Flt64(value));
    }
}
