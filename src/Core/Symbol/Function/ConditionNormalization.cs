#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 归一化后的条件范围 / A normalized condition range.
/// </summary>
/// <remarks>
/// 对齐基线 `Conditional.kt` 的 `NormalizedCondition` 与 `normalizedCondition`。
///
/// **"归一化"在这里的意思是：把四种关系都折算成"<b>严格真</b>"的形态。**
/// **`LT`/`LE` 先把多项式取负，于是"负侧"变成"正侧"；**
/// **`GT`/`LT` 的真分支下界是 `δ`、`GE`/`LE` 的则是 `0`。**
/// **折算之后，下游只需要处理一种形态。**
///
/// **六个字段里 `NegatePolynomial` 是最容易被忽略的**：
/// **它告诉调用方"要不要把多项式取负"，而<b>忘了取负</b>会让整个真假判断反向——
/// 模型仍然可行，只是条件被取反了。**
///
/// Following the baseline's `NormalizedCondition` and `normalizedCondition`.
///
/// **"Normalising" here means folding the four relations into a <b>strictly true</b> shape.**
/// **`LT`/`LE` negate the polynomial so the negative side becomes the positive one;**
/// **`GT`/`LT` put the true branch's lower edge at `δ`, `GE`/`LE` at `0`.**
/// **After folding, downstream handles one shape only.**
///
/// **Of the six fields `NegatePolynomial` is the easiest to overlook**:
/// **it tells the caller whether to negate the polynomial, and <b>forgetting to</b> flips the whole truth decision —
/// the model stays feasible, only the condition is inverted.**
/// </remarks>
/// <param name="QLower">归一化后的下界 / The normalized lower bound.</param>
/// <param name="QUpper">归一化后的上界 / The normalized upper bound.</param>
/// <param name="TrueLower">真分支的下边界 / The true branch's lower edge.</param>
/// <param name="FalseUpper">假分支的上边界 / The false branch's upper edge.</param>
/// <param name="NegatePolynomial">是否取负多项式 / Whether the polynomial must be negated.</param>
/// <param name="TrueBranchExists">真分支是否存在 / Whether the true branch exists.</param>
/// <param name="FalseBranchExists">假分支是否存在 / Whether the false branch exists.</param>
public sealed record NormalizedCondition(
    Flt64 QLower,
    Flt64 QUpper,
    Flt64 TrueLower,
    Flt64 FalseUpper,
    bool NegatePolynomial,
    bool TrueBranchExists,
    bool FalseBranchExists);

