#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 归一化后的离散条件线性化数据 / Normalized linearization data for a discrete condition.
/// </summary>
/// <remarks>
/// 对齐基线 `DiscreteCondition.kt` 的 `DiscreteConditionLinearization`。
///
/// **五个字段各管一件事，其中两个是"分支是否可能"**：
///
/// - **`Polynomial`**：**归一化后的条件多项式**（**关系已折算进去**）
/// - **`Bounds`**：**条件值的有限范围**
/// - **`FixedValue`**：**条件值被界钉死时给出真值，否则 `null`**
/// - **`TrueBranchPossible` / `FalseBranchPossible`**：**两个分支是否都还活着**
///
/// **`FixedValue` 与两个 `Possible` 是<b>冗余的</b>**：
/// **`FixedValue` 非空时，恰好一个 `Possible` 为真。**
/// **基线仍然两个都存**，因为**读的一方常常只关心其中一个，
/// 而"从 `FixedValue` 推回去"要多一次空判断。**
/// **冗余在这里换来的是调用点的直接性——这不是可以随便删的东西。**
///
/// B1171, following the baseline's `DiscreteConditionLinearization`.
///
/// **Five fields, two of which say whether a branch survives**:
///
/// - **`Polynomial`**: the normalized condition polynomial (the relation folded in)
/// - **`Bounds`**: the condition value's finite range
/// - **`FixedValue`**: the truth value when the bounds pin it down, else `null`
/// - **`TrueBranchPossible` / `FalseBranchPossible`**: whether each branch is still alive
///
/// **`FixedValue` and the two `Possible` flags are <b>redundant</b>**: **when `FixedValue` is set exactly one
/// `Possible` is true.** **The baseline keeps both anyway**, because **callers usually care about just one of them,
/// and deriving it from `FixedValue` costs an extra null check.** **The redundancy buys directness at the call site —
/// not something to delete casually.**
/// </remarks>
/// <param name="Polynomial">归一化后的条件多项式 / The normalized condition polynomial.</param>
/// <param name="Bounds">条件值的有限范围 / The condition value's finite range.</param>
/// <param name="FixedValue">被界钉死的真值 / The truth value pinned by the bounds.</param>
/// <param name="TrueBranchPossible">真分支是否可能 / Whether the true branch is possible.</param>
/// <param name="FalseBranchPossible">假分支是否可能 / Whether the false branch is possible.</param>
public sealed record DiscreteConditionLinearization(
    LinearPolynomial<Flt64> Polynomial,
    ConditionBounds<Flt64> Bounds,
    TruthValue? FixedValue,
    bool TrueBranchPossible,
    bool FalseBranchPossible);

/// <summary>
/// 条件值的界能否直接定出真假 / Whether the bounds already settle the truth value.
/// </summary>
/// <remarks>
/// 对齐基线 `DiscreteCondition.kt` 的 `boundsAvoidDiscreteConditionGap`。
///
/// **它回答的是"还要不要生成指示约束"**：**界已经把答案定死时，指示变量没有意义——
/// 直接给真值，省掉一个二元变量与两条行。**
///
/// **四条关系各自的判据<b>不对称</b>，这是本函数唯一的难点**：
///
/// | 关系 | 定为真 | 定为假 |
/// | --- | --- | --- |
/// | `GE` | `lower ≥ 0` | `upper ≤ −δ` |
/// | `LE` | `upper ≤ 0` | `lower ≥ δ` |
/// | `GT` | `lower ≥ δ` | `upper ≤ 0` |
/// | `LT` | `upper ≤ −δ` | `lower ≥ 0` |
///
/// **`GE` 与 `GT` 的差别只在"是否允许等于"**：**`GE` 在 `lower ≥ 0` 时就是真，
/// 而 `GT` 需要 `lower ≥ δ`——因为 `p = 0` 不满足 `p > 0`。**
/// **把这两行写成一样的，会让 `p ≡ 0` 在 `GT` 下被判定为真。**
///
/// **`δ`（`strictBoundary`）出现在"判假"那一侧**：**假分支要求条件值与真分支之间至少隔 `δ`，
/// 否则它离边界太近，算不算假要看容差。**
///
/// **返回 `null` 表示"界定不下来"**，**而 `null` 与"两个分支都可能"是同一个意思——
/// 不是"不知道"，是"需要指示变量"。**
///
/// B1171, following the baseline's `boundsAvoidDiscreteConditionGap`.
///
/// **It answers "do we still need indicator constraints"**: **when the bounds already settle the answer, an indicator
/// variable is pointless — hand back the truth value and save one binary variable and two rows.**
///
/// **The four relations are <b>asymmetric</b>, and that is the only difficulty here**:
///
/// | Relation | Decided true | Decided false |
/// | --- | --- | --- |
/// | `GE` | `lower ≥ 0` | `upper ≤ −δ` |
/// | `LE` | `upper ≤ 0` | `lower ≥ δ` |
/// | `GT` | `lower ≥ δ` | `upper ≤ 0` |
/// | `LT` | `upper ≤ −δ` | `lower ≥ 0` |
///
/// **`GE` and `GT` differ only in whether equality counts**: **`GE` is true at `lower ≥ 0`, while `GT` needs
/// `lower ≥ δ` — because `p = 0` does not satisfy `p > 0`.** **Writing those two rows the same way would call
/// `p ≡ 0` true under `GT`.**
///
/// **`δ` (`strictBoundary`) appears on the "decided false" side**: **a false branch needs at least `δ` of separation
/// from the true branch, or it sits too close to the boundary to call.**
///
/// **`null` means "the bounds cannot settle it"**, **which is the same as "both branches are possible" — not "unknown",
/// but "an indicator is needed".**
/// </remarks>
public static class DiscreteConditionGap
{
    /// <summary>判断界能否定出真假 / Decide whether the bounds settle the truth value.</summary>
    public static TruthValue? AvoidsGap(
        Comparison relation,
        ConditionBounds<Flt64> bounds,
        Flt64 strictBoundary) {
        ArgumentNullException.ThrowIfNull(bounds);

        Flt64 zero = strictBoundary.Minus(strictBoundary);
        Flt64 negativeBoundary = strictBoundary.Negate();

        switch (relation) {
            case Comparison.GE:
                if (bounds.Lower.CompareTo(zero) >= 0) {
                    return TruthValue.True;
                }

                return bounds.Upper.CompareTo(negativeBoundary) <= 0 ? TruthValue.False : null;

            case Comparison.LE:
                if (bounds.Upper.CompareTo(zero) <= 0) {
                    return TruthValue.True;
                }

                return bounds.Lower.CompareTo(strictBoundary) >= 0 ? TruthValue.False : null;

            case Comparison.GT:
                if (bounds.Lower.CompareTo(strictBoundary) >= 0) {
                    return TruthValue.True;
                }

                return bounds.Upper.CompareTo(zero) <= 0 ? TruthValue.False : null;

            case Comparison.LT:
                if (bounds.Upper.CompareTo(negativeBoundary) <= 0) {
                    return TruthValue.True;
                }

                return bounds.Lower.CompareTo(zero) >= 0 ? TruthValue.False : null;

            // **`EQ`/`NE` 折不了**：**`EQ` 的两个方向都要看，`NE` 是析取。
            // 返回 `null` 而不是猜一个。**
            //
            // **`EQ`/`NE` cannot fold**: **`EQ` needs both sides and `NE` is a disjunction.
            // Return `null` rather than guessing.**
            default:
                return null;
        }
    }
}
