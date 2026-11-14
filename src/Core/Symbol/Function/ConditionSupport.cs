#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 条件指示降阶共享构造 / Shared relation-indicator lowering construction.
/// 对应 Kotlin `Conditional.kt` 的 `relationIndicatorConstraints`：不使用 Big-M 回退，
/// 而是直接由条件多项式的有限范围与真假分支间隔导出两行约束。
/// Mirrors Kotlin `relationIndicatorConstraints`: no Big-M fallback; two rows are derived
/// directly from the finite condition range and the true/false branch gap.
/// </summary>
internal static class ConditionSupport {
    /// <summary>
    /// 构造关系指示约束 / Build the two relation-indicator rows.
    /// </summary>
    /// <param name="polynomial">条件差值多项式 lhs-rhs / Condition difference polynomial lhs-rhs.</param>
    /// <param name="indicator">二值指示变量 / Binary indicator variable.</param>
    /// <param name="relation">条件关系，仅支持 GT/GE/LT/LE / Condition relation; only GT/GE/LT/LE.</param>
    /// <param name="lower">条件多项式有限下界 / Finite lower bound of the condition polynomial.</param>
    /// <param name="upper">条件多项式有限上界 / Finite upper bound of the condition polynomial.</param>
    /// <param name="strictBoundary">真假分支最小间隔 / Minimum true-false branch gap.</param>
    /// <param name="namePrefix">约束名称前缀 / Constraint name prefix.</param>
    /// <returns>两行约束或结构化失败 / Two rows or a structured failure.</returns>
    public static Result<
        IReadOnlyList<LinearInequality<Flt64>>,
        ErrorCode,
        Error<ErrorCode>> RelationConstraints(
        LinearPolynomial<Flt64> polynomial,
        BinVar indicator,
        Comparison relation,
        Flt64 lower,
        Flt64 upper,
        Flt64 strictBoundary,
        string namePrefix) {
        if (relation is not (Comparison.GT or Comparison.GE or Comparison.LT or Comparison.LE)) {
            return Unsupported(
                $"关系 {relation.Symbol()} 不支持；EQ/NE 没有通用二值指示表达。 / Relation {relation.Symbol()} is unsupported; EQ/NE have no generic binary-indicator formulation."
            );
        }
        if (!BigMValues.IsUsable(strictBoundary) || !strictBoundary.Gr(Flt64.Zero)) {
            return Unsupported(
                "strictBoundary 必须为有限且大于 0 的 solver 值。 / strictBoundary must be a finite solver value greater than 0."
            );
        }
        if (!BigMValues.IsUsable(lower) || !BigMValues.IsUsable(upper) ||
            lower.Gr(upper)) {
            return Unsupported(
                "条件范围必须有限且满足 lower <= upper。 / The condition range must be finite with lower <= upper."
            );
        }
        if (!IsUsablePolynomial(polynomial)) {
            return Unsupported(
                "条件多项式的常数和系数必须为有限且可编码的 solver 值。 / The condition polynomial constant and coefficients must be finite and solver-representable."
            );
        }
        if (polynomial.Monomials.Any(monomial => ReferenceEquals(monomial.Symbol, indicator))) {
            return Unsupported(
                "条件多项式不得包含其指示变量。 / The condition polynomial must not contain its own indicator variable."
            );
        }

        bool negate = relation is Comparison.LT or Comparison.LE;
        LinearPolynomial<Flt64> condition = negate ? -polynomial : polynomial;
        Flt64 conditionLower = negate ? upper.Negate() : lower;
        Flt64 conditionUpper = negate ? lower.Negate() : upper;
        Flt64 trueLower = relation is Comparison.GT or Comparison.LT
            ? strictBoundary
            : Flt64.Zero;
        Flt64 falseUpper = relation is Comparison.GT or Comparison.LT
            ? Flt64.Zero
            : strictBoundary.Negate();
        if (!BigMValues.IsUsable(conditionLower) || !BigMValues.IsUsable(conditionUpper) ||
            conditionLower.Gr(conditionUpper) ||
            !falseUpper.Ls(trueLower)) {
            return Unsupported(
                "归一化后的条件范围无效。 / The normalized condition range is invalid."
            );
        }
        if (conditionUpper.Ls(trueLower) && conditionLower.Gr(falseUpper)) {
            return Unsupported(
                "给定范围既不覆盖真分支也不覆盖假分支，无法构造可行二值条件。 / The supplied range intersects neither a valid true nor false branch, so no feasible binary condition can be constructed."
            );
        }

        Flt64 lowerCoefficient = trueLower.Minus(conditionLower).Negate();
        Flt64 upperCoefficient = conditionUpper.Minus(falseUpper).Negate();
        if (!BigMValues.IsUsable(lowerCoefficient) || !BigMValues.IsUsable(upperCoefficient)) {
            return Unsupported(
                "条件范围计算产生非有限或不可编码系数。 / The condition range calculation produced a non-finite or unrepresentable coefficient."
            );
        }

        IReadOnlyList<LinearInequality<Flt64>> constraints = new[] {
            IndicatorSupport.Row(
                monomials: Shift(condition, new LinearMonomial<Flt64>(lowerCoefficient, indicator)),
                constant: condition.Constant,
                comparison: Comparison.GE,
                rhs: conditionLower,
                name: $"{namePrefix}_lower"
            ),
            IndicatorSupport.Row(
                monomials: Shift(condition, new LinearMonomial<Flt64>(upperCoefficient, indicator)),
                constant: condition.Constant,
                comparison: Comparison.LE,
                rhs: falseUpper,
                name: $"{namePrefix}_upper"
            ),
        };
        if (constraints.Any(constraint =>
            !IsUsablePolynomial(constraint.Lhs) || !IsUsablePolynomial(constraint.Rhs))) {
            return Unsupported(
                "生成的条件约束包含非有限或不可编码系数。 / The generated condition constraints contain non-finite or unrepresentable coefficients."
            );
        }
        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
            constraints
        );
    }

    private static bool IsUsablePolynomial(LinearPolynomial<Flt64> polynomial) =>
        BigMValues.IsUsable(polynomial.Constant) &&
        polynomial.Monomials.All(monomial => BigMValues.IsUsable(monomial.Coefficient));

    private static IReadOnlyList<LinearMonomial<Flt64>> Shift(
        LinearPolynomial<Flt64> polynomial,
        LinearMonomial<Flt64> indicatorMonomial) {
        var monomials = new List<LinearMonomial<Flt64>>(polynomial.Monomials.Count + 1);
        monomials.AddRange(polynomial.Monomials);
        monomials.Add(indicatorMonomial);
        return monomials;
    }

    private static Result<
        IReadOnlyList<LinearInequality<Flt64>>,
        ErrorCode,
        Error<ErrorCode>> Unsupported(string message) =>
        new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, message)
        );
}