/// <summary>
/// 条件范围归一化与校验 / Normalize and validate condition ranges.
/// </summary>
/// <remarks>
/// 对齐基线 `Conditional.kt` 的 `normalizedCondition`、`validateConditionBounds`、
/// `conditionBranchCoverage` 三个入口补。
///
/// **三件事依次检查，最后一件最容易漏**：
/// **① 端点有限可编码；② `lower ≤ upper`；③ <b>真假分支之间必须有正间隔</b>**
/// （**`FalseUpper &lt; TrueLower`**）。
///
/// **③ 漏掉会怎样**：**`δ = 0` 时 `TrueLower = FalseUpper = 0`，两个分支在零点重合。**
/// **于是"给定一个点，它满足真还是假"没有答案——
/// 而模型仍然可行，因为指示约束两边都能满足。**
///
/// **还有一个"两个分支都空"的检查**：**范围与真假分支都不相交时，无解。**
/// **这时基线报错而不是返回一个"两个都是 `false`"的结果**——
/// **后者会让调用方以为"条件恒假"，而实际是"这个范围根本不合法"。**
///
/// Three things in order, the last easiest to miss:
/// **① endpoints finite and encodable; ② `lower ≤ upper`; ③ <b>a positive gap between the true and false
/// branches</b>** (**`FalseUpper &lt; TrueLower`**).
///
/// **What skipping ③ does**: **at `δ = 0`, `TrueLower = FalseUpper = 0` and the branches meet at zero.**
/// **Then "is this point true or false" has no answer — and the model stays feasible, because both sides of the
/// indicator rows can be satisfied.**
///
/// **There is also a "both branches empty" check**: **when the range meets neither branch there is no solution.**
/// **The baseline errors rather than returning "both are false"** — **the latter would read as "the condition is
/// always false" while the actual problem is "this range is not legal".**
/// </remarks>
public static class ConditionNormalization
{
    /// <summary>归一化条件范围 / Normalize a condition range.</summary>
    public static Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> Normalize(
        Comparison relation,
        ConditionBounds<Flt64> bounds,
        Flt64 strictBoundary) {
        ArgumentNullException.ThrowIfNull(bounds);

        if (!BigMValues.IsUsable(strictBoundary) || strictBoundary.CompareTo(Flt64.Zero) <= 0) {
            return Fail("strictBoundary must be finite and positive");
        }

        if (!BigMValues.IsUsable(bounds.Lower) || !BigMValues.IsUsable(bounds.Upper)) {
            return Fail("the condition bounds must be finite");
        }

        if (bounds.Lower.CompareTo(bounds.Upper) > 0) {
            return Fail(
                $"the condition bounds require lower <= upper (lower={bounds.Lower}, upper={bounds.Upper})");
        }

        Flt64 zero = strictBoundary.Minus(strictBoundary);
        bool negate = relation is Comparison.LT or Comparison.LE;

        Flt64 qLower = negate ? bounds.Upper.Negate() : bounds.Lower;
        Flt64 qUpper = negate ? bounds.Lower.Negate() : bounds.Upper;

        Flt64 trueLower = relation is Comparison.GT or Comparison.LT ? strictBoundary : zero;
        Flt64 falseUpper = relation is Comparison.GT or Comparison.LT ? zero : strictBoundary.Negate();

        if (!BigMValues.IsUsable(qLower) || !BigMValues.IsUsable(qUpper) ||
            !BigMValues.IsUsable(trueLower) || !BigMValues.IsUsable(falseUpper)) {
            return Fail("the normalized range must be finite and solver-representable");
        }

        if (qLower.CompareTo(qUpper) > 0) {
            return Fail("the normalized range is invalid");
        }

        // **正间隔检查**：**`falseUpper >= trueLower` 意味着两个分支相接或交叠，
        // 于是"这个点算真还是假"没有答案，而模型仍然可行。**
        //
        // **The positive-gap check**: **`falseUpper >= trueLower` means the branches touch or overlap, so "true or
        // false at this point" has no answer — while the model stays feasible.**
        if (falseUpper.CompareTo(trueLower) >= 0) {
            return Fail(
                $"a positive gap must separate the true and false branches (falseUpper={falseUpper}, trueLower={trueLower})");
        }

        bool trueBranchExists = qUpper.CompareTo(trueLower) >= 0;
        bool falseBranchExists = qLower.CompareTo(falseUpper) <= 0;

        // **两个分支都空时无解**：**报错而不是返回"两个都是 false"**——
        // **后者会让调用方以为"条件恒假"，而实际是"这个范围不合法"。**
        //
        // **No solution when both branches are empty**: **error rather than returning "both false"** — **the latter
        // would read as "the condition is always false" while the range is simply illegal.**
        if (!trueBranchExists && !falseBranchExists) {
            return Fail(
                "the supplied range intersects neither a valid true nor false branch, so no feasible binary condition exists");
        }

        return new Ok<NormalizedCondition, ErrorCode, Error<ErrorCode>>(
            new NormalizedCondition(
                QLower: qLower,
                QUpper: qUpper,
                TrueLower: trueLower,
                FalseUpper: falseUpper,
                NegatePolynomial: negate,
                TrueBranchExists: trueBranchExists,
                FalseBranchExists: falseBranchExists));
    }

    /// <summary>只校验，不返回归一化结果 / Validate only.</summary>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Validate(
        ConditionBounds<Flt64> bounds,
        Comparison relation,
        Flt64 strictBoundary) {
        Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> normalized =
            Normalize(relation, bounds, strictBoundary);

        return normalized is Ok<NormalizedCondition, ErrorCode, Error<ErrorCode>> ok
            ? new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success())
            : new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ((Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>)normalized).Error);
    }

    private static Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
