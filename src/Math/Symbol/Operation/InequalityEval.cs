#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 不等式求值运算 / Inequality Evaluation Operations.
/// 提供不等式的满足性判断，支持线性、二次和规范不等式。
/// Kotlin Inequality.kt 的对应实现。
/// Provides satisfaction checking for linear, quadratic, and canonical inequalities.
/// </summary>
public static class InequalityEval {
    // ===== Comparison.SatisfiedBy =====

    /// <summary>
    /// 判断 Flt64 值是否满足比较关系 / Check if Flt64 values satisfy comparison relation.
    /// </summary>
    /// <param name="comparison">比较运算符 / Comparison operator.</param>
    /// <param name="lhs">左侧操作数 / Left-hand operand.</param>
    /// <param name="rhs">右侧操作数 / Right-hand operand.</param>
    /// <returns>是否满足比较关系 / Whether the comparison is satisfied.</returns>
    public static bool SatisfiedBy(this Comparison comparison, Flt64 lhs, Flt64 rhs) {
        return comparison switch {
            Comparison.LT => lhs.Value < rhs.Value,
            Comparison.LE => lhs.Value <= rhs.Value,
            Comparison.EQ => lhs.Value == rhs.Value,
            Comparison.NE => lhs.Value != rhs.Value,
            Comparison.GE => lhs.Value >= rhs.Value,
            Comparison.GT => lhs.Value > rhs.Value,
            _ => false
        };
    }

    /// <summary>
    /// 判断泛型值是否满足比较关系 / Check if generic values satisfy comparison relation.
    /// </summary>
    /// <param name="comparison">比较运算符 / Comparison operator.</param>
    /// <param name="lhs">左侧操作数 / Left-hand operand.</param>
    /// <param name="rhs">右侧操作数 / Right-hand operand.</param>
    /// <returns>是否满足比较关系 / Whether the comparison is satisfied.</returns>
    public static bool SatisfiedBy<T>(this Comparison comparison, T lhs, T rhs)
        where T : struct, IRealNumber<T> {
        int cmp = lhs.CompareTo(rhs);
        return comparison switch {
            Comparison.LT => cmp < 0,
            Comparison.LE => cmp <= 0,
            Comparison.EQ => cmp == 0,
            Comparison.NE => cmp != 0,
            Comparison.GE => cmp >= 0,
            Comparison.GT => cmp > 0,
            _ => false
        };
    }

    // ===== LinearInequality satisfaction =====

    /// <summary>
    /// 判断线性不等式是否被给定值满足 / Check if linear inequality is satisfied by given values.
    /// </summary>
    /// <param name="ineq">线性不等式 / Linear inequality.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <returns>是否满足，若缺少值则返回 null / Whether satisfied, or null if values are missing.</returns>
    public static bool? IsSatisfied<T>(
        this LinearInequality<T> ineq,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        T? lhsValue = ineq.Lhs.Evaluate(values);
        T? rhsValue = ineq.Rhs.Evaluate(values);
        if (lhsValue is null || rhsValue is null) {
            return null;
        }

        return ineq.Comparison.SatisfiedBy(lhsValue.Value, rhsValue.Value);
    }

    // ===== QuadraticInequalityOf satisfaction =====

    /// <summary>
    /// 判断二次不等式是否被给定值满足 / Check if quadratic inequality is satisfied by given values.
    /// </summary>
    /// <param name="ineq">二次不等式 / Quadratic inequality.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <returns>是否满足，若缺少值则返回 null / Whether satisfied, or null if values are missing.</returns>
    public static bool? IsSatisfied<T>(
        this QuadraticInequalityOf<T> ineq,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        T? lhsValue = ineq.Lhs.Evaluate(values);
        T? rhsValue = ineq.Rhs.Evaluate(values);
        if (lhsValue is null || rhsValue is null) {
            return null;
        }

        return ineq.Comparison.SatisfiedBy(lhsValue.Value, rhsValue.Value);
    }

    // ===== CanonicalInequality satisfaction =====

    /// <summary>
    /// 判断规范不等式是否被给定值满足 / Check if canonical inequality is satisfied by given values.
    /// </summary>
    /// <param name="ineq">规范不等式 / Canonical inequality.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <returns>是否满足，若缺少值则返回 null / Whether satisfied, or null if values are missing.</returns>
    public static bool? IsSatisfied<T>(
        this CanonicalInequality<T> ineq,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        T? lhsValue = ineq.Lhs.Evaluate(values);
        T? rhsValue = ineq.Rhs.Evaluate(values);
        if (lhsValue is null || rhsValue is null) {
            return null;
        }

        return ineq.Comparison.SatisfiedBy(lhsValue.Value, rhsValue.Value);
    }
}
