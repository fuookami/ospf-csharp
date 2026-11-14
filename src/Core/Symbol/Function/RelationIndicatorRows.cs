#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 关系指示行：用条件自身的范围做两行编码，**不需要 big-M** /
/// Relation-indicator rows, derived from the condition's own bounds — **no big-M needed**.
/// </summary>
/// <remarks>
/// B1184 按基线 `Conditional.kt` 的 `relationIndicatorConstraints` 补。
///
/// **这是本方向第一个<b>不用 big-M</b> 的指示编码，而这不是巧合**：
/// **当条件本身有一个已知范围 `[qLower, qUpper]` 时，松弛量可以<b>精确算出</b>——
/// 它就是"真分支的边界到范围边界"的距离。**
///
/// **两个调整量**：
///
/// - **`lowerAdjustment = trueLower − qLower`**（**真分支下界离范围下界多远**）
/// - **`upperAdjustment = qUpper − falseUpper`**（**范围上界离假分支上界多远**）
///
/// **两行**：
///
/// | 行 | 内容 | 指示 = 0 | 指示 = 1 |
/// | --- | --- | --- | --- |
/// | `_lower` | `q − lowerAdjustment·ind ≥ qLower` | `q ≥ qLower`（放开） | `q ≥ trueLower` |
/// | `_upper` | `q − upperAdjustment·ind ≤ falseUpper` | `q ≤ falseUpper` | `q ≤ qUpper`（放开） |
///
/// **注意两行在指示 = 1 与 = 0 时<b>互换谁紧谁松</b>**：
/// **`_lower` 在指示 = 1 时紧、`_upper` 在指示 = 0 时紧。**
/// **这正好表达"指示为 1 时条件成立、为 0 时不成立"。**
///
/// **三处校验都不是形式**：
///
/// **① 指示必须二值**；**② 多项式<b>不得包含指示变量自己</b>**
/// （**否则两行里同一变量出现两次，`ind` 既在被判定的式子里又在判定它——自指**）；
/// **③ 取负后仍要可用**（**`LT`/`LE` 会先取负，而取负会让一个大数变成负的大数**）。
///
/// B1184, following the baseline's `relationIndicatorConstraints`.
///
/// **The first <b>big-M-free</b> indicator encoding in this direction, and not by accident**:
/// **when the condition already has a known range `[qLower, qUpper]`, the relaxation can be <b>computed exactly</b> —
/// it is the distance from the true branch's edge to the range's edge.**
///
/// **Two adjustments**:
///
/// - **`lowerAdjustment = trueLower − qLower`** (**how far the true branch's lower edge sits from the range's**)
/// - **`upperAdjustment = qUpper − falseUpper`** (**how far the range's upper edge sits from the false branch's**)
///
/// **Two rows**:
///
/// | Row | Content | indicator = 0 | indicator = 1 |
/// | --- | --- | --- | --- |
/// | `_lower` | `q − lowerAdjustment·ind ≥ qLower` | `q ≥ qLower` (relaxed) | `q ≥ trueLower` |
/// | `_upper` | `q − upperAdjustment·ind ≤ falseUpper` | `q ≤ falseUpper` | `q ≤ qUpper` (relaxed) |
///
/// **Note the two rows <b>swap which one binds</b> between indicator 1 and 0**: **`_lower` binds at indicator 1 and
/// `_upper` at indicator 0.** **That is exactly "the condition holds at one and fails at zero".**
///
/// **None of the three checks is cosmetic**:
///
/// **① the indicator must be binary; ② the polynomial <b>must not contain the indicator itself</b>**
/// (**otherwise the same variable appears twice and `ind` both governs and is governed — self-reference**);
/// **③ it must stay usable after negation** (**`LT`/`LE` negate first, and negating turns a large number into a large
/// negative one**).
/// </remarks>
public static class RelationIndicatorRows
{
    /// <summary>构建两行关系指示约束 / Build the two relation-indicator rows.</summary>
    /// <param name="poly">条件多项式 / The condition polynomial.</param>
    /// <param name="indicator">指示列 / The indicator column.</param>
    /// <param name="relation">原关系 / The original relation.</param>
    /// <param name="bounds">条件范围 / The condition's range.</param>
    /// <param name="strictBoundary">真假分支之间的正间隔 / The positive gap between the branches.</param>
    /// <param name="namePrefix">名称前缀 / The name prefix.</param>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Build(
            LinearPolynomial<Flt64> poly,
            IVariableItem indicator,
            Comparison relation,
            ConditionBounds<Flt64> bounds,
            Flt64 strictBoundary,
            string namePrefix) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(indicator);
        ArgumentNullException.ThrowIfNull(bounds);

        if (!indicator.TypeKind.IsBinaryType) {
            return Fail($"the indicator `{indicator.Name}` must be binary");
        }

        // **多项式不得包含指示变量自己：否则 `ind` 既在被判定的式子里又在判定它。**
        // **The polynomial must not contain its own indicator, or `ind` both governs and is governed.**
        foreach (LinearMonomial<Flt64> monomial in poly.Monomials) {
            if (monomial.Symbol.Equals(indicator)) {
                return Fail(
                    "the condition polynomial must not contain its own indicator variable");
            }
        }

        if (!ConditionPolynomials.IsUsable(poly)) {
            return Fail(
                "the condition polynomial's constant and every coefficient must be finite and encodable");
        }

        Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> normalized =
            ConditionNormalization.Normalize(relation, bounds, strictBoundary);
        if (normalized is Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>> failure) {
            return new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                failure.Error);
        }

        NormalizedCondition condition =
            ((Ok<NormalizedCondition, ErrorCode, Error<ErrorCode>>)normalized).Value;

        // **`LT`/`LE` 先取负，把"负侧"折成"正侧"。**
        // **`LT`/`LE` negate first, folding the negative side onto the positive one.**
        LinearPolynomial<Flt64> q = condition.NegatePolynomial ? Negate(poly) : poly;

        if (!ConditionPolynomials.IsUsable(q)) {
            return Fail("negating the condition polynomial produced a non-finite or unrepresentable value");
        }

        Flt64 lowerAdjustment = condition.TrueLower.Minus(condition.QLower);
        Flt64 upperAdjustment = condition.QUpper.Minus(condition.FalseUpper);
        Flt64 lowerIndicatorCoefficient = lowerAdjustment.Negate();
        Flt64 upperIndicatorCoefficient = upperAdjustment.Negate();

        if (!BigMValues.IsUsable(lowerAdjustment) || !BigMValues.IsUsable(upperAdjustment) ||
            !BigMValues.IsUsable(lowerIndicatorCoefficient) ||
            !BigMValues.IsUsable(upperIndicatorCoefficient)) {
            return Fail(
                "a difference or negation in the condition constraints produced a non-finite or unrepresentable value");
        }

        var rows = new List<LinearInequality<Flt64>>(2) {
            // **`_lower`：`q − lowerAdjustment·ind ≥ qLower`。**
            // **指示 = 1 时落到 `q ≥ trueLower`；指示 = 0 时放开到 `q ≥ qLower`。**
            //
            // **`_lower`: `q − lowerAdjustment·ind ≥ qLower`.**
            // **Lands on `q ≥ trueLower` at indicator 1 and relaxes to `q ≥ qLower` at zero.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(q.Monomials, new LinearMonomial<Flt64>(lowerIndicatorCoefficient, indicator)),
                    q.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), condition.QLower),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_lower"),

            // **`_upper`：`q − upperAdjustment·ind ≤ falseUpper`。**
            // **指示 = 0 时落到 `q ≤ falseUpper`；指示 = 1 时放开到 `q ≤ qUpper`。**
            //
            // **`_upper`: `q − upperAdjustment·ind ≤ falseUpper`.**
            // **Lands on `q ≤ falseUpper` at indicator 0 and relaxes to `q ≤ qUpper` at one.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(q.Monomials, new LinearMonomial<Flt64>(upperIndicatorCoefficient, indicator)),
                    q.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), condition.FalseUpper),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_upper"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the generated condition rows contain non-finite or unrepresentable coefficients");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static LinearPolynomial<Flt64> Negate(LinearPolynomial<Flt64> polynomial) {
        var monomials = new LinearMonomial<Flt64>[polynomial.Monomials.Count];
        for (int index = 0; index < polynomial.Monomials.Count; ++index) {
            monomials[index] = new LinearMonomial<Flt64>(
                polynomial.Monomials[index].Coefficient.Negate(), polynomial.Monomials[index].Symbol);
        }

        return new LinearPolynomial<Flt64>(monomials, polynomial.Constant.Negate());
    }

    private static List<LinearMonomial<Flt64>> Append(
        IReadOnlyList<LinearMonomial<Flt64>> monomials, LinearMonomial<Flt64> extra) {
        var copy = new List<LinearMonomial<Flt64>>(monomials.Count + 1);
        copy.AddRange(monomials);
        copy.Add(extra);
        return copy;
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
