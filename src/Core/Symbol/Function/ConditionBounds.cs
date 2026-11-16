#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 一个区间的上下界 / The lower and upper bound of an interval.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **它是 `SelectFunction` / `ElementFunction` / `McCormickEnvelopeFunction` 三者共同的入参类型**，
/// **所以先补它，并在同一批里接上一个真实消费者（`McCormickEnvelopeFunction`）**——
/// **只加一个两字段的 record 而没有使用者，那是骨架，不是能力。**
///
/// **构造时拒绝 `lower > upper`**：**反过来传不是"一个空区间"，而是调用方把两个字段写反了**，
/// **而空区间会让包络的四条不等式互相矛盾、求解器只报告"不可行"，错误信息离原因很远。**
///
/// Following its.
///
/// **It is the shared input type of `SelectFunction`, `ElementFunction` and `McCormickEnvelopeFunction`**,
/// **so it comes first, together with one real consumer in the same batch** —
/// **a two-field record with no user is scaffolding, not capability.**
///
/// **Construction rejects `lower > upper`**: **swapping them is not "an empty interval" but a caller who wrote the two
/// fields the wrong way round**, **and an empty interval makes the envelope's four inequalities contradict each other,
/// so the solver reports only "infeasible" and the message is far from the cause.**
/// </remarks>
public sealed record ConditionBounds<V>
    where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    public ConditionBounds(V lower, V upper) {
        if (lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"the lower bound must not exceed the upper bound (lower={lower}, upper={upper})",
                nameof(lower));
        }

        Lower = lower;
        Upper = upper;
    }

    /// <summary>下界 / The lower bound.</summary>
    public V Lower { get; }

    /// <summary>上界 / The upper bound.</summary>
    public V Upper { get; }

    /// <summary>某个值是否落在闭区间内 / Whether a value lies in the closed interval.</summary>
    public bool Contains(V value) => value.CompareTo(Lower) >= 0 && value.CompareTo(Upper) <= 0;

    /// <summary><paramref name="outer"/> 是否覆盖 <paramref name="inner"/> / Whether `outer` covers `inner`.</summary>
    public static bool Covers(ConditionBounds<V> outer, ConditionBounds<V> inner) =>
        outer.Lower.CompareTo(inner.Lower) <= 0 && outer.Upper.CompareTo(inner.Upper) >= 0;
}
